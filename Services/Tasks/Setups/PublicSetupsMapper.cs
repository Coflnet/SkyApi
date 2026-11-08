using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Api.Helper;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Services.Tasks.Setups;

/// <summary>An item whose price has to be looked up, the filters pick the variant (pet level, attributes, ...)</summary>
public record QuoteTarget(string Tag, IReadOnlyDictionary<string, string> Filters);

/// <summary>
/// Maps upstream gear setups to the public model and works out which items a player still has to buy.
/// Numbers are only copied when the caller says the setup is open, <see cref="TaskSetupsComposer"/> decides that.
/// </summary>
public static class PublicSetupsMapper
{
    public const string StatusBaseline = "baseline";
    public const string StatusUpgrade = "upgrade";
    public const string StatusReached = "reached";
    /// <summary>Tiers whose numbers everyone sees, the tier decides and not the status (a tier profile's budget may be "reached")</summary>
    public static readonly IReadOnlySet<string> FreeTiers =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "baseline", "budget" };

    public static bool IsFreeTier(GearSetupEstimate setup) => setup?.Tier != null && FreeTiers.Contains(setup.Tier);

    /// <summary>
    /// One estimated setup of <paramref name="upstream"/>, with numbers only when <paramref name="open"/>.
    /// Owned items are the estimated baseline's (and the highest reached setup's), as in the estimates only view.
    /// </summary>
    /// <param name="delta">Gain over the baseline shown and used for the payback, the modelled one unless a measured baseline replaced it</param>
    /// <param name="baselineKeys">Owned items of a measured baseline shown instead of the estimated one, see <see cref="OwnedKeys"/></param>
    public static PublicGearSetup MapEstimate(GearSetupEstimate source, TaskGearSetups upstream, IReadOnlyDictionary<string, string> taskSlugs,
        IReadOnlyDictionary<string, ItemResaleQuote> quotes, bool open, double? delta, IReadOnlySet<string> baselineKeys = null)
    {
        var target = MapPublic(source, taskSlugs);
        if (target != null)
            Open(target, source, upstream, quotes, open, delta, baselineKeys);
        return target;
    }

    private static void Open(PublicGearSetup target, GearSetupEstimate source, TaskGearSetups upstream,
        IReadOnlyDictionary<string, ItemResaleQuote> quotes, bool open, double? delta, IReadOnlySet<string> baselineKeys = null)
    {
        target.Locked = !open;
        if (target.Locked)
            return;
        if (ReferenceEquals(source, upstream.Baseline))
            AddRateNumbers(target, source, delta);
        else
            AddPremium(target, source, upstream, quotes, delta, baselineKeys);
    }

    public static string QuoteKey(string tag, IReadOnlyDictionary<string, string> filters)
        => SharedResultCache.Key("quote", filters, tag);

    /// <summary>The tradeable items the player would have to buy for the setups that get numbers, one entry per tag and variant</summary>
    public static List<QuoteTarget> QuoteTargets(TaskGearSetups upstream, bool all = true)
        => QuoteTargets(upstream, (upstream.Setups ?? new()).Where(s => all || IsFreeTier(s)));

    /// <summary>The tradeable items to buy for <paramref name="setups"/> (setups of <paramref name="upstream"/>), one entry per tag and variant</summary>
    /// <param name="baselineKeys">Owned items of the baseline shown, by <see cref="QuoteKey"/>; null for the estimated baseline</param>
    public static List<QuoteTarget> QuoteTargets(TaskGearSetups upstream, IEnumerable<GearSetupEstimate> setups,
        IReadOnlySet<string> baselineKeys = null)
    {
        return setups
            .Where(s => s != null && !ReferenceEquals(s, upstream.Baseline))
            .SelectMany(s => ItemsToBuy(upstream, s, baselineKeys))
            .Select(i => new QuoteTarget(i.ItemTag, i.PriceFilters))
            .GroupBy(t => QuoteKey(t.Tag, t.Filters))
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Items of a setup that cost money: tradeable ones the player does not own. The baseline gear is owned,
    /// on a tier profile (some setup is "reached") so is the gear of the highest reached tier.
    /// A reached setup is only measured against the baseline, it is informational.
    /// </summary>
    public static List<GearSetupItem> ItemsToBuy(TaskGearSetups upstream, GearSetupEstimate setup, IReadOnlySet<string> baselineKeys = null)
    {
        var owned = OwnedKeys(upstream, setup, baselineKeys);
        return (setup.Items ?? new()).Where(i => i.Tradeable && !owned.Contains(QuoteKey(i.ItemTag, i.PriceFilters))).ToList();
    }

    /// <summary>
    /// Owned items by <see cref="QuoteKey"/>, so a Mythic pet is not owned because the baseline has a Legendary one.
    /// The baseline is the one shown: <paramref name="baselineKeys"/> when a measured baseline replaced the estimated one.
    /// </summary>
    public static HashSet<string> OwnedKeys(TaskGearSetups upstream, GearSetupEstimate setup, IReadOnlySet<string> baselineKeys = null)
    {
        var owned = baselineKeys != null ? new HashSet<string>(baselineKeys) : KeysOf(upstream.Baseline);
        if (setup.Status == StatusReached)
            return owned;
        var highestReached = (upstream.Setups ?? new()).LastOrDefault(s => s.Status == StatusReached);
        if (highestReached != null)
            owned.UnionWith(KeysOf(highestReached));
        return owned;
    }

    private static HashSet<string> KeysOf(GearSetupEstimate setup)
        => (setup?.Items ?? new()).Select(i => QuoteKey(i.ItemTag, i.PriceFilters)).ToHashSet();

    private static PublicGearSetup MapPublic(GearSetupEstimate s, IReadOnlyDictionary<string, string> taskSlugs)
    {
        if (s == null)
            return null;
        return new PublicGearSetup
        {
            Id = s.Id,
            Name = s.Name,
            Tier = s.Tier,
            Status = s.Status,
            Basis = SetupBasis.Estimate,
            Confidence = s.Confidence,
            Assumptions = s.Assumptions ?? new(),
            Sources = s.Sources ?? new(),
            UnlocksTask = ResolveSlug(s.UnlocksTask, taskSlugs),
            Items = (s.Items ?? new()).Select(MapPublicItem).ToList(),
        };
    }

    private static string ResolveSlug(string taskName, IReadOnlyDictionary<string, string> taskSlugs)
    {
        if (taskName == null || taskSlugs == null)
            return null;
        return taskSlugs.TryGetValue(taskName, out var slug) ? slug : null;
    }

    private static PublicGearItem MapPublicItem(GearSetupItem i) => new()
    {
        Slot = i.Slot,
        ItemTag = i.ItemTag,
        ItemName = i.Name,
        Reason = i.Reason,
        Tradeable = i.Tradeable,
    };

    private static void AddRateNumbers(PublicGearSetup target, GearSetupEstimate source, double? delta)
    {
        if (target == null)
            return;
        target.CoinsPerHour = source.CoinsPerHour;
        target.DeltaCoinsPerHour = delta;
        target.Multiplier = source.Multiplier;
    }

    private static void AddPremium(PublicGearSetup target, GearSetupEstimate source, TaskGearSetups upstream,
        IReadOnlyDictionary<string, ItemResaleQuote> quotes, double? delta, IReadOnlySet<string> baselineKeys)
    {
        AddRateNumbers(target, source, delta);
        var owned = OwnedKeys(upstream, source, baselineKeys);
        var bought = BoughtItems(source, owned, quotes);
        // owned items are already left out, Aggregate's own tag based subtraction would drop variants
        var resale = ResaleMath.Aggregate(bought.Select(b => b.Quote), new HashSet<string>(), delta);
        AddItemNumbers(target, bought);
        AddCostNumbers(target, resale, delta, source.Status == StatusReached);
    }

    private static List<(int Index, ItemResaleQuote Quote)> BoughtItems(GearSetupEstimate setup, HashSet<string> owned,
        IReadOnlyDictionary<string, ItemResaleQuote> quotes)
    {
        var items = setup.Items ?? new();
        return Enumerable.Range(0, items.Count)
            .Where(i => items[i].Tradeable && !owned.Contains(QuoteKey(items[i].ItemTag, items[i].PriceFilters)))
            .Select(i => (i, QuoteFor(items[i], quotes)))
            .ToList();
    }

    private static ItemResaleQuote QuoteFor(GearSetupItem item, IReadOnlyDictionary<string, ItemResaleQuote> quotes)
    {
        if (quotes != null && quotes.TryGetValue(QuoteKey(item.ItemTag, item.PriceFilters), out var quote))
            return quote;
        return ItemResaleQuote.Unknown(item.ItemTag);
    }

    private static void AddItemNumbers(PublicGearSetup target, List<(int Index, ItemResaleQuote Quote)> bought)
    {
        foreach (var (index, q) in bought)
        {
            var item = target.Items[index];
            item.BuyPrice = q.Buy;
            item.ResaleValue = q.Resale;
            item.Stability = Label(q.Label);
            item.Cv = q.Cv;
            item.TrendPerWeek = q.TrendPerWeek;
            item.P10 = q.P10;
            item.VolumePerDay = q.VolumePerDay;
        }
    }

    /// <summary>Cost, resale and payback numbers of a setup; a reached setup is informational and has no payback</summary>
    public static void AddCostNumbers(PublicGearSetup target, SetupResale resale, double? deltaCoinsPerHour, bool isReached)
    {
        target.BuyCost = resale.BuyCost;
        target.ResaleValue = resale.Resale;
        target.BorrowCost = resale.BorrowCost;
        target.ResaleRatio = resale.ResaleRatio;
        target.Stability = Label(resale.Stability);
        target.PriceIncomplete = resale.PriceIncomplete;
        if (isReached)
            return;
        target.PaybackHoursBorrow = resale.PaybackBorrowHours;
        target.PaybackHoursFull = resale.PaybackFullHours;
        target.NetGain10h = resale.NetGainAfter10h;
        target.NetGain50h = resale.NetGainAfter50h;
        target.NoProfitGain = deltaCoinsPerHour is { } delta ? delta <= 0 : null;
    }

    public static string Label(StabilityLabel label) => label.ToString().ToLowerInvariant();
}
