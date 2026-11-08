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
using static SkyApi.Services.Tasks.Setups.Measured.MeasuredTestData;

namespace SkyApi.Services.Tasks.Setups;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class TaskSetupsComposerTests
{
    public const string MidUuid = "11111111111111111111111111111111";
    public const string EndUuid = "22222222222222222222222222222222";
    public const string BudgetUuid = "33333333333333333333333333333333";
    public const double MidRate = 7_770_000;
    public const double EndRate = 8_880_000;
    public const long MidCost = 55_555_555;
    public const long EndCost = 444_444_444;
    public const int SecretPlayers = 4321;
    public const double SecretHours = 9876.5;
    public const double SecretStat = 31337;
    public const double SecretEffect = 61.803;

    /// <summary>Strings of the locked tiers and of details only data, none of them may be in the public view</summary>
    public static readonly string[] Sentinels =
    [
        MidUuid, EndUuid, "7770000", "8880000", "6770000", "7880000", "55555555", "444444444",
        "4321", "9876.5", "31337", "61.803", "harvesting",
    ];

    private static PricedPiece PieceOf(MeasuredPiece piece, long cost, string uuid)
        => new(piece, Direct(cost, uuid), Quote(piece.Tag, cost), cost, CostSources.Direct, false);

    private static PricedCluster Measured(GearCluster cluster, long toolCost, string uuid, long helmetCost = 1_000_000)
    {
        cluster.StatMedians = new Dictionary<string, double> { ["Farming Fortune"] = SecretStat };
        var pieces = MeasuredPiece.Of(cluster)
            .Select(p => p.Slot == "tool" ? PieceOf(p, toolCost, uuid) : PieceOf(p, helmetCost, "66666666666666666666666666666666"))
            .ToList();
        return new PricedCluster(cluster, pieces);
    }

    private static GearCluster Secret(string tool, double rate, string helmet)
    {
        var cluster = Cluster(tool, rate, players: SecretPlayers, helmet: helmet,
            toolPiece: Piece(tool, new Dictionary<string, int> { ["harvesting"] = 6 }));
        cluster.Hours = SecretHours;
        return cluster;
    }

    public static List<PricedCluster> FourMeasured() =>
    [
        Measured(Cluster("BASE_HOE", 1_000_000), 500_000, "77777777777777777777777777777777"),
        Measured(Cluster("BUDGET_HOE", 1_500_000), 5_000_000, BudgetUuid),
        Measured(Secret("MID_HOE", MidRate, "MID_HELMET"), MidCost, MidUuid),
        Measured(Secret("END_HOE", EndRate, "END_HELMET"), EndCost, EndUuid),
    ];

    public static readonly List<GearModifierEffect> Effects =
    [
        new() { Changes = [new GearKeyChange { Component = "tool.enchants.harvesting", From = "5", To = "6" }], Players = 3, Pairs = 4, Ratio = 1.61803, PercentChange = SecretEffect, Rounded = true },
    ];

    public static SetupsModel Model(List<PricedCluster> clusters, TaskGearSetups estimates = null) => new(
        "task-a", "Task A", "farming", null, estimates, SetupTierSelector.Select(clusters, estimates), Effects,
        new Dictionary<string, string>(), false);

    private static JObject Json(PublicTaskSetups value)
        => JObject.Parse(JsonConvert.SerializeObject(value, new Microsoft.AspNetCore.Mvc.MvcNewtonsoftJsonOptions().SerializerSettings));

    private static Dictionary<string, JToken> ByTier(JObject json)
        => ((JArray)json["setups"]!).Append(json["baseline"]!).ToDictionary(s => s["tier"]!.Value<string>()!);

    [Test]
    public void Public_view_contains_no_locked_number_uuid_or_details_only_data()
    {
        var model = Model(FourMeasured());

        var publicJson = JsonConvert.SerializeObject(TaskSetupsComposer.Compose(model, new Dictionary<string, ItemResaleQuote>(), SetupsAccess.Public));
        var detailsJson = JsonConvert.SerializeObject(TaskSetupsComposer.Compose(model, new Dictionary<string, ItemResaleQuote>(), SetupsAccess.PremiumPlus));

        foreach (var sentinel in Sentinels)
        {
            publicJson.Should().NotContain(sentinel);
            detailsJson.Should().Contain(sentinel, "the details view carries it, so the public check is meaningful");
        }
        publicJson.Should().Contain(BudgetUuid, "the budget acquisition is free");
    }

