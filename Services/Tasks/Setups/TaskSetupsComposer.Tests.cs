using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Api.Services.Tasks.Setups;
using Coflnet.Sky.Api.Services.Tasks.Setups.Measured;
using Coflnet.Sky.PlayerState.Client.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SkyApi.Services.Tasks.Setups.Measured;
using static SkyApi.Services.Tasks.Setups.Measured.MeasuredTestData;

namespace SkyApi.Services.Tasks.Setups;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class TaskSetupsComposerTests
{
    public const string MidUuid = FourTierModel.MidUuid;
    public const string EndUuid = FourTierModel.EndUuid;
    public const string BudgetUuid = FourTierModel.BudgetUuid;
    public const double SecretEffect = 61.803;

    /// <summary>Strings of the locked tiers and of details only data, none of them may be in the public view</summary>
    public static readonly string[] Sentinels =
    [
        MidUuid, EndUuid, "7770000", "8880000", "6770000", "7880000", "55555555", "444444444",
        "4321", "9876.5", "31337", "61.803", "harvesting",
    ];

    /// <summary>Everything that identifies the endgame build or its late game cohort, none of it may reach a caller below premium plus</summary>
    private static readonly string[] EndgameSentinels =
        ["END_HOE", "End Hoe", "end hoe", EndUuid, "8880000", "7880000", "444444444", "4321", "9876.5", "31337", "lateGame\":{"];

    public static readonly List<GearModifierEffect> Effects =
    [
        new() { Changes = [new GearKeyChange { Component = "tool.enchants.harvesting", From = "5", To = "6" }], Players = 3, Pairs = 4, Ratio = 1.61803, PercentChange = SecretEffect, Rounded = true },
    ];

    public static SetupsModel Model(PricedGearModel gear, TaskGearSetups estimates = null) => new(
        "task-a", "Task A", "farming", null, estimates, SetupTierSelector.Select(gear, estimates, FourTierModel.CommunityRate), Effects,
        new Dictionary<string, string>(), false, FourTierModel.CommunityRate, gear?.Model?.ScoreFormula);

    public static SetupsModel FourTiers(TaskGearSetups estimates = null) => Model(FourTierModel.Priced(), estimates);

    private static PublicTaskSetups Compose(SetupsModel model, SetupsAccess access)
        => TaskSetupsComposer.Compose(model, new Dictionary<string, ItemResaleQuote>(), access);

    private static JObject Json(PublicTaskSetups value)
        => JObject.Parse(JsonConvert.SerializeObject(value, new Microsoft.AspNetCore.Mvc.MvcNewtonsoftJsonOptions().SerializerSettings));

    private static Dictionary<string, JToken> ByTier(JObject json)
        => ((JArray)json["setups"]!).Append(json["baseline"]!).Where(s => s.Type == JTokenType.Object).ToDictionary(s => s["tier"]!.Value<string>()!);

    private static string ViewJson(SetupsAccess access, SetupsModel model = null)
        => JsonConvert.SerializeObject(Compose(model ?? FourTiers(), access), new Microsoft.AspNetCore.Mvc.MvcNewtonsoftJsonOptions().SerializerSettings);

    [TestCase(SetupsAccess.Public)]
    [TestCase(SetupsAccess.Premium)]
    [TestCase(SetupsAccess.PremiumPlus)]
    public void Slow_estimated_budget_is_omitted_from_every_view(SetupsAccess access)
    {
        var source = PublicSetupsMapperTests.Setup("budget", "upgrade", 1_000, PublicSetupsMapperTests.Item("NEW"));
        var estimates = PublicSetupsMapperTests.Gear(PublicSetupsMapperTests.Setup("baseline", "baseline", 0), source);
        var quotes = new Dictionary<string, ItemResaleQuote>
        {
            [PublicSetupsMapper.QuoteKey("NEW", null)] = PublicSetupsMapperTests.Quote("NEW", 151_000, 150_900),
        };

        var view = TaskSetupsComposer.Compose(Model(null, estimates), quotes, access);

        view.Setups.Should().BeEmpty();
        view.Baseline.Should().NotBeNull();
        view.Locked.Should().BeFalse();
    }

