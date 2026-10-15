using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Core;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.PlayerState.Client.Api;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services.Mod;

/// <summary>One row of the "For your goal" bazaar panel.</summary>
public class GoalPanelEntry
{
    /// <summary>Display name of the item</summary>
    public string Name { get; set; }
    /// <summary>Amount to order</summary>
    public long Amount { get; set; }
    /// <summary>Command that sets the amount and opens the bazaar page</summary>
    public string Command { get; set; }
}

/// <summary>Data shown in the bazaar panel for the focused goal.</summary>
public class GoalPanelData
{
    /// <summary>Actionable bazaar steps of the focused goal</summary>
    public List<GoalPanelEntry> Entries { get; set; } = new();
    /// <summary>Display name of the first pending auction house item, set while an auction step is actionable (it is bought before the bazaar items)</summary>
    public string FirstAuctionName { get; set; }
}

/// <summary>Holds the live goal list of a user, disposable so the settings subscription can be released.</summary>
public sealed class GoalListHolder : IDisposable
{
    private readonly IDisposable inner;
    private readonly Func<GoalList> getter;
    /// <summary>The current list (kept up to date by the underlying subscription)</summary>
    public GoalList Value => getter() ?? new GoalList();

    /// <summary>Raised after the list was replaced by a change made outside this pod (cleared on dispose).</summary>
    public event Action Changed;

    /// <summary>Signals that the list changed, called by the subscription behind the holder.</summary>
    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>Creates a holder.</summary>
    public GoalListHolder(Func<GoalList> getter, IDisposable inner = null)
    {
        this.getter = getter;
        this.inner = inner;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Changed = null;
        inner?.Dispose();
    }
}

/// <summary>What the player should do next for the focused goal, read from the goal cache.</summary>
/// <param name="Step">The first actionable step</param>
/// <param name="Kind">Kind of the step</param>
/// <param name="ItemName">Display name of the item</param>
/// <param name="SearchName">Name to search for on the bazaar</param>
/// <param name="Quantity">Amount needed</param>
/// <param name="HasCookie">Whether the player has a booster cookie (bazaar from anywhere)</param>
/// <param name="Offer">Cached cheapest active auction of the item, if known</param>
public record GoalGuideContext(GoalStep Step, StepKind Kind, string ItemName, string SearchName, long Quantity, bool HasCookie, AuctionOffer Offer = null);

/// <summary>Source of the guide context for <see cref="Description.GoalGuideModifier"/>.</summary>
public interface IGoalGuideSource
{
    /// <summary>Loads the goals of the user so lookups by name work.</summary>
    Task EnsureLoaded(string userId, string mcName);
    /// <summary>First actionable step of the focused goal, null without goals. Cache only, never throws.</summary>
    GoalGuideContext GetGuideContext(string mcName);
    /// <summary>Marks a buy step as ordered (optimistic, the orders overview verifies later).</summary>
    Task MarkOrdered(string mcName, string stepId);
}

/// <summary>
/// Tracks the goals of mod users: evaluates them against the inventory, keeps the hud in the mod
/// up to date and offers the goal management used by the mod bridge endpoints.
/// </summary>
public class GoalService : IGoalGuideSource, IDisposable
{
    /// <summary>Settings key the goal list is stored under.</summary>
    public const string SettingsKey = "goals";
    private static readonly TimeSpan OfferCacheTime = TimeSpan.FromSeconds(20);

    private readonly SettingsService settings;
    private readonly IModUserPublisher publisher;
    private readonly IBoosterCookieService cookies;
    private readonly IPlayerStateApi stateApi;
    private readonly GoalPlanner planner;
    private readonly IServiceProvider services;
    private readonly ILogger<GoalService> logger;
    private readonly MemoryCache entries = new(new MemoryCacheOptions());
    private readonly ConcurrentDictionary<string, string> userByMcName = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (DateTime at, AuctionOffer offer)> offerCache = new();