    [Test]
    public void Public_view_has_numbers_and_acquisition_for_baseline_and_budget_only()
    {
        var json = Json(TaskSetupsComposer.Compose(Model(FourMeasured()), new Dictionary<string, ItemResaleQuote>(), SetupsAccess.Public));

        json["locked"]!.Value<bool>().Should().BeTrue();
        json["modifierEffects"]!.Type.Should().Be(JTokenType.Null);
        json["baselineCoinsPerHour"]!.Value<double>().Should().Be(1_000_000);
        var tiers = ByTier(json);
        foreach (var free in new[] { "baseline", "budget" })
        {
            tiers[free]["locked"]!.Value<bool>().Should().BeFalse();
            tiers[free]["basis"]!.Value<string>().Should().Be("measured");
            tiers[free]["players"]!.Value<int>().Should().Be(6);
            tiers[free]["statMedians"]!.Type.Should().Be(JTokenType.Null, "stat medians are details only");
            tiers[free]["items"]!.Should().OnlyContain(i => i["acquisition"]!.Type == JTokenType.Object);
        }
        tiers["budget"]["deltaCoinsPerHour"]!.Value<double>().Should().Be(500_000);
        tiers["budget"]["buyCost"]!.Value<double>().Should().Be(5_000_000, "the baseline helmet is owned");
        foreach (var locked in new[] { "mid" })
        {
            var setup = tiers[locked];
            setup["locked"]!.Value<bool>().Should().BeTrue();
            setup["basis"]!.Value<string>().Should().Be("measured");
            foreach (var field in new[] { "coinsPerHour", "deltaCoinsPerHour", "buyCost", "players", "hours", "observations", "statMedians" })
                setup[field]!.Type.Should().Be(JTokenType.Null, field);
            setup["items"]!.Should().OnlyContain(i => i["acquisition"]!.Type == JTokenType.Null && i["spec"]!.Type == JTokenType.Null
                && i["buyPrice"]!.Type == JTokenType.Null);
        }
    }

    /// <summary>Everything that identifies the endgame cluster: tags, names, uuids, rates, costs, the secret player count and the raw cluster fields</summary>
    private static readonly string[] EndgameSentinels =
        ["END_HOE", "END_HELMET", "End Hoe", "End Helmet", "end hoe", EndUuid, "8880000", "7880000", "444444444", "445444444"];

    private static string ViewJson(SetupsAccess access, SetupsModel model = null)
        => JsonConvert.SerializeObject(TaskSetupsComposer.Compose(model ?? Model(FourMeasured()), new Dictionary<string, ItemResaleQuote>(), access));

    [TestCase(SetupsAccess.Public)]
    [TestCase(SetupsAccess.Premium)]
    public void Endgame_is_a_bare_placeholder_below_premium_plus(SetupsAccess access)
    {
        var json = Json(TaskSetupsComposer.Compose(Model(FourMeasured()), new Dictionary<string, ItemResaleQuote>(), access));

        var endgame = ByTier(json)["endgame"];
        endgame.Children<JProperty>().Where(p => p.Value.Type != JTokenType.Null)
            .Select(p => p.Name).Should().BeEquivalentTo("locked", "id", "tier", "requiredTier", "items", "assumptions", "sources");
        endgame["locked"]!.Value<bool>().Should().BeTrue();
        endgame["requiredTier"]!.Value<string>().Should().Be("premium_plus");
        endgame["items"]!.Should().BeEmpty();
        endgame["assumptions"]!.Should().BeEmpty();
        endgame["sources"]!.Should().BeEmpty();
        var wholeView = ViewJson(access);
        foreach (var sentinel in EndgameSentinels)
            wholeView.Should().NotContain(sentinel);
    }

    [Test]
    public void Premium_keeps_the_full_mid_details_but_not_the_endgame()
    {
        var view = TaskSetupsComposer.Compose(Model(FourMeasured()), new Dictionary<string, ItemResaleQuote>(), SetupsAccess.Premium);

        var mid = view.Setups.Single(s => s.Tier == "mid");
        mid.Locked.Should().BeFalse();
        mid.StatMedians.Should().ContainKey("Farming Fortune");
        mid.Items.Single(i => i.Slot == "tool").Acquisition.Should().NotBeNull();
        view.ModifierEffects.Should().BeNull("pooled effects can describe endgame gear");
        view.Setups.Single(s => s.Tier == "endgame").Locked.Should().BeTrue();
    }