    [TestCase(SetupsAccess.Public, 150d, true)]
    [TestCase(SetupsAccess.Public, 151d, false)]
    [TestCase(SetupsAccess.Premium, 151d, false)]
    [TestCase(SetupsAccess.PremiumPlus, 151d, false)]
    public void Measured_budget_uses_the_same_payback_filter(SetupsAccess access, double hours, bool kept)
    {
        var model = FourTiers();
        model = model with { Plan = model.Plan with
        {
            Setups = model.Plan.Setups.Select(c => c.Tier == "budget"
                ? c with { DeltaCoinsPerHour = FourTierModel.BudgetCost / hours } : c).ToList(),
        } };

        var view = Compose(model, access);

        view.Setups.Any(s => s.Tier == "budget").Should().Be(kept);
        view.Setups.Should().Contain(s => s.Tier == "mid").And.Contain(s => s.Tier == "endgame");
    }

    [Test]
    public void Public_view_contains_no_locked_number_uuid_or_details_only_data()
    {
        var publicJson = ViewJson(SetupsAccess.Public);
        var detailsJson = ViewJson(SetupsAccess.PremiumPlus);

        foreach (var sentinel in Sentinels)
        {
            publicJson.Should().NotContain(sentinel);
            detailsJson.Should().Contain(sentinel, "the details view carries it, so the public check is meaningful");
        }
        publicJson.Should().Contain(BudgetUuid, "the budget acquisition is free");
    }

    [Test]
    public void Public_view_has_numbers_and_acquisition_for_budget_only()
    {
        var json = Json(Compose(FourTiers(), SetupsAccess.Public));

        json["locked"]!.Value<bool>().Should().BeTrue();
        json["modifierEffects"]!.Type.Should().Be(JTokenType.Null);
        json["baselineCoinsPerHour"]!.Value<double>().Should().Be(1_000_000, "the community rate without an estimated baseline");
        var tiers = ByTier(json);
        var budget = tiers["budget"];
        budget["locked"]!.Value<bool>().Should().BeFalse();
        budget["basis"]!.Value<string>().Should().Be("measured");
        budget["rateBasis"]!.Value<string>().Should().Be("curve");
        budget["score"]!.Value<double>().Should().Be(250);
        budget["scorePercentile"]!.Value<double>().Should().Be(25);
        budget["extrapolated"]!.Value<bool>().Should().BeFalse();
        budget["players"]!.Type.Should().Be(JTokenType.Null, "a composed build has no players of its own");
        budget["lateGame"]!.Type.Should().Be(JTokenType.Null);
        budget["deltaCoinsPerHour"]!.Value<double>().Should().Be(500_000);
        budget["buyCost"]!.Value<double>().Should().Be(FourTierModel.BudgetCost);
        budget["items"]!.Should().OnlyContain(i => i["acquisition"]!.Type == JTokenType.Object && i["scoreContribution"]!.Type == JTokenType.Float
            && i["players"]!.Value<int>() == 6);
        var mid = tiers["mid"];
        mid["locked"]!.Value<bool>().Should().BeTrue();
        mid["basis"]!.Value<string>().Should().Be("measured");
        foreach (var field in new[] { "confidence", "coinsPerHour", "deltaCoinsPerHour", "buyCost", "score", "scorePercentile", "rateBasis", "extrapolated", "capped", "lateGame" })
            mid[field]!.Type.Should().Be(JTokenType.Null, field);
        mid["items"]!.Should().OnlyContain(i => i["acquisition"]!.Type == JTokenType.Null && i["spec"]!.Type == JTokenType.Null
            && i["buyPrice"]!.Type == JTokenType.Null && i["scoreContribution"]!.Type == JTokenType.Null && i["players"]!.Type == JTokenType.Null);
    }

