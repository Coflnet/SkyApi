using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky;
using Coflnet.Sky.Api.Services.Tasks.Setups;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;

namespace SkyApi.Services.Tasks.Setups;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class ItemResaleServiceTests
{
    private FakeTimeProvider clock;
    private Mock<IItemPriceSource> source;
    private ItemResaleService service;

    [SetUp]
    public void Setup()
    {
        clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
        source = new Mock<IItemPriceSource>();
        source.Setup(s => s.GetCurrentAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new PriceSnapshot(new CurrentPrice { Buy = 1_000_000, Sell = 980_000, IsAh = true }, true));
        source.Setup(s => s.GetHistoryAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(() => History());
        service = new ItemResaleService(source.Object, clock, NullLogger<ItemResaleService>.Instance);
    }

    private IReadOnlyList<AveragePrice> History()
        => Enumerable.Range(1, 20)
            .Select(i => new AveragePrice { Avg = 1_000_000, Min = 900_000, Volume = 100, Date = clock.GetUtcNow().UtcDateTime.Date.AddDays(-i) })
            .ToList();

    [Test]
    public async Task ReturnsQuoteFromCurrentPriceAndHistory()
    {
        var quote = await service.GetQuoteAsync("ITEM");
        quote.Buy.Should().Be(1_000_000);
        quote.Label.Should().Be(StabilityLabel.Stable);
        quote.Resale.Should().BeApproximately(MarketTax.NetAh(1_000_000), 0.01);
    }

    [Test]
    public async Task SecondCall_UsesCaches()
    {
        await service.GetQuoteAsync("ITEM");
        await service.GetQuoteAsync("ITEM");
        source.Verify(s => s.GetCurrentAsync("ITEM", It.IsAny<Dictionary<string, string>>()), Times.Once);
        source.Verify(s => s.GetHistoryAsync("ITEM", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()), Times.Once);
    }

    [Test]
    public async Task CurrentPrice_ExpiresAfterFiveMinutes_HistoryDoesNot()
    {
        await service.GetQuoteAsync("ITEM");
        clock.Advance(TimeSpan.FromMinutes(6));
        await service.GetQuoteAsync("ITEM");
        source.Verify(s => s.GetCurrentAsync("ITEM", It.IsAny<Dictionary<string, string>>()), Times.Exactly(2));
        source.Verify(s => s.GetHistoryAsync("ITEM", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()), Times.Once);
    }

    [Test]
    public async Task History_ExpiresAfterOneHour()
    {
        await service.GetQuoteAsync("ITEM");
        clock.Advance(TimeSpan.FromMinutes(61));
        await service.GetQuoteAsync("ITEM");
        source.Verify(s => s.GetHistoryAsync("ITEM", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()), Times.Exactly(2));
    }

    [Test]
    public async Task DifferentFilters_AreCachedSeparately_AndCallerDictionaryIsNotMutated()
    {
        source.Setup(s => s.GetHistoryAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()))
            .Callback<string, DateTime, DateTime, Dictionary<string, string>>((_, _, _, f) => f["ItemId"] = "5")
            .ReturnsAsync(() => History());
        var filters = new Dictionary<string, string> { ["Rarity"] = "LEGENDARY" };
        await service.GetQuoteAsync("PET", filters);
        await service.GetQuoteAsync("PET", filters);
        await service.GetQuoteAsync("PET");
        filters.Should().ContainSingle();
        source.Verify(s => s.GetHistoryAsync("PET", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()), Times.Exactly(2));
    }