    [Test]
    public void Modifier_effects_are_premium_plus_only()
    {
        var model = Model(FourMeasured());

        TaskSetupsComposer.Compose(model, new Dictionary<string, ItemResaleQuote>(), SetupsAccess.Public).ModifierEffects.Should().BeNull();
        TaskSetupsComposer.Compose(model, new Dictionary<string, ItemResaleQuote>(), SetupsAccess.Premium).ModifierEffects.Should().BeNull();
        TaskSetupsComposer.Compose(model, new Dictionary<string, ItemResaleQuote>(), SetupsAccess.PremiumPlus).ModifierEffects.Should().NotBeEmpty();
    }

    [TestCase(SetupsAccess.Public)]
    [TestCase(SetupsAccess.Premium)]
    public void Several_endgame_estimates_give_one_placeholder(SetupsAccess access)
    {
        GearSetupEstimate Endgame(string id) => new() { Id = id, Name = id, Tier = "endgame", Status = "upgrade", CoinsPerHour = 9_000_000 };
        var model = Model([], new TaskGearSetups { TaskName = "Task A", Setups = [Endgame("end-1"), Endgame("end-2")] });

        var view = TaskSetupsComposer.Compose(model, new Dictionary<string, ItemResaleQuote>(), access);

        view.Setups.Should().ContainSingle().Which.Id.Should().Be("locked-endgame");
        TaskSetupsComposer.Compose(model, new Dictionary<string, ItemResaleQuote>(), SetupsAccess.PremiumPlus).Setups.Should().HaveCount(2);
    }

    [Test]
    public void Premium_plus_sees_the_full_endgame()
    {
        var view = TaskSetupsComposer.Compose(Model(FourMeasured()), new Dictionary<string, ItemResaleQuote>(), SetupsAccess.PremiumPlus);

        var endgame = view.Setups.Single(s => s.Tier == "endgame");
        endgame.Locked.Should().BeFalse();
        endgame.RequiredTier.Should().BeNull();
        endgame.CoinsPerHour.Should().Be(EndRate);
        endgame.Items.Select(i => i.ItemTag).Should().Contain(["END_HOE", "END_HELMET"]);
        endgame.StatMedians.Should().NotBeEmpty();
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
        var model = Model([], estimates);

        var json = ViewJson(access, model);
        var premiumPlus = ViewJson(SetupsAccess.PremiumPlus, model);

        foreach (var secret in new[] { "EST_END_ITEM", "Est End Item", "Secret Estimate Name", "secret assumption", "secret.example", "9990000", "est-end" })
        {
            json.Should().NotContain(secret);
            premiumPlus.Should().Contain(secret);
        }
        Json(TaskSetupsComposer.Compose(model, new Dictionary<string, ItemResaleQuote>(), access))["setups"]![0]!["requiredTier"]!
            .Value<string>().Should().Be("premium_plus");
        TaskSetupsComposer.QuotedEstimates(model.Plan, access).Should().BeEmpty("a hidden tier is not priced");
    }

    [Test]
    public void Details_view_unlocks_every_tier_with_stat_medians_and_modifier_effects()
    {
        var view = TaskSetupsComposer.Compose(Model(FourMeasured()), new Dictionary<string, ItemResaleQuote>(), SetupsAccess.PremiumPlus);

        view.Locked.Should().BeFalse();
        var mid = view.Setups.Single(s => s.Tier == "mid");
        mid.StatMedians.Should().ContainKey("Farming Fortune");
        mid.BuyCost.Should().Be(MidCost + 1_000_000, "its helmet is not the baseline's");
        mid.Items.Single(i => i.Slot == "tool").Acquisition.Direct.Command.Should().Be("/viewauction " + MidUuid);
        mid.Items.Single(i => i.Slot == "tool").Spec.Enchants.Should().ContainKey("harvesting");
        view.Baseline.StatMedians.Should().NotBeNull();
        var effect = view.ModifierEffects.Should().ContainSingle().Which;
        effect.PercentChange.Should().Be(SecretEffect);
        effect.Players.Should().Be(2, "a rounded effect shows 2+ players");
        effect.PlayersAtLeast.Should().BeTrue();
        effect.PlayersLabel.Should().Be("2+");
    }

