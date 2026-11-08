using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Coflnet.Sky.Api.Services.Tasks.Setups.Measured;
using NUnit.Framework;
using static SkyApi.Services.Tasks.Setups.Measured.MeasuredTestData;

namespace SkyApi.Services.Tasks.Setups.Measured;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class BuildComposerTests
{
    private const double M = 1_000_000;

    private static ComposedBuild Compose(double maxCost, params PricedPiece[] pieces)
        => BuildComposer.Compose(BuildComposer.Units(pieces, Formula()), maxCost);

    private static List<string> Tags(ComposedBuild build) => build.Pieces.Select(p => p.Piece.Tag).OrderBy(t => t).ToList();

    [Test]
    public void Greedy_takes_the_most_score_per_coin_first_and_stays_within_the_band()
    {
        var cheapTool = Priced(Entry("tool", "CHEAP_HOE", 100), 1 * M);
        var bigTool = Priced(Entry("tool", "BIG_HOE", 300), 20 * M);
        var helmet = Priced(Entry("helmet", "HELMET", 50), 2 * M);

        var build = Compose(10 * M, cheapTool, bigTool, helmet);

        Tags(build).Should().Equal("CHEAP_HOE", "HELMET");
        build.Cost.Should().Be(3 * M);
        build.Score.Should().Be(150);
    }

    [Test]
    public void Replacement_upgrades_a_slot_when_the_band_allows_it()
    {
        var cheapTool = Priced(Entry("tool", "CHEAP_HOE", 100), 1 * M);
        var betterTool = Priced(Entry("tool", "BETTER_HOE", 200), 8 * M);

        var build = Compose(10 * M, cheapTool, betterTool);

        Tags(build).Should().Equal("BETTER_HOE");
        build.Cost.Should().Be(8 * M, "the replaced tool is not bought");
    }

    [Test]
    public void Continuing_from_a_build_keeps_its_score_and_adds_within_the_higher_band()
    {
        var units = BuildComposer.Units([Priced(Entry("tool", "HOE", 100), 5 * M), Priced(Entry("helmet", "HELM", 80), 50 * M)], Formula());
        var budget = BuildComposer.Compose(units, 10 * M);

        var mid = BuildComposer.Compose(units, 100 * M, budget);

        Tags(budget).Should().Equal("HOE");
        Tags(mid).Should().Equal("HELM", "HOE");
        mid.Score.Should().BeGreaterThan(budget.Score);
    }

    private static PricedPiece Member(string slot, string set, double contribution, double share, double? cost, string mode = "add",
        bool lowerBound = false)
        => Priced(Entry(slot, $"{set}_{slot.ToUpperInvariant()}", contribution, effects: SetShare(set, share, mode)), cost, lowerBound);

    /// <summary>Helmet to boots of <paramref name="set"/>, each 10 own + <paramref name="share"/> of the full bonus</summary>
    private static List<PricedPiece> FullSet(string set, double share = 20, double cost = 1_000_000, string mode = "add")
        => BuildComposer.ArmorSlots.Select(slot => Member(slot, set, 10 + share, share, cost, mode)).ToList();

    [Test]
    public void Full_set_is_one_unit_and_wins_when_its_bonus_makes_it_better()
    {
        var otherHelmet = Priced(Entry("helmet", "OTHER_HELMET", 25), 1 * M);

        var build = Compose(10 * M, [.. FullSet("MELON"), otherHelmet]);

        Tags(build).Should().Equal("MELON_BOOTS", "MELON_CHESTPLATE", "MELON_HELMET", "MELON_LEGGINGS");
        build.Score.Should().Be(120, "the set unit carries the full bonus");
        build.Units.Should().ContainSingle().Which.Pieces.Should().HaveCount(4);
    }

    /// <summary>Regression: two members of a four piece set were scored with the full set bonus share</summary>
    [Test]
    public void Partial_set_is_scored_as_the_sum_of_its_singles()
    {
        var set = FullSet("MELON");
        set[3] = Member("boots", "MELON", 30, 20, 1 * M, lowerBound: true);

        var build = Compose(10 * M, [.. set]);

        build.Units.Should().OnlyContain(u => u.Pieces.Count == 1, "the boots can't be bought, so there is no full set");
        build.Score.Should().Be(30, "three members of 10 each without their share");
        Compose(10 * M, set[0], set[1]).Score.Should().Be(20);
    }