    [TestCase(SetupsAccess.Public)]
    [TestCase(SetupsAccess.Premium)]
    public void Endgame_is_a_bare_placeholder_below_premium_plus(SetupsAccess access)
    {
        var json = Json(Compose(FourTiers(), access));

        var endgame = ByTier(json)["endgame"];
        endgame.Children<JProperty>().Where(p => p.Value.Type != JTokenType.Null)
            .Select(p => p.Name).Should().BeEquivalentTo("locked", "id", "tier", "requiredTier", "items", "assumptions", "sources");
        endgame["locked"]!.Value<bool>().Should().BeTrue();
        endgame["requiredTier"]!.Value<string>().Should().Be("premium_plus");
        endgame["items"]!.Should().BeEmpty();
        var wholeView = ViewJson(access);
        foreach (var sentinel in EndgameSentinels)
            wholeView.Should().NotContain(sentinel);
        ViewJson(SetupsAccess.PremiumPlus).Should().Contain("lateGame\":{", "the check above is meaningful");
    }

    [Test]
    public void Premium_keeps_the_full_mid_details_but_not_the_endgame()
    {
        var view = Compose(FourTiers(), SetupsAccess.Premium);

        var mid = view.Setups.Single(s => s.Tier == "mid");
        mid.Locked.Should().BeFalse();
        mid.CoinsPerHour.Should().Be(FourTierModel.MidRate);
        mid.Score.Should().Be(350);
        mid.ScorePercentile.Should().Be(75);
        mid.Items.Single(i => i.Slot == "helmet").Acquisition.Should().NotBeNull();
        mid.LateGame.Should().BeNull("the cohort only travels with the endgame");
        view.ModifierEffects.Should().BeNull("pooled effects can describe endgame gear");
        view.Setups.Single(s => s.Tier == "endgame").Locked.Should().BeTrue();
    }

    [Test]
    public void Modifier_effects_are_premium_plus_only()
    {
        var model = FourTiers();

        Compose(model, SetupsAccess.Public).ModifierEffects.Should().BeNull();
        Compose(model, SetupsAccess.Premium).ModifierEffects.Should().BeNull();
        Compose(model, SetupsAccess.PremiumPlus).ModifierEffects.Should().NotBeEmpty();
    }

    [TestCase(SetupsAccess.Public)]
    [TestCase(SetupsAccess.Premium)]
    [TestCase(SetupsAccess.PremiumPlus)]
    public void Score_formula_is_shown_to_everyone(SetupsAccess access)
    {
        var formula = Compose(FourTiers(), access).ScoreFormula;

        formula.Kind.Should().Be("weighted-sum");
        formula.Expression.Should().Be("1 × Farming Fortune");
        formula.Weights.Should().ContainKey(FarmingFortune);
        Compose(Model(null, RisingEstimates()), access).ScoreFormula.Should().BeNull("nothing is measured");
    }

    /// <summary>Non-combat "other" tasks have no gear score: kind none, no distribution, curve, endgame or pieces</summary>
    [Test]
    public void Task_without_a_gear_score_shows_the_estimates_and_no_formula()
    {
        var gear = MeasuredTestData.Model(null, null, []);
        gear.ScoreFormula = new GearScoreFormulaInfo { Kind = "none", Weights = new(), Expression = "" };

        var view = Compose(Model(PricedModel(gear), RisingEstimates()), SetupsAccess.PremiumPlus);

        view.ScoreFormula.Should().BeNull();
        view.Setups.Should().OnlyContain(s => s.Basis == "estimate");
    }

    [TestCase(SetupsAccess.Public)]
    [TestCase(SetupsAccess.Premium)]
    public void Several_endgame_estimates_give_one_placeholder(SetupsAccess access)
    {
        GearSetupEstimate Endgame(string id) => new() { Id = id, Name = id, Tier = "endgame", Status = "upgrade", CoinsPerHour = 9_000_000 };
        var model = Model(null, new TaskGearSetups { TaskName = "Task A", Setups = [Endgame("end-1"), Endgame("end-2")] });

        var view = Compose(model, access);

        view.Setups.Should().ContainSingle().Which.Id.Should().Be("locked-endgame");
        Compose(model, SetupsAccess.PremiumPlus).Setups.Should().HaveCount(2);
    }

