using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.Core;
using NUnit.Framework;
using static Coflnet.Sky.Core.Enchantment;

namespace SkyApi.Services.Tasks.Setups.Acquisition;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class UpgradePlannerTests
{
    private const string Hoe = "THEORETICAL_HOE_WHEAT_3";
    private FakePriceBook prices;
    private UpgradePlanner planner;

    [SetUp]
    public void Setup()
    {
        prices = new FakePriceBook();
        planner = new UpgradePlanner(prices);
    }

    private static GearTarget Target(System.Action<GearItemSpec> configure, string tag = Hoe)
    {
        var spec = new GearItemSpec { Tag = tag };
        configure(spec);
        return GearTarget.From(spec);
    }

    private Task<CheapestBase> Plan(SaveAuction candidate, GearTarget target)
        => planner.PlanAsync(candidate, GearAcquisitionService.NormalizeUuid(candidate.Uuid), candidate.StartingBid, target);

    [Test]
    public async Task Lower_enchant_level_is_upgraded_with_a_book_of_the_target_level()
    {
        prices.Enchants[(EnchantmentType.harvesting, 6)] = 2_000_000;
        var candidate = AuctionFactory.Bin(Hoe, 10_000_000, enchants: (EnchantmentType.harvesting, 5));

        var plan = await Plan(candidate, Target(s => s.Enchants["harvesting"] = 6));

        plan.Upgrades.Should().ContainSingle().Which.Should().Be(
            new AcquisitionUpgrade("enchant", "harvesting", 5, 6, "ENCHANTMENT_HARVESTING_6", 1, 2_000_000, true));
        plan.Total.Should().Be(12_000_000);
        plan.Incomplete.Should().BeFalse();
    }

    [Test]
    public async Task Met_modifiers_cost_nothing()
    {
        var candidate = AuctionFactory.Bin(Hoe, 10_000_000, enchants: (EnchantmentType.harvesting, 6));

        var plan = await Plan(candidate, Target(s => s.Enchants["harvesting"] = 5));

        plan.Upgrades.Should().BeEmpty();
        plan.Total.Should().Be(10_000_000);
    }

    [Test]
    public async Task Counter_enchant_above_one_is_earned_and_becomes_grind()
    {
        var candidate = AuctionFactory.Bin(Hoe, 10_000_000, enchants: (EnchantmentType.cultivating, 3));

        var plan = await Plan(candidate, Target(s => s.Enchants["cultivating"] = 8));

        plan.Upgrades.Should().BeEmpty();
        plan.GrindRequired.Should().ContainSingle().Which.Should().Be(new GrindRequirement("enchant", "cultivating", 3, 8));
        plan.GrindRequired[0].Deficit.Should().Be(5);
    }

    [Test]
    public async Task Counter_enchant_level_one_is_bought_as_a_book()
    {
        prices.Enchants[(EnchantmentType.cultivating, 1)] = 4_800_000;

        var plan = await Plan(AuctionFactory.Bin(Hoe, 1_000), Target(s => s.Enchants["cultivating"] = 1));

        plan.GrindRequired.Should().BeEmpty();
        plan.Upgrades.Single().ItemTag.Should().Be("ENCHANTMENT_CULTIVATING_1");
        plan.Total.Should().Be(4_801_000);
    }

    [Test]
    public async Task Earned_counter_is_grind_with_the_deficit()
    {
        var candidate = AuctionFactory.Bin(Hoe, 1_000, nbt: new() { { "farmed_cultivating", "400000" } });

        var plan = await Plan(candidate, Target(s => s.Counters["farmed_cultivating"] = 1_000_000));

        plan.GrindRequired.Should().ContainSingle().Which.Deficit.Should().Be(600_000);
        plan.Upgrades.Should().BeEmpty();
    }

    [Test]
    public async Task Applied_count_is_bought_for_the_missing_count()
    {
        prices.Items["FARMING_FOR_DUMMIES"] = 100_000;
        var candidate = AuctionFactory.Bin(Hoe, 1_000, nbt: new() { { "farming_for_dummies_count", "4" } });

        var plan = await Plan(candidate, Target(s => s.Applied["farming_for_dummies_count"] = 5));

        plan.Upgrades.Should().ContainSingle().Which.Should().Be(
            new AcquisitionUpgrade("applied", "farming_for_dummies_count", 4, 5, "FARMING_FOR_DUMMIES", 1, 100_000, true));
    }

