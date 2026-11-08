using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Measured;

/// <summary>Cost bands of the composed tiers, by what every item of the build costs together</summary>
public record TierBands(double BudgetMaxCost, double MidMaxCost)
{
    public static readonly TierBands Default = new(10_000_000, 100_000_000);
}

/// <summary>One tier of the plan, either a measured build or an estimate</summary>
/// <param name="DeltaCoinsPerHour">Gain over the baseline, see <see cref="SetupTierSelector"/> for the rules</param>
/// <param name="Rate">Coins per hour of the tier, from the gear model or modelled</param>
public record TierChoice(string Tier, PricedBuild Measured, GearSetupEstimate Estimate, double? DeltaCoinsPerHour, double? Rate = null)
{
    public bool IsMeasured => Measured != null;
}

/// <param name="Baseline">The estimated baseline, null when there is none</param>
/// <param name="BaselineCoinsPerHour">Rate shown for the baseline: the estimated baseline's, else the community rate</param>
/// <param name="ComparisonCoinsPerHour">Rate the measured tiers' gain is relative to: the community rate</param>
public record TierPlan(TierChoice Baseline, IReadOnlyList<TierChoice> Setups, double? BaselineCoinsPerHour, double? ComparisonCoinsPerHour = null);

/// <summary>The task's gear model with its priced catalog pieces (by catalog id)</summary>
public record PricedGearModel(TaskGearModel Model, IReadOnlyDictionary<string, PricedPiece> Pieces)
{
    public static readonly PricedGearModel None = new(null, new Dictionary<string, PricedPiece>());
}

/// <summary>
/// Picks one setup per tier in the order baseline, budget, mid, endgame from the gear score model; a tier without a
/// measured build falls back to the curated estimate. Every measured tier has to earn more than the measured tiers below
/// it (the first one more than the community rate), else it is dropped.
/// <list type="bullet">
/// <item>baseline: the estimated baseline, else none (the community rate is the baseline rate then).</item>
/// <item>budget: the build <see cref="BuildComposer.ComposeTier"/> composes from the exactly priced catalog within
/// <see cref="TierBands.BudgetMaxCost"/> (always with a tool, empty armor slots filled; none without a tool that fits,
/// none below the 10th score percentile); rate = the curve at its score (<see cref="GearCurveReader"/>). No curve (or a
/// formula whose contributions don't add up) means no composed tiers.</item>
/// <item>mid: the greedy continued from the budget build (or from the best tool when there is no budget build) up to <see cref="TierBands.MidMaxCost"/>; it must cost more than
/// the budget band.</item>
/// <item>budget and mid stay below the late game cohort's score (else the 90th score percentile), and one that earns at
/// least the endgame's rate is not shown: the endgame tier is never replaced or outdone by a composed build.</item>
/// <item>endgame: PlayerState's recommended "maximum achievable" build with its rate (curve or late game cohort), the
/// pieces priced as they are (lower bound or unknown prices allowed). A build with a transient price sits this build out.
/// A composed build with exactly the endgame's pieces is not shown as budget or mid, the endgame tier is that build.</item>
/// </list>
/// Gain rules: a measured tier's gain is its rate minus <see cref="TierPlan.ComparisonCoinsPerHour"/>, the community rate.
/// With a measured tier below, an estimate is only shown when its modelled rate beats every measured tier below it;
/// otherwise estimates keep their curated order and modelled gain.
/// </summary>
public static class SetupTierSelector
{
    public const string Baseline = "baseline";
    public const string Budget = "budget";
    public const string Mid = "mid";
    public const string Endgame = "endgame";
    public const string LowConfidence = "low";
    public const string MediumConfidence = "medium";
    /// <summary>Curve players from which a composed build that stays in the observed range is medium confidence</summary>
    public const int MediumFromPlayers = 5;
    public static readonly IReadOnlyList<string> UpgradeTiers = [Budget, Mid, Endgame];