    [Test]
    public async Task Bazaar_UsesMinAsSeries()
    {
        source.Setup(s => s.GetCurrentAsync("BZ", It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new PriceSnapshot(new CurrentPrice { Buy = 110, Sell = 100, IsAh = false }, false));
        source.Setup(s => s.GetHistoryAsync("BZ", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(Enumerable.Range(1, 20)
                .Select(i => new AveragePrice { Avg = 500, Min = 100, Volume = 7000, Date = clock.GetUtcNow().UtcDateTime.Date.AddDays(-i) }).ToList());
        var quote = await service.GetQuoteAsync("BZ");
        quote.P10.Should().Be(100);
        quote.SellNow.Should().BeApproximately(98.75, 1e-9);
    }

    [Test]
    public async Task SourceFailure_ReturnsUnknownQuote_AndIsNotCached()
    {
        source.Setup(s => s.GetCurrentAsync("BAD", It.IsAny<Dictionary<string, string>>())).ThrowsAsync(new InvalidOperationException());
        var quote = await service.GetQuoteAsync("BAD");
        quote.Label.Should().Be(StabilityLabel.Unknown);
        quote.Buy.Should().BeNull();
    }

    [Test]
    public async Task HistoryFailure_KeepsTheQuoteWithConservativeResale()
    {
        source.Setup(s => s.GetHistoryAsync("ITEM", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()))
            .ThrowsAsync(new InvalidOperationException("history down"));

        var quote = await service.GetQuoteAsync("ITEM");

        quote.Buy.Should().Be(1_000_000);
        quote.Label.Should().Be(StabilityLabel.Unknown);
        quote.Resale.Should().NotBeNull();
        quote.Resale.Should().BeLessThan(quote.SellNow!.Value);
    }

    [Test]
    public async Task FilteredPrice_WithoutHaircut_IsNotDividedBy098()
    {
        source.Setup(s => s.GetCurrentAsync("PET", It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(PricesServiceSource.FromSummary(1_100_000, 1_000_000));
        source.Setup(s => s.GetHistoryAsync("PET", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new List<AveragePrice>());

        var quote = await service.GetQuoteAsync("PET", new Dictionary<string, string> { ["Rarity"] = "MYTHIC" });

        quote.SellNow.Should().BeApproximately(MarketTax.NetAh(1_000_000), 0.01);
    }

    [Test]
    public void Source_ClassifiesWhichSellPricesCarryTheHaircut()
    {
        // BIN path: Sell = BIN * 0.98 < Buy = BIN
        PricesServiceSource.Classify(new CurrentPrice { Buy = 1_000_000, Sell = 980_000, IsAh = true, Available = 5 })
            .SellIncludesAhHaircut.Should().BeTrue();
        // no BIN listed: Sell is the summary minimum, Available stays 0
        PricesServiceSource.Classify(new CurrentPrice { Buy = 1_200_000, Sell = 1_000_000, IsAh = true })
            .SellIncludesAhHaircut.Should().BeFalse();
        // craft cost cap: Sell and Buy were both clamped to the cap
        PricesServiceSource.Classify(new CurrentPrice { Buy = 500_000, Sell = 500_000, IsAh = true, Available = 5 })
            .SellIncludesAhHaircut.Should().BeFalse();
        PricesServiceSource.Classify(new CurrentPrice { Buy = 110, Sell = 100, IsAh = false, Available = 5 })
            .SellIncludesAhHaircut.Should().BeFalse();
        PricesServiceSource.Classify(null).SellIncludesAhHaircut.Should().BeFalse();
    }

    [Test]
    public async Task ConcurrentCallers_ShareOneUpstreamCall()
    {
        var calls = 0;
        source.Setup(s => s.GetCurrentAsync("SHARED", It.IsAny<Dictionary<string, string>>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref calls);
                await Task.Delay(100);
                return new PriceSnapshot(new CurrentPrice { Buy = 1_000_000, Sell = 980_000, IsAh = true }, true);
            });
        var historyCalls = 0;
        source.Setup(s => s.GetHistoryAsync("SHARED", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()))
            .Returns(async () =>
            {
                Interlocked.Increment(ref historyCalls);
                await Task.Delay(100);
                return History();
            });

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.GetQuoteAsync("SHARED")));

        calls.Should().Be(1);
        historyCalls.Should().Be(1);
    }

    [Test]
    public async Task FailedTag_IsNotRetriedWithinTheNegativeCacheTtl()
    {
        source.Setup(s => s.GetCurrentAsync("BAD", It.IsAny<Dictionary<string, string>>())).ThrowsAsync(new InvalidOperationException());

        await service.GetQuoteAsync("BAD");
        clock.Advance(TimeSpan.FromMinutes(2));
        await service.GetQuoteAsync("BAD");
        source.Verify(s => s.GetCurrentAsync("BAD", It.IsAny<Dictionary<string, string>>()), Times.Once);

        clock.Advance(TimeSpan.FromMinutes(2));
        await service.GetQuoteAsync("BAD");
        source.Verify(s => s.GetCurrentAsync("BAD", It.IsAny<Dictionary<string, string>>()), Times.Exactly(2));
    }

    /// <summary>Polls a condition in real time (generous bound), the service itself only sees the fake clock</summary>
    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 2000 && !condition(); i++)
            await Task.Delay(5);
        condition().Should().BeTrue("the condition should be reached");
    }

    private ItemResaleService NewService() => new(source.Object, clock, NullLogger<ItemResaleService>.Instance);

    /// <summary>History calls that stay pending until their gate is completed</summary>
    private List<TaskCompletionSource<IReadOnlyList<AveragePrice>>> GateHistory()
    {
        var gates = new List<TaskCompletionSource<IReadOnlyList<AveragePrice>>>();
        source.Setup(s => s.GetHistoryAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()))
            .Returns(() =>
            {
                var gate = new TaskCompletionSource<IReadOnlyList<AveragePrice>>();
                lock (gates)
                    gates.Add(gate);
                return gate.Task;
            });
        return gates;
    }

