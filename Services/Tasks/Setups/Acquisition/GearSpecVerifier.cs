using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;

public enum DeficitKind
{
    Enchant,
    Stars,
    HotPotato,
    Recombobulator,
    Reforge,
    Gem,
    Part,
    Counter,
    /// <summary>A bought and applied count, eg farming_for_dummies_count</summary>
    Applied,
    PetLevel,
    PetItem,
    /// <summary>Rarity too low even after a recombobulator, can't be fixed</summary>
    Rarity,
    /// <summary>Other pet rarity, can't be fixed</summary>
    PetTier,
    /// <summary>A modifier of the spec that is not understood</summary>
    Unsupported
}

/// <summary>What an item lacks compared to the spec</summary>
/// <param name="Have">Numeric value on the item (level, count, quality rank)</param>
/// <param name="Need">Numeric value of the spec</param>
/// <param name="HaveValue">Value on the item for non numeric modifiers</param>
/// <param name="NeedValue">Value of the spec for non numeric modifiers (part, gem or pet item tag)</param>
public record GearDeficit(DeficitKind Kind, string Name, long Have, long Need, string HaveValue = null, string NeedValue = null);

/// <summary>
/// Compares the NBT of an auction with a <see cref="GearTarget"/>. Pure, never mutates the auction.
/// An empty result means the auction meets the spec.
/// </summary>
public static class GearSpecVerifier
{
    private static readonly Regex PetLevelRegex = new(@"\[Lvl (\d{1,3})\]", RegexOptions.Compiled);

    /// <summary>Part keys of the spec to the flattened NBT keys that hold them (newest first)</summary>
    private static readonly Dictionary<string, string[]> PartKeys = new()
    {
        { "drill_part_engine", new[] { "engine.id", "drill_part_engine" } },
        { "drill_part_fuel_tank", new[] { "fuel_tank.id", "drill_part_fuel_tank" } },
        { "drill_part_upgrade_module", new[] { "upgrade_module.id", "drill_part_upgrade_module" } },
        { "hook", new[] { "hook.part" } },
        { "line", new[] { "line.part" } },
        { "sinker", new[] { "sinker.part" } }
    };

    public static bool Meets(SaveAuction auction, GearTarget target) => Deficits(auction, target).Count == 0;

    public static List<GearDeficit> Deficits(SaveAuction auction, GearTarget target)
    {
        var flat = auction.FlatenedNBT ?? new Dictionary<string, string>();
        var result = target.Unsupported.Select(u => new GearDeficit(DeficitKind.Unsupported, u, 0, 0)).ToList();
        if (target.IsPet)
        {
            AddPetDeficits(auction, flat, target, result);
            return result;
        }
        AddEnchantDeficits(auction, target, result);
        AddNumber(result, DeficitKind.Stars, "stars", StarsOf(flat), target.Stars);
        AddNumber(result, DeficitKind.HotPotato, "hpc", Number(flat, "hpc"), target.HotPotatoCount);
        AddRecombAndRarity(auction, flat, target, result);
        AddReforgeDeficit(auction, target, result);
        AddGemDeficits(flat, target, result);
        AddPartDeficits(flat, target, result);
        foreach (var (key, need) in target.Counters)
            AddNumber(result, DeficitKind.Counter, key, Number(flat, key), need);
        foreach (var (key, need) in target.Applied)
            AddNumber(result, DeficitKind.Applied, key, AppliedCount(flat, key), need);
        return result;
    }

    /// <summary>True when no upgrade can ever make the item meet the spec (other pet rarity, rarity out of reach)</summary>
    public static bool HasHardDeficit(SaveAuction auction, GearTarget target)
    {
        if (target.IsPet)
            return target.PetTier != null && auction.Tier != target.PetTier;
        return RarityCheck(auction, target, IsRecombobulated(auction.FlatenedNBT ?? new())) == RarityResult.OutOfReach;
    }

    private enum RarityResult { Met, NeedsRecombobulator, OutOfReach }

    /// <summary>Unknown rarity (clean item without item data) counts as met</summary>
    private static RarityResult RarityCheck(SaveAuction auction, GearTarget target, bool recombed)
    {
        var have = RarityRank(auction.Tier);
        if (target.MinRarity == null || have == 0)
            return RarityResult.Met;
        var need = RarityRank(target.MinRarity.Value);
        if (have >= need)
            return RarityResult.Met;
        return !recombed && have + 1 >= need ? RarityResult.NeedsRecombobulator : RarityResult.OutOfReach;
    }

    /// <summary>Applied count on the item, a present flag without a number counts as one</summary>
    private static long AppliedCount(Dictionary<string, string> flat, string key)
    {
        var count = Number(flat, key);
        return count == 0 && flat.ContainsKey(key) ? 1 : count;
    }

    /// <summary>1 for COMMON up to 7 for DIVINE, specials above, 0 when unknown</summary>
    public static int RarityRank(Tier tier) => tier switch
    {
        Tier.COMMON => 1,
        Tier.UNCOMMON => 2,
        Tier.RARE => 3,
        Tier.EPIC => 4,
        Tier.LEGENDARY => 5,
        Tier.MYTHIC => 6,
        Tier.DIVINE => 7,
        Tier.SPECIAL => 8,
        Tier.VERY_SPECIAL => 9,
        Tier.ULTIMATE => 10,
        _ => 0
    };