    /// <param name="communityRate">The task's community rate, used when the estimates have none</param>
    public static TierPlan Select(PricedGearModel gear, TaskGearSetups estimates, double? communityRate = null, TierBands bands = null)
    {
        bands ??= TierBands.Default;
        var community = CommunityRate(estimates, communityRate);
        var builds = MeasuredBuilds(gear ?? PricedGearModel.None, bands);
        var selection = new Selection(community);
        var setups = UpgradeTiers.SelectMany(tier => selection.Choose(tier, builds.GetValueOrDefault(tier), estimates)).ToList();
        setups.AddRange(OtherTierEstimates(estimates).Select(s => EstimateChoice(s.Tier, s, s.DeltaCoinsPerHour)));
        var baseline = EstimateChoice(Baseline, estimates?.Baseline, estimates?.Baseline?.DeltaCoinsPerHour);
        return new TierPlan(baseline, setups, estimates?.BaselineCoinsPerHour ?? community, community);
    }

    /// <summary>The estimates' community rate, else the task's</summary>
    public static double? CommunityRate(TaskGearSetups estimates, double? taskRate)
    {
        if (estimates?.CommunityCoinsPerHour is > 0 and var community)
            return community;
        return taskRate is > 0 ? taskRate : null;
    }

    /// <summary>The measured candidate of each tier before the rate rules, a tier without one is missing</summary>
    public static Dictionary<string, PricedBuild> MeasuredBuilds(PricedGearModel gear, TierBands bands)
    {
        var builds = new Dictionary<string, PricedBuild>();
        var endgame = EndgameBuild(gear);
        if (endgame != null)
            builds[Endgame] = endgame;
        var model = gear.Model;
        if (model?.Curve == null || !BuildComposer.CanCompose(model.ScoreFormula))
            return builds;
        var catalog = BuildComposer.Catalog(gear.Pieces.Values, model.ScoreFormula);
        var ceiling = ScoreCeiling(model);
        var budget = BuildComposer.ComposeTier(catalog, bands.BudgetMaxCost, null, ceiling);
        var mid = BuildComposer.ComposeTier(catalog, bands.MidMaxCost, budget, ceiling);
        AddComposed(builds, Budget, budget, model, endgame, cost => cost > 0);
        AddComposed(builds, Mid, mid, model, endgame, cost => cost > bands.BudgetMaxCost);
        return builds;
    }

    private static void AddComposed(Dictionary<string, PricedBuild> builds, string tier, ComposedBuild composed, TaskGearModel model,
        PricedBuild endgame, Func<double, bool> inBand)
    {
        if (composed == null || composed.Units.Count == 0 || !inBand(composed.Cost) || BelowP10(composed, model))
            return;
        var build = Priced(composed, model);
        if (build == null || build.SameBuildAs(endgame))
            return;
        if (endgame != null && build.Rate >= endgame.Rate)
            return;
        builds[tier] = build;
    }

    /// <summary>A build below what 90 % of the players wear is no upgrade to show</summary>
    private static bool BelowP10(ComposedBuild composed, TaskGearModel model)
        => model.Distribution != null && composed.Score < model.Distribution.ScoreP10;

    /// <summary>Composed builds stay below the late game cohort's score, else below the 90th score percentile; null without either</summary>
    public static double? ScoreCeiling(TaskGearModel model)
        => model.LateGame?.ScoreFrom ?? model.Distribution?.ScoreP90;

    /// <summary>A composed build with the curve's rate at its score, null when the curve has no point</summary>
    public static PricedBuild Priced(ComposedBuild composed, TaskGearModel model)
    {
        var rate = GearCurveReader.RateAt(model.Curve, composed.Score);
        if (rate == null)
            return null;
        var confidence = rate.Clamped || model.Curve.Players < MediumFromPlayers ? LowConfidence : MediumConfidence;
        return new PricedBuild(InSlotOrder(composed.Pieces), rate.CoinsPerHour, Math.Round(composed.Score, 1),
            GearCurveReader.PercentileOf(model.Distribution, model.Curve, composed.Score), RateBases.Curve, rate.Clamped, confidence);
    }