    [Test]
    public async Task Unpriced_book_marks_the_plan_incomplete_and_is_left_out_of_the_total()
    {
        var plan = await Plan(AuctionFactory.Bin("CACTUS_KNIFE", 5_000), Target(s => s.Enchants["sunder"] = 5, "CACTUS_KNIFE"));

        plan.Incomplete.Should().BeTrue();
        plan.Upgrades.Single().Priced.Should().BeFalse();
        plan.Total.Should().Be(5_000);
    }

    [Test]
    public async Task Hot_potato_count_above_ten_needs_fuming_books()
    {
        prices.Items[UpgradePlanner.HotPotatoBook] = 80_000;
        prices.Items[UpgradePlanner.FumingPotatoBook] = 1_200_000;
        var candidate = AuctionFactory.Bin(Hoe, 1_000, nbt: new() { { "hpc", "5" } });

        var plan = await Plan(candidate, Target(s => s.HotPotatoCount = 15));

        plan.Upgrades.Select(u => (u.ItemTag, u.Amount)).Should().Equal((UpgradePlanner.HotPotatoBook, 5), (UpgradePlanner.FumingPotatoBook, 5));
        plan.Total.Should().Be(1_000 + 5 * 80_000 + 5 * 1_200_000);
    }

    [Test]
    public async Task Hot_potato_count_one_below_target_needs_one_book()
    {
        prices.Items[UpgradePlanner.HotPotatoBook] = 80_000;
        var candidate = AuctionFactory.Bin(Hoe, 1_000, nbt: new() { { "hpc", "9" } });

        var plan = await Plan(candidate, Target(s => s.HotPotatoCount = 10));

        plan.Upgrades.Should().ContainSingle().Which.Amount.Should().Be(1);
    }

    [Test]
    public async Task Efficiency_above_five_is_added_with_sil_ex()
    {
        prices.Items["SIL_EX"] = 3_000_000;
        var candidate = AuctionFactory.Bin("DIVAN_DRILL", 1_000, enchants: (EnchantmentType.efficiency, 5));

        var plan = await Plan(candidate, Target(s => s.Enchants["efficiency"] = 10, "DIVAN_DRILL"));

        plan.Upgrades.Should().ContainSingle().Which.Should().Be(
            new AcquisitionUpgrade("enchant", "efficiency", 5, 10, "SIL_EX", 5, 15_000_000, true));
    }

    [Test]
    public async Task Recombobulator_reforge_part_and_stars_are_priced_from_their_items()
    {
        prices.Items["RECOMBOBULATOR_3000"] = 7_000_000;
        prices.Items["BLESSED_FRUIT"] = 500_000;
        prices.Items["AMBER_POLISHED_DRILL_ENGINE"] = 90_000_000;
        prices.Items["ESSENCE_DRAGON"] = 2_000;
        prices.Reforges[ItemReferences.Reforge.blessed] = new PricedIngredient("BLESSED_FRUIT", 1, 100_000);
        prices.Stars[$"{Hoe}:0-2"] = new[] { new PricedIngredient("ESSENCE_DRAGON", 50) };
        var target = Target(s =>
        {
            s.Recombobulated = true;
            s.Reforge = "blessed";
            s.Parts["drill_part_engine"] = "AMBER_POLISHED_DRILL_ENGINE";
            s.Stars = 2;
        });

        var plan = await Plan(AuctionFactory.Bin(Hoe, 1_000), target);

        plan.Incomplete.Should().BeFalse();
        plan.Upgrades.Select(u => (u.Kind, u.Cost)).Should().BeEquivalentTo(new[]
        {
            ("stars", 100_000L), ("recombobulator", 7_000_000L), ("reforge", 600_000L), ("part", 90_000_000L)
        });
    }

