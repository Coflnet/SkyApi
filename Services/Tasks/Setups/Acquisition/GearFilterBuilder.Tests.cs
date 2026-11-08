using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Moq;
using NUnit.Framework;

namespace SkyApi.Services.Tasks.Setups.Acquisition;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class GearFilterBuilderTests
{
    private static readonly FilterEngine Engine = new(new Mock<INBT>().Object);

    private static GearItemSpec Hoe(Action<GearItemSpec> configure = null)
    {
        var spec = new GearItemSpec { Tag = "theoretical_hoe_wheat_3" };
        configure?.Invoke(spec);
        return spec;
    }

    private static Dictionary<string, string> Filters(GearItemSpec spec) => GearFilterBuilder.Build(GearTarget.From(spec)).Filters;

    internal static SaveAuction Item(int harvesting = 0, Dictionary<string, string> nbt = null)
    {
        var auction = new SaveAuction
        {
            Tag = "THEORETICAL_HOE_WHEAT_3",
            Enchantments = new(),
            FlatenedNBT = nbt ?? new()
        };
        if (harvesting > 0)
            auction.Enchantments.Add(new Enchantment(Enchantment.EnchantmentType.harvesting, (byte)harvesting));
        return auction;
    }

    [TestCase(1, ">0")]
    [TestCase(5, ">4")]
    [TestCase(10, ">9")]
    public void Minimum_is_written_as_greater_than_minimum_minus_one(int minimum, string expected)
    {
        GearFilterBuilder.AtLeast(minimum).Should().Be(expected);
    }

    [TestCase(4, false)]
    [TestCase(5, true)]
    [TestCase(6, true)]
    public void Enchant_filter_matches_the_minimum_level_and_above(int level, bool matches)
    {
        var filters = Filters(Hoe(s => s.Enchants["harvesting"] = 5));

        filters.Should().Contain("harvesting", ">4");
        Engine.GetMatcher(filters)(Item(level)).Should().Be(matches);
    }

    [Test]
    public void Level_one_excludes_items_without_the_enchant()
    {
        var matcher = Engine.GetMatcher(Filters(Hoe(s => s.Enchants["harvesting"] = 1)));

        matcher(Item(0)).Should().BeFalse();
        matcher(Item(1)).Should().BeTrue();
    }

    [TestCase("999", false)]
    [TestCase("1000", true)]
    [TestCase("1500000", true)]
    public void Counter_filter_is_a_minimum(string farmed, bool matches)
    {
        var filters = Filters(Hoe(s => s.Counters["farmed_cultivating"] = 1000));

        filters.Should().Contain("FarmedCultivating", ">999");
        Engine.GetMatcher(filters)(Item(nbt: new() { { "farmed_cultivating", farmed } })).Should().Be(matches);
    }

    [Test]
    public void Zero_levels_add_no_filter()
    {
        Filters(Hoe(s => { s.Enchants["harvesting"] = 0; s.Stars = 0; s.HotPotatoCount = 0; s.Recombobulated = false; }))
            .Should().BeEmpty();
    }

    [Test]
    public void Item_modifiers_map_to_their_filters()
    {
        var filters = Filters(Hoe(s =>
        {
            s.Stars = 5;
            s.HotPotatoCount = 15;
            s.Recombobulated = true;
            s.Reforge = "blessed";
        }));

        filters.Should().Contain("Stars", ">4").And.Contain("HotPotatoCount", ">14")
            .And.Contain("Recombobulated", "true").And.Contain("Reforge", "blessed");
    }

    [Test]
    public void Parts_and_counters_map_to_their_filters_and_unfilterable_ones_are_checked_on_the_nbt()
    {
        var set = GearFilterBuilder.Build(GearTarget.From(Hoe(s =>
        {
            s.Parts["drill_part_engine"] = "amber_polished_drill_engine";
            s.Parts["hook"] = "hotspot_hook";
            s.Counters["expertise_kills"] = 15000;
            s.Applied["farming_for_dummies_count"] = 5;
            s.Applied["divan_powder_coating"] = 1;
            s.Applied["mithril_infusion"] = 1;
            s.Counters["levelable_lvl"] = 40;
            s.Gems["PERIDOT_0"] = "FINE_PERIDOT_GEM";
            s.Rarity = "LEGENDARY";
        })));

        set.Filters.Should().Contain("DrillPartEngine", "AMBER_POLISHED_DRILL_ENGINE").And.Contain("RodHook", "HOTSPOT_HOOK")
            .And.Contain("ExpertiseKills", ">14999").And.Contain("FarmingForDummies", ">4").And.Contain("PowderCoating", "true");
        set.PostHocOnly.Should().BeEquivalentTo("counter:levelable_lvl", "applied:mithril_infusion", "gem:PERIDOT_0", "rarity");
    }