    [Test]
    public void Premium_plus_sees_the_full_endgame_with_its_late_game_cohort()
    {
        var view = Compose(FourTiers(), SetupsAccess.PremiumPlus);

        var endgame = view.Setups.Single(s => s.Tier == "endgame");
        endgame.Locked.Should().BeFalse();
        endgame.RequiredTier.Should().BeNull();
        endgame.CoinsPerHour.Should().Be(FourTierModel.EndRate);
        endgame.Items.Select(i => i.ItemTag).Should().Equal("END_HOE", "MID_HELMET");
        endgame.Score.Should().Be(500);
        endgame.ScorePercentile.Should().Be(97);
        endgame.RateBasis.Should().Be("curve");
        endgame.Extrapolated.Should().BeTrue();
        endgame.Capped.Should().BeFalse();
        endgame.Confidence.Should().Be("low", "an extrapolated rate");
        endgame.Players.Should().Be(FourTierModel.SecretPlayers, "the cohort's players");
        endgame.Hours.Should().Be(FourTierModel.SecretHours);
        endgame.StatMedians.Should().ContainKey(FarmingFortune);
        endgame.LateGame.CoinsPerHour.Should().Be(8_000_000);
        endgame.LateGame.Confidence.Should().Be("high");
        endgame.LateGame.StatMedians.Should().ContainKey(FarmingFortune);
        endgame.BuyCost.Should().Be(FourTierModel.EndCost + FourTierModel.MidCost);
    }

    [TestCase(SetupsAccess.Public)]
    [TestCase(SetupsAccess.Premium)]
    public void Curated_endgame_estimate_is_hidden_below_premium_plus(SetupsAccess access)
    {
        var estimates = new TaskGearSetups
        {
            TaskName = "Task A",
            Setups = [new GearSetupEstimate
            {
                Id = "est-end", Name = "Secret Estimate Name", Tier = "endgame", Status = "upgrade", CoinsPerHour = 9_990_000, Assumptions = ["secret assumption"],
                Sources = ["https://secret.example"], Items = [new GearSetupItem { ItemTag = "EST_END_ITEM", Name = "Est End Item", Slot = "tool", Tradeable = true }],
            }],
        };
        var model = Model(null, estimates);

        var json = ViewJson(access, model);
        var premiumPlus = ViewJson(SetupsAccess.PremiumPlus, model);

        foreach (var secret in new[] { "EST_END_ITEM", "Est End Item", "Secret Estimate Name", "secret assumption", "secret.example", "9990000", "est-end" })
        {
            json.Should().NotContain(secret);
            premiumPlus.Should().Contain(secret);
        }
        Json(Compose(model, access))["setups"]![0]!["requiredTier"]!.Value<string>().Should().Be("premium_plus");
        TaskSetupsComposer.QuotedEstimates(model.Plan, access).Should().BeEmpty("a hidden tier is not priced");
    }

    [Test]
    public void Details_view_unlocks_every_tier_with_modifier_effects()
    {
        var view = Compose(FourTiers(), SetupsAccess.PremiumPlus);

        view.Locked.Should().BeFalse();
        var mid = view.Setups.Single(s => s.Tier == "mid");
        mid.BuyCost.Should().Be(FourTierModel.BudgetCost + FourTierModel.MidCost, "nothing is owned without a measured baseline");
        mid.Items.Single(i => i.Slot == "helmet").Acquisition.Direct.Command.Should().Be("/viewauction " + MidUuid);
        mid.Items.Should().OnlyContain(i => i.Owned == false);
        view.Setups.Single(s => s.Tier == "endgame").Items.Single(i => i.Slot == "tool").Spec.Enchants.Should().ContainKey("harvesting");
        var effect = view.ModifierEffects.Should().ContainSingle().Which;
        effect.PercentChange.Should().Be(SecretEffect);
        effect.Players.Should().Be(2, "a rounded effect shows 2+ players");
        effect.PlayersAtLeast.Should().BeTrue();
        effect.PlayersLabel.Should().Be("2+");
    }