    [Test]
    public async Task Gem_of_lower_quality_is_replaced_and_slot_unlock_is_priced()
    {
        prices.Items["FINE_PERIDOT_GEM"] = 300_000;
        prices.SlotUnlocks["PERIDOT_1"] = new[] { new PricedIngredient(null, 0, 50_000) };
        var candidate = AuctionFactory.Bin(Hoe, 1_000, nbt: new() { { "PERIDOT_0", "ROUGH" }, { "unlocked_slots", "PERIDOT_0" } });
        var target = Target(s => { s.Gems["PERIDOT_0"] = "FINE_PERIDOT_GEM"; s.Gems["PERIDOT_1"] = "FINE_PERIDOT_GEM"; });

        var plan = await Plan(candidate, target);

        plan.Upgrades.Select(u => (u.Kind, u.Name, u.Cost)).Should().BeEquivalentTo(new[]
        {
            ("gem", "PERIDOT_0", 300_000L), ("gemSlot", "PERIDOT_1", 50_000L), ("gem", "PERIDOT_1", 300_000L)
        });
    }

    [Test]
    public void Better_gem_and_universal_slot_with_matching_type_meet_the_spec()
    {
        var candidate = AuctionFactory.Bin("DIVAN_DRILL", 1_000, nbt: new()
        {
            { "PERIDOT_0", "PERFECT" }, { "UNIVERSAL_0", "FINE" }, { "UNIVERSAL_0_gem", "JASPER" }, { "UNIVERSAL_1", "PERFECT" }, { "UNIVERSAL_1_gem", "RUBY" }
        });
        var target = Target(s =>
        {
            s.Gems["PERIDOT_0"] = "FINE_PERIDOT_GEM";
            s.Gems["UNIVERSAL_0"] = "FINE_JASPER_GEM";
            s.Gems["UNIVERSAL_1"] = "FINE_JASPER_GEM";
        }, "DIVAN_DRILL");

        var deficits = GearSpecVerifier.Deficits(candidate, target);

        deficits.Should().ContainSingle().Which.Name.Should().Be("UNIVERSAL_1"); // wrong gem type does not count
    }

    [Test]
    public async Task Rarity_two_steps_below_rejects_the_candidate()
    {
        prices.Items["RECOMBOBULATOR_3000"] = 7_000_000;
        var rare = AuctionFactory.Bin(Hoe, 1_000, Tier.RARE);
        var epic = AuctionFactory.Bin(Hoe, 1_000, Tier.EPIC);

        (await Plan(rare, Target(s => s.Rarity = "LEGENDARY"))).Should().BeNull();
        (await Plan(epic, Target(s => { s.Rarity = "LEGENDARY"; s.Recombobulated = true; }))).Should().NotBeNull();
    }

    [Test]
    public async Task Pet_level_is_grind_and_the_held_item_is_bought()
    {
        prices.Items["GREEN_BANDANA"] = 3_000_000;
        var pet = AuctionFactory.Bin("PET_ELEPHANT", 20_000_000);
        pet.ItemName = "[Lvl 80] Elephant";
        var target = GearTarget.From(new GearPetSpec { Type = "ELEPHANT", Tier = "LEGENDARY", Level = 100, HeldItem = "GREEN_BANDANA" });

        var plan = await Plan(pet, target);

        plan.GrindRequired.Should().ContainSingle().Which.Should().Be(new GrindRequirement("petLevel", "PET_ELEPHANT", 80, 100));
        plan.Upgrades.Should().ContainSingle().Which.Cost.Should().Be(3_000_000);
    }

    [Test]
    public async Task Pet_of_another_rarity_is_no_candidate()
    {
        var pet = AuctionFactory.Bin("PET_ELEPHANT", 1_000, Tier.EPIC);

        (await Plan(pet, GearTarget.From(new GearPetSpec { Type = "ELEPHANT", Tier = "LEGENDARY", Level = 1 }))).Should().BeNull();
    }

    [Test]
    public async Task Unknown_modifier_is_listed_unpriced()
    {
        var plan = await Plan(AuctionFactory.Bin(Hoe, 1_000), Target(s => s.Enchants["not_an_enchant"] = 1));

        plan.Upgrades.Should().ContainSingle().Which.Should().Be(new AcquisitionUpgrade("unsupported", "enchant:not_an_enchant", null, null, null, 0, 0, false));
        plan.Incomplete.Should().BeTrue();
    }

