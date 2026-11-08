using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Tasks.Setups;

public interface IItemResaleService
{
    Task<ItemResaleQuote> GetQuoteAsync(string tag, IReadOnlyDictionary<string, string> priceFilters = null, CancellationToken cancellationToken = default);
}

/// <summary>Seam over the price database so the service can be tested without it</summary>
public interface IItemPriceSource
{
    Task<PriceSnapshot> GetCurrentAsync(string tag, Dictionary<string, string> filters);
    Task<IReadOnlyList<AveragePrice>> GetHistoryAsync(string tag, DateTime start, DateTime end, Dictionary<string, string> filters);
}

/// <summary>Reads prices from the scoped <see cref="PricesService"/></summary>
public class PricesServiceSource(IServiceScopeFactory scopeFactory) : IItemPriceSource
{
    public async Task<PriceSnapshot> GetCurrentAsync(string tag, Dictionary<string, string> filters)
    {
        using var scope = scopeFactory.CreateScope();
        var prices = scope.ServiceProvider.GetRequiredService<PricesService>();
        if (filters.Count == 0)
            return Classify(await prices.GetCurrentPrice(tag, 1));
        var summary = await prices.GetSumaryCache(tag, filters);
        return FromSummary(summary.Med, summary.Min);
    }

    /// <summary>
    /// PricesService only multiplies Sell by 0.98 on its BIN path (Available is the number of BINs found, at least 1).
    /// Not haircut: the no-BIN fallback (Sell is the summary minimum, Available 0), and prices capped by the craft cost,
    /// where Sell was clamped to the same cap as Buy so it is not below Buy (a haircut BIN price always is for one item).
    /// What can't be told apart: a cap that happens to equal the haircut price exactly.
    /// </summary>
    public static PriceSnapshot Classify(CurrentPrice price)
        => new(price, price != null && price.IsAh && price.Available > 0 && price.Sell < price.Buy);

    /// <summary>Filtered prices come from the summary, its minimum never had the haircut</summary>
    public static PriceSnapshot FromSummary(double median, double min)
        => new(new CurrentPrice { Buy = median, Sell = min, IsAh = true }, false);

    public async Task<IReadOnlyList<AveragePrice>> GetHistoryAsync(string tag, DateTime start, DateTime end, Dictionary<string, string> filters)
    {
        using var scope = scopeFactory.CreateScope();
        var prices = scope.ServiceProvider.GetRequiredService<PricesService>();
        return (await prices.GetHistory(tag, start, end, filters)).ToList();
    }
}

