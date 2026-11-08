using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Measured;

/// <summary>
/// Cost bands of the measured tiers, by what every item of the setup costs together.
/// Budget is anything up to <see cref="BudgetMaxCost"/>, mid up to <see cref="MidMaxCost"/>; when nothing fits the mid
/// band a pricier frontier setup is taken unless it is the one the endgame would be. Endgame costs more than the budget band.
/// A measured baseline costs at most <see cref="BaselineMaxCost"/>, an expensive cluster never becomes the free baseline.
/// </summary>
public record TierBands(double BaselineMaxCost, double BudgetMaxCost, double MidMaxCost)
{
    public static readonly TierBands Default = new(10_000_000, 10_000_000, 100_000_000);
}

/// <summary>One tier of the plan, either a measured cluster or an estimate</summary>
/// <param name="DeltaCoinsPerHour">Gain over the baseline, see <see cref="SetupTierSelector"/> for the rules</param>
/// <param name="Rate">Coins per hour of the tier, measured or modelled</param>
public record TierChoice(string Tier, PricedCluster Measured, GearSetupEstimate Estimate, double? DeltaCoinsPerHour, double? Rate = null)
{
    public bool IsMeasured => Measured != null;
}

/// <param name="Baseline">Null when there is neither a measured nor an estimated baseline</param>
/// <param name="BaselineCoinsPerHour">Rate shown for the baseline: the measured baseline's, the estimated baseline's, else the community rate</param>
/// <param name="OwnedSpecKeys">Items of a measured baseline, they are owned and left out of the measured tiers' costs</param>
/// <param name="ComparisonCoinsPerHour">Rate the measured tiers' gain is relative to: the measured baseline's, else the community rate</param>
public record TierPlan(TierChoice Baseline, IReadOnlyList<TierChoice> Setups, double? BaselineCoinsPerHour,
    IReadOnlySet<string> OwnedSpecKeys, double? ComparisonCoinsPerHour = null);

/// <summary>
/// Picks one setup per tier in the order baseline, budget, mid, endgame. Measured clusters come first, a tier without one
/// falls back to the curated estimate. Every tier has to earn more than the measured tiers below it.
/// <list type="bullet">
/// <item>Clusters with an item whose price lookup failed sit the build out, an unknown price must not decide a tier.</item>
/// <item>baseline: the cheapest fully priced cluster within <see cref="TierBands.BaselineMaxCost"/>, else the estimated
/// baseline, else none (the community rate is the baseline rate then).</item>
/// <item>budget and mid: fully priced clusters (no lower bound price) on the (cost, rate) Pareto frontier in the cost
/// band, the best gain per coin spent on items the baseline lacks. A cluster that costs nothing extra is no upgrade.</item>
/// <item>endgame: the highest rate among frontier clusters and clusters without a reliable price (unknown or lower bound)
/// that cost more than the budget band.</item>
/// </list>
/// Nesting: a parent cluster contains its children's players. A parent is dropped for a child it does not dominate, and a
/// cluster related to an already picked one is not picked again, so the same players are never shown twice.
/// Gain rules: a measured tier's gain is its rate minus <see cref="TierPlan.ComparisonCoinsPerHour"/>, the measured
/// baseline's rate, else the community rate: the modelled baseline rate never rules out a measured cluster.
/// With a measured tier below, an estimate is only shown when its modelled rate beats every measured tier below it, and
/// with a measured baseline its gain is its modelled rate minus the measured baseline rate. Without a measured tier below,
/// estimates keep their curated order and modelled gain.
/// </summary>
public static class SetupTierSelector
{
    public const string Baseline = "baseline";
    public const string Budget = "budget";
    public const string Mid = "mid";
    public const string Endgame = "endgame";
    public static readonly IReadOnlyList<string> UpgradeTiers = [Budget, Mid, Endgame];