    [Test]
    public void Rounded_cohort_and_pieces_show_two_plus_players()
    {
        var model = FourTierModel.Model();
        model.LateGame = LateGame(8_000_000, players: 3, rounded: true);
        model.Pieces.Single(p => p.Id == "tool:BUDGET_HOE").Players = 3;
        model.Pieces.Single(p => p.Id == "tool:BUDGET_HOE").Rounded = true;

        var view = Compose(Model(FourTierModel.Priced(model)), SetupsAccess.PremiumPlus);

        var endgame = view.Setups.Single(s => s.Tier == "endgame");
        endgame.Players.Should().Be(2);
        endgame.PlayersAtLeast.Should().BeTrue();
        endgame.PlayersLabel.Should().Be("2+");
        endgame.LateGame.PlayersLabel.Should().Be("2+");
        endgame.LateGame.Confidence.Should().Be("low", "upstream says medium, that would tell the rounded player count");
        var tool = view.Setups.Single(s => s.Tier == "budget").Items.Single();
        tool.Players.Should().Be(2);
        tool.PlayersLabel.Should().Be("2+");
        tool.UsedAtScorePercentile.Should().BeNull("it would place the few players of the piece in the distribution");
        view.Setups.Single(s => s.Tier == "mid").Items.Single(i => i.Slot == "helmet").UsedAtScorePercentile.Should().Be(50);
        JsonConvert.SerializeObject(view).Should().NotContain("\"Players\":3", "the exact count of rounded numbers is never shown");
    }

    [Test]
    public void Mixed_view_locks_estimated_mid_and_endgame_and_marks_their_basis()
    {
        var model = MeasuredTestData.Model(null, FourTierModel.Model().Curve, [FourTierModel.BudgetHoe()]);
        var setups = Model(PricedModel(model, Priced(model.Pieces[0], FourTierModel.BudgetCost)), RisingEstimates());

        var view = Compose(setups, SetupsAccess.Public);
        var details = Compose(setups, SetupsAccess.PremiumPlus);

        view.Baseline.Basis.Should().Be("estimate");
        view.Setups.Single(s => s.Tier == "budget").Basis.Should().Be("measured");
        var mid = view.Setups.Single(s => s.Tier == "mid");
        mid.Basis.Should().Be("estimate");
        mid.Locked.Should().BeTrue();
        mid.CoinsPerHour.Should().BeNull();
        details.Setups.Single(s => s.Tier == "mid").DeltaCoinsPerHour.Should().Be(2_000_000, "the curated gain");
        view.ComparisonCoinsPerHour.Should().Be(1_000_000);
        TaskSetupsComposer.QuotedEstimates(setups.Plan, SetupsAccess.Public).Should().BeEmpty("only locked tiers are estimated");
        TaskSetupsComposer.QuotedEstimates(setups.Plan, SetupsAccess.PremiumPlus).Select(s => s.Tier).Should().Equal("mid", "endgame");
    }

    [TestCase(4, true, "medium", "low")]
    [TestCase(3, true, "medium", "low")]
    [TestCase(2, true, "low", "low")]
    [TestCase(6, false, "medium", "medium")]
    [TestCase(6, false, "", "low")]
    public void Rounded_confidence_does_not_tell_the_player_count(int players, bool rounded, string upstream, string shown)
        => MeasuredSetupMapper.ConfidenceOf(upstream, players, rounded).Should().Be(shown);

    [Test]
    public void Lower_bound_prices_are_flagged_on_the_item_and_the_setup()
    {
        var tool = Priced(Entry("tool", "HOE", 100), 2_000_000, lowerBound: true);
        var helmet = Priced(Entry("helmet", "HELM", 50), 1_000_000);
        var build = new PricedBuild([tool, helmet], 1_500_000, 150, 40, RateBases.Curve, false, "medium");
        var plan = new TierPlan(null, [new TierChoice("budget", build, null, 500_000, 1_500_000)], 1_000_000, 1_000_000);

        var view = Compose(FourTiers() with { Plan = plan }, SetupsAccess.Public).Setups.Single();

        view.PriceLowerBound.Should().BeTrue();
        view.Items.Single(i => i.Slot == "tool").PriceLowerBound.Should().BeTrue();
        view.Items.Single(i => i.Slot == "helmet").PriceLowerBound.Should().BeFalse();
    }

