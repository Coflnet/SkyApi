using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Core.Archive;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services;

#pragma warning disable CS1591
public class ArchivedPlayerAuctionsTests
{
    private static ArchivedAuctionDto Dto(long uid, int day) => new()
    {
        Uid = uid, Uuid = uid.ToString("x32"), Tag = "HYPERION", ItemName = "Hyperion", Bin = true, Sold = true, Start = new DateTime(2021, 1, day, 0, 0, 0, DateTimeKind.Utc),
        End = new DateTime(2021, 1, day, 12, 0, 0, DateTimeKind.Utc), StartingBid = 5, HighestBidAmount = 9, Auctioneer = "abc", Count = 1, Tier = "LEGENDARY",
        Enchantments = new Dictionary<string, int> { ["sharpness"] = 6, ["not_an_enchant"] = 1 }, NbtLookup = new Dictionary<string, string> { ["k"] = "v" },
    };

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Serves a fixed newest-first list in cursor pages like the SkyAuctions endpoint (Next = cursor of the last row, like X-Next-Before).</summary>
    private class FakeReader : IArchivedAuctionsReader
    {
        public readonly List<ArchivedAuctionDto> All;
        public readonly List<(AuctionIndexCursor before, int limit)> Calls = new();
        public Exception Failure;
        public ArchiveFallbackOptions Options { get; init; } = new() { Enabled = true };
        private static long EndSeconds(ArchivedAuctionDto a) => new DateTimeOffset(DateTime.SpecifyKind(a.End, DateTimeKind.Utc)).ToUnixTimeSeconds();
        public FakeReader(IEnumerable<ArchivedAuctionDto> all) => All = all.OrderByDescending(a => a.End).ThenByDescending(a => a.Uid).ToList();
        public Task<ArchivePage> GetSellsAsync(int sellerId, AuctionIndexCursor before, int limit, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add((before, limit));
            if (Failure != null)
                throw Failure;
            var rows = All.Where(a => before == null || EndSeconds(a) < before.End || EndSeconds(a) == before.End && a.Uid < before.Uid).Take(limit).ToList();
            var next = rows.Count < limit ? null : AuctionIndexCursor.After(rows[^1].End, rows[^1].Uid);
            return Task.FromResult(new ArchivePage(rows, next));
        }
    }

    private static Task<HashSet<long>> None(IReadOnlyCollection<long> uids) => Task.FromResult(new HashSet<long>());

    [Test]
    public async Task ContinuesWithArchivedRowsAfterMariaDbRunsOut()
    {
        var reader = new FakeReader(Enumerable.Range(1, 25).Select(i => Dto(i, i)));

        var page = await ArchivedPlayerAuctions.CollectAsync(reader, None, 7, skip: 0, take: 10, maxPages: 5, CallTimeout, CancellationToken.None);

        Assert.That(page.Select(a => a.Uid), Is.EqualTo(Enumerable.Range(16, 10).Reverse().Select(i => (long)i)), "newest first");
    }

    [Test]
    public async Task SkipsAlreadyConsumedArchivedRowsAcrossPages()
    {
        var reader = new FakeReader(Enumerable.Range(1, 3000).Select(i => Dto(i, 1).Also(d => d.End = new DateTime(2021, 1, 1).AddMinutes(i))));

        var page = await ArchivedPlayerAuctions.CollectAsync(reader, None, 7, skip: 1995, take: 10, maxPages: 5, CallTimeout, CancellationToken.None);

        Assert.That(page.Select(a => a.Uid), Is.EqualTo(Enumerable.Range(0, 10).Select(i => 3000L - 1995 - i)));
        Assert.That(reader.Calls, Has.Count.EqualTo(3), "1000 + 1000 + 1000 rows read to skip 1995");
        Assert.That(reader.Calls[1].before, Is.Not.Null, "pages continue with the cursor of the previous page");
    }