    [Test]
    public void Rounded_cluster_shows_two_plus_players()
    {
        var clusters = FourMeasured();
        clusters[1] = Measured(Cluster("BUDGET_HOE", 1_500_000, players: 4, rounded: true), 5_000_000, BudgetUuid);
        var banded = Measured(Cluster("BASE_HOE", 1_000_000, players: 3, rounded: true), 500_000, "77777777777777777777777777777777");
        banded.Cluster.ObservationsLabel = "5+";
        clusters[0] = banded;

        var budget = TaskSetupsComposer.Compose(Model(clusters), new Dictionary<string, ItemResaleQuote>(), SetupsAccess.Public)
            .Setups.Single(s => s.Tier == "budget");

        budget.Players.Should().Be(2);
        budget.PlayersAtLeast.Should().BeTrue();
        budget.PlayersLabel.Should().Be("2+");
        budget.ObservationsLabel.Should().Be("12", "without a band label the count is shown");
        budget.Confidence.Should().Be("low");
        var view = TaskSetupsComposer.Compose(Model(clusters), new Dictionary<string, ItemResaleQuote>(), SetupsAccess.Public);
        view.Baseline.ObservationsLabel.Should().Be("5+");
        view.Baseline.PlayersLabel.Should().Be("2+");
        JsonConvert.SerializeObject(view.Baseline).Should().NotContain("\"Players\":3", "the exact count of a rounded cluster is never shown");
    }

    [Test]
    public void Owned_baseline_items_are_flagged_and_left_out_of_the_cost()
    {
        var budget = TaskSetupsComposer.Compose(Model(FourMeasured()), new Dictionary<string, ItemResaleQuote>(), SetupsAccess.Public)
            .Setups.Single(s => s.Tier == "budget");

        budget.Items.Single(i => i.Slot == "helmet").Owned.Should().BeTrue();
        budget.Items.Single(i => i.Slot == "helmet").BuyPrice.Should().Be(1_000_000, "the price to get it is still shown");
        budget.Items.Single(i => i.Slot == "tool").Owned.Should().BeFalse();
        budget.PaybackHoursFull.Should().Be(10, "5M at +500k/h");
    }

    [Test]
    public void Mixed_view_locks_estimated_mid_and_endgame_and_marks_their_basis()
    {
        var model = Model(FourMeasured().Take(2).ToList(), RisingEstimates());

        var view = TaskSetupsComposer.Compose(model, new Dictionary<string, ItemResaleQuote>(), SetupsAccess.Public);
        var details = TaskSetupsComposer.Compose(model, new Dictionary<string, ItemResaleQuote>(), SetupsAccess.PremiumPlus);

        view.Baseline.Basis.Should().Be("measured");
        view.Setups.Single(s => s.Tier == "budget").Basis.Should().Be("measured");
        var mid = view.Setups.Single(s => s.Tier == "mid");
        mid.Basis.Should().Be("estimate");
        mid.Locked.Should().BeTrue();
        mid.CoinsPerHour.Should().BeNull();
        details.Setups.Single(s => s.Tier == "mid").DeltaCoinsPerHour.Should().Be(2_000_000, "3M modelled minus the measured 1M baseline");
        view.ComparisonCoinsPerHour.Should().Be(1_000_000);
        TaskSetupsComposer.QuotedEstimates(model.Plan, SetupsAccess.Public).Should().BeEmpty("only locked tiers are estimated");
        TaskSetupsComposer.QuotedEstimates(model.Plan, SetupsAccess.PremiumPlus).Select(s => s.Tier).Should().Equal("mid", "endgame");
    }

    [TestCase(4, true, "medium")]
    [TestCase(3, true, "medium")]
    [TestCase(2, true, "low")]
    [TestCase(6, false, "medium")]
    public void Rounded_cluster_confidence_does_not_tell_the_player_count(int players, bool rounded, string upstream)
    {
        var cluster = Cluster("X", 1, players: players, rounded: rounded);
        cluster.Confidence = upstream;

        Coflnet.Sky.Api.Services.Tasks.Setups.Measured.MeasuredSetupMapper.ConfidenceOf(cluster)
            .Should().Be(players < 5 ? "low" : upstream);
    }

