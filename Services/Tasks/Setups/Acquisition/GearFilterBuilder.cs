using System.Collections.Generic;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;

/// <param name="Filters">FilterEngine filters every matching auction has to pass</param>
/// <param name="PostHocOnly">Requirements no filter expresses as a minimum, they are only checked on the auction NBT</param>
public record GearFilterSet(Dictionary<string, string> Filters, IReadOnlyList<string> PostHocOnly);

/// <summary>
/// Turns a <see cref="GearTarget"/> into FilterEngine filters.
/// Number filters take "&gt;n" as "at least n+1" (and "n-" the same), so a minimum of L is written as "&gt;(L-1)".
/// The rarity is not filtered: the rarity filter is an exact match, a minimum rarity is only verified on the auction.
/// </summary>
public static class GearFilterBuilder
{
    /// <summary>Spec part key to the filter matching that part (the filter also reads the newer engine.id style keys)</summary>
    public static readonly IReadOnlyDictionary<string, string> PartFilters = new Dictionary<string, string>
    {
        { "drill_part_engine", "DrillPartEngine" },
        { "drill_part_fuel_tank", "DrillPartFuelTank" },
        { "drill_part_upgrade_module", "DrillPartUpgradeModule" },
        { "hook", "RodHook" },
        { "line", "RodLine" },
        { "sinker", "RodSinker" }
    };

    /// <summary>Earned counter key to its number filter</summary>
    public static readonly IReadOnlyDictionary<string, string> CounterFilters = new Dictionary<string, string>
    {
        { "farmed_cultivating", "FarmedCultivating" },
        { "mined_crops", "MinedCrops" },
        { "expertise_kills", "ExpertiseKills" },
        { "absorb_logs_chopped", "AbsorbLogs" },
        { "logs_cut", "LogsCut" }
    };

    /// <summary>Applied count key to its number filter</summary>
    public static readonly IReadOnlyDictionary<string, string> AppliedFilters = new Dictionary<string, string>
    {
        { "farming_for_dummies_count", "FarmingForDummies" },
        { "polarvoid", "PlarvoidBook" },
        { "tuned_transmission", "TunedTransmission" },
        { "mana_disintegrator_count", "ManaDisintegrator" }
    };

    /// <summary>Applied once modifiers to their presence filter</summary>
    public static readonly IReadOnlyDictionary<string, string> PresenceFilters = new Dictionary<string, string>
    {
        { "divan_powder_coating", "PowderCoating" },
        { "wood_singularity_count", "WoodSingularity" },
        { "art_of_war_count", "ArtOfTheWar" },
        { "jalapeno_count", "JalapenoBook" }
    };

    /// <summary>
    /// Enchants whose own name resolves to another filter in FilterEngine (an attribute registered first),
    /// mapped to the enchant filter the engine registers for them
    /// </summary>
    public static readonly IReadOnlyDictionary<Enchantment.EnchantmentType, string> EnchantFilterAliases = new Dictionary<Enchantment.EnchantmentType, string>
    {
        { Enchantment.EnchantmentType.experience, "ExperienceEnchant" },
        { Enchantment.EnchantmentType.mana_steal, "ManaStealEnchant" }
    };

    /// <summary>Enchants without any filter for their own level, checked on the item only</summary>
    public static readonly IReadOnlySet<Enchantment.EnchantmentType> UnfilterableEnchants = new HashSet<Enchantment.EnchantmentType>();

    /// <summary>The FilterEngine filter for the level of an enchant, null when there is none</summary>
    public static string EnchantFilterName(Enchantment.EnchantmentType type)
    {
        if (UnfilterableEnchants.Contains(type))
            return null;
        return EnchantFilterAliases.GetValueOrDefault(type) ?? type.ToString();
    }

    /// <summary>Filter value for "at least <paramref name="minimum"/>"</summary>
    public static string AtLeast(long minimum) => ">" + (minimum - 1);

    public static GearFilterSet Build(GearTarget target)
    {
        var filters = new Dictionary<string, string>();
        var postHoc = new List<string>();
        if (target.IsPet)
            AddPet(target, filters, postHoc);
        else
            AddItem(target, filters, postHoc);
        return new GearFilterSet(filters, postHoc);
    }

    private static void AddPet(GearTarget target, Dictionary<string, string> filters, List<string> postHoc)
    {
        if (target.PetTier != null)
            filters["Rarity"] = target.PetTier.Value.ToString();
        if (target.PetLevel > 0 && target.PetTier != null)
            filters["PetLevel"] = AtLeast(target.PetLevel);
        else if (target.PetLevel > 0)
            postHoc.Add("petLevel"); // level ranges need the rarity on the database
        if (target.PetItem != null)
            filters["PetItem"] = target.PetItem;
    }

    private static void AddItem(GearTarget target, Dictionary<string, string> filters, List<string> postHoc)
    {
        AddEnchants(target, filters, postHoc);
        if (target.Stars > 0)
            filters["Stars"] = AtLeast(target.Stars);
        if (target.HotPotatoCount > 0)
            filters["HotPotatoCount"] = AtLeast(target.HotPotatoCount);
        if (target.Recombobulated)
            filters["Recombobulated"] = "true";
        if (target.Reforge != null)
            filters["Reforge"] = target.Reforge.Value.ToString();
        AddParts(target, filters, postHoc);
        AddCounters(target, filters, postHoc);
        AddApplied(target, filters, postHoc);
        foreach (var slot in target.Gems.Keys)
            postHoc.Add("gem:" + slot); // gem filters match one exact quality, a better gem has to count too
        if (target.MinRarity != null)
            postHoc.Add("rarity"); // the rarity filter is exact, a minimum is only checked on the item
    }

    private static void AddEnchants(GearTarget target, Dictionary<string, string> filters, List<string> postHoc)
    {
        foreach (var (type, level) in target.Enchants)
        {
            var name = EnchantFilterName(type);
            if (name == null)
                postHoc.Add("enchant:" + type);
            else
                filters[name] = AtLeast(level);
        }
    }

    private static void AddApplied(GearTarget target, Dictionary<string, string> filters, List<string> postHoc)
    {
        foreach (var (key, value) in target.Applied)
        {
            if (AppliedFilters.TryGetValue(key, out var name))
                filters[name] = AtLeast(value);
            else if (PresenceFilters.TryGetValue(key, out name))
                filters[name] = "true";
            else
                postHoc.Add("applied:" + key);
        }
    }

    private static void AddParts(GearTarget target, Dictionary<string, string> filters, List<string> postHoc)
    {
        foreach (var (key, part) in target.Parts)
        {
            if (PartFilters.TryGetValue(key, out var name))
                filters[name] = part;
            else
                postHoc.Add("part:" + key);
        }
    }

    private static void AddCounters(GearTarget target, Dictionary<string, string> filters, List<string> postHoc)
    {
        foreach (var (key, value) in target.Counters)
        {
            if (CounterFilters.TryGetValue(key, out var name))
                filters[name] = AtLeast(value);
            else
                postHoc.Add("counter:" + key);
        }
    }
}
