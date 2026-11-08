using System.Collections.Generic;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;

/// <summary>
/// One gear piece of a measured setup as SkyUserState produces it.
/// Every level and value is a minimum ("at least"), parts and the reforge are exact.
/// </summary>
public class GearItemSpec
{
    /// <summary>Item tag, eg THEORETICAL_HOE_WHEAT_3</summary>
    public string Tag { get; set; }
    /// <summary>Minimum rarity (after a recombobulator), eg LEGENDARY</summary>
    public string Rarity { get; set; }
    /// <summary>Reforge name as stored on the item, eg blessed</summary>
    public string Reforge { get; set; }
    public int? Stars { get; set; }
    public int? HotPotatoCount { get; set; }
    public bool? Recombobulated { get; set; }
    /// <summary>Lowercase NBT enchant name to minimum level</summary>
    public Dictionary<string, int> Enchants { get; set; } = new();
    /// <summary>Gem slot (eg PERIDOT_0, COMBAT_0) to the minimum gem item tag (eg FINE_PERIDOT_GEM)</summary>
    public Dictionary<string, string> Gems { get; set; } = new();
    /// <summary>NBT part key (drill_part_engine, hook, line, sinker, ...) to the part item tag</summary>
    public Dictionary<string, string> Parts { get; set; } = new();
    /// <summary>Earned NBT counter (eg farmed_cultivating) to the minimum value, can't be bought</summary>
    public Dictionary<string, long> Counters { get; set; } = new();
    /// <summary>Bought and applied counts (eg farming_for_dummies_count, polarvoid, divan_powder_coating) to the minimum count</summary>
    public Dictionary<string, int> Applied { get; set; } = new();
    public string ItemType { get; set; }
}

/// <summary>A pet of a measured setup, the level is a minimum</summary>
public class GearPetSpec
{
    /// <summary>Pet type without the PET_ prefix, eg ELEPHANT</summary>
    public string Type { get; set; }
    /// <summary>Exact pet rarity, eg LEGENDARY</summary>
    public string Tier { get; set; }
    public int Level { get; set; }
    /// <summary>Held pet item tag, eg GREEN_BANDANA</summary>
    public string HeldItem { get; set; }
}
