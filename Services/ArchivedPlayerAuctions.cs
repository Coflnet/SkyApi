using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Core;
using Coflnet.Sky.Core.Archive;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services;

/// <summary>Settings of the player-auction archive fallback.</summary>
public class ArchiveFallbackOptions
{
    /// <summary>Config key: no archive call at all unless true (MariaDB still holds every row until the deletion runs).</summary>
    public const string EnabledKey = "ARCHIVE:PLAYER_FALLBACK_ENABLED";
    /// <summary>Config key: per-call timeout in ms on the player page path.</summary>
    public const string TimeoutKey = "ARCHIVE:PLAYER_FALLBACK_TIMEOUT_MS";
    /// <summary>Whether archive calls are made at all.</summary>
    public bool Enabled { get; init; }
    /// <summary>Per-call timeout of the interactive player page.</summary>
    public TimeSpan PageTimeout { get; init; } = TimeSpan.FromSeconds(3);
    /// <summary>Per-call timeout of the export, which prefers completeness over latency.</summary>
    public TimeSpan ExportTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Reads the options, defaults: disabled, 3 s.</summary>
    public static ArchiveFallbackOptions From(IConfiguration config) => new()
    {
        Enabled = string.Equals(config[EnabledKey], "true", StringComparison.OrdinalIgnoreCase),
        PageTimeout = TimeSpan.FromMilliseconds(int.TryParse(config[TimeoutKey], out var ms) && ms > 0 ? ms : 3000),
    };
}

/// <summary>An archive call failed (HTTP error, timeout, unreadable body, bad configuration); never raised for the caller's own cancellation.</summary>
public class ArchiveReadException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ArchiveReadException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The archive still had more rows when the page cap of an all-rows read was reached.</summary>
public class ArchiveTruncatedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ArchiveTruncatedException(int maxPages) : base($"Archive still has more rows after {maxPages} pages") { }
}

/// <summary>One archive page; <see cref="Next"/> is the <c>X-Next-Before</c> cursor computed from the index, null when there is no further page.</summary>
public record ArchivePage(List<ArchivedAuctionDto> Rows, AuctionIndexCursor Next);

/// <summary>Reads a player's sold/listed auctions from the SkyAuctions archive v2 (R2 blobs + index), the only copy once MariaDB rows are deleted.</summary>
public interface IArchivedAuctionsReader
{
    /// <summary>Fallback settings.</summary>
    ArchiveFallbackOptions Options { get; }

