using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Helper;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Api.Services.Tasks.Setups.Measured;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Caching.Memory;

namespace Coflnet.Sky.Api.Services.Tasks.Setups;

/// <summary>A setups response and whether a price was missing or degraded, such a response should only be cached briefly</summary>
public record SetupsView(PublicTaskSetups Setups, bool Degraded);

public interface ITaskSetupsService
{
    /// <summary>Setups for everyone: numbers for the free tiers only. Throws a <see cref="CoflnetException"/> with slug task_not_found or gear_setups_not_found</summary>
    Task<SetupsView> GetPublicAsync(string slug, CancellationToken cancellationToken = default);
    /// <summary>Setups with all numbers except the endgame tier (a placeholder), only call it after the premium check. Throws like <see cref="GetPublicAsync"/></summary>
    Task<SetupsView> GetPremiumAsync(string slug, CancellationToken cancellationToken = default);
    /// <summary>Setups with all numbers including the endgame tier, only call it after the premium plus check. Throws like <see cref="GetPublicAsync"/></summary>
    Task<SetupsView> GetPremiumPlusAsync(string slug, CancellationToken cancellationToken = default);
}

/// <summary>
/// Combines task slugs, the measured gear model, curated estimates and prices into the public and premium setups views.
/// The gear model comes from PlayerState's gear model endpoint; when that fails the curated estimates of the
/// gear setups list are served alone and the views are only cached briefly. Only the catalog pieces a tier can use are
/// priced: the recommended endgame's first, then (with a curve) the composable ones, at most <see cref="MaxPiecesPerSlot"/> tags per slot.
/// The shared model is served stale for up to <see cref="StaleModelDuration"/> while one caller's build refreshes it in
/// the background, so only the very first build of a slug makes callers wait.
/// </summary>
public class TaskSetupsService : ITaskSetupsService
{
    public static readonly TimeSpan PremiumCacheDuration = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DegradedCacheDuration = TimeSpan.FromSeconds(15);
    /// <summary>How long a model past its fresh time is still served while a refresh runs</summary>
    public static readonly TimeSpan StaleModelDuration = TimeSpan.FromMinutes(10);
    /// <summary>Distinct tags priced per slot (highest score contribution first), bounds the acquisitions of one build</summary>
    public const int MaxPiecesPerSlot = 6;
    private readonly IPublicTaskService taskService;
    private readonly IGearSetupService gearService;
    private readonly IGearModelService modelService;
    private readonly IMeasuredSetupPricer pricer;
    private readonly IItemResaleService resaleService;
    private readonly IMemoryCache cache;
    private readonly TimeProvider time;
    private readonly ConcurrentDictionary<string, Lazy<Task<object>>> builds = new();

    /// <summary>A model, until when it is fresh (then it is served stale while it is rebuilt) and when it must not be served any more</summary>
    private sealed record StampedModel(SetupsModel Model, DateTimeOffset FreshUntil, DateTimeOffset ExpiresAt);

    public TaskSetupsService(IPublicTaskService taskService, IGearSetupService gearService, IGearModelService modelService,
        IMeasuredSetupPricer pricer, IItemResaleService resaleService, IMemoryCache cache, TimeProvider time)
    {
        this.time = time ?? TimeProvider.System;
        this.taskService = taskService;
        this.gearService = gearService;
        this.modelService = modelService;
        this.pricer = pricer;
        this.resaleService = resaleService;
        this.cache = cache;
    }

    public static string PublicKey(string slug) => SharedResultCache.Key("task-setups-public", null, slug);
    public static string PremiumKey(string slug) => SharedResultCache.Key("task-setups-premium", null, slug);
    public static string PremiumPlusKey(string slug) => SharedResultCache.Key("task-setups-premium-plus", null, slug);
    /// <summary>The model holds the numbers of every tier, it is shared by both views and never returned itself</summary>
    public static string ModelKey(string slug) => SharedResultCache.Key("task-setups-model", null, slug);

    public Task<SetupsView> GetPublicAsync(string slug, CancellationToken cancellationToken = default)
        => GetCached(PublicKey(slug), () => BuildView(slug, SetupsAccess.Public), ViewLifetime, cancellationToken);

    public Task<SetupsView> GetPremiumAsync(string slug, CancellationToken cancellationToken = default)
        => GetCached(PremiumKey(slug), () => BuildView(slug, SetupsAccess.Premium), ViewLifetime, cancellationToken);

    public Task<SetupsView> GetPremiumPlusAsync(string slug, CancellationToken cancellationToken = default)
        => GetCached(PremiumPlusKey(slug), () => BuildView(slug, SetupsAccess.PremiumPlus), ViewLifetime, cancellationToken);

    private static TimeSpan ViewLifetime(SetupsView view) => view.Degraded ? DegradedCacheDuration : PremiumCacheDuration;

