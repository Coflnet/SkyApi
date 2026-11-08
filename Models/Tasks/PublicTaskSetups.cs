using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Api.Models.Tasks;

/// <summary>
/// Gear setups of a task. The baseline and budget setups carry their numbers for everyone; the mid and endgame setups
/// are locked on the public endpoint (their numbers are null) and open on the details endpoint (premium).
/// "Locked: null" below means the field is null while its setup is locked. "Details only" fields are null on the public endpoint.
/// The endgame setup is only described for premium plus on the details endpoint, everyone else gets a placeholder with
/// locked, id, tier "endgame" and requiredTier "premium_plus" and no other field filled in (not even the name or the items).
/// A setup is either measured (played by at least two players) or a labelled estimate for a tier nothing is measured for.
/// </summary>
public class PublicTaskSetups
{
    /// <summary>Slug of the task</summary>
    public string Slug { get; set; }
    /// <summary>Display name of the task</summary>
    public string TaskName { get; set; }
    /// <summary>Name of the gear profile the setups come from, eg. Crop farming</summary>
    public string ProfileName { get; set; }
    /// <summary>Gear category of the task, eg. farming, null when unknown</summary>
    public string Category { get; set; }
    /// <summary>When the measured setups were derived, null when nothing is measured yet</summary>
    public DateTime? MeasuredAt { get; set; }
    /// <summary>True when any setup is locked, so its numbers are withheld</summary>
    public bool Locked { get; set; }
    /// <summary>Coins per hour with the baseline gear</summary>
    public double? BaselineCoinsPerHour { get; set; }
    /// <summary>The gear everyone is assumed to own, the other setups are measured against it. Null when there is none, the baseline rate is the community average then</summary>
    public PublicGearSetup Baseline { get; set; }
    /// <summary>Upgrade setups, usually budget, mid and endgame</summary>
    public List<PublicGearSetup> Setups { get; set; } = new();
    /// <summary>Rate the measured setups' gain is relative to: the measured baseline's, else the community average</summary>
    public double? ComparisonCoinsPerHour { get; set; }
    /// <summary>Premium plus only: measured effect of changing one modifier, eg. Harvesting 5 to 6</summary>
    public List<PublicModifierEffect> ModifierEffects { get; set; }
}

/// <summary>One gear setup</summary>
public class PublicGearSetup
{
    /// <summary>True when the numbers of this setup are withheld (mid and endgame on the public endpoint)</summary>
    public bool Locked { get; set; }
    /// <summary>Only set on the endgame placeholder: the tier needed to see this setup, premium_plus. The placeholder has nothing but locked, id, tier and this</summary>
    public string RequiredTier { get; set; }
    /// <summary>Id of the setup within the task</summary>
    public string Id { get; set; }
    /// <summary>Display name</summary>
    public string Name { get; set; }
    /// <summary>Price tier, eg. budget, mid or endgame</summary>
    public string Tier { get; set; }
    /// <summary>baseline, upgrade or reached</summary>
    public string Status { get; set; }
    /// <summary>measured (from players using this gear) or estimate (modelled, shown where nothing is measured)</summary>
    public string Basis { get; set; }
    /// <summary>low, medium or high; measured setups below 5 players are always low</summary>
    public string Confidence { get; set; }
    /// <summary>What the numbers assume</summary>
    public List<string> Assumptions { get; set; } = new();
    /// <summary>https sources the setup is based on</summary>
    public List<string> Sources { get; set; } = new();
    /// <summary>Slug of the task this setup unlocks, if any</summary>
    public string UnlocksTask { get; set; }
    /// <summary>The gear pieces</summary>
    public List<PublicGearItem> Items { get; set; } = new();