    [Test]
    public void Equal_specs_have_equal_keys_regardless_of_order_and_case()
    {
        var a = GearTarget.From(new GearItemSpec { Tag = "hoe", Enchants = new() { { "harvesting", 5 }, { "dedication", 4 } } });
        var b = GearTarget.From(new GearItemSpec { Tag = "HOE", Enchants = new() { { "dedication", 4 }, { "Harvesting", 5 } } });

        a.Key.Should().Be(b.Key);
        a.Key.Should().NotBe(GearTarget.From(new GearItemSpec { Tag = "HOE", Enchants = new() { { "harvesting", 6 } } }).Key);
    }
    [Test]
    public async Task Item_one_rarity_below_without_recombobulator_gets_a_recombobulator_line()
    {
        prices.Items["RECOMBOBULATOR_3000"] = 7_000_000;
        var epic = AuctionFactory.Bin(Hoe, 1_000, Tier.EPIC);

        var plan = await Plan(epic, Target(s => s.Rarity = "LEGENDARY"));

        plan.Upgrades.Should().ContainSingle().Which.Should().Be(
            new AcquisitionUpgrade("recombobulator", "rarity_upgrades", 0, 1, "RECOMBOBULATOR_3000", 1, 7_000_000, true));
    }

    [Test]
    public async Task Recombobulated_item_below_the_rarity_can_not_be_lifted_again()
    {
        var epic = AuctionFactory.Bin(Hoe, 1_000, Tier.EPIC, nbt: new() { { "rarity_upgrades", "1" } });

        (await Plan(epic, Target(s => s.Rarity = "LEGENDARY"))).Should().BeNull();
    }

    [Test]
    public async Task Counter_enchant_missing_entirely_needs_the_level_one_book_and_the_grind()
    {
        prices.Enchants[(EnchantmentType.cultivating, 1)] = 4_800_000;

        var plan = await Plan(AuctionFactory.Bin(Hoe, 1_000), Target(s => s.Enchants["cultivating"] = 5));

        plan.Upgrades.Should().ContainSingle().Which.Should().Be(
            new AcquisitionUpgrade("enchant", "cultivating", 0, 1, "ENCHANTMENT_CULTIVATING_1", 1, 4_800_000, true));
        plan.GrindRequired.Should().ContainSingle().Which.Should().Be(new GrindRequirement("enchant", "cultivating", 0, 5));
    }

    [Test]
    public async Task Turbo_above_five_is_unpriced_because_it_comes_from_an_applied_gourd()
    {
        prices.Enchants[(EnchantmentType.turbo_wheat, 6)] = 1; // must not be used, books stop at V

        var plan = await Plan(AuctionFactory.Bin(Hoe, 1_000, enchants: (EnchantmentType.turbo_wheat, 5)), Target(s => s.Enchants["turbo_wheat"] = 6));

        plan.Upgrades.Should().ContainSingle().Which.Should().Be(new AcquisitionUpgrade("enchant", "turbo_wheat", 5, 6, null, 1, 0, false));
        plan.Incomplete.Should().BeTrue();
    }

    [TestCase(true, "ENCHANTMENT_HARVESTING_4", 300_000)]
    [TestCase(false, "ENCHANTMENT_HARVESTING_5", 1_000_000)]
    public async Task One_level_short_combines_a_second_book_when_cheaper_and_allowed(bool combinable, string expectedBook, long expectedCost)
    {
        prices.Enchants[(EnchantmentType.harvesting, 5)] = 1_000_000;
        prices.Enchants[(EnchantmentType.harvesting, 4)] = 300_000;
        if (combinable)
            prices.Combinable.Add((EnchantmentType.harvesting, 5));

        var plan = await Plan(AuctionFactory.Bin(Hoe, 1_000, enchants: (EnchantmentType.harvesting, 4)), Target(s => s.Enchants["harvesting"] = 5));

        plan.Upgrades.Should().ContainSingle().Which.Should().Be(
            new AcquisitionUpgrade("enchant", "harvesting", 4, 5, expectedBook, 1, expectedCost, true));
    }

    [Test]
    public async Task Combining_is_skipped_when_the_target_book_is_cheaper()
    {
        prices.Enchants[(EnchantmentType.harvesting, 5)] = 200_000;
        prices.Enchants[(EnchantmentType.harvesting, 4)] = 300_000;
        prices.Combinable.Add((EnchantmentType.harvesting, 5));

        var plan = await Plan(AuctionFactory.Bin(Hoe, 1_000, enchants: (EnchantmentType.harvesting, 4)), Target(s => s.Enchants["harvesting"] = 5));

        plan.Upgrades.Single().ItemTag.Should().Be("ENCHANTMENT_HARVESTING_5");
    }