    private static TimeSpan FreshFor(SetupsModel model) => model.Degraded ? DegradedCacheDuration : PremiumCacheDuration;

    /// <summary>
    /// The cache holds community data only, so one entry per key is shared between all callers (the premium and premium plus views
    /// are only reached after their tier gate and have an entry each). A cold entry is built once per key, concurrent callers wait for that build.
    /// A degraded value is only kept for <see cref="DegradedCacheDuration"/>.
    /// </summary>
    private async Task<T> GetCached<T>(string key, Func<Task<T>> build, Func<T, TimeSpan> lifetime, CancellationToken cancellationToken) where T : class
    {
        if (cache != null && cache.TryGetValue(key, out T hit))
            return hit;
        return (T)await BuildOnce(key, async () => await build(), value => lifetime((T)value)).WaitAsync(cancellationToken);
    }

    /// <summary>Fresh model, else the stale one while a background build refreshes it, else the (shared) cold build</summary>
    private async Task<SetupsModel> GetModel(string slug)
    {
        var key = ModelKey(slug);
        if (cache == null || !cache.TryGetValue(key, out StampedModel hit))
            return (await RefreshModel(key, slug)).Model;
        if (hit.FreshUntil <= time.GetUtcNow())
            _ = RefreshModel(key, slug).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        return hit.Model;
    }

    private async Task<StampedModel> RefreshModel(string key, string slug)
    {
        var stamped = await BuildOnce(key, async () => Stamp(key, await BuildModel(slug)), value => Lifetime((StampedModel)value));
        return (StampedModel)stamped;
    }

    /// <summary>
    /// A degraded rebuild (eg. estimates only after a PlayerState blip) does not replace a good model that may still be
    /// served: the good one is kept and retried soon, the degraded one is only accepted once the good one has expired
    /// </summary>
    private StampedModel Stamp(string key, SetupsModel model)
    {
        var now = time.GetUtcNow();
        if (model.Degraded && cache != null && cache.TryGetValue(key, out StampedModel current)
            && !current.Model.Degraded && current.ExpiresAt > now)
            return current with { FreshUntil = now + DegradedCacheDuration };
        var freshUntil = now + FreshFor(model);
        return new StampedModel(model, freshUntil, freshUntil + StaleModelDuration);
    }