    /// <summary>Several rows share the End second at the page boundary; a server cursor that re-includes the boundary row must not duplicate it.</summary>
    [Test]
    public async Task RepeatedEndSecondsAtAPageBoundaryAreDeduplicatedByUid()
    {
        var sameSecond = new DateTime(2021, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var rows = Enumerable.Range(1, 30).Select(i => Dto(i, 1).Also(d => d.End = sameSecond)).OrderByDescending(a => a.Uid).ToList();
        var reader = new PagedFake(rows, 20);

        var page = await ArchivedPlayerAuctions.CollectAsync(reader, None, 7, skip: 0, take: 25, maxPages: 5, CallTimeout, CancellationToken.None);

        Assert.That(page.Select(a => a.Uid), Is.EqualTo(Enumerable.Range(6, 25).Reverse().Select(i => (long)i)));
        Assert.That(reader.Calls[1].before.ToString(), Is.EqualTo(reader.Cursor.ToString()), "the X-Next-Before cursor of the server is used");
    }

    /// <summary>An empty page that still carries X-Next-Before (all index rows unlocated) must not end the walk.</summary>
    [Test]
    public async Task EmptyPageWithNextCursorContinuesToTheNextPage()
    {
        var reader = new EmptyFirstPageFake(new[] { Dto(5, 1), Dto(4, 1) });

        var page = await ArchivedPlayerAuctions.CollectAsync(reader, None, 7, skip: 0, take: 10, maxPages: 5, CallTimeout, CancellationToken.None);

        Assert.That(page.Select(a => a.Uid), Is.EqualTo(new long[] { 5, 4 }));
        Assert.That(reader.CallCount, Is.EqualTo(2));
    }

    private class EmptyFirstPageFake : IArchivedAuctionsReader
    {
        private readonly ArchivedAuctionDto[] rows;
        public int CallCount;
        public ArchiveFallbackOptions Options { get; } = new() { Enabled = true };
        public EmptyFirstPageFake(ArchivedAuctionDto[] rows) => this.rows = rows;
        public Task<ArchivePage> GetSellsAsync(int sellerId, AuctionIndexCursor before, int limit, TimeSpan timeout, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(before == null
                ? new ArchivePage(new List<ArchivedAuctionDto>(), new AuctionIndexCursor(1609502400, 11))
                : new ArchivePage(rows.ToList(), null));
        }
    }

    /// <summary>Page boundaries of fixed size whose next page repeats the last row of the previous one.</summary>
    private class PagedFake : IArchivedAuctionsReader
    {
        private readonly List<ArchivedAuctionDto> rows;
        private readonly int size;
        public readonly List<(AuctionIndexCursor before, int limit)> Calls = new();
        public readonly AuctionIndexCursor Cursor = new(1609502400, 11);
        public ArchiveFallbackOptions Options { get; } = new() { Enabled = true };
        public PagedFake(List<ArchivedAuctionDto> rows, int size) { this.rows = rows; this.size = size; }
        public Task<ArchivePage> GetSellsAsync(int sellerId, AuctionIndexCursor before, int limit, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls.Add((before, limit));
            var page = before == null ? rows.Take(size).ToList() : rows.Skip(size - 1).Take(size).ToList();
            return Task.FromResult(new ArchivePage(page, before == null ? Cursor : null));
        }
    }

    [Test]
    public async Task RowsStillInMariaDbAreNeitherReturnedNorCounted()
    {
        var reader = new FakeReader(Enumerable.Range(1, 12).Select(i => Dto(i, i)));
        Task<HashSet<long>> InDb(IReadOnlyCollection<long> uids) => Task.FromResult(uids.Where(u => u > 10).ToHashSet());

        var page = await ArchivedPlayerAuctions.CollectAsync(reader, InDb, 7, skip: 1, take: 3, maxPages: 5, CallTimeout, CancellationToken.None);

        Assert.That(page.Select(a => a.Uid), Is.EqualTo(new long[] { 9, 8, 7 }), "12 and 11 are in MariaDB, 10 is skipped");
    }

    [Test]
    public async Task StopsAtThePageCapAndWithoutSellerOrReader()
    {
        var reader = new FakeReader(Enumerable.Range(1, 5000).Select(i => Dto(i, 1).Also(d => d.End = new DateTime(2021, 1, 1).AddMinutes(i))));
        var page = await ArchivedPlayerAuctions.CollectAsync(reader, None, 7, skip: 4990, take: 100, maxPages: 2, CallTimeout, CancellationToken.None);
        Assert.That(page, Is.Empty, "the cap bounds a very deep request");
        Assert.That(reader.Calls, Has.Count.EqualTo(2));
        Assert.That(await ArchivedPlayerAuctions.CollectAsync(null, None, 7, 0, 10, 5, CallTimeout, CancellationToken.None), Is.Empty);
        Assert.That(await ArchivedPlayerAuctions.CollectAsync(reader, None, 0, 0, 10, 5, CallTimeout, CancellationToken.None), Is.Empty, "unknown player");
    }

    [Test]
    public async Task DisabledFallbackMakesNoArchiveCall()
    {
        var reader = new FakeReader(Enumerable.Range(1, 5).Select(i => Dto(i, i))) { Options = new ArchiveFallbackOptions { Enabled = false } };

        var collected = await ArchivedPlayerAuctions.CollectAsync(reader, None, 7, 0, 10, 5, CallTimeout, CancellationToken.None);
        var forPage = await ArchivedPlayerAuctions.TryCollectForPageAsync(reader, None, 7, 0, 10, null, CancellationToken.None);

        Assert.That(collected.Concat(forPage), Is.Empty);
        Assert.That(reader.Calls, Is.Empty);
    }

    [Test]
    public async Task PagePathFallsBackToEmptyOnAnyReaderFailure()
    {
        var reader = new FakeReader(Enumerable.Range(1, 5).Select(i => Dto(i, i))) { Failure = new ArchiveReadException("boom", new Exception()) };
        Assert.That(await ArchivedPlayerAuctions.TryCollectForPageAsync(reader, None, 7, 0, 10, NullLogger.Instance, CancellationToken.None), Is.Empty);
        reader.Failure = new InvalidOperationException("unexpected");
        Assert.That(await ArchivedPlayerAuctions.TryCollectForPageAsync(reader, None, 7, 0, 10, NullLogger.Instance, CancellationToken.None), Is.Empty);
    }

    [Test]
    public void PagePathDoesNotSwallowTheRequestsOwnCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var reader = new FakeReader(Enumerable.Range(1, 5).Select(i => Dto(i, i))) { Failure = new OperationCanceledException(cts.Token) };
        Assert.ThrowsAsync<OperationCanceledException>(() => ArchivedPlayerAuctions.TryCollectForPageAsync(reader, None, 7, 0, 10, NullLogger.Instance, cts.Token));
    }