    /// <summary>
    /// One page (newest first) of <c>GET api/archive/v2/player/{sellerId}/sells?deletedOnly=true</c>; <paramref name="before"/> continues after a cursor.
    /// Empty when the fallback is disabled (no call) or the archive answers 503. Throws <see cref="ArchiveReadException"/> on any other failure.
    /// </summary>
    Task<ArchivePage> GetSellsAsync(int sellerId, AuctionIndexCursor before, int limit, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>HTTP reader (the generated SkyAuctions client has no archive v2 endpoints).</summary>
public class HttpArchivedAuctionsReader : IArchivedAuctionsReader
{
    /// <summary>Largest page the archive endpoint serves.</summary>
    public const int MaxLimit = 1000;
    private static readonly ArchivePage Empty = new(new List<ArchivedAuctionDto>(), null);
    private readonly HttpClient http;
    private readonly string baseUrl;

    /// <summary>Uses <c>AUCTIONS_BASE_URL</c> like the other SkyAuctions clients.</summary>
    public HttpArchivedAuctionsReader(HttpClient http, IConfiguration config)
    {
        this.http = http;
        baseUrl = (config["AUCTIONS_BASE_URL"] ?? "").TrimEnd('/');
        Options = ArchiveFallbackOptions.From(config);
    }

    /// <inheritdoc/>
    public ArchiveFallbackOptions Options { get; }

    /// <inheritdoc/>
    public async Task<ArchivePage> GetSellsAsync(int sellerId, AuctionIndexCursor before, int limit, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!Options.Enabled)
            return Empty;
        var url = $"{baseUrl}/api/archive/v2/player/{sellerId}/sells?deletedOnly=true&limit={Math.Clamp(limit, 1, MaxLimit)}"
            + (before == null ? "" : "&before=" + Uri.EscapeDataString(before.ToString()));
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            return await ReadPageAsync(url, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ArchiveReadException($"Archive v2 sells of player {sellerId} could not be read", ex);
        }
    }

    private async Task<ArchivePage> ReadPageAsync(string url, CancellationToken token)
    {
        using var response = await http.GetAsync(url, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            return Empty;
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        var rows = JsonConvert.DeserializeObject<List<ArchivedAuctionDto>>(body) ?? new List<ArchivedAuctionDto>();
        return new ArchivePage(rows, ReadNextCursor(response));
    }

    private static AuctionIndexCursor ReadNextCursor(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Next-Before", out var values))
            return null;
        return AuctionIndexCursor.TryParse(values.FirstOrDefault(), out var cursor) ? cursor : null;
    }
}

/// <summary>Merging of archived auctions into answers that were built from MariaDB only.</summary>
public static class ArchivedPlayerAuctions
{
    /// <summary>Pages requested at most per call of <see cref="CollectAsync"/> (bounds a deep page request to 5000 archived rows).</summary>
    public const int MaxPagesPerRequest = 5;

    /// <summary>
    /// Page path: like <see cref="CollectAsync"/> with the short timeout, but any failure (except the request's own cancellation)
    /// is logged and answered with an empty list, so the MariaDB-only answer still goes out.
    /// </summary>
    public static async Task<List<ArchivedAuctionDto>> TryCollectForPageAsync(IArchivedAuctionsReader reader, Func<IReadOnlyCollection<long>, Task<HashSet<long>>> inMariaDb,
        int sellerId, long skip, int take, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            return await CollectAsync(reader, inMariaDb, sellerId, skip, take, MaxPagesPerRequest, reader?.Options.PageTimeout ?? default, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(ex, "Archive sells of player {SellerId} could not be read; answering with MariaDB rows only", sellerId);
            return new List<ArchivedAuctionDto>();
        }
    }

    /// <summary>
    /// Archived sells of a player that MariaDB does not have (anything in MariaDB was already counted there, the archive of a month is
    /// written before its MariaDB rows are deleted so both may hold it), skipping <paramref name="skip"/> and returning at most
    /// <paramref name="take"/> of them, newest first. <paramref name="inMariaDb"/> answers which of the uids still exist in MariaDB.
    /// Pages follow the <c>X-Next-Before</c> cursor and rows are deduplicated by uid across pages. Throws <see cref="ArchiveReadException"/> when a page fails.
    /// With <paramref name="failOnTruncation"/> it throws <see cref="ArchiveTruncatedException"/> when <paramref name="maxPages"/> was reached while the archive reports more (for all-rows reads such as the export).
    /// </summary>
    public static async Task<List<ArchivedAuctionDto>> CollectAsync(IArchivedAuctionsReader reader, Func<IReadOnlyCollection<long>, Task<HashSet<long>>> inMariaDb,
        int sellerId, long skip, int take, int maxPages, TimeSpan timeout, CancellationToken cancellationToken, bool failOnTruncation = false)
    {
        var result = new List<ArchivedAuctionDto>();
        if (reader == null || !reader.Options.Enabled || sellerId <= 0 || take <= 0)
            return result;
        var seen = new HashSet<long>();
        AuctionIndexCursor before = null;
        for (var page = 0; page < maxPages && result.Count < take; page++)
        {
            var limit = (int)Math.Min(HttpArchivedAuctionsReader.MaxLimit, Math.Max(10, skip + take));
            var batch = await reader.GetSellsAsync(sellerId, before, limit, timeout, cancellationToken).ConfigureAwait(false);
            var fresh = (batch?.Rows ?? new List<ArchivedAuctionDto>()).Where(a => seen.Add(a.Uid)).ToList();
            var known = await inMariaDb(fresh.Select(a => a.Uid).ToList()).ConfigureAwait(false);
            skip = TakeUnseen(fresh.Where(a => !known.Contains(a.Uid)), skip, take, result);
            if (batch?.Next == null)
                break;
            before = batch.Next;
            if (failOnTruncation && page == maxPages - 1)
                throw new ArchiveTruncatedException(maxPages);
        }
        return result;
    }

    /// <summary>Moves rows into <paramref name="result"/> after skipping <paramref name="skip"/>; returns the remaining skip.</summary>
    private static long TakeUnseen(IEnumerable<ArchivedAuctionDto> rows, long skip, int take, List<ArchivedAuctionDto> result)
    {
        foreach (var auction in rows)
        {
            if (skip > 0)
                skip--;
            else if (result.Count < take)
                result.Add(auction);
        }
        return skip;
    }

    /// <summary>The list-entry shape of <c>GET player/{uuid}/auctions</c>.</summary>
    public static AuctionResult ToAuctionResult(ArchivedAuctionDto a) => new()
    {
        AuctionId = a.Uuid,
        HighestBid = a.HighestBidAmount,
        ItemName = a.ItemName,
        End = a.End,
        Tag = a.Tag,
        StartingBid = a.StartingBid,
        Bin = a.Bin,
    };

    /// <summary>The shape of <c>GET player/{uuid}/auctions/export</c> (<c>Id</c> is 0: archived rows have no MariaDB id).</summary>
    public static SoldAuction ToSoldAuction(ArchivedAuctionDto a, string auctioneerUuid) => new()
    {
        Id = 0,
        Uuid = a.Uuid,
        Tag = a.Tag,
        ItemName = a.ItemName,
        AuctioneerId = a.Auctioneer ?? auctioneerUuid,
        StartingBid = a.StartingBid,
        HighestBidAmount = a.HighestBidAmount,
        Start = a.Start,
        End = a.End,
        Bin = a.Bin,
        Count = a.Count,
        Tier = Enum.TryParse<Tier>(a.Tier, true, out var tier) ? tier : default,
        Enchantments = (a.Enchantments ?? new Dictionary<string, int>())
            .Where(kv => Enum.TryParse<Enchantment.EnchantmentType>(kv.Key, true, out _))
            .Select(kv => new Enchantment { Type = Enum.Parse<Enchantment.EnchantmentType>(kv.Key, true), Level = (byte)kv.Value })
            .ToList(),
        FlattenedNbt = a.NbtLookup ?? new Dictionary<string, string>(),
    };
}