    [Test]
    public void Set_with_a_member_beyond_the_armor_needs_that_slot_too()
    {
        var gauntlet = Member("gloves", "MAGMA", 30, 20, null);
        var units = BuildComposer.Units([.. FullSet("MAGMA"), gauntlet], Formula());

        units.Should().OnlyContain(u => u.Pieces.Count == 1, "the unpriced gauntlet is part of the set");
        BuildComposer.SetSlots("MAGMA", [gauntlet]).Should().BeEquivalentTo("helmet", "chestplate", "leggings", "boots", "gloves");
    }

    [Test]
    public void Single_set_member_scores_without_its_share_of_the_bonus()
    {
        var set = FullSet("MELON");
        set[1] = Member("chestplate", "MELON", 30, 20, 50 * M);
        var otherHelmet = Priced(Entry("helmet", "OTHER_HELMET", 25), 1 * M);

        var build = Compose(10 * M, [.. set, otherHelmet]);

        Tags(build).Should().Equal(["MELON_BOOTS", "MELON_LEGGINGS", "OTHER_HELMET"], "25 beats the 10 the melon helmet scores alone, the set does not fit");
        BuildComposer.Units([set[0]], Formula()).Should().ContainSingle().Which.Score.Should().Be(10);
    }

    [Test]
    public void Member_whose_share_is_no_added_stat_is_only_bought_within_its_set()
    {
        var set = FullSet("M", 0.1, mode: "multiply");

        var units = BuildComposer.Units(set, Formula());

        units.Should().ContainSingle().Which.Pieces.Should().HaveCount(4);
        BuildComposer.Units([set[0]], Formula()).Should().BeEmpty("one member is no set");
    }

    [Test]
    public void Set_unit_also_comes_with_the_cheapest_members()
    {
        var bestHelmet = Priced(Entry("helmet", "S_HELMET_BEST", 40, effects: SetShare("S", 20)), 30 * M);

        var build = Compose(10 * M, [.. FullSet("S"), bestHelmet]);

        Tags(build).Should().Equal("S_BOOTS", "S_CHESTPLATE", "S_HELMET", "S_LEGGINGS");
    }

    [Test]
    public void Greedy_stops_below_the_score_ceiling()
    {
        var units = BuildComposer.Units([Priced(Entry("tool", "HOE", 100), 1 * M), Priced(Entry("helmet", "HELM", 80), 1 * M)], Formula());

        var build = BuildComposer.Compose(units, 10 * M, null, scoreCeiling: 150);

        build.Score.Should().Be(100, "adding the helmet would reach the late game cohort's score");
        BuildComposer.Compose(units, 10 * M).Score.Should().Be(180, "no ceiling");
    }

    [TestCase(null, false, false, false)]
    [TestCase(1_000_000d, true, false, false)]
    [TestCase(1_000_000d, false, true, false)]
    [TestCase(1_000_000d, false, false, true)]
    public void Unpriced_lower_bound_transient_or_untradeable_pieces_are_never_composed(double? cost, bool lowerBound, bool transient, bool noMarket)
    {
        var piece = Priced(Entry("tool", "HOE", 500), cost, lowerBound, transient, noMarket);

        BuildComposer.Units([piece], Formula()).Should().BeEmpty();
        Compose(10 * M, piece, Priced(Entry("tool", "OK_HOE", 10), 1 * M)).Pieces.Single().Piece.Tag.Should().Be("OK_HOE");
    }

    [Test]
    public void Only_weighted_sum_formulas_can_be_composed()
    {
        BuildComposer.CanCompose(Formula()).Should().BeTrue();
        BuildComposer.CanCompose(Formula("log-damage-product")).Should().BeFalse("contributions on a log scale don't add up");
        BuildComposer.CanCompose(null).Should().BeFalse();
    }

    [Test]
    public void Nothing_affordable_gives_an_empty_build()
        => Compose(1 * M, Priced(Entry("tool", "HOE", 500), 5 * M)).Units.Should().BeEmpty();
}