    /// <param name="communityRate">The task's community rate, used when the estimates have none</param>
    public static TierPlan Select(IReadOnlyList<PricedCluster> clusters, TaskGearSetups estimates, double? communityRate = null, TierBands bands = null)
    {
        bands ??= TierBands.Default;
        var usable = (clusters ?? []).Where(c => c != null && !c.TransientlyUnpriced).ToList();
        var community = CommunityRate(estimates, communityRate);
        var baseline = PickBaseline(usable, bands);
        var selection = new Selection(usable, baseline, baseline?.Rate ?? community, bands);
        var setups = UpgradeTiers.SelectMany(tier => selection.Choose(tier, estimates)).ToList();
        setups.AddRange(OtherTierEstimates(estimates).Select(s => EstimateChoice(s.Tier, s, s.DeltaCoinsPerHour)));
        var baselineChoice = baseline != null
            ? new TierChoice(Baseline, baseline, null, 0, baseline.Rate)
            : EstimateChoice(Baseline, estimates?.Baseline, estimates?.Baseline?.DeltaCoinsPerHour);
        var shownRate = baseline?.Rate ?? estimates?.BaselineCoinsPerHour ?? community;
        return new TierPlan(baselineChoice, setups, shownRate, selection.Owned, baseline?.Rate ?? community);
    }

    /// <summary>The estimates' community rate, else the task's</summary>
    public static double? CommunityRate(TaskGearSetups estimates, double? taskRate)
    {
        if (estimates?.CommunityCoinsPerHour is > 0 and var community)
            return community;
        return taskRate is > 0 ? taskRate : null;
    }

    /// <summary>The band is applied first, so a parent is not dropped for a child that is too expensive to be the baseline</summary>
    private static PricedCluster PickBaseline(IReadOnlyList<PricedCluster> clusters, TierBands bands)
    {
        var inBand = clusters.Where(c => c.FullCost <= bands.BaselineMaxCost && !c.LowerBound).ToList();
        return ClusterKeys.Finest(inBand).OrderBy(c => c.FullCost).ThenByDescending(c => c.Cluster.Players).FirstOrDefault();
    }

    private static IEnumerable<GearSetupEstimate> OtherTierEstimates(TaskGearSetups estimates)
        => (estimates?.Setups ?? new()).Where(s => s != null && !UpgradeTiers.Any(t => IsTier(s, t)));

    private static bool IsTier(GearSetupEstimate setup, string tier)
        => string.Equals(setup?.Tier, tier, StringComparison.OrdinalIgnoreCase);

    private static TierChoice EstimateChoice(string tier, GearSetupEstimate estimate, double? delta)
        => estimate == null ? null : new TierChoice(tier, null, estimate, delta, estimate.CoinsPerHour);

    /// <summary>
    /// Fully priced clusters no other one beats on both cost and rate. A parent contains its children's players,
    /// so relatives don't rule each other out here, <see cref="ClusterKeys.Finest"/> settles them.
    /// </summary>
    public static List<PricedCluster> ParetoFrontier(IReadOnlyList<PricedCluster> clusters)
    {
        var priced = clusters.Where(c => c.FullCost != null && !c.LowerBound).ToList();
        return priced.Where(c => !priced.Any(other => !ClusterKeys.AreRelated(other, c) && ClusterKeys.Dominates(other, c))).ToList();
    }

    /// <summary>State of one selection: what is taken and the rate the next measured tier has to beat</summary>
    private sealed class Selection
    {
        private readonly IReadOnlyList<PricedCluster> clusters;
        private readonly PricedCluster baseline;
        private readonly double? comparisonRate;
        private readonly TierBands bands;
        private readonly List<PricedCluster> taken = new();
        private readonly List<PricedCluster> frontier;
        private double? floor;