    private TimeSpan Lifetime(StampedModel stamped)
    {
        var left = stamped.ExpiresAt - time.GetUtcNow();
        return left > TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1);
    }

    private Task<object> BuildOnce(string key, Func<Task<object>> build, Func<object, TimeSpan> lifetime)
    {
        async Task<object> BuildAndRelease()
        {
            await Task.Yield();
            try
            {
                var value = await build();
                cache?.Set(key, value, lifetime(value));
                return value;
            }
            finally
            {
                builds.TryRemove(key, out _);
            }
        }
        return builds.GetOrAdd(key, _ => new Lazy<Task<object>>(BuildAndRelease)).Value;
    }

    /// <summary>Estimated setups are only quoted for the tiers the view shows numbers of</summary>
    private async Task<SetupsView> BuildView(string slug, SetupsAccess access)
    {
        // shared by all callers, so nothing below depends on one caller's cancellation
        var model = await GetModel(slug);
        if (model.Plan == null)
            throw new CoflnetException("gear_setups_not_found", $"No gear setups for task {slug}");
        var quoted = TaskSetupsComposer.QuotedEstimates(model.Plan, access);
        var targets = model.Estimates == null ? new List<QuoteTarget>() : PublicSetupsMapper.QuoteTargets(model.Estimates, quoted, null);
        var (quotes, lookupFailed) = await TryLoadQuotes(targets, CancellationToken.None);
        var view = TaskSetupsComposer.Compose(model, quotes, access);
        // a permanently missing price (item without a market) is not degraded, it keeps the normal cache time
        return new SetupsView(view, model.Degraded || lookupFailed || quotes.Values.Any(q => q.Degraded));
    }

    /// <summary>A task without any setup gets a model without a plan, so repeated requests don't ask PlayerState again</summary>
    private async Task<SetupsModel> BuildModel(string slug)
    {
        var (task, slugs) = await LoadTask(slug, CancellationToken.None);
        var lookup = await modelService.GetAsync(task.Name, CancellationToken.None);
        var (estimates, estimatesFailed) = await LoadEstimates(task.Name, lookup);
        // not derived yet (right after a PlayerState restart) is no failure: the estimates keep the normal cache time
        var degraded = lookup.Failed || estimatesFailed;
        var gear = lookup.Model;
        var pieces = PricedCatalog(gear);
        if (pieces.Count == 0 && estimates == null)
            return new SetupsModel(slug, task.Name, null, null, null, null, [], slugs, degraded);
        var priced = await pricer.PriceAsync(pieces, UntradeableTags(estimates), CancellationToken.None);
        var community = SetupTierSelector.CommunityRate(estimates, task.CoinsPerHour);
        var plan = SetupTierSelector.Select(new PricedGearModel(gear, priced.Pieces), estimates, community, TierBands.Default);
        return new SetupsModel(slug, task.Name, gear?.Category, gear?.ComputedAt, estimates, plan, gear?.ModifierEffects ?? new(),
            slugs, degraded || priced.Degraded, community, gear?.ScoreFormula);
    }

    /// <summary>
    /// The catalog pieces a tier can use, in pricing order: the recommended endgame's first, then with a curve the best
    /// <see cref="MaxPiecesPerSlot"/> tags per slot (the best variant of each tag) to compose budget and mid from.
    /// Nothing is priced for a model that can't produce a measured tier.
    /// </summary>
    public static List<MeasuredPiece> PricedCatalog(TaskGearModel gear)
    {
        var catalog = (gear?.Pieces ?? new()).Select(MeasuredPiece.Of).Where(p => p != null).ToList();
        var endgameIds = (gear?.RecommendedEndgame?.Pieces?.Values ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrEmpty(id)).ToHashSet();
        var endgame = catalog.Where(p => endgameIds.Contains(p.Id));
        var composable = gear?.Curve != null && BuildComposer.CanCompose(gear.ScoreFormula);
        var top = composable ? TopPerSlot(catalog) : [];
        return endgame.Concat(top).DistinctBy(p => p.Id).ToList();
    }

    private static IEnumerable<MeasuredPiece> TopPerSlot(List<MeasuredPiece> catalog)
        => catalog.GroupBy(p => p.Slot).SelectMany(slot => slot
            .GroupBy(p => p.Tag).Select(BestVariant)
            .OrderByDescending(p => p.Catalog.ScoreContribution).Take(MaxPiecesPerSlot));

    private static MeasuredPiece BestVariant(IEnumerable<MeasuredPiece> variants)
        => variants.OrderByDescending(p => p.Catalog.ScoreContribution).ThenByDescending(p => p.Catalog.Players).First();

    /// <summary>Positive evidence only: tags the curated profile marks as untradeable (soulbound)</summary>
    private static HashSet<string> UntradeableTags(TaskGearSetups estimates)
        => (estimates?.Setups ?? new()).Append(estimates?.Baseline)
            .SelectMany(s => s?.Items ?? new())
            .Where(i => i != null && !i.Tradeable && !string.IsNullOrEmpty(i.ItemTag))
            .Select(i => i.ItemTag.ToUpperInvariant())
            .ToHashSet();

    /// <summary>
    /// The gear model carries the estimates. Without a model (failed or unknown task) the estimates come from
    /// the gear setups list as before, its outage is a 503. A model without estimates may mean the estimate failed
    /// upstream, the list is asked then too, but its outage only degrades the view.
    /// </summary>
    private async Task<(TaskGearSetups Estimates, bool Failed)> LoadEstimates(string taskName, GearModelLookup lookup)
    {
        if (lookup.Model?.Estimates != null)
            return (lookup.Model.Estimates, false);
        if (lookup.Model == null)
            return (await gearService.GetByTaskNameAsync(taskName, CancellationToken.None), false);
        try
        {
            return (await gearService.GetByTaskNameAsync(taskName, CancellationToken.None), false);
        }
        catch (CoflnetException)
        {
            return (null, true);
        }
    }

    /// <summary>A failing price lookup must not fail the response, the affected setups just have no cost numbers</summary>
    private async Task<(Dictionary<string, ItemResaleQuote> Quotes, bool Failed)> TryLoadQuotes(List<QuoteTarget> targets, CancellationToken cancellationToken)
    {
        try
        {
            return (await LoadQuotes(targets, cancellationToken), false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (new Dictionary<string, ItemResaleQuote>(), true);
        }
    }

    private async Task<Dictionary<string, ItemResaleQuote>> LoadQuotes(List<QuoteTarget> targets, CancellationToken cancellationToken)
    {
        var quotes = await Task.WhenAll(targets.Select(t => resaleService.GetQuoteAsync(t.Tag, t.Filters, cancellationToken)));
        return targets.Zip(quotes, (t, q) => (Key: PublicSetupsMapper.QuoteKey(t.Tag, t.Filters), Quote: q))
            .ToDictionary(p => p.Key, p => p.Quote);
    }

    private async Task<(PublicTaskEstimate Task, IReadOnlyDictionary<string, string> Slugs)> LoadTask(string slug, CancellationToken cancellationToken)
    {
        var tasks = await taskService.GetAllAsync(cancellationToken);
        var task = tasks.FirstOrDefault(t => t.Slug == slug)
            ?? throw new CoflnetException("task_not_found", $"No task with slug {slug}");
        return (task, tasks.Where(t => t.Name != null).GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First().Slug));
    }
}