/// <summary>
/// Resale quotes per item with in memory caches (history 1h, current price 5min).
/// Concurrent callers share one upstream call per key, a quote takes at most <see cref="DefaultQuoteDeadline"/>
/// and failures are remembered for <see cref="FailureCacheDuration"/> so a slow tag doesn't block every build.
/// </summary>
public class ItemResaleService : IItemResaleService
{
    public static readonly TimeSpan HistoryCacheDuration = TimeSpan.FromHours(1);
    public static readonly TimeSpan PriceCacheDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan DefaultQuoteDeadline = TimeSpan.FromSeconds(8);
    public const int MaxParallelHistoryQueries = 4;
    public const int MaxParallelPriceQueries = 8;
    private readonly IItemPriceSource source;
    private readonly TimeProvider time;
    private readonly ILogger<ItemResaleService> logger;
    private readonly TimeSpan quoteDeadline;
    private readonly SemaphoreSlim historyLimit = new(MaxParallelHistoryQueries);
    private readonly SemaphoreSlim priceLimit = new(MaxParallelPriceQueries);
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, PriceSnapshot Value)> priceCache = new();
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<DailyPoint> Value)> historyCache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<PriceSnapshot>>> priceFlights = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<DailyPoint>>>> historyFlights = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> failures = new();
    private int waitingForSlot;

    /// <summary>History queries currently queued for a free slot, for diagnostics and tests</summary>
    internal int HistoryQueriesWaitingForSlot => Volatile.Read(ref waitingForSlot);

    public ItemResaleService(IItemPriceSource source, TimeProvider time, ILogger<ItemResaleService> logger, TimeSpan? quoteDeadline = null)
    {
        this.source = source;
        this.time = time;
        this.logger = logger;
        this.quoteDeadline = quoteDeadline ?? DefaultQuoteDeadline;
    }

    public async Task<ItemResaleQuote> GetQuoteAsync(string tag, IReadOnlyDictionary<string, string> priceFilters = null, CancellationToken cancellationToken = default)
    {
        var filters = priceFilters?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, string>();
        var key = Key(tag, filters);
        using var timeout = new CancellationTokenSource(quoteDeadline, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var snapshot = await GetCurrentCached(key, tag, filters).WaitAsync(linked.Token);
            var quote = ToQuote(snapshot);
            var (points, historyOk) = await GetHistoryOrEmpty(key, tag, filters, quote.IsBazaar, linked.Token, cancellationToken);
            var result = ResaleMath.Evaluate(tag, quote, points);
            return historyOk ? result : result with { Degraded = true };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not compute resale quote for {Tag}", tag);
            return ItemResaleQuote.Unknown(tag) with { Degraded = true };
        }
    }

    /// <summary>A failing history must not blank the quote, without history the resale stays conservative</summary>
    private async Task<(IReadOnlyList<DailyPoint> Points, bool Ok)> GetHistoryOrEmpty(string key, string tag, Dictionary<string, string> filters,
        bool isBazaar, CancellationToken deadline, CancellationToken callerToken)
    {
        try
        {
            return (await GetHistoryCached(key, tag, filters, isBazaar).WaitAsync(deadline), true);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not load price history for {Tag}, using the current price only", tag);
            return ([], false);
        }
    }

    /// <summary>True while a price or history call for the item failed recently and is not retried yet</summary>
    internal bool HasRecentFailure(string tag, IReadOnlyDictionary<string, string> priceFilters = null)
    {
        var key = Key(tag, priceFilters?.ToDictionary(p => p.Key, p => p.Value) ?? new Dictionary<string, string>());
        return IsFailed("price|" + key) || IsFailed("history|" + key);
    }

    private bool IsFailed(string failureKey)
        => failures.TryGetValue(failureKey, out var at) && time.GetUtcNow() - at < FailureCacheDuration;

    /// <summary>Thrown instead of calling the source while an earlier call for the key failed recently</summary>
    private sealed class RecentlyFailedException(string key) : Exception($"Skipped {key}, it failed recently");

    /// <summary>The call never started because no slot became free before the deadline, says nothing about the tag</summary>
    private sealed class QueueTimeoutException : TimeoutException
    {
        public QueueTimeoutException() : base("No free slot for the price query before the deadline") { }
    }

    private void ThrowIfFailedRecently(string failureKey)
    {
        if (IsFailed(failureKey))
            throw new RecentlyFailedException(failureKey);
    }

    /// <summary>
    /// Remembers a failure of the call itself (it threw or ran past the deadline), never a slot queue timeout.
    /// Only the shared load writes here, so a success of the same load can't be overwritten by a late caller.
    /// </summary>
    private async Task<T> RememberFailure<T>(string failureKey, Func<Task<T>> load)
    {
        try
        {
            return await load();
        }
        catch (Exception e) when (e is not QueueTimeoutException)
        {
            failures[failureKey] = time.GetUtcNow();
            throw;
        }
    }

    private static ItemQuote ToQuote(PriceSnapshot snapshot)
    {
        var current = snapshot?.Price;
        if (current == null)
            return new ItemQuote(null, null, false);
        return new ItemQuote(current.Buy > 0 ? current.Buy : null, current.Sell > 0 ? current.Sell : null,
            !current.IsAh, snapshot.SellIncludesAhHaircut);
    }

    private static string Key(string tag, Dictionary<string, string> filters)
        => tag + "|" + JsonConvert.SerializeObject(filters.OrderBy(f => f.Key, StringComparer.Ordinal).ToList());

    private Task<PriceSnapshot> GetCurrentCached(string key, string tag, Dictionary<string, string> filters)
    {
        if (priceCache.TryGetValue(key, out var hit) && time.GetUtcNow() - hit.At < PriceCacheDuration)
            return Task.FromResult(hit.Value);
        ThrowIfFailedRecently("price|" + key);
        return SingleFlight(priceFlights, key, () => RememberFailure("price|" + key, async () =>
        {
            var fresh = await RunLimited(priceLimit, () => source.GetCurrentAsync(tag, new Dictionary<string, string>(filters)));
            priceCache[key] = (time.GetUtcNow(), fresh);
            failures.TryRemove("price|" + key, out _);
            return fresh;
        }));
    }

    private Task<IReadOnlyList<DailyPoint>> GetHistoryCached(string key, string tag, Dictionary<string, string> filters, bool isBazaar)
    {
        var historyKey = key + (isBazaar ? "|bz" : "|ah");
        if (historyCache.TryGetValue(historyKey, out var hit) && time.GetUtcNow() - hit.At < HistoryCacheDuration)
            return Task.FromResult(hit.Value);
        ThrowIfFailedRecently("history|" + key);
        return SingleFlight(historyFlights, historyKey, () => RememberFailure("history|" + key, async () =>
        {
            var now = time.GetUtcNow().UtcDateTime;
            var raw = await RunLimited(historyLimit, () => source.GetHistoryAsync(tag, now.AddDays(-ResaleMath.HistoryDays), now, new Dictionary<string, string>(filters)));
            var points = ToDailyPoints(raw, isBazaar, now);
            historyCache[historyKey] = (time.GetUtcNow(), points);
            failures.TryRemove("history|" + key, out _);
            return points;
        }));
    }

    /// <summary>All concurrent callers of a key share one load, the entry is removed when it completes or fails</summary>
    private static Task<T> SingleFlight<T>(ConcurrentDictionary<string, Lazy<Task<T>>> flights, string key, Func<Task<T>> load)
    {
        async Task<T> RunAndRelease()
        {
            await Task.Yield();
            try
            {
                return await load();
            }
            finally
            {
                flights.TryRemove(key, out _);
            }
        }
        return flights.GetOrAdd(key, _ => new Lazy<Task<T>>(RunAndRelease)).Value;
    }

    /// <summary>
    /// Waiting for a slot and the call itself share one deadline, the slot is freed when it passes.
    /// IItemPriceSource takes no cancellation token (PricesService has none), so a call that ran past the deadline
    /// keeps running in the background and holds its slot until it finishes (see <see cref="ReleaseWhenDone"/>).
    /// The trade-off is that a query that never finishes keeps its slot.
    /// </summary>
    private async Task<T> RunLimited<T>(SemaphoreSlim limit, Func<Task<T>> call)
    {
        using var timeout = new CancellationTokenSource(quoteDeadline, time);
        var isHistory = ReferenceEquals(limit, historyLimit);
        if (isHistory)
            Interlocked.Increment(ref waitingForSlot);
        try
        {
            await limit.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new QueueTimeoutException();
        }
        finally
        {
            if (isHistory)
                Interlocked.Decrement(ref waitingForSlot);
        }
        var releaseHere = true;
        try
        {
            // the budget can run out in the same instant the slot is handed over, then the call never really started
            if (timeout.IsCancellationRequested)
                throw new QueueTimeoutException();
            var running = call();
            try
            {
                return await running.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                releaseHere = false;
                ReleaseWhenDone(running, limit);
                throw new TimeoutException("The price query ran past the deadline");
            }
        }
        finally
        {
            if (releaseHere)
                limit.Release();
        }
    }

    /// <summary>
    /// A query that ran past its deadline keeps running (no cancellation token), so it keeps its slot until it
    /// really finishes and the real database concurrency stays bounded. Its fault is logged so it isn't unobserved.
    /// The slot is released exactly once, here.
    /// </summary>
    private void ReleaseWhenDone(Task abandoned, SemaphoreSlim limit)
        => abandoned.ContinueWith(t =>
        {
            if (t.IsFaulted)
                logger.LogWarning(t.Exception, "Abandoned price query failed after its deadline");
            limit.Release();
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

    /// <summary>Daily average for auctions, daily min (sell side) for bazaar, days without volume are dropped</summary>
    public static IReadOnlyList<DailyPoint> ToDailyPoints(IEnumerable<AveragePrice> history, bool isBazaar, DateTime now)
    {
        var origin = now.Date.AddDays(-ResaleMath.HistoryDays);
        return history
            .Where(h => h.Volume > 0)
            .OrderBy(h => h.Date)
            .Select(h => new DailyPoint(isBazaar ? h.Min : h.Avg, h.Volume, (int)(h.Date.Date - origin).TotalDays))
            .Where(p => p.Value > 0)
            .ToList();
    }
}