    [Test]
    public void ExportPathFailsWhenPageCapIsReachedWithMoreRows()
    {
        var reader = new FakeReader(Enumerable.Range(1, 2500).Select(i => Dto(i, 1 + i % 28)));
        Assert.ThrowsAsync<ArchiveTruncatedException>(() => ArchivedPlayerAuctions.CollectAsync(reader, None, 7, 0, int.MaxValue, 2, CallTimeout, CancellationToken.None, failOnTruncation: true));
    }

    [Test]
    public async Task ExportPathSucceedsWhenArchiveEndsExactlyAtCap()
    {
        var reader = new FakeReader(Enumerable.Range(1, 1500).Select(i => Dto(i, 1 + i % 28)));
        var all = await ArchivedPlayerAuctions.CollectAsync(reader, None, 7, 0, int.MaxValue, 2, CallTimeout, CancellationToken.None, failOnTruncation: true);
        Assert.That(all, Has.Count.EqualTo(1500));
    }

    [Test]
    public void ExportPathSurfacesAFailingPage()
    {
        var reader = new FakeReader(Enumerable.Range(1, 5).Select(i => Dto(i, i))) { Failure = new ArchiveReadException("timeout", new Exception()) };
        Assert.ThrowsAsync<ArchiveReadException>(() => ArchivedPlayerAuctions.CollectAsync(reader, None, 7, 0, int.MaxValue, 200, CallTimeout, CancellationToken.None));
    }

    [Test]
    public void MapsToTheExistingResponseShapes()
    {
        var a = Dto(5, 3);
        var list = ArchivedPlayerAuctions.ToAuctionResult(a);
        Assert.That((list.AuctionId, list.HighestBid, list.StartingBid, list.Bin, list.Tag, list.End), Is.EqualTo((a.Uuid, 9L, 5L, true, "HYPERION", a.End)));
        var sold = ArchivedPlayerAuctions.ToSoldAuction(a, "fallback");
        Assert.That((sold.Uuid, sold.AuctioneerId, sold.Tier, sold.FlattenedNbt["k"]), Is.EqualTo((a.Uuid, "abc", Tier.LEGENDARY, "v")));
        Assert.That(sold.Enchantments.Select(e => (e.Type.ToString(), (int)e.Level)), Is.EqualTo(new[] { ("sharpness", 6) }), "unknown enchantment names are dropped");
    }