    [Test]
    public void Late_game_cohort_is_never_mapped_onto_another_tier()
    {
        var tool = Priced(Entry("tool", "HOE", 100), 2_000_000);
        var build = new PricedBuild([tool], 1_500_000, 150, 40, RateBases.Curve, false, "medium", LateGame: LateGame(8_000_000, players: 4321));
        var plan = new TierPlan(null, [new TierChoice("mid", build, null, 500_000, 1_500_000)], 1_000_000, 1_000_000);

        var mid = Compose(FourTiers() with { Plan = plan }, SetupsAccess.PremiumPlus).Setups.Single();

        mid.LateGame.Should().BeNull();
        mid.Players.Should().BeNull();
        mid.StatMedians.Should().BeNull();
    }

    /// <summary>Regression: a missing resale price also blanked the buy cost, which is known from the acquisitions</summary>
    [Test]
    public void Buy_cost_is_shown_without_a_resale_price()
    {
        var gear = FourTierModel.Priced();
        var pieces = gear.Pieces.ToDictionary(p => p.Key, p => p.Value with { Quote = p.Value.Quote with { Resale = null } });

        var setup = Compose(Model(gear with { Pieces = pieces }), SetupsAccess.Public).Setups.Single(s => s.Tier == "budget");

        setup.BuyCost.Should().Be(FourTierModel.BudgetCost);
        setup.PriceIncomplete.Should().BeTrue();
        setup.ResaleValue.Should().BeNull();
        setup.PaybackHoursBorrow.Should().BeNull();
    }

    /// <summary>Regression: the id was a 48 bit hash of the cluster key, a locked tier's modifiers could be brute forced from it</summary>
    [Test]
    public void Measured_setup_id_does_not_derive_from_the_pieces()
    {
        var first = Compose(FourTiers(), SetupsAccess.PremiumPlus);
        var model = FourTierModel.Model();
        model.Pieces.Single(p => p.Id == "tool:END_HOE").Item.Enchants["harvesting"] = 7;
        var second = Compose(Model(FourTierModel.Priced(model)), SetupsAccess.PremiumPlus);

        first.Setups.Single(s => s.Tier == "endgame").Id.Should().Be("measured-endgame");
        second.Setups.Single(s => s.Tier == "endgame").Id.Should().Be("measured-endgame");
        first.Setups.Single(s => s.Tier == "budget").Id.Should().Be("measured-budget");
    }

    private static SetupsModel UnratedEndgame()
    {
        var hoe = Entry("tool", "END_HOE", 400);
        var model = MeasuredTestData.Model(null, null, [hoe], Recommended(500, -9_700_000, hoe), LateGame(-9_700_000));
        return Model(PricedModel(model, Priced(hoe, 10_000_000)));
    }

    /// <summary>Regression: a hidden or unrated endgame left the tier list empty, not even the locked placeholder was shown</summary>
    [TestCase(SetupsAccess.Public)]
    [TestCase(SetupsAccess.Premium)]
    public void Unrated_endgame_is_the_locked_placeholder_below_premium_plus(SetupsAccess access)
    {
        var view = Compose(UnratedEndgame(), access);

        var endgame = view.Setups.Single();
        endgame.Locked.Should().BeTrue();
        endgame.Id.Should().Be("locked-endgame");
        endgame.CoinsPerHour.Should().BeNull();
        endgame.Items.Should().BeNullOrEmpty();
        JsonConvert.SerializeObject(view).Should().NotContain("END_HOE").And.NotContain("9700000");
    }

    [Test]
    public void Unrated_endgame_has_no_row_for_premium_plus()
    {
        var view = Compose(UnratedEndgame(), SetupsAccess.PremiumPlus);

        view.Setups.Should().BeEmpty("premium plus would otherwise see a locked premium plus row");
        JsonConvert.SerializeObject(view).Should().NotContain("9700000");
    }
}