    private static int Count<T>(List<T> list)
    {
        lock (list)
            return list.Count;
    }

    [Test]
    public async Task SlowSource_DegradesAfterTheDeadline_RemembersTheFailure_AndIsNotCachedAsSuccess()
    {
        var started = 0;
        source.Setup(s => s.GetCurrentAsync("SLOW", It.IsAny<Dictionary<string, string>>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref started);
                return new TaskCompletionSource<PriceSnapshot>().Task;
            });
        var pending = service.GetQuoteAsync("SLOW");
        await Eventually(() => Volatile.Read(ref started) == 1);

        clock.Advance(TimeSpan.FromSeconds(9));
        var quote = await pending;

        quote.Should().Be(ItemResaleQuote.Unknown("SLOW") with { Degraded = true });
        await Eventually(() => service.HasRecentFailure("SLOW"));
        source.Setup(s => s.GetCurrentAsync("SLOW", It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new PriceSnapshot(new CurrentPrice { Buy = 1_000_000, Sell = 980_000, IsAh = true }, true));
        (await service.GetQuoteAsync("SLOW")).Degraded.Should().BeTrue("a call that ran past the deadline is not retried right away");
        clock.Advance(TimeSpan.FromMinutes(4));
        var later = await service.GetQuoteAsync("SLOW");
        later.Buy.Should().Be(1_000_000);
        later.Degraded.Should().BeFalse();
    }

    [Test]
    public async Task CallerCancellation_IsNotRememberedAsAFailure()
    {
        var gates = GateHistory();
        using var cts = new CancellationTokenSource();
        var pending = service.GetQuoteAsync("ITEM", null, cts.Token);
        await Eventually(() => Count(gates) == 1);

        cts.Cancel();

        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
        service.HasRecentFailure("ITEM").Should().BeFalse();
    }

    [Test]
    public async Task DegradedQuote_IsFlaggedSoCallersCanCacheItBriefly()
    {
        source.Setup(s => s.GetHistoryAsync("ITEM", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()))
            .ThrowsAsync(new InvalidOperationException("history down"));

        (await service.GetQuoteAsync("ITEM")).Degraded.Should().BeTrue();
        (await service.GetQuoteAsync("OTHER")).Degraded.Should().BeFalse();
    }

