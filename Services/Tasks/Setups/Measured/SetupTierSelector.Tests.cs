using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Coflnet.Sky.Api.Services.Tasks.Setups.Measured;
using Coflnet.Sky.PlayerState.Client.Model;
using NUnit.Framework;
using static SkyApi.Services.Tasks.Setups.Measured.MeasuredTestData;
using Estimates = SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests;

namespace SkyApi.Services.Tasks.Setups.Measured;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class SetupTierSelectorTests
{
    private const double M = 1_000_000;

    /// <summary>Estimated baseline at 1M/h, budget 1.01M, mid 1.02M, endgame 1.03M (see PublicSetupsMapperTests.Setup)</summary>
    private static TaskGearSetups FourEstimates() => Estimates.Gear(
        Estimates.Setup("baseline", "baseline", 0, Estimates.Item("OLD")),
        Estimates.Setup("budget", "upgrade", 10_000, Estimates.Item("B")),
        Estimates.Setup("mid", "upgrade", 20_000, Estimates.Item("M")),
        Estimates.Setup("endgame", "upgrade", 30_000, Estimates.Item("E")));

    private static Dictionary<string, TierChoice> ByTier(TierPlan plan)
        => plan.Setups.Append(plan.Baseline).Where(c => c != null).ToDictionary(c => c.Tier);

    private static List<string> Tags(TierChoice choice) => choice.Measured.Pieces.Select(p => p.Piece.Tag).ToList();

    private static TierPlan FourTiers(TaskGearSetups estimates = null) => SetupTierSelector.Select(FourTierModel.Priced(), estimates, FourTierModel.CommunityRate);

    [Test]
    public void Only_estimates_keep_every_tier_with_its_modelled_gain()
    {
        var plan = SetupTierSelector.Select(null, FourEstimates());

        var tiers = ByTier(plan);
        tiers.Values.Should().OnlyContain(c => !c.IsMeasured);
        tiers["baseline"].Estimate.Id.Should().Be("baseline");
        tiers["mid"].DeltaCoinsPerHour.Should().Be(20_000);
        plan.Setups.Select(s => s.Tier).Should().Equal("budget", "mid", "endgame");
        plan.BaselineCoinsPerHour.Should().Be(1_000_000);
    }

    [Test]
    public void Model_fills_budget_and_mid_from_the_curve_and_endgame_from_the_recommendation()
    {
        var tiers = ByTier(FourTiers(FourEstimates()));

        tiers["baseline"].IsMeasured.Should().BeFalse("the baseline is the curated estimate");
        Tags(tiers["budget"]).Should().Equal("BUDGET_HOE");
        tiers["budget"].Rate.Should().Be(FourTierModel.BudgetRate);
        tiers["budget"].Measured.Score.Should().Be(250);
        tiers["budget"].Measured.ScorePercentile.Should().Be(25);
        tiers["budget"].DeltaCoinsPerHour.Should().Be(0.5 * M, "gain over the community rate");
        Tags(tiers["mid"]).Should().Equal("BUDGET_HOE", "MID_HELMET");
        tiers["mid"].Rate.Should().Be(FourTierModel.MidRate);
        tiers["mid"].Measured.RateBasis.Should().Be(RateBases.Curve);
        Tags(tiers["endgame"]).Should().Equal("END_HOE", "MID_HELMET");
        tiers["endgame"].Rate.Should().Be(FourTierModel.EndRate);
        tiers["endgame"].Measured.Extrapolated.Should().BeTrue();
        tiers["endgame"].Measured.LateGame.Players.Should().Be(FourTierModel.SecretPlayers);
        tiers.Values.Where(c => c.Tier != "endgame").Should().OnlyContain(c => c.Measured == null || c.Measured.LateGame == null);
    }

    [Test]
    public void Budget_and_mid_stay_in_their_cost_bands()
    {
        var tiers = ByTier(FourTiers());

        tiers["budget"].Measured.FullCost.Should().BeLessThanOrEqualTo(TierBands.Default.BudgetMaxCost);
        tiers["mid"].Measured.FullCost.Should().BeGreaterThan(TierBands.Default.BudgetMaxCost).And.BeLessThanOrEqualTo(TierBands.Default.MidMaxCost);
    }

