using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Api.Services.Tasks.Setups;
using Coflnet.Sky.PlayerState.Client.Model;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace SkyApi.Services.Tasks.Setups;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class PublicSetupsMapperTests
{
    private static readonly Dictionary<string, string> Slugs = new() { ["Mining Gems"] = "mining-gems" };

    internal static GearSetupItem Item(string tag, bool tradeable = true, Dictionary<string, string> filters = null)
        => new() { Slot = "slot-" + tag, ItemTag = tag, Name = "Name " + tag, Reason = "r", Tradeable = tradeable, PriceFilters = filters };

    internal static GearSetupEstimate Setup(string id, string status, double? delta, params GearSetupItem[] items) => new()
    {
        Id = id,
        Name = id,
        Tier = id,
        Status = status,
        Items = items.ToList(),
        CoinsPerHour = delta == null ? null : 1_000_000 + delta,
        DeltaCoinsPerHour = delta,
        Multiplier = 1.5,
        Confidence = "medium",
        Assumptions = ["a"],
        Sources = ["https://example.com"],
        UnlocksTask = id == "mid" ? "Mining Gems" : null,
    };

    internal static TaskGearSetups Gear(GearSetupEstimate baseline, params GearSetupEstimate[] setups) => new()
    {
        TaskName = "Task",
        ProfileName = "Profile",
        BaselineCoinsPerHour = 1_000_000,
        Baseline = baseline,
        Setups = setups.ToList(),
    };

    internal static ItemResaleQuote Quote(string tag, double buy, double resale, StabilityLabel label = StabilityLabel.Stable)
        => new(tag, buy, buy * 0.9, resale, 0.02, -0.01, resale, 12, label);

    private static Dictionary<string, ItemResaleQuote> Quotes(params ItemResaleQuote[] quotes)
        => quotes.ToDictionary(q => PublicSetupsMapper.QuoteKey(q.Tag, null));

    private static readonly string[] NumberFields =
    [
        "coinsPerHour", "deltaCoinsPerHour", "multiplier", "buyCost", "resaleValue", "borrowCost", "resaleRatio",
        "paybackHoursBorrow", "paybackHoursFull", "paybackTooLong", "netGain10h", "netGain50h", "stability", "priceIncomplete", "noProfitGain",
        "buyPrice", "cv", "trendPerWeek", "p10", "volumePerDay",
    ];

    /// <summary>Test only mapping of the curated setups alone, production goes through <see cref="TaskSetupsComposer"/></summary>
    private static PublicTaskSetups ToPublic(string slug, TaskGearSetups upstream, IReadOnlyDictionary<string, string> taskSlugs,
        IReadOnlyDictionary<string, ItemResaleQuote> quotes) => MapAll(slug, upstream, taskSlugs, quotes, false);

    private static PublicTaskSetups ToPremium(string slug, TaskGearSetups upstream, IReadOnlyDictionary<string, string> taskSlugs,
        IReadOnlyDictionary<string, ItemResaleQuote> quotes) => MapAll(slug, upstream, taskSlugs, quotes, true);

    private static PublicTaskSetups MapAll(string slug, TaskGearSetups upstream, IReadOnlyDictionary<string, string> taskSlugs,
        IReadOnlyDictionary<string, ItemResaleQuote> quotes, bool all)
    {
        PublicGearSetup Map(GearSetupEstimate s) => PublicSetupsMapper.MapEstimate(s, upstream, taskSlugs, quotes,
            all || PublicSetupsMapper.IsFreeTier(s), s?.DeltaCoinsPerHour);
        var result = new PublicTaskSetups
        {
            Slug = slug,
            TaskName = upstream.TaskName,
            ProfileName = upstream.ProfileName,
            Baseline = Map(upstream.Baseline),
            Setups = (upstream.Setups ?? new()).Select(Map).Where(PublicSetupsMapper.IsRecommended).ToList(),
        };
        if (all || result.Baseline == null || !result.Baseline.Locked)
            result.BaselineCoinsPerHour = upstream.BaselineCoinsPerHour;
        result.Locked = result.Setups.Append(result.Baseline).Any(x => x is { Locked: true });
        return result;
    }

    private static JObject Json(object value)
        => JObject.Parse(JsonConvert.SerializeObject(value, new Microsoft.AspNetCore.Mvc.MvcNewtonsoftJsonOptions().SerializerSettings));

    private static bool HasNumbers(JToken setup)
        => ((JContainer)setup).Descendants().OfType<JProperty>().Any(p => NumberFields.Contains(p.Name) && p.Value.Type != JTokenType.Null);

    private static TaskGearSetups FourTiers(string budgetStatus = "upgrade") => Gear(
        Setup("baseline", "baseline", 0, Item("OLD")),
        Setup("budget", budgetStatus, 10_000, Item("B")),
        Setup("mid", "upgrade", 50_000, Item("M")),
        Setup("endgame", "upgrade", 90_000, Item("E")));

    private static Dictionary<string, ItemResaleQuote> AllQuotes()
        => Quotes(Quote("B", 100, 90), Quote("M", 200, 180), Quote("E", 300, 270));

    [TestCase("upgrade")]
    [TestCase("reached")]
    public void Public_view_has_numbers_for_baseline_and_budget_only(string budgetStatus)
    {
        var json = Json(ToPublic("slug", FourTiers(budgetStatus), Slugs, AllQuotes()));

        json["locked"]!.Value<bool>().Should().BeTrue("a mid and an endgame setup are locked");
        json["baselineCoinsPerHour"]!.Value<double>().Should().Be(1_000_000);
        var byTier = ((JArray)json["setups"]!).ToDictionary(s => s["tier"]!.Value<string>()!);
        HasNumbers(json["baseline"]!).Should().BeTrue();
        json["baseline"]!["locked"]!.Value<bool>().Should().BeFalse();
        HasNumbers(byTier["budget"]).Should().BeTrue("the tier decides, not the status");
        byTier["budget"]["locked"]!.Value<bool>().Should().BeFalse();
        byTier["budget"]["buyCost"]!.Value<double>().Should().Be(100);
        foreach (var tier in new[] { "mid", "endgame" })
        {
            HasNumbers(byTier[tier]).Should().BeFalse(tier);
            byTier[tier]["locked"]!.Value<bool>().Should().BeTrue(tier);
        }
    }

    [Test]
    public void Public_view_is_not_locked_when_every_setup_is_free()
    {
        var gear = Gear(Setup("baseline", "baseline", 0), Setup("budget", "upgrade", 1, Item("B")));

        var result = ToPublic("slug", gear, Slugs, Quotes(Quote("B", 100, 90)));

        result.Locked.Should().BeFalse();
    }

    [Test]
    public void Public_view_does_not_need_quotes_of_locked_tiers()
    {
        PublicSetupsMapper.QuoteTargets(FourTiers(), all: false).Select(t => t.Tag).Should().Equal("B");
        PublicSetupsMapper.QuoteTargets(FourTiers()).Select(t => t.Tag).Should().Equal("B", "M", "E");
    }

    [Test]
    public void Public_view_never_leaks_locked_numbers_even_when_quotes_for_them_exist()
    {
        var json = Json(ToPublic("slug", FourTiers(), Slugs, AllQuotes()));

        var text = json.ToString();
        text.Should().NotContain("\"buyPrice\": 200").And.NotContain("\"buyPrice\": 300");
        json["setups"]![2]!["items"]![0]!["buyPrice"]!.Type.Should().Be(JTokenType.Null);
    }

    [Test]
    public void Premium_view_unlocks_every_setup()
    {
        var result = ToPremium("slug", FourTiers(), Slugs, AllQuotes());

        result.Locked.Should().BeFalse();
        result.Setups.Should().OnlyContain(s => !s.Locked && s.BuyCost != null);
    }

    [Test]
    public void Public_setup_keeps_the_public_fields_and_resolves_the_unlocked_task_slug()
    {
        var gear = Gear(Setup("baseline", "baseline", 0, Item("OLD")), Setup("mid", "upgrade", 50_000, Item("NEW")));

        var result = ToPublic("slug", gear, Slugs, Quotes());

        result.Slug.Should().Be("slug");
        result.ProfileName.Should().Be("Profile");
        var mid = result.Setups.Single();
        mid.UnlocksTask.Should().Be("mining-gems");
        mid.Confidence.Should().Be("medium");
        mid.Sources.Should().Equal("https://example.com");
        mid.Items.Single().Should().BeEquivalentTo(new { Slot = "slot-NEW", ItemTag = "NEW", ItemName = "Name NEW", Reason = "r", Tradeable = true });
    }

    [Test]
    public void Unresolvable_unlocked_task_is_null()
    {
        var gear = Gear(Setup("baseline", "baseline", 0), Setup("mid", "upgrade", 1));

        ToPublic("slug", gear, new Dictionary<string, string>(), Quotes()).Setups.Single().UnlocksTask.Should().BeNull();
    }

    [Test]
    public void Premium_subtracts_baseline_items_from_the_cost()
    {
        var gear = Gear(Setup("baseline", "baseline", 0, Item("OLD")),
            Setup("budget", "upgrade", 10_000, Item("OLD"), Item("NEW")));
        var quotes = Quotes(Quote("OLD", 5_000_000, 4_000_000), Quote("NEW", 100_000, 90_000));

        var setup = ToPremium("slug", gear, Slugs, quotes).Setups.Single();

        setup.BuyCost.Should().Be(100_000);
        setup.ResaleValue.Should().Be(90_000);
        setup.BorrowCost.Should().BeApproximately(10_000, 0.001);
        setup.PaybackHoursBorrow.Should().BeApproximately(1, 0.0001);
        setup.PaybackHoursFull.Should().BeApproximately(10, 0.0001);
        setup.NetGain10h.Should().BeApproximately(90_000, 0.001);
        setup.Items.Single(i => i.ItemTag == "OLD").BuyPrice.Should().BeNull("owned items cost nothing");
        setup.Items.Single(i => i.ItemTag == "NEW").BuyPrice.Should().Be(100_000);
    }

    [Test]
    public void Premium_on_a_tier_profile_also_subtracts_the_highest_reached_setup()
    {
        var gear = Gear(Setup("baseline", "baseline", 0, Item("BASE")),
            Setup("tier1", "reached", 0, Item("T1")),
            Setup("tier2", "reached", 0, Item("T2")),
            Setup("budget", "upgrade", 10_000, Item("T1"), Item("T2"), Item("NEW")));
        var quotes = Quotes(Quote("T1", 1_000_000, 900_000), Quote("T2", 2_000_000, 1_800_000), Quote("NEW", 100_000, 90_000));

        var result = ToPremium("slug", gear, Slugs, quotes);

        var budget = result.Setups.Single(s => s.Id == "budget");
        budget.BuyCost.Should().Be(1_100_000, "only the highest reached tier is owned, T1 is bought again");
        PublicSetupsMapper.OwnedKeys(gear, gear.Setups[2]).Should().BeEquivalentTo([PublicSetupsMapper.QuoteKey("BASE", null), PublicSetupsMapper.QuoteKey("T2", null)]);
    }

    [Test]
    public void Premium_reached_setup_is_measured_against_the_baseline_only_and_has_no_payback()
    {
        var gear = Gear(Setup("baseline", "baseline", 0, Item("BASE")),
            Setup("tier1", "reached", 20_000, Item("T1")));
        var quotes = Quotes(Quote("T1", 1_000_000, 900_000));

        var reached = ToPremium("slug", gear, Slugs, quotes).Setups.Single();

        reached.BuyCost.Should().Be(1_000_000);
        reached.PaybackHoursBorrow.Should().BeNull();
        reached.PaybackHoursFull.Should().BeNull();
        reached.NetGain10h.Should().BeNull();
        reached.NetGain50h.Should().BeNull();
        reached.NoProfitGain.Should().BeNull();
    }

    [Test]
    public void Untradeable_items_are_excluded_from_all_costs_and_listed_without_prices()
    {
        var gear = Gear(Setup("baseline", "baseline", 0),
            Setup("budget", "upgrade", 10_000, Item("SOULBOUND", tradeable: false), Item("NEW")));
        var quotes = Quotes(Quote("NEW", 100_000, 90_000), Quote("SOULBOUND", 9_999_999, 9_999_999));

        var setup = ToPremium("slug", gear, Slugs, quotes).Setups.Single();

        setup.BuyCost.Should().Be(100_000);
        var untradeable = setup.Items.Single(i => i.ItemTag == "SOULBOUND");
        untradeable.Tradeable.Should().BeFalse();
        untradeable.BuyPrice.Should().BeNull();
        untradeable.Stability.Should().BeNull();
        PublicSetupsMapper.QuoteTargets(gear).Select(t => t.Tag).Should().Equal("NEW");
    }

    [TestCase(0d)]
    [TestCase(-5_000d)]
    public void Upgrade_without_gain_has_no_payback_and_is_flagged(double delta)
    {
        var gear = Gear(Setup("baseline", "baseline", 0), Setup("budget", "upgrade", delta, Item("NEW")));
        var quotes = Quotes(Quote("NEW", 100_000, 90_000));

        var setup = ToPremium("slug", gear, Slugs, quotes).Setups.Single();

        setup.PaybackHoursBorrow.Should().BeNull();
        setup.PaybackHoursFull.Should().BeNull();
        setup.NoProfitGain.Should().BeTrue();
        setup.BuyCost.Should().Be(100_000);
    }

    [Test]
    public void Upgrade_with_gain_is_not_flagged()
    {
        var gear = Gear(Setup("baseline", "baseline", 0), Setup("budget", "upgrade", 1, Item("NEW")));

        ToPremium("slug", gear, Slugs, Quotes(Quote("NEW", 10, 9))).Setups.Single().NoProfitGain.Should().BeFalse();
    }

    [TestCase(149d, false)]
    [TestCase(150d, false)]
    [TestCase(150.01d, true)]
    [TestCase(169d, true)]
    [TestCase(213d, true)]
    [TestCase(627d, true)]
    [TestCase(1245d, true)]
    [TestCase(1357d, true)]
    public void Budget_filter_uses_full_payback_instead_of_borrow_payback(double hours, bool tooLong)
    {
        var source = Setup("budget", "upgrade", 1_000, Item("NEW"));
        var gear = Gear(Setup("baseline", "baseline", 0), source);
        var quotes = Quotes(Quote("NEW", hours * 1_000, hours * 1_000 - 100));

        var setup = PublicSetupsMapper.MapEstimate(source, gear, Slugs, quotes, true, source.DeltaCoinsPerHour);

        setup.PaybackHoursFull.Should().Be(hours);
        setup.PaybackHoursBorrow.Should().BeApproximately(0.1, 0.0001);
        setup.PaybackTooLong.Should().Be(tooLong);
        PublicSetupsMapper.IsRecommended(setup).Should().Be(!tooLong);
    }

    [TestCase("budget", "reached", true)]
    [TestCase("baseline", "baseline", true)]
    [TestCase("mid", "upgrade", true)]
    [TestCase("endgame", "upgrade", true)]
    [TestCase("BUDGET", "upgrade", false)]
    public void Slow_payback_filter_preserves_reached_baseline_and_other_tiers(string tier, string status, bool kept)
    {
        var source = Setup(tier, status, 1_000, Item("NEW"));
        var gear = Gear(status == "baseline" ? source : Setup("baseline", "baseline", 0), source);

        var setup = PublicSetupsMapper.MapEstimate(source, gear, Slugs, Quotes(Quote("NEW", 200_000, 199_000)), true, 1_000);

        PublicSetupsMapper.IsRecommended(setup).Should().Be(kept);
        setup.PaybackTooLong.Should().Be(status == "upgrade" ? true : null);
    }

    [TestCase(null)]
    [TestCase(0d)]
    [TestCase(-1d)]
    [TestCase(1d)]
    public void Budget_with_unknown_payback_is_retained(double? delta)
    {
        var source = Setup("budget", "upgrade", delta, Item("NEW"));
        var gear = Gear(Setup("baseline", "baseline", 0), source);
        var quotes = delta == 1 ? Quotes() : Quotes(Quote("NEW", 200_000, 199_000));

        var setup = PublicSetupsMapper.MapEstimate(source, gear, Slugs, quotes, true, delta);

        setup.PaybackHoursFull.Should().BeNull();
        setup.PaybackTooLong.Should().BeNull();
        PublicSetupsMapper.IsRecommended(setup).Should().BeTrue();
    }

    [Test]
    public void Missing_quote_makes_the_price_incomplete()
    {
        var gear = Gear(Setup("baseline", "baseline", 0), Setup("budget", "upgrade", 10_000, Item("NEW")));

        var setup = ToPremium("slug", gear, Slugs, Quotes()).Setups.Single();

        setup.PriceIncomplete.Should().BeTrue();
        setup.BuyCost.Should().BeNull();
        setup.Stability.Should().Be("unknown");
    }

    [Test]
    public void Premium_view_is_unlocked_and_carries_the_baseline_rate()
    {
        var gear = Gear(Setup("baseline", "baseline", 0), Setup("budget", "upgrade", 10_000, Item("NEW")));

        var result = ToPremium("slug", gear, Slugs, Quotes(Quote("NEW", 10, 9)));

        result.Locked.Should().BeFalse();
        result.BaselineCoinsPerHour.Should().Be(1_000_000);
        result.Setups.Single().CoinsPerHour.Should().Be(1_010_000);
        result.Setups.Single().Stability.Should().Be("stable");
    }

    [Test]
    public void Quote_targets_are_distinct_per_tag_and_variant()
    {
        var pet = new Dictionary<string, string> { ["PetLevel"] = "100" };
        var gear = Gear(Setup("baseline", "baseline", 0),
            Setup("a", "upgrade", 1, Item("PET", filters: pet), Item("X")),
            Setup("b", "upgrade", 1, Item("PET", filters: pet), Item("PET"), Item("X")));

        PublicSetupsMapper.QuoteTargets(gear).Should().HaveCount(3);
    }

    [Test]
    public void Owned_baseline_variant_does_not_make_another_variant_owned()
    {
        var legendary = new Dictionary<string, string> { ["PetRarity"] = "LEGENDARY" };
        var mythic = new Dictionary<string, string> { ["PetRarity"] = "MYTHIC" };
        var gear = Gear(Setup("baseline", "baseline", 0, Item("PET_ELEPHANT", filters: legendary)),
            Setup("endgame", "upgrade", 10_000, Item("PET_ELEPHANT", filters: mythic)),
            Setup("same", "upgrade", 10_000, Item("PET_ELEPHANT", filters: legendary)));
        var quotes = new Dictionary<string, ItemResaleQuote>
        {
            [PublicSetupsMapper.QuoteKey("PET_ELEPHANT", mythic)] = Quote("PET_ELEPHANT", 50_000_000, 45_000_000),
        };

        var result = ToPremium("slug", gear, Slugs, quotes);

        result.Setups.Single(s => s.Id == "endgame").BuyCost.Should().Be(50_000_000);
        result.Setups.Single(s => s.Id == "same").BuyCost.Should().Be(0);
    }
}