    /// <summary>Overridable factory for the per user list (tests replace it).</summary>
    internal Func<string, Task<GoalListHolder>> HolderFactory;
    /// <summary>Overridable source of the tags traded on the bazaar (tests replace it).</summary>
    internal Func<ISet<string>> BazaarTagSource;
    /// <summary>Overridable lookup of the cheapest active BIN of an item, null when there is none (tests replace it).</summary>
    internal Func<string, Task<AuctionOffer>> LowestBinSource;
    /// <summary>Overridable clock (tests replace it).</summary>
    internal Func<DateTime> Now = () => DateTime.UtcNow;
    /// <summary>
    /// How long an unchanged hud is not pushed again. The mod keeps the hud only in memory and nothing tells the api
    /// when the game restarted, so the next inventory upload after this interval restores it.
    /// </summary>
    internal static readonly TimeSpan HudRefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>Creates a new instance.</summary>
    public GoalService(SettingsService settings, IModUserPublisher publisher, IBoosterCookieService cookies,
        IPlayerStateApi stateApi, GoalPlanner planner, IServiceProvider services, ILogger<GoalService> logger)
    {
        this.settings = settings;
        this.publisher = publisher;
        this.cookies = cookies;
        this.stateApi = stateApi;
        this.planner = planner;
        this.services = services;
        this.logger = logger;
        BazaarTagSource = () => Mod.DeserializedCache.BazaarItems.Keys.ToHashSet();
        LowestBinSource = LoadLowestBin;
        HolderFactory = async userId =>
        {
            var value = await SelfUpdatingValue<GoalList>.Create(userId, SettingsKey, () => new GoalList());
            var holder = new GoalListHolder(() => value.Value, value);
            value.AfterChange += _ => holder.RaiseChanged();
            return holder;
        };
    }

    private class Entry : IDisposable
    {
        public GoalListHolder Holder;
        public readonly SemaphoreSlim Gate = new(1, 1);
        public Dictionary<string, GoalProgress> Progress = new();
        public Dictionary<string, long> LastCounts = new();
        public ISet<string> LastForging = new HashSet<string>();
        public Dictionary<string, long> OpenBuyOrders = new();
        public string LastHash;
        public DateTime LastPush = DateTime.MinValue;
        public string McName;
        public bool HasCookie;
        public void Dispose() => Holder?.Dispose();
    }

    private ModDescriptionService Mod => services.GetRequiredService<ModDescriptionService>();

    private Func<string, string> NameLookup()
    {
        Dictionary<string, string> names = null;
        try { names = Mod.DeserializedCache.ItemTagToName; } catch (Exception) { /* not available */ }
        return tag => tag != null && names != null && names.TryGetValue(tag, out var n) ? n : tag;
    }