    [Test]
    public void Mid_that_costs_no_more_than_the_budget_band_is_dropped()
    {
        var model = MeasuredTestData.Model(null, Curve([(100, 1 * M), (300, 3 * M)]), [Entry("tool", "HOE", 200)]);

        var plan = SetupTierSelector.Select(PricedModel(model, Priced(model.Pieces[0], 2 * M)), RisingEstimates(), 1 * M);

        var tiers = ByTier(plan);
        tiers["budget"].IsMeasured.Should().BeTrue();
        tiers["mid"].IsMeasured.Should().BeFalse("nothing above the budget band is affordable, so mid falls back");
        tiers["mid"].Estimate.Id.Should().Be("mid", "the 3M/h estimate beats the 2M/h budget");
    }

    [Test]
    public void Tier_that_does_not_beat_the_one_below_is_dropped_and_falls_back_to_an_estimate_that_does()
    {
        // a flat curve: the mid build scores higher but earns no more than the budget build
        var model = MeasuredTestData.Model(null, Curve([(100, 2 * M), (400, 2 * M)]),
            [Entry("tool", "HOE", 100), Entry("helmet", "HELM", 100)]);
        var priced = PricedModel(model, Priced(model.Pieces[0], 1 * M), Priced(model.Pieces[1], 50 * M));

        var tiers = ByTier(SetupTierSelector.Select(priced, RisingEstimates(), 1 * M));

        tiers["budget"].IsMeasured.Should().BeTrue();
        tiers["mid"].IsMeasured.Should().BeFalse();
        tiers["mid"].Estimate.CoinsPerHour.Should().Be(3 * M, "the estimate beats the measured 2M/h below it");
    }

    [Test]
    public void Budget_below_the_community_rate_is_dropped()
    {
        var model = MeasuredTestData.Model(null, Curve([(100, 0.5 * M), (400, 0.8 * M)]), [Entry("tool", "HOE", 100)]);

        var tiers = ByTier(SetupTierSelector.Select(PricedModel(model, Priced(model.Pieces[0], 1 * M)), null, 1 * M));

        tiers.Should().NotContainKey("budget");
    }

    [Test]
    public void Without_a_curve_budget_and_mid_are_the_estimates_and_the_endgame_uses_the_cohort()
    {
        var model = FourTierModel.Model();
        model.Curve = null;
        model.RecommendedEndgame.Basis = RateBases.Cohort;
        model.RecommendedEndgame.CoinsPerHour = 8 * M;

        var tiers = ByTier(SetupTierSelector.Select(FourTierModel.Priced(model), RisingEstimates(), 1 * M));

        tiers["budget"].IsMeasured.Should().BeFalse();
        tiers["mid"].IsMeasured.Should().BeFalse();
        tiers["endgame"].Measured.RateBasis.Should().Be(RateBases.Cohort);
        tiers["endgame"].Rate.Should().Be(8 * M);
    }

    [Test]
    public void Combat_formula_is_not_composed()
    {
        var model = FourTierModel.Model();
        model.ScoreFormula = Formula("log-damage-product");

        var tiers = ByTier(SetupTierSelector.Select(FourTierModel.Priced(model), RisingEstimates(), 1 * M));

        tiers["budget"].IsMeasured.Should().BeFalse();
        tiers["mid"].IsMeasured.Should().BeFalse();
        tiers["endgame"].IsMeasured.Should().BeTrue();
    }

    [Test]
    public void Composed_build_with_the_endgame_pieces_is_left_to_the_endgame_tier()
    {
        var model = FourTierModel.Model();
        model.LateGame = null;
        model.Distribution = null; // no score ceiling, so only the same-build check keeps them apart
        var cheapEnd = FourTierModel.Priced(model).Pieces.ToDictionary(p => p.Key, p => p.Key == "tool:END_HOE"
            ? Priced(model.Pieces.Single(e => e.Id == p.Key), 40 * M) : p.Value);

        var tiers = ByTier(SetupTierSelector.Select(new PricedGearModel(model, cheapEnd), RisingEstimates(), 1 * M));

        tiers["endgame"].IsMeasured.Should().BeTrue();
        tiers["mid"].IsMeasured.Should().BeFalse("the mid greedy reaches exactly the endgame build");
    }