    [Test]
    public async Task SlotWaitAndUpstreamCall_ShareOneDeadline()
    {
        var gates = GateHistory();
        var holders = Enumerable.Range(0, 4).Select(i => service.GetQuoteAsync("H" + i)).ToList();
        await Eventually(() => Count(gates) == 4);
        var queued = service.GetQuoteAsync("Q");
        await Eventually(() => service.HistoryQueriesWaitingForSlot == 1);
        clock.Advance(TimeSpan.FromSeconds(5));
        gates[0].SetResult(History().ToList());
        await Eventually(() => Count(gates) == 5);

        // 8.1s after the load started. With a separate deadline for the call it would only end at 13s
        clock.Advance(TimeSpan.FromSeconds(3.1));

        await Eventually(() => service.HasRecentFailure("Q"));
        await Task.WhenAll(holders.Append(queued));
    }

    [Test]
    public async Task AbandonedQuery_KeepsItsSlotUntilItFinishes()
    {
        var fast = new ItemResaleService(source.Object, clock, NullLogger<ItemResaleService>.Instance);
        var gates = GateHistory();
        var holders = Enumerable.Range(0, 4).Select(i => fast.GetQuoteAsync("H" + i)).ToList();
        await Eventually(() => Count(gates) == 4);
        clock.Advance(TimeSpan.FromSeconds(9));
        await Task.WhenAll(holders);

        // the four timed out queries still run, so a new one has to wait for a slot
        var next = fast.GetQuoteAsync("NEXT");
        await Eventually(() => fast.HistoryQueriesWaitingForSlot == 1);
        Count(gates).Should().Be(4);

        gates[0].SetResult(History().ToList());
        await Eventually(() => Count(gates) == 5);
        fast.HistoryQueriesWaitingForSlot.Should().Be(0);
        gates[4].SetResult(History().ToList());
        (await next).Degraded.Should().BeFalse();
    }

    [Test]
    public async Task DeadlineHistory_FailureIsNotRetriedButQuoteStillDegrades()
    {
        source.Setup(s => s.GetHistoryAsync("ITEM", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()))
            .ThrowsAsync(new InvalidOperationException("history down"));

        var first = await service.GetQuoteAsync("ITEM");
        var second = await service.GetQuoteAsync("ITEM");

        first.Degraded.Should().BeTrue();
        second.Degraded.Should().BeTrue();
        source.Verify(s => s.GetHistoryAsync("ITEM", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()), Times.Once);
    }

    [Test]
    public async Task UnknownItem_WithZeroPrices_HasNoBuyPrice()
    {
        source.Setup(s => s.GetCurrentAsync("NONE", It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new PriceSnapshot(new CurrentPrice { Available = -1 }, false));
        source.Setup(s => s.GetHistoryAsync("NONE", It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()))
            .ReturnsAsync(new List<AveragePrice>());
        var quote = await service.GetQuoteAsync("NONE");
        quote.Buy.Should().BeNull();
        quote.Resale.Should().BeNull();
    }

    [Test]
    public async Task HistoryQueries_AreLimitedToFourParallel()
    {
        var running = 0;
        var maxRunning = 0;
        source.Setup(s => s.GetHistoryAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Dictionary<string, string>>()))
            .Returns(async () =>
            {
                var now = Interlocked.Increment(ref running);
                int seen;
                while (now > (seen = Volatile.Read(ref maxRunning)) && Interlocked.CompareExchange(ref maxRunning, now, seen) != seen) { }
                await Task.Delay(50);
                Interlocked.Decrement(ref running);
                return History();
            });
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => service.GetQuoteAsync("ITEM" + i)));
        maxRunning.Should().BeLessThanOrEqualTo(4);
        maxRunning.Should().BeGreaterThan(1);
    }

    [Test]
    public void ToDailyPoints_DropsDaysWithoutVolume_AndKeepsRealDayNumbers()
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var history = new[]
        {
            new AveragePrice { Avg = 10, Volume = 1, Date = now.Date.AddDays(-5) },
            new AveragePrice { Avg = 20, Volume = 0, Date = now.Date.AddDays(-4) },
            new AveragePrice { Avg = 30, Volume = 2, Date = now.Date.AddDays(-2) }
        };
        var points = ItemResaleService.ToDailyPoints(history, false, now);
        points.Select(p => p.Value).Should().Equal(10, 30);
        (points[1].Day - points[0].Day).Should().Be(3);
    }
}