    private Task<Entry> GetEntry(string userId)
    {
        var lazy = entries.GetOrCreate(userId, e =>
        {
            e.SlidingExpiration = TimeSpan.FromMinutes(30);
            e.RegisterPostEvictionCallback(static (_, value, _, _) =>
            {
                if (value is Lazy<Task<Entry>> l && l.Value.IsCompletedSuccessfully)
                    l.Value.Result.Dispose();
            });
            return new Lazy<Task<Entry>>(async () =>
            {
                var entry = new Entry { Holder = await HolderFactory(userId) };
                entry.Holder.Changed += () => _ = OnListChanged(userId, entry);
                return entry;
            });
        });
        var task = lazy.Value;
        _ = task.ContinueWith(t => entries.Remove(userId), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        return task;
    }

    /// <summary>
    /// The list was changed outside this pod (for example a step completed from chat), shows it on the hud
    /// with the last known inventory. Never throws.
    /// </summary>
    private async Task OnListChanged(string userId, Entry e)
    {
        try
        {
            // a pod that never showed this hud has no inventory to judge the steps with, the next upload refreshes it
            if (e.LastHash == null)
                return;
            await e.Gate.WaitAsync();
            try
            {
                if (Evaluate(e, e.LastForging))
                    await Persist(userId, e);
                await RefreshHud(userId, e, false);
            }
            finally
            {
                e.Gate.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to refresh the goal hud of {UserId} after a change", userId);
        }
    }

    /// <summary>Extracts the item counts of the player inventory slots.</summary>
    public static Dictionary<string, long> ExtractCounts(IList<Item> items)
    {
        var counts = new Dictionary<string, long>();
        if (items == null)
            return counts;
        // mirrors ModDescriptionService: 46 or less is the player inventory screen, otherwise the last 36 are the player slots
        var start = items.Count <= 46 ? 9 : items.Count - 36;
        var end = items.Count <= 46 ? Math.Min(45, items.Count) : items.Count;
        for (int i = start; i < end; i++)
        {
            var item = items[i];
            if (item?.Tag == null || item.Tag == "SKYBLOCK_MENU")
                continue;
            counts[item.Tag] = counts.GetValueOrDefault(item.Tag) + item.Count;
        }
        return counts;
    }

    /// <summary>Registers the player name of a user and loads the entry, so panel lookups work.</summary>
    public async Task EnsureLoaded(string userId, string mcName)
    {
        var e = await GetEntry(userId);
        if (!string.IsNullOrEmpty(mcName))
        {
            e.McName = mcName;
            userByMcName[mcName] = userId;
        }
    }

    /// <summary>Evaluates inventory before guides are built; force restores unchanged HUD and expected-command state after reconnect. Never throws.</summary>
    public async Task OnInventory(string userId, string mcName, List<Item> items, bool force = false, DescriptionSetting description = null)
    {
        try
        {
            var e = await GetEntry(userId);
            if (!string.IsNullOrEmpty(mcName))
            {
                e.McName = mcName;
                userByMcName[mcName] = userId;
            }
            var list = e.Holder.Value;
            if (list?.Goals == null || list.Goals.Count == 0)
                return;
            var counts = ExtractCounts(items);
            var forging = list.Goals.Any(g => HasPendingForge(g.Root))
                ? await LoadForgingTags(mcName) : new HashSet<string>();
            await e.Gate.WaitAsync();
            try
            {
                e.LastCounts = counts;
                e.LastForging = forging;
                var changed = Evaluate(e, forging);
                if (changed)
                    await Persist(userId, e);
                await RefreshHud(userId, e, force, description);
            }
            finally
            {
                e.Gate.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update goals of {UserId}", userId);
        }
    }

    /// <summary>Stores the open bazaar orders and marks matching buy steps as ordered. Never throws.</summary>
    public async Task OnBazaarOrders(string userId, IEnumerable<(string tag, bool isBuy, long amount)> orders)
    {
        try
        {
            var e = await GetEntry(userId);
            var buys = new Dictionary<string, long>();
            foreach (var (tag, isBuy, amount) in orders ?? Enumerable.Empty<(string, bool, long)>())
                if (isBuy && tag != null)
                    buys[tag] = buys.GetValueOrDefault(tag) + amount;
            await e.Gate.WaitAsync();
            try
            {
                e.OpenBuyOrders = buys;
                var list = e.Holder.Value;
                if (list?.Goals == null || list.Goals.Count == 0)
                    return;
                var now = DateTime.UtcNow;
                var changed = false;
                foreach (var goal in list.Goals)
                    changed |= MarkOrdered(goal.Root, buys, now);
                Evaluate(e, e.LastForging);
                if (changed)
                    await settings.UpdateSetting(userId, SettingsKey, list);
                await RefreshHud(userId, e, false);
            }
            finally
            {
                e.Gate.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process bazaar orders of {UserId}", userId);
        }
    }

    private static bool MarkOrdered(GoalStep step, Dictionary<string, long> buys, DateTime now)
    {
        if (step == null)
            return false;
        var changed = false;
        if (step.Kind == StepKind.BuyOrder && step.DoneAt == null && step.OrderedAt == null && step.ItemTag != null && buys.ContainsKey(step.ItemTag))
        {
            step.OrderedAt = now;
            changed = true;
        }
        foreach (var child in step.Children)
            changed |= MarkOrdered(child, buys, now);
        return changed;
    }

    /// <summary>Re-evaluates all goals, returns true when steps were newly completed.</summary>
    private bool Evaluate(Entry e, ISet<string> forging)
    {
        var now = DateTime.UtcNow;
        var changed = false;
        var progress = new Dictionary<string, GoalProgress>();
        foreach (var goal in e.Holder.Value.Goals)
        {
            var p = GoalProgressEvaluator.Evaluate(goal, e.LastCounts, e.OpenBuyOrders, forging, now);
            if (GoalProgressEvaluator.MarkNewlyDone(goal, p, now).Count > 0)
                changed = true;
            progress[goal.Id] = p;
        }
        e.Progress = progress;
        return changed;
    }

    private static bool HasPendingForge(GoalStep step)
        => step != null && !step.DoneAt.HasValue
            && (step.Kind == StepKind.Forge || step.Children.Any(HasPendingForge));

    private async Task<ISet<string>> LoadForgingTags(string mcName)
    {
        var tags = new HashSet<string>();
        if (string.IsNullOrEmpty(mcName))
            return tags;
        try
        {
            var info = await stateApi.PlayerStatePlayerIdExtractedGetAsync(mcName);
            foreach (var forge in info?.ForgeItems ?? new())
                if (!string.IsNullOrEmpty(forge?.Tag))
                    tags.Add(forge.Tag);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load forge items of {Name}", mcName);
        }
        return tags;
    }

    // PricesService uses a scoped db context, so it is resolved in a fresh scope per lookup
    private async Task<AuctionOffer> LoadLowestBin(string tag)
    {
        try
        {
            using var scope = services.CreateScope();
            var (cost, uuid, _) = await scope.ServiceProvider.GetRequiredService<PricesService>().GetLowestBinData(tag);
            return uuid == null ? null : new AuctionOffer(uuid, cost);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load the lowest bin of {Tag} for goals", tag);
            return null;
        }
    }

    private async Task<Dictionary<string, AuctionOffer>> LoadOffers(IEnumerable<string> tags)
    {
        var result = new Dictionary<string, AuctionOffer>();
        foreach (var tag in tags.Where(t => t != null).Distinct())
        {
            if (!offerCache.TryGetValue(tag, out var cached) || Now() - cached.at >= OfferCacheTime)
            {
                cached = (Now(), await LowestBinSource(tag));
                offerCache[tag] = cached;
            }
            if (cached.offer != null)
                result[tag] = cached.offer;
        }
        return result;
    }

    private bool IsBazaarItem(string tag)
    {
        try { return tag != null && BazaarTagSource().Contains(tag); }
        catch (Exception) { return false; }
    }

    /// <summary>Builds the hud and pushes it when changed (or the last push is older than the refresh interval). Caller holds the gate.</summary>
    private async Task RefreshHud(string userId, Entry e, bool force, DescriptionSetting description = null)
    {
        var list = e.Holder.Value;
        var names = NameLookup();
        var focus = list.Goals.FirstOrDefault(g => g.Id == list.FocusId) ?? list.Goals.FirstOrDefault();
        var focusProgress = focus == null ? null : e.Progress.GetValueOrDefault(focus.Id);
        description ??= await settings.GetCurrentValue<DescriptionSetting>(userId, "description", () => DescriptionSetting.Default);
        e.HasCookie = BoosterCookieService.HasCookie(description, await cookies.Get(e.McName));
        var auctionTags = focusProgress?.Actionable.Where(s => s.Kind == StepKind.Auction).Select(s => s.ItemTag) ?? Enumerable.Empty<string>();
        var offers = await LoadOffers(auctionTags);
        var dto = GoalHud.BuildDisplay(list, e.Progress, e.HasCookie, names, tag => BazaarUtils.GetSearchValue(tag, names(tag)), offers, IsBazaarItem);
        var hash = GoalHud.Hash(dto);
        var hashChanged = hash != e.LastHash;
        var stale = Now() - e.LastPush > HudRefreshInterval;
        if (!force && !hashChanged && !stale)
            return;
        await publisher.DisplayForUser(userId, dto);
        e.LastHash = hash;
        e.LastPush = Now();
        if ((force || hashChanged) && !dto.Clear && focusProgress?.Actionable.Count > 0
            && dto.Lines.FirstOrDefault()?.OnClick is { Length: > 0 } command && command.StartsWith('/'))
            await publisher.ExpectForUser(userId, command, 120);
    }

    private async Task Persist(string userId, Entry e)
    {
        await settings.UpdateSetting(userId, SettingsKey, e.Holder.Value);
    }

    /// <summary>Gets the goal list of the user.</summary>
    public async Task<GoalList> Get(string userId) => (await GetEntry(userId)).Holder.Value;

    /// <summary>Gets the goal list together with the latest progress (evaluated with the last known inventory if needed).</summary>
    public async Task<(GoalList list, IReadOnlyDictionary<string, GoalProgress> progress)> GetWithProgress(string userId)
    {
        var e = await GetEntry(userId);
        await e.Gate.WaitAsync();
        try
        {
            var list = e.Holder.Value;
            if (list.Goals.Any(g => !e.Progress.ContainsKey(g.Id)))
                Evaluate(e, e.LastForging);
            return (list, new Dictionary<string, GoalProgress>(e.Progress));
        }
        finally
        {
            e.Gate.Release();
        }
    }

    /// <summary>Resolves an item name for display.</summary>
    public string GetName(string tag) => NameLookup()(tag);

    /// <summary>Creates a goal for the item.</summary>
    /// <exception cref="ArgumentException">When the planner rejects the item (e.g. not a forge item)</exception>
    public async Task<Goal> Add(string userId, StepKind rootKind, string tag, long quantity, bool sell, string source)
    {
        var e = await GetEntry(userId);
        ISet<string> bazaarTags;
        try { bazaarTags = BazaarTagSource(); }
        catch (Exception) { bazaarTags = new HashSet<string>(); }
        var goal = await planner.Build(rootKind, tag, quantity, sell, bazaarTags, NameLookup(), source);
        await e.Gate.WaitAsync();
        try
        {
            var list = e.Holder.Value;
            list.Goals.Add(goal);
            if (list.FocusId == null || list.Goals.All(g => g.Id != list.FocusId))
                list.FocusId = goal.Id;
            await Persist(userId, e);
            Evaluate(e, e.LastForging);
            await RefreshHud(userId, e, true);
        }
        finally
        {
            e.Gate.Release();
        }
        return goal;
    }

    /// <summary>Removes a goal, returns false when it does not exist.</summary>
    public async Task<bool> Remove(string userId, string id)
    {
        var e = await GetEntry(userId);
        await e.Gate.WaitAsync();
        try
        {
            var list = e.Holder.Value;
            if (list.Goals.RemoveAll(g => g.Id == id) == 0)
                return false;
            if (list.FocusId == id)
                list.FocusId = list.Goals.FirstOrDefault()?.Id;
            await Persist(userId, e);
            Evaluate(e, e.LastForging);
            if (list.Goals.Count == 0)
            {
                e.LastHash = null;
                await publisher.DisplayForUser(userId, new InfoDisplayDto { Id = 3, Clear = true });
            }
            else
                await RefreshHud(userId, e, true);
            return true;
        }
        finally
        {
            e.Gate.Release();
        }
    }

    /// <summary>Focuses a goal, returns false when it does not exist.</summary>
    public async Task<bool> Focus(string userId, string id)
    {
        var e = await GetEntry(userId);
        await e.Gate.WaitAsync();
        try
        {
            var list = e.Holder.Value;
            if (list.Goals.All(g => g.Id != id))
                return false;
            list.FocusId = id;
            await Persist(userId, e);
            await RefreshHud(userId, e, true);
            return true;
        }
        finally
        {
            e.Gate.Release();
        }
    }

    /// <summary>Resets the progress of a goal, returns false when it does not exist.</summary>
    public async Task<bool> Reset(string userId, string id)
    {
        var e = await GetEntry(userId);
        await e.Gate.WaitAsync();
        try
        {
            var goal = e.Holder.Value.Goals.FirstOrDefault(g => g.Id == id);
            if (goal == null)
                return false;
            if (goal.BazaarFlip != null && e.Holder.Value.Goals.Any(other => other.Id != goal.Id
                && other.BazaarFlip != null && other.RootItemTag == goal.RootItemTag
                && BazaarFlipProgress.Next(other) != BazaarFlipAction.Done))
                throw new CoflnetException("bazaar_goal_active", "Finish or remove the active Bazaar flip for this item before resetting this goal.");
            GoalProgressEvaluator.Reset(goal);
            await Persist(userId, e);
            Evaluate(e, e.LastForging);
            await RefreshHud(userId, e, true);
            return true;
        }
        finally
        {
            e.Gate.Release();
        }
    }

    /// <summary>Actionable bazaar steps of the focused goal of the player, for the bazaar panel.</summary>
    public GoalPanelData GetPanel(string mcName)
    {
        var data = new GoalPanelData();
        try
        {
            if (string.IsNullOrEmpty(mcName) || !userByMcName.TryGetValue(mcName, out var userId)
                || !entries.TryGetValue(userId, out Lazy<Task<Entry>> lazy) || !lazy.Value.IsCompletedSuccessfully)
                return data;
            var e = lazy.Value.Result;
            var list = e.Holder.Value;
            var focus = list?.Goals.FirstOrDefault(g => g.Id == list.FocusId) ?? list?.Goals.FirstOrDefault();
            if (focus == null || !e.Progress.TryGetValue(focus.Id, out var progress))
                return data;
            var names = NameLookup();
            var merged = GoalHud.MergeSameItem(progress.Actionable);
            if (merged.FirstOrDefault(s => s.Kind == StepKind.Auction) is { } auction)
                data.FirstAuctionName = names(auction.ItemTag) ?? auction.ItemTag;
            foreach (var step in merged.Where(s => s.Kind is StepKind.BuyOrder or StepKind.InstaBuy))
            {
                var name = names(step.ItemTag) ?? step.ItemTag;
                var search = BazaarUtils.GetSearchValue(step.ItemTag, name);
                data.Entries.Add(new GoalPanelEntry
                {
                    Name = name,
                    Amount = step.Quantity,
                    Command = e.HasCookie ? $"/cofl bzorder {step.Quantity} {search}" : $"/cofl bzorder search {step.Quantity} {search}"
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build goal panel for {Name}", mcName);
        }
        return data;
    }

    private Entry CachedEntry(string mcName)
        => !string.IsNullOrEmpty(mcName) && userByMcName.TryGetValue(mcName, out var userId)
            && entries.TryGetValue(userId, out Lazy<Task<Entry>> lazy) && lazy.Value.IsCompletedSuccessfully
            ? lazy.Value.Result : null;

    /// <inheritdoc/>
    public GoalGuideContext GetGuideContext(string mcName)
    {
        try
        {
            var e = CachedEntry(mcName);
            var list = e?.Holder.Value;
            var focus = list?.Goals.FirstOrDefault(g => g.Id == list.FocusId) ?? list?.Goals.FirstOrDefault();
            if (focus == null || !e.Progress.TryGetValue(focus.Id, out var progress))
                return null;
            var step = GoalHud.MergeSameItem(progress.Actionable).FirstOrDefault();
            if (step == null)
                return null;
            var name = NameLookup()(step.ItemTag) ?? step.ItemTag;
            // cache only, the hud refresh keeps it warm
            AuctionOffer offer = null;
            if (step.Kind == StepKind.Auction && step.ItemTag != null && offerCache.TryGetValue(step.ItemTag, out var cached))
                offer = cached.offer;
            return new GoalGuideContext(step, step.Kind, name, BazaarUtils.GetSearchValue(step.ItemTag, name), step.Quantity, e.HasCookie, offer);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to build goal guide for {Name}", mcName);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task MarkOrdered(string mcName, string stepId)
    {
        try
        {
            var e = CachedEntry(mcName);
            if (e == null || stepId == null || !userByMcName.TryGetValue(mcName, out var userId))
                return;
            await e.Gate.WaitAsync();
            try
            {
                var step = e.Holder.Value.Goals.Select(g => FindStep(g.Root, stepId)).FirstOrDefault(s => s != null);
                if (step == null || step.OrderedAt != null || step.DoneAt != null)
                    return;
                var now = DateTime.UtcNow;
                step.OrderedAt = now;
                // steps merged into the same hud line are ordered together
                var goal = e.Holder.Value.Goals.First(g => FindStep(g.Root, stepId) != null);
                if (e.Progress.TryGetValue(goal.Id, out var progress))
                    foreach (var other in progress.Actionable.Where(o => o != step && o.Kind == step.Kind && o.ItemTag == step.ItemTag
                        && o.OrderedAt == null && o.DoneAt == null))
                        other.OrderedAt = now;
                await Persist(userId, e);
                Evaluate(e, e.LastForging);
                await RefreshHud(userId, e, false);
            }
            finally
            {
                e.Gate.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to mark step {Step} ordered for {Name}", stepId, mcName);
        }
    }

    private static GoalStep FindStep(GoalStep step, string id)
    {
        if (step == null)
            return null;
        if (step.Id == id)
            return step;
        foreach (var child in step.Children)
            if (FindStep(child, id) is { } found)
                return found;
        return null;
    }

    /// <inheritdoc/>
    public void Dispose() => entries.Dispose();
}