    [Test]
    public async Task Master_stars_above_five_are_priced_from_their_items()
    {
        prices.Items["FIRST_MASTER_STAR"] = 10_000_000;
        prices.Items["SECOND_MASTER_STAR"] = 20_000_000;
        prices.Stars["HYPERION:5-7"] = new[] { new PricedIngredient("FIRST_MASTER_STAR", 1), new PricedIngredient("SECOND_MASTER_STAR", 1) };
        var candidate = AuctionFactory.Bin("HYPERION", 1_000, nbt: new() { { "dungeon_item_level", "5" } });

        var plan = await Plan(candidate, Target(s => s.Stars = 7, "HYPERION"));

        plan.Upgrades.Select(u => (u.Kind, u.FromLevel, u.ToLevel, u.ItemTag, u.Cost)).Should().Equal(
            ("stars", 5, 7, "FIRST_MASTER_STAR", 10_000_000L), ("stars", 5, 7, "SECOND_MASTER_STAR", 20_000_000L));
        plan.Incomplete.Should().BeFalse();
    }

    [Test]
    public async Task Slot_that_holds_a_gem_is_not_unlocked_again()
    {
        prices.Items["FINE_PERIDOT_GEM"] = 300_000;
        prices.SlotUnlocks["PERIDOT_0"] = new[] { new PricedIngredient(null, 0, 50_000) };
        var candidate = AuctionFactory.Bin(Hoe, 1_000, nbt: new() { { "PERIDOT_0", "ROUGH" } }); // no unlocked_slots key

        var plan = await Plan(candidate, Target(s => s.Gems["PERIDOT_0"] = "FINE_PERIDOT_GEM"));

        plan.Upgrades.Should().ContainSingle().Which.Kind.Should().Be("gem");
    }

    [TestCase("PERIDOT_0,PERIDOT_1")]
    [TestCase("PERIDOT_0 PERIDOT_1")]
    [TestCase("PERIDOT_0, PERIDOT_1")]
    public void Unlocked_slots_split_on_commas_and_spaces(string value)
    {
        UpgradePlanner.UnlockedSlots(AuctionFactory.Bin(Hoe, 1, nbt: new() { { "unlocked_slots", value } }))
            .Should().Equal("PERIDOT_0", "PERIDOT_1");
    }

    [Test]
    public async Task Applied_modifier_without_known_items_is_unpriced_not_grind()
    {
        var plan = await Plan(AuctionFactory.Bin(Hoe, 1_000), Target(s => s.Applied["mithril_infusion"] = 1));

        plan.GrindRequired.Should().BeEmpty();
        plan.Upgrades.Should().ContainSingle().Which.Should().Be(new AcquisitionUpgrade("applied", "mithril_infusion", 0, 1, null, 0, 0, false));
    }

    [Test]
    public async Task Counters_are_always_earned()
    {
        var plan = await Plan(AuctionFactory.Bin(Hoe, 1_000), Target(s => s.Counters["farming_for_dummies_count"] = 5));

        plan.GrindRequired.Should().ContainSingle().Which.Kind.Should().Be("counter");
    }

    [Test]
    public void Hard_deficits_are_another_pet_tier_or_an_unreachable_rarity()
    {
        var legendaryPet = GearTarget.From(new GearPetSpec { Type = "ELEPHANT", Tier = "LEGENDARY", Level = 1 });
        var legendaryHoe = Target(s => s.Rarity = "LEGENDARY");

        GearSpecVerifier.HasHardDeficit(AuctionFactory.Bin("PET_ELEPHANT", 1, Tier.RARE), legendaryPet).Should().BeTrue();
        GearSpecVerifier.HasHardDeficit(AuctionFactory.Bin("PET_ELEPHANT", 1, Tier.LEGENDARY), legendaryPet).Should().BeFalse();
        GearSpecVerifier.HasHardDeficit(AuctionFactory.Bin(Hoe, 1, Tier.EPIC), legendaryHoe).Should().BeFalse();
        GearSpecVerifier.HasHardDeficit(AuctionFactory.Bin(Hoe, 1, Tier.RARE), legendaryHoe).Should().BeTrue();
    }
}
