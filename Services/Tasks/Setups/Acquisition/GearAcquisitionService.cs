using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;

public interface IGearAcquisitionService
{
    /// <summary>
    /// Direct purchase and cheapest base for a gear piece. Failures come back as unavailable reasons,
    /// it only throws <see cref="OperationCanceledException"/> when the caller cancels.
    /// </summary>
    Task<AcquisitionResult> GetAsync(GearItemSpec spec, CancellationToken cancellationToken = default);
    /// <summary>Direct purchase and cheapest base for a pet</summary>
    Task<AcquisitionResult> GetAsync(GearPetSpec pet, CancellationToken cancellationToken = default);
}

/// <summary>Slugs of <see cref="AcquisitionResult.DirectUnavailableReason"/> and <see cref="AcquisitionResult.BaseUnavailableReason"/></summary>
public static class AcquisitionReasons
{
    public const string MissingTag = "missing_tag";
    public const string Unsupported = "unsupported_modifiers";
    public const string NoListing = "no_matching_listing";
    public const string NoBase = "no_base_candidate";
    public const string ListingsNotLoaded = "listings_not_loaded";
    public const string PricesNotLoaded = "prices_not_loaded";
    public const string Timeout = "timeout";
    public const string Failed = "lookup_failed";

    /// <summary>Reasons that may be gone on the next try, results with them are cached shorter</summary>
    public static readonly IReadOnlySet<string> Transient = new HashSet<string> { ListingsNotLoaded, PricesNotLoaded, Timeout, Failed };
}