    /// <summary>The recommended endgame with its priced pieces, null without one or when one of its pieces is missing</summary>
    public static PricedBuild EndgameBuild(PricedGearModel gear)
    {
        var recommended = gear.Model?.RecommendedEndgame;
        var ids = (recommended?.Pieces?.Values ?? Enumerable.Empty<string>()).Where(id => !string.IsNullOrEmpty(id)).ToList();
        if (ids.Count == 0 || recommended.CoinsPerHour <= 0)
            return null;
        var pieces = ids.Select(id => gear.Pieces.GetValueOrDefault(id)).ToList();
        if (pieces.Any(p => p == null))
            return null;
        var basis = recommended.Basis == RateBases.Cohort ? RateBases.Cohort : RateBases.Curve;
        return new PricedBuild(InSlotOrder(pieces), recommended.CoinsPerHour, recommended.Score, recommended.ScorePercentile, basis,
            recommended.Extrapolated, EndgameConfidence(gear.Model, recommended), recommended.Capped, gear.Model.LateGame);
    }

    private static List<PricedPiece> InSlotOrder(IEnumerable<PricedPiece> pieces)
        => pieces.OrderBy(p => MeasuredPiece.SlotOrder(p.Piece.Slot)).ThenBy(p => p.Piece.Slot, StringComparer.Ordinal).ToList();

    /// <summary>The late game cohort's confidence (low below 5 players), low for an extrapolated or capped rate</summary>
    private static string EndgameConfidence(TaskGearModel model, GearRecommendedBuild recommended)
    {
        var cohort = model.LateGame;
        if (recommended.Extrapolated || recommended.Capped || cohort == null)
            return LowConfidence;
        return MeasuredSetupMapper.ConfidenceOf(cohort.Confidence, cohort.Players, cohort.Rounded);
    }

    private static IEnumerable<GearSetupEstimate> OtherTierEstimates(TaskGearSetups estimates)
        => (estimates?.Setups ?? new()).Where(s => s != null && !UpgradeTiers.Any(t => IsTier(s, t)));

    private static bool IsTier(GearSetupEstimate setup, string tier)
        => string.Equals(setup?.Tier, tier, StringComparison.OrdinalIgnoreCase);

    private static TierChoice EstimateChoice(string tier, GearSetupEstimate estimate, double? delta)
        => estimate == null ? null : new TierChoice(tier, null, estimate, delta, estimate.CoinsPerHour);

    /// <summary>State of one selection: the measured tiers taken and the rate the next one has to beat</summary>
    private sealed class Selection(double? comparisonRate)
    {
        private readonly List<PricedBuild> taken = new();

        /// <summary>The measured build of the tier when it beats the tiers below, else the tier's estimates that do</summary>
        public IEnumerable<TierChoice> Choose(string tier, PricedBuild build, TaskGearSetups estimates)
        {
            if (build != null && !build.TransientlyUnpriced && BeatsMeasured(build.Rate))
                return [Take(tier, build)];
            return (estimates?.Setups ?? new()).Where(s => IsTier(s, tier) && EstimateBeatsMeasured(s))
                .Select(s => EstimateChoice(tier, s, s.DeltaCoinsPerHour)).ToList();
        }

        private TierChoice Take(string tier, PricedBuild build)
        {
            taken.Add(build);
            return new TierChoice(tier, build, null, build.Rate - comparisonRate, build.Rate);
        }

        private bool BeatsMeasured(double rate)
        {
            var floor = taken.Count > 0 ? taken.Max(t => t.Rate) : comparisonRate;
            return floor == null || rate > floor;
        }

        private bool EstimateBeatsMeasured(GearSetupEstimate estimate)
            => taken.Count == 0 || estimate.CoinsPerHour is { } rate && rate > taken.Max(t => t.Rate);
    }
}