        public Selection(IReadOnlyList<PricedCluster> clusters, PricedCluster baseline, double? comparisonRate, TierBands bands)
        {
            this.clusters = clusters;
            this.baseline = baseline;
            this.comparisonRate = comparisonRate;
            this.bands = bands;
            floor = comparisonRate;
            frontier = ParetoFrontier(clusters);
            Owned = baseline?.SpecKeys() ?? new HashSet<string>();
            if (baseline != null)
                taken.Add(baseline);
        }

        public IReadOnlySet<string> Owned { get; }

        /// <summary>The measured cluster of the tier, else the estimates of the tier that still earn more than the measured tiers below</summary>
        public IEnumerable<TierChoice> Choose(string tier, TaskGearSetups estimates)
        {
            var cluster = Pick(tier);
            if (cluster != null)
                return [Take(tier, cluster)];
            return (estimates?.Setups ?? new()).Where(s => IsTier(s, tier) && EstimateBeatsMeasured(s))
                .Select(s => EstimateChoice(tier, s, EstimateDelta(s))).ToList();
        }

        private PricedCluster Pick(string tier) => tier switch
        {
            Budget => BestValue(Banded(c => c.FullCost <= bands.BudgetMaxCost)),
            Mid => BestValue(Banded(c => c.FullCost > bands.BudgetMaxCost && c.FullCost <= bands.MidMaxCost))
                ?? BestValue(Banded(c => c.FullCost > bands.MidMaxCost).Where(NotReservedForEndgame)),
            _ => BestEndgame(),
        };

        private TierChoice Take(string tier, PricedCluster cluster)
        {
            taken.Add(cluster);
            floor = cluster.Rate;
            return new TierChoice(tier, cluster, null, cluster.Rate - comparisonRate, cluster.Rate);
        }

        /// <summary>Not related to a picked cluster and earning more than the measured tiers below</summary>
        private bool Open(PricedCluster c) => !taken.Any(t => ClusterKeys.AreRelated(t, c)) && (floor == null || c.Rate > floor);

        /// <summary>Fully priced frontier clusters in a band; a cluster costing nothing extra is no upgrade to buy</summary>
        private IEnumerable<PricedCluster> Banded(Func<PricedCluster, bool> band)
            => frontier.Where(c => Open(c) && band(c) && c.CostWithout(Owned) is > 0);

        private PricedCluster BestValue(IEnumerable<PricedCluster> candidates)
            => ClusterKeys.Finest(candidates.ToList())
                .OrderByDescending(Score).ThenByDescending(c => c.Rate).ThenBy(c => c.FullCost).FirstOrDefault();

        /// <summary>Gain per coin spent; <see cref="Banded"/> guarantees a positive cost</summary>
        private double Score(PricedCluster c) => (c.Rate - (comparisonRate ?? 0)) / c.CostWithout(Owned)!.Value;

        private IEnumerable<PricedCluster> EndgameCandidates()
            => clusters.Where(c => Open(c) && AboveBudget(c) && (frontier.Contains(c) || c.FullCost == null || c.LowerBound));

        private PricedCluster BestEndgame()
            => ClusterKeys.Finest(EndgameCandidates().ToList())
                .OrderByDescending(c => c.Rate).ThenByDescending(c => c.Cluster.Players).FirstOrDefault();

        private bool NotReservedForEndgame(PricedCluster c)
        {
            var endgame = BestEndgame();
            return endgame == null || !ClusterKeys.AreRelated(endgame, c);
        }

        private bool AboveBudget(PricedCluster c) => c.FullCost is not { } cost || c.LowerBound || cost > bands.BudgetMaxCost;

        private bool EstimateBeatsMeasured(GearSetupEstimate estimate)
        {
            if (taken.Count == 0)
                return true;
            return estimate.CoinsPerHour is { } rate && rate > taken.Max(t => t.Rate);
        }

        private double? EstimateDelta(GearSetupEstimate estimate)
            => baseline == null ? estimate.DeltaCoinsPerHour : estimate.CoinsPerHour - baseline.Rate;
    }
}
