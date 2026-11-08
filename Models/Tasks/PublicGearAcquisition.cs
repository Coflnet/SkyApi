using System.Collections.Generic;

namespace Coflnet.Sky.Api.Models.Tasks;

/// <summary>Modifiers of a measured gear piece or pet, every level is a minimum</summary>
public class PublicGearSpec
{
    /// <summary>Rarity of the item (recombobulator included) or of the pet</summary>
    public string Rarity { get; set; }
    /// <summary>Reforge, eg. blessed</summary>
    public string Reforge { get; set; }
    /// <summary>Dungeon stars</summary>
    public int? Stars { get; set; }
    /// <summary>Hot and fuming potato books</summary>
    public int? HotPotatoCount { get; set; }
    /// <summary>True when recombobulated</summary>
    public bool? Recombobulated { get; set; }
    /// <summary>Enchant name to level</summary>
    public Dictionary<string, int> Enchants { get; set; } = new();
    /// <summary>Gem slot to gem item tag</summary>
    public Dictionary<string, string> Gems { get; set; } = new();
    /// <summary>Part key (drill_part_engine, hook, ...) to the part item tag</summary>
    public Dictionary<string, string> Parts { get; set; } = new();
    /// <summary>Earned counter (eg. farmed_cultivating) to its value, can only be ground, not bought</summary>
    public Dictionary<string, long> Counters { get; set; } = new();
    /// <summary>Applied item (eg. farming_for_dummies_count) to its count</summary>
    public Dictionary<string, int> Applied { get; set; } = new();
    /// <summary>Item type from the lore, eg. FARMING TOOL</summary>
    public string ItemType { get; set; }
    /// <summary>Rate relevant stats of the item, eg. Farming Fortune</summary>
    public Dictionary<string, double> Stats { get; set; } = new();
    /// <summary>Pets only: minimum pet level</summary>
    public int? PetLevel { get; set; }
    /// <summary>Pets only: held pet item tag</summary>
    public string HeldItem { get; set; }
}

/// <summary>How to get a measured item: buy one that already matches, or buy the cheapest base and upgrade it</summary>
public class PublicAcquisition
{
    /// <summary>Coins the setup cost counts for this item, null when no price is known</summary>
    public double? Cost { get; set; }
    /// <summary>Where <see cref="Cost"/> comes from: direct, base or market (recent sales, when nothing is listed)</summary>
    public string CostSource { get; set; }
    /// <summary>Cheapest auction that already matches, null when there is none</summary>
    public PublicDirectPurchase Direct { get; set; }
    /// <summary>Why <see cref="Direct"/> is null, eg. no_matching_listing</summary>
    public string DirectUnavailableReason { get; set; }
    /// <summary>Cheapest base item plus the upgrades it lacks, null when there is none</summary>
    public PublicCheapestBase CheapestBase { get; set; }
    /// <summary>Why <see cref="CheapestBase"/> is null</summary>
    public string BaseUnavailableReason { get; set; }
}

/// <summary>An auction to buy right away</summary>
public class PublicDirectPurchase
{
    /// <summary>BIN price</summary>
    public long Price { get; set; }
    /// <summary>Auction uuid, 32 hex characters</summary>
    public string AuctionUuid { get; set; }
    /// <summary>In game command that opens the auction, eg. /viewauction &lt;uuid&gt;</summary>
    public string Command { get; set; }
}

/// <summary>The cheapest item to start from and what to apply to it</summary>
public class PublicCheapestBase
{
    /// <summary>Auction of the base item, null when it is a clean item at its clean price</summary>
    public string AuctionUuid { get; set; }
    /// <summary>In game command that opens the base auction, null without an auction</summary>
    public string Command { get; set; }
    /// <summary>Price of the base item</summary>
    public long BasePrice { get; set; }
    /// <summary>Upgrades to buy and apply</summary>
    public List<PublicAcquisitionUpgrade> Upgrades { get; set; } = new();
    /// <summary>Base price plus the priced upgrades</summary>
    public long Total { get; set; }
    /// <summary>Earned modifiers the base lacks, they have to be ground</summary>
    public List<PublicGrindRequirement> GrindRequired { get; set; } = new();
    /// <summary>True when an upgrade has no price, the total is then too low</summary>
    public bool Incomplete { get; set; }
}

/// <summary>One upgrade to apply to a base item</summary>
public class PublicAcquisitionUpgrade
{
    /// <summary>enchant, stars, hotPotato, recombobulator, reforge, gem, gemSlot, part, applied, petItem or unsupported</summary>
    public string Kind { get; set; }
    /// <summary>Enchant name, gem slot, part key or modifier key</summary>
    public string Name { get; set; }
    /// <summary>Level on the base</summary>
    public int? FromLevel { get; set; }
    /// <summary>Target level</summary>
    public int? ToLevel { get; set; }
    /// <summary>Item to buy, null when unknown</summary>
    public string ItemTag { get; set; }
    /// <summary>How many of the item</summary>
    public int Amount { get; set; }
    /// <summary>Coins for this upgrade, 0 when not priced</summary>
    public long Cost { get; set; }
    /// <summary>False when no price was found</summary>
    public bool Priced { get; set; }
}

/// <summary>An earned modifier a base item lacks</summary>
public class PublicGrindRequirement
{
    /// <summary>counter, enchant or petLevel</summary>
    public string Kind { get; set; }
    /// <summary>Counter key, enchant name or pet type</summary>
    public string Name { get; set; }
    /// <summary>Value on the base</summary>
    public long Have { get; set; }
    /// <summary>Value the setup has</summary>
    public long Need { get; set; }
    /// <summary>How much is missing</summary>
    public long Deficit { get; set; }
}

/// <summary>Measured effect of changing part of a setup, from players who played the task with both</summary>
public class PublicModifierEffect
{
    /// <summary>What differs between the two setups</summary>
    public List<PublicGearChange> Changes { get; set; } = new();
    /// <summary>Distinct players, a minimum when <see cref="PlayersAtLeast"/> is true</summary>
    public int Players { get; set; }
    /// <summary>True below 5 players: the numbers are rounded, <see cref="Players"/> and <see cref="Pairs"/> are minimums</summary>
    public bool PlayersAtLeast { get; set; }
    /// <summary>Player count for display, eg. "2+" when rounded</summary>
    public string PlayersLabel { get; set; }
    /// <summary>Before and after pairs over all players, a minimum when <see cref="PlayersAtLeast"/> is true</summary>
    public int Pairs { get; set; }
    /// <summary>Common coins per hour after divided by before</summary>
    public double Ratio { get; set; }
    /// <summary>(Ratio - 1) * 100</summary>
    public double PercentChange { get; set; }
}

/// <summary>One changed part of a setup, eg. tool.enchants.harvesting from 5 to 6</summary>
public class PublicGearChange
{
    /// <summary>tool, armor, pet, equipment, tool.reforge, tool.enchants.&lt;name&gt;, ...</summary>
    public string Component { get; set; }
    /// <summary>Null when the part was absent</summary>
    public string From { get; set; }
    /// <summary>Null when the part was removed</summary>
    public string To { get; set; }
}