    /// <summary>Regression: a composed mid earning the endgame's rate dropped the endgame tier (and showed its build to premium)</summary>
    [Test]
    public void Composed_build_that_earns_the_endgame_rate_is_not_shown()
    {
        var model = FourTierModel.Model();
        model.RecommendedEndgame.CoinsPerHour = FourTierModel.MidRate;

        var tiers = ByTier(SetupTierSelector.Select(FourTierModel.Priced(model), RisingEstimates(), 1 * M));

        tiers["endgame"].IsMeasured.Should().BeTrue();
        tiers["endgame"].Rate.Should().Be(FourTierModel.MidRate);
        tiers["mid"].IsMeasured.Should().BeFalse("the mid would earn as much as the endgame");
        tiers["budget"].IsMeasured.Should().BeTrue();
    }

    /// <summary>Regression: a budget build clamped at the top of the curve outdid the capped endgame and removed it</summary>
    [Test]
    public void Budget_clamped_at_the_top_of_the_curve_never_beats_or_removes_a_capped_endgame()
    {
        var model = FourTierModel.Model();
        model.RecommendedEndgame.CoinsPerHour = 6 * M;
        model.RecommendedEndgame.Capped = true;
        var priced = FourTierModel.Priced(model).Pieces.ToDictionary(p => p.Key, p => p.Value);
        var strong = Entry("tool", "STRONG_HOE", 399);
        model.Pieces.Add(strong);
        priced[strong.Id] = Priced(strong, 2 * M);

        var tiers = ByTier(SetupTierSelector.Select(new PricedGearModel(model, priced), null, 1 * M));

        tiers["endgame"].IsMeasured.Should().BeTrue();
        tiers["endgame"].Rate.Should().Be(6 * M);
        tiers.Values.Where(c => c.Tier != "endgame").Should().OnlyContain(c => c.Rate < 6 * M);
        GearCurveReader.RateAt(model.Curve, 399).CoinsPerHour.Should().BeGreaterThan(6 * M, "the curve alone would outdo the endgame");
    }

    /// <summary>Regression: the mid could be the endgame build minus a lower bound priced piece, ie. the endgame for premium</summary>
    [Test]
    public void Mid_stays_below_the_late_game_score_so_it_is_never_the_endgame_minus_a_piece()
    {
        var tool = Entry("tool", "END_HOE", 400);
        var helmet = Entry("helmet", "END_HELMET", 100);
        var pet = PetEntry("ELEPHANT", 50);
        var model = MeasuredTestData.Model(null, FourTierModel.Model().Curve, [tool, helmet, pet, Entry("tool", "CHEAP_HOE", 200)],
            Recommended(550, 9 * M, tool, helmet, pet), LateGame(8 * M), Distribution(100, 250, 300, 350, 450));
        model.LateGame.ScoreFrom = 450;
        var priced = PricedModel(model, Priced(tool, 40 * M), Priced(helmet, 20 * M), Priced(pet, 500 * M, lowerBound: true),
            Priced(model.Pieces[3], 1 * M));

        var tiers = ByTier(SetupTierSelector.Select(priced, null, 1 * M));

        tiers["endgame"].IsMeasured.Should().BeTrue();
        var mid = tiers["mid"].Measured;
        mid.Score.Should().BeLessThan(450);
        mid.PieceIds().Should().NotBeEquivalentTo(new[] { "tool:END_HOE", "helmet:END_HELMET" });
    }

    [Test]
    public void Score_ceiling_is_the_late_game_score_else_the_90th_percentile()
    {
        var model = FourTierModel.Model();
        SetupTierSelector.ScoreCeiling(model).Should().Be(400);
        model.LateGame = null;
        SetupTierSelector.ScoreCeiling(model).Should().Be(450);
        model.Distribution = null;
        SetupTierSelector.ScoreCeiling(model).Should().BeNull();
    }