    [Test]
    public void Lower_bound_prices_are_flagged_on_the_item_and_the_setup()
    {
        var clusters = FourMeasured();
        var budget = clusters[1];
        clusters[1] = budget with { Pieces = budget.Pieces.Select(p => p.Piece.Slot == "tool" ? p with { LowerBound = true } : p).ToList() };
        var plan = new TierPlan(null, [new TierChoice("budget", clusters[1], null, 500_000, 1_500_000)], 1_000_000, new HashSet<string>(), 1_000_000);
        var model = Model(clusters) with { Plan = plan };

        var view = TaskSetupsComposer.Compose(model, new Dictionary<string, ItemResaleQuote>(), SetupsAccess.Public).Setups.Single();

        view.PriceLowerBound.Should().BeTrue();
        view.Items.Single(i => i.Slot == "tool").PriceLowerBound.Should().BeTrue();
        view.Items.Single(i => i.Slot == "helmet").PriceLowerBound.Should().BeFalse();
    }

    /// <summary>Regression: a missing resale price also blanked the buy cost, which is known from the acquisitions</summary>
    [Test]
    public void Buy_cost_is_shown_without_a_resale_price()
    {
        var clusters = FourMeasured();
        var budget = clusters[1];
        clusters[1] = budget with { Pieces = budget.Pieces.Select(p => p with { Quote = p.Quote with { Resale = null } }).ToList() };

        var setup = TaskSetupsComposer.Compose(Model(clusters), new Dictionary<string, ItemResaleQuote>(), SetupsAccess.Public)
            .Setups.Single(s => s.Tier == "budget");

        setup.BuyCost.Should().Be(5_000_000);
        setup.PriceIncomplete.Should().BeTrue();
        setup.ResaleValue.Should().BeNull();
        setup.PaybackHoursBorrow.Should().BeNull();
    }

    /// <summary>Regression: estimate tiers subtracted the curated baseline's items while the measured baseline was shown</summary>
    [Test]
    public void Estimate_tier_owns_the_items_of_the_measured_baseline_shown()
    {
        var estimates = RisingEstimates();
        estimates.Setups.Single(s => s.Tier == "mid").Items =
            [PublicSetupsMapperTests.Item("BASE_HOE"), PublicSetupsMapperTests.Item("OLD"), PublicSetupsMapperTests.Item("M")];
        var model = Model(FourMeasured().Take(2).ToList(), estimates);
        var keys = TaskSetupsComposer.DisplayedBaselineKeys(model.Plan);
        var quotes = new[] { "BASE_HOE", "OLD", "M" }.ToDictionary(t => PublicSetupsMapper.QuoteKey(t, null), t => PublicSetupsMapperTests.Quote(t, 100, 90));

        var targets = PublicSetupsMapper.QuoteTargets(estimates, TaskSetupsComposer.QuotedEstimates(model.Plan, SetupsAccess.PremiumPlus), keys);
        var mid = TaskSetupsComposer.Compose(model, quotes, SetupsAccess.PremiumPlus).Setups.Single(s => s.Tier == "mid");

        targets.Select(t => t.Tag).Should().BeEquivalentTo(new[] { "OLD", "M", "E" }, "the measured baseline's hoe is owned, the curated OLD is not");
        mid.BuyCost.Should().Be(200);
        mid.Items.Single(i => i.ItemTag == "BASE_HOE").BuyPrice.Should().BeNull();
    }

    /// <summary>Regression: the id was a 48 bit hash of the cluster key, a locked tier's modifiers could be brute forced from it</summary>
    [Test]
    public void Measured_setup_id_does_not_derive_from_the_cluster_key()
    {
        var first = TaskSetupsComposer.Compose(Model(FourMeasured()), new Dictionary<string, ItemResaleQuote>(), SetupsAccess.PremiumPlus);
        var clusters = FourMeasured();
        clusters[3].Cluster.Key += "|tool.enchants.harvesting=7";
        var second = TaskSetupsComposer.Compose(Model(clusters), new Dictionary<string, ItemResaleQuote>(), SetupsAccess.PremiumPlus);

        first.Setups.Single(s => s.Tier == "endgame").Id.Should().Be("measured-endgame");
        second.Setups.Single(s => s.Tier == "endgame").Id.Should().Be("measured-endgame");
        first.Baseline.Id.Should().Be("measured-baseline");
    }
}