    [Test]
    public void Every_emitted_filter_name_is_known_to_the_filter_engine()
    {
        var spec = Hoe(s =>
        {
            s.Enchants["ultimate_flowstate"] = 3;
            s.Enchants["turbo_wheat"] = 5;
            s.Stars = 1;
            s.HotPotatoCount = 1;
            s.Recombobulated = true;
            s.Reforge = "blessed";
        });
        foreach (var part in GearFilterBuilder.PartFilters.Keys)
            spec.Parts[part] = "SOME_PART";
        foreach (var counter in GearFilterBuilder.CounterFilters.Keys)
            spec.Counters[counter] = 2;
        foreach (var applied in GearFilterBuilder.AppliedFilters.Keys)
            spec.Applied[applied] = 2;
        foreach (var applied in GearFilterBuilder.PresenceFilters.Keys)
            spec.Applied[applied] = 1;
        var filters = Filters(spec);

        var compile = () => Engine.GetMatcher(filters);

        compile.Should().NotThrow();
        filters.Count.Should().Be(6 + GearFilterBuilder.PartFilters.Count + GearFilterBuilder.CounterFilters.Count
            + GearFilterBuilder.AppliedFilters.Count + GearFilterBuilder.PresenceFilters.Count);
    }

    [Test]
    public void Reforge_filter_matches_only_that_reforge()
    {
        var matcher = Engine.GetMatcher(Filters(Hoe(s => s.Reforge = "Blessed")));
        var blessed = Item();
        blessed.Reforge = ItemReferences.Reforge.blessed;

        matcher(blessed).Should().BeTrue();
        matcher(Item()).Should().BeFalse();
    }

    [Test]
    public void Pet_filters_use_rarity_level_minimum_and_held_item()
    {
        var filters = GearFilterBuilder.Build(GearTarget.From(new GearPetSpec { Type = "elephant", Tier = "legendary", Level = 100, HeldItem = "green_bandana" })).Filters;

        filters.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            { "Rarity", "LEGENDARY" }, { "PetLevel", ">99" }, { "PetItem", "GREEN_BANDANA" }
        });
    }

    [TestCase("25353230", true)]
    [TestCase("25000000", false)]
    public void Pet_level_filter_matches_the_minimum_level_by_exp(string exp, bool matches)
    {
        var filters = GearFilterBuilder.Build(GearTarget.From(new GearPetSpec { Type = "ELEPHANT", Tier = "LEGENDARY", Level = 100 })).Filters;
        var pet = new SaveAuction
        {
            Tag = "PET_ELEPHANT",
            Tier = Tier.LEGENDARY,
            ItemName = "[Lvl 100] Elephant",
            Enchantments = new(),
            FlatenedNBT = new() { { "exp", exp }, { "candyUsed", "0" } }
        };

        Engine.GetMatcher(filters)(pet).Should().Be(matches);
    }

    [Test]
    public void Rarity_is_not_filtered_because_the_filter_is_exact_and_is_checked_on_the_item()
    {
        var set = GearFilterBuilder.Build(GearTarget.From(Hoe(s => s.Rarity = "MYTHIC")));

        set.Filters.Should().NotContainKey("Rarity");
        set.PostHocOnly.Should().Contain("rarity");
    }

    /// <summary>Regression: experience and mana_steal resolved to the attribute filters of the same name</summary>
    [Test]
    public void Every_enchant_filter_selects_that_enchant_level()
    {
        var wrong = new List<string>();
        foreach (var type in Enum.GetValues<Enchantment.EnchantmentType>())
        {
            var spec = Hoe(s => s.Enchants[type.ToString()] = 1);
            var set = GearFilterBuilder.Build(GearTarget.From(spec));
            if (set.PostHocOnly.Contains("enchant:" + type))
                continue;
            var withEnchant = Item();
            withEnchant.Enchantments.Add(new Enchantment(type, 1));
            try
            {
                var matcher = Engine.GetMatcher(set.Filters);
                if (!matcher(withEnchant) || matcher(Item()))
                    wrong.Add(type.ToString());
            }
            catch (Exception e)
            {
                wrong.Add($"{type} ({e.GetType().Name})");
            }
        }

        wrong.Should().BeEmpty();
    }

    [TestCase("experience", "ExperienceEnchant")]
    [TestCase("mana_steal", "ManaStealEnchant")]
    [TestCase("harvesting", "harvesting")]
    public void Enchants_shadowed_by_an_attribute_use_their_enchant_alias(string enchant, string filter)
    {
        Filters(Hoe(s => s.Enchants[enchant] = 2)).Should().ContainKey(filter);
    }

    [Test]
    public void Pet_without_tier_checks_the_level_on_the_item_because_the_database_needs_the_rarity()
    {
        var set = GearFilterBuilder.Build(GearTarget.From(new GearPetSpec { Type = "ELEPHANT", Level = 100 }));

        set.Filters.Should().NotContainKey("PetLevel");
        set.PostHocOnly.Should().Contain("petLevel");
    }

    [TestCase("3")]
    [TestCase("999")]
    public void Numeric_rarity_or_enchant_names_are_unsupported(string value)
    {
        var target = GearTarget.From(new GearItemSpec { Tag = "HOE", Rarity = value, Enchants = new() { { value, 1 } } });

        target.MinRarity.Should().BeNull();
        target.Enchants.Should().BeEmpty();
        target.Unsupported.Should().BeEquivalentTo($"rarity:{value}", $"enchant:{value}");
    }

    [Test]
    public void Enchant_levels_are_clamped_to_a_byte()
    {
        GearTarget.From(Hoe(s => s.Enchants["harvesting"] = 300)).Enchants[Enchantment.EnchantmentType.harvesting]
            .Should().Be(GearTarget.MaxEnchantLevel);
    }
}
