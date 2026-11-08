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

    private static PricedCluster At(string tool, double rate, double? cost, int level = 1, string extraKey = null)
        => Priced(Cluster(tool, rate, level: level, extraKey: extraKey), cost);

    private static PricedCluster Base() => At("BASE", 1.0 * M, 0.5 * M);

    private static Dictionary<string, TierChoice> ByTier(TierPlan plan)
        => plan.Setups.Append(plan.Baseline).Where(c => c != null).ToDictionary(c => c.Tier);

    private static string Tool(TierChoice choice) => choice.Measured.Cluster.Slots["tool"].Tag;

    [Test]
    public void Only_estimates_keep_every_tier_with_its_modelled_gain()
    {
        var plan = SetupTierSelector.Select([], FourEstimates());

        var tiers = ByTier(plan);
        tiers.Values.Should().OnlyContain(c => !c.IsMeasured);
        tiers["baseline"].Estimate.Id.Should().Be("baseline");
        tiers["mid"].DeltaCoinsPerHour.Should().Be(20_000);
        plan.Setups.Select(s => s.Tier).Should().Equal("budget", "mid", "endgame");
        plan.BaselineCoinsPerHour.Should().Be(1_000_000);
        plan.OwnedSpecKeys.Should().BeEmpty();
    }

    [Test]
    public void Only_measured_fills_every_tier_by_cost_band_and_rate()
    {
        var baseline = Base();
        var budget = At("BUDGET", 1.5 * M, 5 * M);
        var mid = At("MID", 2.0 * M, 50 * M);
        var endgame = At("END", 3.0 * M, 300 * M);

        var plan = SetupTierSelector.Select([endgame, mid, budget, baseline], null);

        var tiers = ByTier(plan);
        tiers.Values.Should().OnlyContain(c => c.IsMeasured);
        tiers["baseline"].Measured.Should().BeSameAs(baseline);
        tiers["budget"].Measured.Should().BeSameAs(budget);
        tiers["mid"].Measured.Should().BeSameAs(mid);
        tiers["endgame"].Measured.Should().BeSameAs(endgame);
        tiers["budget"].DeltaCoinsPerHour.Should().Be(0.5 * M, "measured vs measured is the difference of the rates");
        tiers["endgame"].DeltaCoinsPerHour.Should().Be(2 * M);
        plan.BaselineCoinsPerHour.Should().Be(1 * M);
        plan.ComparisonCoinsPerHour.Should().Be(1 * M);
    }

    [Test]
    public void Mixed_estimate_tiers_get_their_gain_against_the_measured_baseline()
    {
        var plan = SetupTierSelector.Select([Base(), At("BUDGET", 1.4 * M, 4 * M)], RisingEstimates());

        var tiers = ByTier(plan);
        tiers["baseline"].IsMeasured.Should().BeTrue();
        tiers["budget"].IsMeasured.Should().BeTrue();
        tiers["mid"].IsMeasured.Should().BeFalse();
        tiers["mid"].DeltaCoinsPerHour.Should().Be(2.0 * M, "the modelled 3M/h minus the measured baseline's 1M/h");
        tiers["endgame"].DeltaCoinsPerHour.Should().Be(5.0 * M);
    }

    [Test]
    public void Estimate_tier_below_a_measured_baseline_is_left_out()
    {
        var plan = SetupTierSelector.Select([At("BASE", 1.5 * M, 0.5 * M)], FourEstimates());

        plan.Baseline.IsMeasured.Should().BeTrue();
        plan.Setups.Should().BeEmpty("no estimate earns more than the measured 1.5M/h baseline");
    }

    [Test]
    public void Estimate_must_beat_every_measured_tier_below_it()
    {
        var plan = SetupTierSelector.Select([Base(), At("BUDGET", 4 * M, 4 * M)], RisingEstimates());

        var tiers = ByTier(plan);
        tiers.Should().NotContainKey("mid", "the modelled 3M/h does not beat the measured 4M/h budget");
        tiers["endgame"].Estimate.Id.Should().Be("endgame");
    }

    [Test]
    public void Endgame_dominated_by_budget_is_not_shown()
    {
        var budget = At("BUDGET", 10 * M, 8 * M);

        var plan = SetupTierSelector.Select([Base(), budget, At("WORSE", 6 * M, 50 * M)], FourEstimates());

        var tiers = ByTier(plan);
        tiers["budget"].Measured.Should().BeSameAs(budget);
        tiers.Should().NotContainKey("endgame", "6M/h is worse than the 10M/h budget and the estimate is lower still");
        tiers.Should().NotContainKey("mid");
    }

    [Test]
    public void Every_measured_tier_earns_more_than_the_one_below()
    {
        var plan = SetupTierSelector.Select([Base(), At("BUDGET", 2 * M, 5 * M), At("MIDLOW", 1.8 * M, 41 * M), At("END", 3 * M, 300 * M)], null);

        var tiers = ByTier(plan);
        tiers.Should().NotContainKey("mid");
        tiers["endgame"].Rate.Should().BeGreaterThan(tiers["budget"].Rate!.Value);
    }

    [Test]
    public void Measured_tier_against_an_estimated_baseline_is_compared_with_the_community_rate()
    {
        var estimates = FourEstimates();
        estimates.CommunityCoinsPerHour = 1.2 * M;

        var plan = SetupTierSelector.Select([At("END", 3 * M, 300 * M)], estimates);

        var tiers = ByTier(plan);
        tiers["baseline"].IsMeasured.Should().BeFalse("a 300M setup is no baseline");
        plan.BaselineCoinsPerHour.Should().Be(1_000_000, "the estimated baseline's rate is shown with it");
        plan.ComparisonCoinsPerHour.Should().Be(1.2 * M);
        tiers["endgame"].DeltaCoinsPerHour.Should().Be(1.8 * M);
    }

    [Test]
    public void Modelled_baseline_above_every_measured_cluster_does_not_hide_them()
    {
        var estimates = FourEstimates();
        estimates.BaselineCoinsPerHour = 5 * M;
        estimates.CommunityCoinsPerHour = 1 * M;

        var plan = SetupTierSelector.Select([At("END", 2 * M, 300 * M)], estimates);

        ByTier(plan)["endgame"].IsMeasured.Should().BeTrue("measured clusters are compared with the community rate, not the modelled baseline");
    }

    [Test]
    public void Without_estimates_the_task_community_rate_is_used()
    {
        var plan = SetupTierSelector.Select([At("END", 2 * M, 300 * M)], null, communityRate: 0.8 * M);

        ByTier(plan)["endgame"].DeltaCoinsPerHour.Should().BeApproximately(1.2 * M, 0.001);
    }

    [Test]
    public void Expensive_cluster_never_becomes_the_free_baseline()
    {
        var only = At("END", 3 * M, 300 * M);

        var plan = SetupTierSelector.Select([only], null, communityRate: 1 * M);

        plan.Baseline.Should().BeNull();
        plan.BaselineCoinsPerHour.Should().Be(1 * M, "the community average is the baseline rate");
        ByTier(plan)["endgame"].Measured.Should().BeSameAs(only);
    }

    [Test]
    public void Empty_budget_band_falls_back_to_the_estimate()
    {
        var plan = SetupTierSelector.Select([Base(), At("MID", 2 * M, 40 * M), At("END", 3 * M, 300 * M)], FourEstimates());

        var tiers = ByTier(plan);
        tiers["budget"].IsMeasured.Should().BeFalse();
        tiers["budget"].Estimate.Id.Should().Be("budget");
        tiers["budget"].DeltaCoinsPerHour.Should().BeApproximately(10_000, 0.001);
        Tool(tiers["mid"]).Should().Be("MID");
    }

    [Test]
    public void Mid_takes_a_pricier_frontier_setup_that_is_not_the_endgame_when_its_band_is_empty()
    {
        var plan = SetupTierSelector.Select([Base(), At("BIG", 2 * M, 150 * M), At("END", 3 * M, 400 * M)], FourEstimates());

        var tiers = ByTier(plan);
        Tool(tiers["mid"]).Should().Be("BIG");
        Tool(tiers["endgame"]).Should().Be("END");
    }

    [Test]
    public void Dominated_cluster_is_never_a_tier()
    {
        var budget = At("BUDGET", 2 * M, 5 * M);

        var plan = SetupTierSelector.Select([Base(), budget, At("WORSE", 1.5 * M, 20 * M), At("END", 3 * M, 400 * M)], FourEstimates());

        var tiers = ByTier(plan);
        tiers["budget"].Measured.Should().BeSameAs(budget);
        tiers.Should().NotContainKey("mid");
        plan.Setups.Should().NotContain(c => c.IsMeasured && Tool(c) == "WORSE");
    }

    [Test]
    public void Cluster_below_the_baseline_rate_is_no_upgrade()
    {
        var plan = SetupTierSelector.Select([Base(), At("SLOW", 0.8 * M, 5 * M)], FourEstimates());

        plan.Setups.Should().OnlyContain(c => !c.IsMeasured);
    }

    [Test]
    public void Budget_prefers_the_best_gain_per_coin()
    {
        var cheapGood = At("CHEAP", 1.3 * M, 2 * M);

        var plan = SetupTierSelector.Select([Base(), cheapGood, At("DEAR", 1.4 * M, 9 * M), At("END", 3 * M, 400 * M)], FourEstimates());

        ByTier(plan)["budget"].Measured.Should().BeSameAs(cheapGood);
    }

    [Test]
    public void Owned_baseline_items_are_not_part_of_the_value_cost()
    {
        // both share the baseline helmet (HELMET_A) which costs 3M, only the tool counts
        var baseline = Priced(Cluster("BASE", 1 * M), 0.1 * M, 3 * M);
        var a = Priced(Cluster("A", 1.5 * M), 5 * M, 3 * M);
        var b = Priced(Cluster("B", 1.3 * M, helmet: "HELMET_B"), 1 * M, 3 * M);

        var plan = SetupTierSelector.Select([baseline, a, b, At("END", 3 * M, 400 * M)], null);

        plan.OwnedSpecKeys.Should().HaveCount(2);
        ByTier(plan)["budget"].Measured.Should().BeSameAs(a, "A gains 0.5M for 5M, B 0.3M for 4M with its own helmet");
    }

    [Test]
    public void Setup_that_costs_nothing_extra_never_wins_the_value_ranking()
    {
        // same published items as the baseline (eg. its modifiers were not shared by enough players), another key
        var free = Priced(Cluster("BASE", 1.1 * M), 0.5 * M);
        free.Cluster.Key = "L1|armor=OTHER|tool=OTHER";
        var paid = At("PAID", 1.2 * M, 3 * M);

        var plan = SetupTierSelector.Select([Base(), free, paid], null);

        ByTier(plan)["budget"].Measured.Should().BeSameAs(paid, "a gain over zero extra coins must not divide by zero and win");
    }

    [Test]
    public void Child_the_parent_does_not_dominate_wins_and_the_parent_is_not_shown_again()
    {
        var parent = At("TOOL", 1.5 * M, 4 * M, level: 1);
        var child = At("TOOL", 1.6 * M, 4.5 * M, level: 3, extraKey: "tool.enchants.harvesting=6");

        var plan = SetupTierSelector.Select([Base(), parent, child, At("END", 3 * M, 400 * M)], FourEstimates());

        ByTier(plan)["budget"].Measured.Should().BeSameAs(child);
        plan.Setups.Should().NotContain(c => c.Measured == parent);
    }

    [Test]
    public void Dominated_child_does_not_hide_its_better_parent()
    {
        var parent = At("TOOL", 1.5 * M, 4 * M, level: 1);
        var child = At("TOOL", 1.45 * M, 4.5 * M, level: 3, extraKey: "tool.enchants.harvesting=6");

        var plan = SetupTierSelector.Select([Base(), parent, child, At("END", 3 * M, 400 * M)], FourEstimates());

        ByTier(plan)["budget"].Measured.Should().BeSameAs(parent);
        plan.Setups.Should().NotContain(c => c.Measured == child);
    }

    /// <summary>Regression: P dominates C1 but not C2, so P was dropped for C2 and the dominated C1 won on gain per coin</summary>
    [Test]
    public void Child_dominated_by_its_parent_never_wins_even_when_a_sibling_hides_the_parent()
    {
        var baseline = At("BASE", 2 * M, 0.5 * M);
        var parent = At("TOOL", 3 * M, 5 * M, level: 1);
        var c1 = At("TOOL", 2.8 * M, 6 * M, level: 3, extraKey: "tool.enchants.harvesting=5");
        var c2 = At("TOOL", 3.1 * M, 9.9 * M, level: 3, extraKey: "tool.enchants.harvesting=6");

        var plan = SetupTierSelector.Select([baseline, parent, c1, c2], null);

        ByTier(plan)["budget"].Measured.Should().BeSameAs(c2);
        ClusterKeys.Finest([parent, c1, c2]).Should().Equal(c2);
    }

    [Test]
    public void Baseline_parent_stays_when_its_only_child_is_outside_the_band()
    {
        var parent = At("TOOL", 1.0 * M, 5 * M, level: 1);
        var child = At("TOOL", 1.2 * M, 20 * M, level: 3, extraKey: "tool.enchants.harvesting=6");

        var plan = SetupTierSelector.Select([parent, child], null);

        plan.Baseline.Measured.Should().BeSameAs(parent);
    }

    [Test]
    public void Endgame_picks_the_child_its_parent_does_not_dominate()
    {
        var parent = At("TOOL", 3 * M, 300 * M, level: 2);
        var child = At("TOOL", 3.1 * M, 320 * M, level: 3, extraKey: "tool.reforge=blessed");

        var plan = SetupTierSelector.Select([Base(), parent, child], FourEstimates());

        ByTier(plan)["endgame"].Measured.Should().BeSameAs(child);
        plan.Setups.Should().NotContain(c => c.Measured == parent);
    }

    [Test]
    public void Parent_key_links_clusters_whose_keys_do_not_nest()
    {
        var parent = At("TOOL", 1.5 * M, 4 * M, level: 2);
        var child = Priced(Cluster("OTHER", 1.6 * M, level: 3), 4.5 * M);
        child.Cluster.ParentKey = parent.Cluster.Key;

        ClusterKeys.IsAncestor(parent, child).Should().BeTrue();
        ClusterKeys.IsAncestor(child, parent).Should().BeFalse();
        var plan = SetupTierSelector.Select([Base(), parent, child, At("END", 3 * M, 400 * M)], FourEstimates());
        ByTier(plan)["budget"].Measured.Should().BeSameAs(child);
        plan.Setups.Should().NotContain(c => c.Measured == parent);
    }

    [Test]
    public void Unrelated_clusters_of_different_levels_are_no_ancestors()
    {
        ClusterKeys.AreRelated(At("A", 1, 1, level: 1), At("B", 1, 1, level: 3, extraKey: "pet=ELEPHANT:LEGENDARY")).Should().BeFalse();
    }

    [Test]
    public void Unpriced_cluster_can_be_endgame_but_not_budget_or_baseline()
    {
        var unpriced = At("RARE", 1.2 * M, null);

        var plan = SetupTierSelector.Select([unpriced], FourEstimates());

        var tiers = ByTier(plan);
        tiers["baseline"].IsMeasured.Should().BeFalse();
        tiers["endgame"].Measured.Should().BeSameAs(unpriced);
        tiers["budget"].IsMeasured.Should().BeFalse();
    }

    [Test]
    public void Transiently_unpriced_cluster_sits_the_build_out()
    {
        var failed = Priced(Cluster("RARE", 9 * M), null, toolTransient: true);

        var plan = SetupTierSelector.Select([Base(), failed], FourEstimates());

        plan.Setups.Should().NotContain(c => c.IsMeasured);
    }

    [Test]
    public void Lower_bound_price_keeps_a_cluster_out_of_the_bands_but_not_out_of_the_endgame()
    {
        var lowerBound = Priced(Cluster("LB", 2 * M), 3 * M, toolLowerBound: true);

        var plan = SetupTierSelector.Select([Base(), lowerBound], FourEstimates());

        var tiers = ByTier(plan);
        tiers["budget"].IsMeasured.Should().BeFalse("its real cost may be far above the budget band");
        tiers["endgame"].Measured.Should().BeSameAs(lowerBound);
        SetupTierSelector.Select([Priced(Cluster("LB", 1 * M), 3 * M, toolLowerBound: true)], null).Baseline.Should().BeNull();
    }

    [Test]
    public void Bands_are_configurable()
    {
        var plan = SetupTierSelector.Select([Base(), At("X", 2 * M, 20 * M), At("END", 3 * M, 400 * M)], FourEstimates(), null,
            new TierBands(1 * M, 30 * M, 100 * M));

        Tool(ByTier(plan)["budget"]).Should().Be("X");
    }
}