/// <summary>
/// Finds the cheapest active BIN that meets a gear spec and the cheapest base item plus upgrades.
/// Results are cached per normalized spec and concurrent callers of one spec share a computation that runs on the thread pool.
/// At most <see cref="MaxParallel"/> computations run at once. The deadline bounds how long a caller waits (a late caller
/// gets a timeout result), not the work: a computation runs on, up to <see cref="WorkTimeout"/>, and caches its result
/// for the next caller. Its slot stays taken until the work it started (eg a database query that ignores cancellation) has ended.
/// </summary>
public class GearAcquisitionService : IGearAcquisitionService
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan TransientCacheDuration = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(5);
    /// <summary>Upper bound for one computation including the wait for a slot, past it the result is a (transient) timeout</summary>
    public static readonly TimeSpan WorkTimeout = TimeSpan.FromSeconds(60);
    public const int MaxParallel = 4;
    public const int BaseCandidates = 30;
    public const int MaxCacheEntries = 5000;
    private static readonly Regex UuidPattern = new("^[0-9a-f]{32}$", RegexOptions.Compiled);

    private readonly IGearListingSource listings;
    private readonly IGearPriceBook prices;
    private readonly UpgradePlanner planner;
    private readonly TimeProvider time;
    private readonly ILogger<GearAcquisitionService> logger;
    private readonly TimeSpan deadline;
    private readonly int maxCacheEntries;
    private readonly SemaphoreSlim limit;
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, AcquisitionResult Value)> cache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<AcquisitionResult>>> flights = new();

    public GearAcquisitionService(IGearListingSource listings, IGearPriceBook prices, TimeProvider time, ILogger<GearAcquisitionService> logger)
        : this(listings, prices, time, logger, DefaultDeadline)
    {
    }

    internal GearAcquisitionService(IGearListingSource listings, IGearPriceBook prices, TimeProvider time,
        ILogger<GearAcquisitionService> logger, TimeSpan deadline, int maxParallel = MaxParallel, int maxCacheEntries = MaxCacheEntries)
    {
        this.listings = listings;
        this.prices = prices;
        this.time = time;
        this.logger = logger;
        this.deadline = deadline;
        this.maxCacheEntries = maxCacheEntries;
        limit = new SemaphoreSlim(maxParallel);
        planner = new UpgradePlanner(prices);
    }

    /// <summary>Computation slots not taken, for tests and diagnostics</summary>
    internal int FreeSlots => limit.CurrentCount;
    internal int CachedEntries => cache.Count;

    public Task<AcquisitionResult> GetAsync(GearItemSpec spec, CancellationToken cancellationToken = default)
        => GetAsync(spec == null ? null : GearTarget.From(spec), cancellationToken);

    public Task<AcquisitionResult> GetAsync(GearPetSpec pet, CancellationToken cancellationToken = default)
        => GetAsync(pet == null ? null : GearTarget.From(pet), cancellationToken);

    /// <summary>Normalized 32 hex uuid of the auction or null when it has none</summary>
    public static string NormalizeUuid(string uuid)
    {
        var clean = uuid?.Replace("-", "").ToLowerInvariant();
        return clean != null && UuidPattern.IsMatch(clean) ? clean : null;
    }

    /// <summary>Buyable without grinding first, then completely priced, then the lowest total</summary>
    public static CheapestBase Cheapest(IEnumerable<CheapestBase> options)
        => options.Where(o => o != null)
            .OrderBy(o => o.GrindRequired.Count > 0)
            .ThenBy(o => o.Incomplete)
            .ThenBy(o => o.Total)
            .FirstOrDefault();

    private async Task<AcquisitionResult> GetAsync(GearTarget target, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(target?.Tag))
            return AcquisitionResult.Failed(AcquisitionReasons.MissingTag, time.GetUtcNow());
        var key = target.Key;
        if (cache.TryGetValue(key, out var entry) && entry.Expires > time.GetUtcNow())
            return entry.Value;
        // Task.Run: the shared computation must not run on (and block) the thread of the first caller
        var flight = flights.GetOrAdd(key, k => new Lazy<Task<AcquisitionResult>>(() => Task.Run(() => ComputeAndStore(k, target))));
        try
        {
            return await flight.Value.WaitAsync(deadline, time, cancellationToken);
        }
        catch (TimeoutException)
        {
            // the computation goes on and caches its result, only this caller stops waiting
            return AcquisitionResult.Failed(AcquisitionReasons.Timeout, time.GetUtcNow());
        }
    }

    private async Task<AcquisitionResult> ComputeAndStore(string key, GearTarget target)
    {
        try
        {
            var result = await ComputeBounded(target);
            Store(key, result);
            return result;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not compute the acquisition for {Tag}", target.Tag);
            return AcquisitionResult.Failed(AcquisitionReasons.Failed, time.GetUtcNow());
        }
        finally
        {
            flights.TryRemove(key, out _);
        }
    }

    private async Task<AcquisitionResult> ComputeBounded(GearTarget target)
    {
        var timeout = new CancellationTokenSource(WorkTimeout, time);
        if (!await TryEnter(timeout))
            return AcquisitionResult.Failed(AcquisitionReasons.Timeout, time.GetUtcNow());
        var work = new PendingWork();
        // read the token once: ReleaseWhenDone disposes the source as soon as the computation ends
        var token = timeout.Token;
        var compute = Task.Run(() => Compute(target, work, token));
        _ = ReleaseWhenDone(compute, work, timeout);
        try
        {
            return await compute.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return AcquisitionResult.Failed(AcquisitionReasons.Timeout, time.GetUtcNow());
        }
    }

    private async Task<bool> TryEnter(CancellationTokenSource timeout)
    {
        try
        {
            await limit.WaitAsync(timeout.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            timeout.Dispose();
            return false;
        }
    }

    /// <summary>Frees the slot (and the deadline) only once the computation and everything it started has ended</summary>
    private async Task ReleaseWhenDone(Task compute, PendingWork work, CancellationTokenSource timeout)
    {
        try
        {
            await compute;
        }
        catch (Exception)
        {
            // the outcome is handled by the caller, here only the end counts
        }
        await work.WhenAllEnded();
        limit.Release();
        timeout.Dispose();
    }

    private async Task<AcquisitionResult> Compute(GearTarget target, PendingWork work, CancellationToken token)
    {
        var filterSet = GearFilterBuilder.Build(target);
        var bins = Safe(() => listings.IsReady ? listings.ActiveBins(target.Tag) : null, target.Tag);
        var (direct, directReason, directAuction) = await Guard(() => FindDirect(target, filterSet, bins, work, token), target.Tag);
        var (baseOption, baseReason) = await Guard(() => FindBase(target, bins, directAuction, token), target.Tag);
        return new AcquisitionResult(direct, directReason, baseOption, baseReason, filterSet.Filters, time.GetUtcNow());
    }

    private async Task<(DirectPurchase, string, SaveAuction)> FindDirect(GearTarget target, GearFilterSet filterSet,
        IReadOnlyList<SaveAuction> bins, PendingWork work, CancellationToken token)
    {
        if (target.Unsupported.Count > 0)
            return (null, AcquisitionReasons.Unsupported, null);
        if (bins != null)
            return FindDirectInMemory(target, filterSet, bins);
        if (filterSet.PostHocOnly.Count > 0)
            return (null, AcquisitionReasons.ListingsNotLoaded, null); // only the in-memory NBT can verify these
        var lookup = work.Track(listings.LowestFromDatabaseAsync(target.Tag, filterSet.Filters, token));
        var lowest = await lookup.WaitAsync(token);
        var uuid = NormalizeUuid(lowest?.Uuid);
        if (uuid == null)
            return (null, AcquisitionReasons.NoListing, null);
        return (ToDirect(uuid, lowest.Value.Price), null, null);
    }

    private (DirectPurchase, string, SaveAuction) FindDirectInMemory(GearTarget target, GearFilterSet filterSet, IReadOnlyList<SaveAuction> bins)
    {
        var matcher = CompileMatcher(filterSet.Filters, target.Tag);
        var match = bins.FirstOrDefault(a => NormalizeUuid(a.Uuid) != null && Matches(matcher, a) && GearSpecVerifier.Meets(a, target));
        if (match == null)
            return (null, AcquisitionReasons.NoListing, null);
        return (ToDirect(NormalizeUuid(match.Uuid), match.StartingBid), null, match);
    }

    private async Task<(CheapestBase, string)> FindBase(GearTarget target, IReadOnlyList<SaveAuction> bins,
        SaveAuction directAuction, CancellationToken token)
    {
        if (!prices.IsLoaded)
            return (null, AcquisitionReasons.PricesNotLoaded);
        var options = new List<CheapestBase>();
        foreach (var candidate in BaseCandidatesOf(target, bins, directAuction))
        {
            token.ThrowIfCancellationRequested();
            options.Add(await planner.PlanAsync(candidate, NormalizeUuid(candidate.Uuid), candidate.StartingBid, target));
        }
        options.Add(await PlanCleanItem(target));
        var best = Cheapest(options);
        if (best != null)
            return (best, null);
        return (null, bins == null ? AcquisitionReasons.ListingsNotLoaded : AcquisitionReasons.NoBase);
    }

    /// <summary>The cheapest listings that can still reach the spec, plus the direct match</summary>
    private static List<SaveAuction> BaseCandidatesOf(GearTarget target, IReadOnlyList<SaveAuction> bins, SaveAuction directAuction)
    {
        var candidates = (bins ?? Array.Empty<SaveAuction>())
            .Where(a => NormalizeUuid(a.Uuid) != null && !GearSpecVerifier.HasHardDeficit(a, target))
            .Take(BaseCandidates).ToList();
        if (directAuction != null && !candidates.Contains(directAuction))
            candidates.Add(directAuction);
        return candidates;
    }

    /// <summary>A clean item at its clean price, with the base rarity of the tag (unknown rarity is not checked)</summary>
    private async Task<CheapestBase> PlanCleanItem(GearTarget target)
    {
        var clean = target.IsPet ? null : prices.CleanItemPrice(target.Tag);
        if (clean == null)
            return null;
        var rarity = await TryBaseRarity(target.Tag);
        var item = new SaveAuction { Tag = target.Tag, Tier = rarity ?? Tier.UNKNOWN, Enchantments = new(), FlatenedNBT = new() };
        return await planner.PlanAsync(item, null, clean.Value, target);
    }

    private async Task<Tier?> TryBaseRarity(string tag)
    {
        try
        {
            return await prices.BaseRarityAsync(tag);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not read the base rarity of {Tag}", tag);
            return null;
        }
    }

    private static DirectPurchase ToDirect(string uuid, long price) => new(uuid, price, "/viewauction " + uuid);

    private Func<SaveAuction, bool> CompileMatcher(Dictionary<string, string> filters, string tag)
    {
        try
        {
            return listings.Matcher(filters);
        }
        catch (Exception e)
        {
            // the verifier checks every modifier on its own, the filters only narrow
            logger.LogWarning(e, "Could not compile acquisition filters for {Tag}", tag);
            return null;
        }
    }

    private static bool Matches(Func<SaveAuction, bool> matcher, SaveAuction auction)
    {
        if (matcher == null)
            return true;
        try
        {
            return matcher(auction);
        }
        catch
        {
            return false; // one broken auction must not fail the lookup
        }
    }

    private Task<(DirectPurchase, string, SaveAuction)> Guard(Func<Task<(DirectPurchase, string, SaveAuction)>> find, string tag)
        => Guarded(find, reason => (null, reason, null), tag, "direct");

    private Task<(CheapestBase, string)> Guard(Func<Task<(CheapestBase, string)>> find, string tag)
        => Guarded(find, reason => (null, reason), tag, "base");

    private async Task<T> Guarded<T>(Func<Task<T>> find, Func<string, T> unavailable, string tag, string part)
    {
        try
        {
            return await find();
        }
        catch (OperationCanceledException)
        {
            return unavailable(AcquisitionReasons.Timeout);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not find the {Part} acquisition for {Tag}", part, tag);
            return unavailable(AcquisitionReasons.Failed);
        }
    }

    private IReadOnlyList<SaveAuction> Safe(Func<IReadOnlyList<SaveAuction>> load, string tag)
    {
        try
        {
            return load();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not read active listings for {Tag}", tag);
            return null;
        }
    }

    private void Store(string key, AcquisitionResult result)
    {
        if (cache.Count >= maxCacheEntries)
            PruneCache();
        var transient = AcquisitionReasons.Transient.Contains(result.DirectUnavailableReason ?? "")
            || AcquisitionReasons.Transient.Contains(result.BaseUnavailableReason ?? "");
        cache[key] = (time.GetUtcNow() + (transient ? TransientCacheDuration : CacheDuration), result);
    }

    /// <summary>Drops expired entries, then the tenth that expires soonest</summary>
    private void PruneCache()
    {
        var now = time.GetUtcNow();
        foreach (var (key, entry) in cache)
            if (entry.Expires <= now)
                cache.TryRemove(key, out _);
        var excess = cache.Count - maxCacheEntries + Math.Max(1, maxCacheEntries / 10);
        if (excess <= 0)
            return;
        foreach (var key in cache.OrderBy(e => e.Value.Expires).Take(excess).Select(e => e.Key).ToList())
            cache.TryRemove(key, out _);
    }

    /// <summary>Tasks a computation started and does not wait for when its deadline passes</summary>
    private sealed class PendingWork
    {
        private readonly List<Task> tasks = new();

        public Task<T> Track<T>(Task<T> task)
        {
            lock (tasks)
                tasks.Add(task);
            return task;
        }

        public async Task WhenAllEnded()
        {
            Task[] all;
            lock (tasks)
                all = tasks.ToArray();
            foreach (var task in all)
            {
                try
                {
                    await task;
                }
                catch (Exception)
                {
                    // ended is all that counts
                }
            }
        }
    }
}