    [Test]
    public void Empty_piece_ids_of_the_endgame_are_ignored()
    {
        var model = FourTierModel.Model();
        model.RecommendedEndgame.Pieces["pet"] = null;
        model.RecommendedEndgame.Pieces["cloak"] = "";

        SetupTierSelector.EndgameBuild(FourTierModel.Priced(model)).PieceIds().Should().BeEquivalentTo("tool:END_HOE", "helmet:MID_HELMET");
        model.RecommendedEndgame.Pieces = new() { ["tool"] = null };
        SetupTierSelector.EndgameBuild(FourTierModel.Priced(model)).Should().BeNull();
    }

    [Test]
    public void Endgame_keeps_lower_bound_and_unknown_prices_but_sits_out_a_transient_one()
    {
        var model = FourTierModel.Model();
        var priced = FourTierModel.Priced(model).Pieces.ToDictionary(p => p.Key, p => p.Value);
        priced["tool:END_HOE"] = Priced(model.Pieces[0], 300 * M, lowerBound: true);
        priced["helmet:MID_HELMET"] = Priced(model.Pieces[3], null);

        var endgame = ByTier(SetupTierSelector.Select(new PricedGearModel(model, priced), null, 1 * M))["endgame"];

        endgame.IsMeasured.Should().BeTrue();
        endgame.Measured.LowerBound.Should().BeTrue();
        endgame.Measured.FullCost.Should().BeNull();

        priced["tool:END_HOE"] = Priced(model.Pieces[0], null, transient: true);
        var fallback = ByTier(SetupTierSelector.Select(new PricedGearModel(model, priced), RisingEstimates(), 1 * M))["endgame"];
        fallback.IsMeasured.Should().BeFalse();
    }

    [Test]
    public void Missing_curve_distribution_and_endgame_give_estimates_only()
    {
        var model = MeasuredTestData.Model(null, null, [Entry("tool", "HOE", 100)]);

        var plan = SetupTierSelector.Select(PricedModel(model, Priced(model.Pieces[0], 1 * M)), FourEstimates());

        plan.Setups.Should().OnlyContain(c => !c.IsMeasured);
    }

    [Test]
    public void Composed_build_outside_the_observed_range_is_flagged_and_low_confidence()
    {
        var model = MeasuredTestData.Model(null, Curve([(100, 1 * M), (200, 2 * M)]), [Entry("tool", "HOE", 50)],
            distribution: Distribution(100, 120, 150, 180, 200));

        var budget = ByTier(SetupTierSelector.Select(PricedModel(model, Priced(model.Pieces[0], 1 * M)), null, 0.5 * M))["budget"];

        budget.Measured.Extrapolated.Should().BeTrue();
        budget.Measured.Confidence.Should().Be("low");
        budget.Rate.Should().Be(1 * M, "the rate at the edge of the observed range");
    }

    [Test]
    public void Endgame_confidence_follows_the_cohort_and_is_low_when_rounded_or_extrapolated()
    {
        var model = FourTierModel.Model();
        model.RecommendedEndgame.Extrapolated = false;
        model.LateGame = LateGame(8 * M, players: 7);
        ByTier(SetupTierSelector.Select(FourTierModel.Priced(model), null, 1 * M))["endgame"].Measured.Confidence.Should().Be("high");

        model.LateGame = LateGame(8 * M, players: 3, rounded: true);
        ByTier(SetupTierSelector.Select(FourTierModel.Priced(model), null, 1 * M))["endgame"].Measured.Confidence.Should().Be("low");

        ByTier(FourTiers())["endgame"].Measured.Confidence.Should().Be("low", "the four tier endgame is extrapolated");
    }

    [Test]
    public void Endgame_with_a_piece_missing_from_the_catalog_is_not_measured()
    {
        var model = FourTierModel.Model();
        model.RecommendedEndgame.Pieces["pet"] = "pet:UNKNOWN";

        SetupTierSelector.EndgameBuild(FourTierModel.Priced(model)).Should().BeNull();
    }
}