    public static bool IsRecombobulated(Dictionary<string, string> flat) => flat.ContainsKey("rarity_upgrades");

    public static int PetLevelOf(SaveAuction auction)
    {
        var match = auction.ItemName == null ? Match.Empty : PetLevelRegex.Match(auction.ItemName);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    public static int EnchantLevel(SaveAuction auction, Enchantment.EnchantmentType type)
        => auction.Enchantments?.Where(e => e.Type == type).Select(e => (int)e.Level).DefaultIfEmpty(0).Max() ?? 0;

    /// <summary>The flattened NBT keys that may hold a part of the spec</summary>
    public static string[] PartKeysOf(string partKey) => PartKeys.GetValueOrDefault(partKey) ?? new[] { partKey };

    public static long Number(Dictionary<string, string> flat, string key)
    {
        if (!flat.TryGetValue(key, out var value))
            return 0;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? (long)number : 0;
    }

    private static void AddPetDeficits(SaveAuction auction, Dictionary<string, string> flat, GearTarget target, List<GearDeficit> result)
    {
        if (target.PetTier != null && auction.Tier != target.PetTier)
            result.Add(new(DeficitKind.PetTier, "tier", RarityRank(auction.Tier), RarityRank(target.PetTier.Value),
                auction.Tier.ToString(), target.PetTier.ToString()));
        AddNumber(result, DeficitKind.PetLevel, target.Tag, PetLevelOf(auction), target.PetLevel);
        var held = flat.GetValueOrDefault("heldItem");
        if (target.PetItem != null && !string.Equals(held, target.PetItem, StringComparison.OrdinalIgnoreCase))
            result.Add(new(DeficitKind.PetItem, "heldItem", 0, 1, held, target.PetItem));
    }

    private static void AddEnchantDeficits(SaveAuction auction, GearTarget target, List<GearDeficit> result)
    {
        foreach (var (type, need) in target.Enchants)
            AddNumber(result, DeficitKind.Enchant, type.ToString(), EnchantLevel(auction, type), need);
    }

    private static void AddRecombAndRarity(SaveAuction auction, Dictionary<string, string> flat, GearTarget target, List<GearDeficit> result)
    {
        var recombed = IsRecombobulated(flat);
        var rarity = RarityCheck(auction, target, recombed);
        if (rarity == RarityResult.OutOfReach)
            result.Add(new(DeficitKind.Rarity, "rarity", RarityRank(auction.Tier), RarityRank(target.MinRarity.Value),
                auction.Tier.ToString(), target.MinRarity.ToString()));
        // a recombobulator also lifts an item one rarity below the target
        if (!recombed && (target.Recombobulated || rarity == RarityResult.NeedsRecombobulator))
            result.Add(new(DeficitKind.Recombobulator, "rarity_upgrades", 0, 1));
    }

    private static void AddReforgeDeficit(SaveAuction auction, GearTarget target, List<GearDeficit> result)
    {
        if (target.Reforge != null && auction.Reforge != target.Reforge)
            result.Add(new(DeficitKind.Reforge, "reforge", 0, 1, auction.Reforge.ToString(), target.Reforge.ToString()));
    }

    private static void AddGemDeficits(Dictionary<string, string> flat, GearTarget target, List<GearDeficit> result)
    {
        foreach (var (slot, gem) in target.Gems)
        {
            var quality = flat.GetValueOrDefault(slot);
            var type = flat.GetValueOrDefault(slot + "_gem") ?? SlotType(slot);
            var have = string.Equals(type, gem.Type, StringComparison.OrdinalIgnoreCase) ? GemRequirement.Rank(quality) : 0;
            if (have < GemRequirement.Rank(gem.Quality))
                result.Add(new(DeficitKind.Gem, slot, have, GemRequirement.Rank(gem.Quality), quality, gem.ItemTag));
        }
    }

    private static void AddPartDeficits(Dictionary<string, string> flat, GearTarget target, List<GearDeficit> result)
    {
        foreach (var (key, part) in target.Parts)
        {
            var have = PartKeysOf(key).Select(flat.GetValueOrDefault).FirstOrDefault(v => v != null);
            if (!string.Equals(have, part, StringComparison.OrdinalIgnoreCase))
                result.Add(new(DeficitKind.Part, key, 0, 1, have, part));
        }
    }

    private static void AddNumber(List<GearDeficit> result, DeficitKind kind, string name, long have, long need)
    {
        if (have < need)
            result.Add(new(kind, name, have, need));
    }

    private static long StarsOf(Dictionary<string, string> flat)
        => Math.Max(Number(flat, "upgrade_level"), Number(flat, "dungeon_item_level"));

    private static string SlotType(string slot)
    {
        var index = slot.LastIndexOf('_');
        return index > 0 ? slot[..index] : slot;
    }
}