    private class Handler : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "[]";
        public string NextBefore;
        public bool Hang;
        public int Calls;
        public Uri Last;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Last = request.RequestUri;
            if (Hang)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            var response = new HttpResponseMessage(Status) { Content = new StringContent(Body) };
            if (NextBefore != null)
                response.Headers.Add("X-Next-Before", NextBefore);
            return response;
        }
    }

    private static HttpArchivedAuctionsReader Reader(Handler h, bool enabled = true, string baseUrl = "http://auctions:5031/") => new(new HttpClient(h),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["AUCTIONS_BASE_URL"] = baseUrl,
            [ArchiveFallbackOptions.EnabledKey] = enabled ? "true" : "false",
        }).Build());

    [Test]
    public async Task HttpReaderCallsTheSellsEndpointWithDeletedOnlyAndCursorAndParsesTheDtos()
    {
        var h = new Handler { Body = Newtonsoft.Json.JsonConvert.SerializeObject(new[] { Dto(9, 2) }), NextBefore = "1609502400:9" };

        var page = await Reader(h).GetSellsAsync(77, new AuctionIndexCursor(1609502400, 9), 5000, CallTimeout, CancellationToken.None);

        Assert.That(h.Last.ToString(), Is.EqualTo("http://auctions:5031/api/archive/v2/player/77/sells?deletedOnly=true&limit=1000&before=1609502400%3A9"));
        Assert.That(page.Rows.Single().Uid, Is.EqualTo(9));
        Assert.That(page.Next.ToString(), Is.EqualTo("1609502400:9"), "X-Next-Before is taken over");
    }

    [Test]
    public async Task HttpReaderWithoutTheFlagMakesNoCall()
    {
        var h = new Handler();
        var page = await Reader(h, enabled: false).GetSellsAsync(77, null, 10, CallTimeout, CancellationToken.None);
        Assert.That((page.Rows, h.Calls), Is.EqualTo((new List<ArchivedAuctionDto>(), 0)));
        Assert.That(ArchiveFallbackOptions.From(new ConfigurationBuilder().Build()).Enabled, Is.False, "default is off");
    }

    [Test]
    public async Task DisabledArchiveKeepsTheMariaDbAnswer()
    {
        var page = await Reader(new Handler { Status = HttpStatusCode.ServiceUnavailable }).GetSellsAsync(77, null, 10, CallTimeout, CancellationToken.None);
        Assert.That(page.Rows, Is.Empty);
    }

    [TestCase("this is not json", HttpStatusCode.OK, "http://auctions:5031/")]
    [TestCase("[]", HttpStatusCode.InternalServerError, "http://auctions:5031/")]
    [TestCase("[]", HttpStatusCode.OK, "")]
    public async Task HttpReaderThrowsArchiveReadExceptionAndPageFallsBackToMariaDb(string body, HttpStatusCode status, string baseUrl)
    {
        var reader = Reader(new Handler { Body = body, Status = status }, baseUrl: baseUrl);

        Assert.ThrowsAsync<ArchiveReadException>(() => reader.GetSellsAsync(77, null, 10, CallTimeout, CancellationToken.None));
        Assert.That(await ArchivedPlayerAuctions.TryCollectForPageAsync(reader, None, 77, 0, 10, NullLogger.Instance, CancellationToken.None), Is.Empty);
    }

    [Test]
    public async Task HttpReaderTimeoutIsAnArchiveReadExceptionButCallerCancellationIsNot()
    {
        var reader = Reader(new Handler { Hang = true });
        Assert.ThrowsAsync<ArchiveReadException>(() => reader.GetSellsAsync(77, null, 10, TimeSpan.FromMilliseconds(50), CancellationToken.None));

        using var cts = new CancellationTokenSource(50);
        Assert.CatchAsync<OperationCanceledException>(() => reader.GetSellsAsync(77, null, 10, TimeSpan.FromMinutes(1), cts.Token));
    }
}

internal static class TestExtensions
{
    public static T Also<T>(this T value, Action<T> action) { action(value); return value; }
}