    /// <summary>Measured only, locked: null. Distinct players, a minimum when <see cref="PlayersAtLeast"/> is true</summary>
    public int? Players { get; set; }
    /// <summary>Measured only, locked: null. True below 5 players: the rates are rounded and <see cref="Players"/> is a minimum ("2+")</summary>
    public bool? PlayersAtLeast { get; set; }
    /// <summary>Measured only, locked: null. Player count for display, eg. "2+" when rounded, else the exact count</summary>
    public string PlayersLabel { get; set; }
    /// <summary>Measured only, locked: null. Number of play sessions, below 5 players the lower bound of a 1-2-5 band</summary>
    public int? Observations { get; set; }
    /// <summary>Measured only, locked: null. Play sessions for display, eg. "5+" when banded, else the exact count</summary>
    public string ObservationsLabel { get; set; }
    /// <summary>Measured only, locked: null. Hours played with this setup, 2 significant digits when rounded</summary>
    public double? Hours { get; set; }
    /// <summary>Measured only, locked: null. 25th percentile of the players' common coins per hour, null below 5 players</summary>
    public double? CommonP25 { get; set; }
    /// <summary>Measured only, locked: null. 75th percentile of the players' common coins per hour, null below 5 players</summary>
    public double? CommonP75 { get; set; }
    /// <summary>Details only, measured only: median stats of the players with this setup, eg. Farming Fortune</summary>
    public Dictionary<string, double> StatMedians { get; set; }
    /// <summary>Locked: null. Coins per hour with this setup</summary>
    public double? CoinsPerHour { get; set; }
    /// <summary>Locked: null. Coins per hour gained over the baseline</summary>
    public double? DeltaCoinsPerHour { get; set; }
    /// <summary>Locked: null. Coins per hour relative to the community average</summary>
    public double? Multiplier { get; set; }
    /// <summary>Locked: null. Price of the items that are not owned yet</summary>
    public double? BuyCost { get; set; }
    /// <summary>Locked: null. What the bought items can be sold for after fees</summary>
    public double? ResaleValue { get; set; }
    /// <summary>Locked: null. Coins lost when buying and later selling the items</summary>
    public double? BorrowCost { get; set; }
    /// <summary>Locked: null. Resale value divided by buy cost</summary>
    public double? ResaleRatio { get; set; }
    /// <summary>Locked: null. Hours until the extra income covers the borrow cost</summary>
    public double? PaybackHoursBorrow { get; set; }
    /// <summary>Locked: null. Hours until the extra income covers the full buy cost</summary>
    public double? PaybackHoursFull { get; set; }
    /// <summary>Locked: null. Income gain after 10 hours minus the borrow cost</summary>
    public double? NetGain10h { get; set; }
    /// <summary>Locked: null. Income gain after 50 hours minus the borrow cost</summary>
    public double? NetGain50h { get; set; }
    /// <summary>Locked: null. Stable, moderate, volatile, illiquid or unknown market for the bought items</summary>
    public string Stability { get; set; }
    /// <summary>Locked: null. True when a price was missing: the resale and payback numbers are then null, and so is buyCost unless every buy price is known</summary>
    public bool? PriceIncomplete { get; set; }
    /// <summary>Measured only, locked: null. True when an item's price is only a lower bound (its plain item's or a base that still needs grinding), the real cost may be higher</summary>
    public bool? PriceLowerBound { get; set; }
    /// <summary>Locked: null. True when the setup does not earn more than the baseline, so there is no payback</summary>
    public bool? NoProfitGain { get; set; }
}

/// <summary>One gear piece of a setup</summary>
public class PublicGearItem
{
    /// <summary>Slot, eg. helmet or tool</summary>
    public string Slot { get; set; }
    /// <summary>Item tag</summary>
    public string ItemTag { get; set; }
    /// <summary>Display name</summary>
    public string ItemName { get; set; }
    /// <summary>Why the piece is in the setup</summary>
    public string Reason { get; set; }
    /// <summary>False for items that can't be traded, they are excluded from all costs</summary>
    public bool Tradeable { get; set; }
    /// <summary>Measured only, locked: null. False when too few players share the modifiers, the item is then priced without any</summary>
    public bool? ModifiersMeasured { get; set; }
    /// <summary>Measured only, locked: null. True when the baseline has the same item, it is left out of the setup cost</summary>
    public bool? Owned { get; set; }
    /// <summary>Measured only, locked: null. The modifiers the measured item has</summary>
    public PublicGearSpec Spec { get; set; }
    /// <summary>Measured only, locked: null. How to get the item, buy it now or upgrade the cheapest base</summary>
    public PublicAcquisition Acquisition { get; set; }

    /// <summary>Locked: null. Price to buy, null for untradeable items and for owned items of estimated setups</summary>
    public double? BuyPrice { get; set; }
    /// <summary>Measured only, locked: null. True when <see cref="BuyPrice"/> is only a lower bound</summary>
    public bool? PriceLowerBound { get; set; }
    /// <summary>Locked: null. What the item sells for after fees</summary>
    public double? ResaleValue { get; set; }
    /// <summary>Locked: null. Market stability label</summary>
    public string Stability { get; set; }
    /// <summary>Locked: null. Coefficient of variation of the daily price over 30 days</summary>
    public double? Cv { get; set; }
    /// <summary>Locked: null. Relative price change per week</summary>
    public double? TrendPerWeek { get; set; }
    /// <summary>Locked: null. 10th percentile of the daily price</summary>
    public double? P10 { get; set; }
    /// <summary>Locked: null. Sales per day</summary>
    public double? VolumePerDay { get; set; }
}
