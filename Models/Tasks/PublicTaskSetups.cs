using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Api.Models.Tasks;

/// <summary>
/// Gear setups of a task. The baseline and budget setups carry their numbers for everyone; the mid and endgame setups
/// are locked on the public endpoint (their numbers are null) and open on the details endpoint (premium).
/// "Locked: null" below means the field is null while its setup is locked. "Details only" fields are null on the public endpoint.
/// The endgame setup is only described for premium plus on the details endpoint, everyone else gets a placeholder with
/// locked, id, tier "endgame" and requiredTier "premium_plus" and no other field filled in (not even the name or the items).
/// A setup is either measured or a labelled estimate for a tier nothing is measured for. Measured setups come from the
/// task's gear score model (every number from at least two distinct players): budget and mid are composed from the
/// measured pieces within their cost band (budget up to 10M, mid up to 100M) and their rate is predicted from the
/// measured rate curve at the build's gear score (rateBasis "curve"); they stay below the late game cohort's score and
/// never earn as much as the endgame. The endgame is the "maximum achievable" build of the best measured piece per slot
/// with the rate of the curve or, without one, of the late game cohort (rateBasis "cohort"), plus the cohort's numbers. There is no measured baseline: the baseline is the curated estimate,
/// else the community average.
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
    /// <summary>How the gear score of the measured setups is computed, null when nothing is measured for the task</summary>
    public PublicScoreFormula ScoreFormula { get; set; }
}

/// <summary>The gear score: the task category's formula over the stats of tool, armor, equipment and pet plus approximate special effects</summary>
public class PublicScoreFormula
{
    /// <summary>weighted-sum (Σ weight × stat total) or log-damage-product (combat, a log scale)</summary>
    public string Kind { get; set; }
    /// <summary>Readable formula, eg. "1 × Farming Fortune + 1 × Wheat Fortune"</summary>
    public string Expression { get; set; }
    /// <summary>Stat name to weight (for the damage product each listed stat is a factor)</summary>
    public Dictionary<string, double> Weights { get; set; } = new();
}

/// <summary>
/// Premium plus only, endgame only: the late game cohort, each player's best build from the 90th score percentile
/// (widened down to at least two players, never below the 75th). Its rate is measured, the endgame's own rate is
/// predicted for the endgame build's score.
/// </summary>
public class PublicLateGame
{
    /// <summary>Gear score from which builds belong to the cohort</summary>
    public double ScoreFrom { get; set; }
    /// <summary>True when the cohort had to be widened below the 90th percentile to reach two players</summary>
    public bool Widened { get; set; }
    /// <summary>Distinct players, a minimum when <see cref="PlayersAtLeast"/> is true</summary>
    public int Players { get; set; }
    /// <summary>True below 5 players: the numbers are rounded and <see cref="Players"/> is a minimum ("2+")</summary>
    public bool PlayersAtLeast { get; set; }
    /// <summary>Player count for display, eg. "2+" when rounded</summary>
    public string PlayersLabel { get; set; }
    /// <summary>Hours played, 2 significant digits (a band lower bound below 5 players)</summary>
    public double Hours { get; set; }
    /// <summary>Median over the players of their common coins per hour</summary>
    public double CommonCoinsPerHour { get; set; }
    /// <summary>Rare drop value per hour, pooled</summary>
    public double RareCoinsPerHour { get; set; }
    public double CoinsPerHour { get; set; }
    /// <summary>25th / 75th percentile of the players' common rates, null below 5 players</summary>
    public double? CommonP25 { get; set; }
    public double? CommonP75 { get; set; }
    /// <summary>low, medium or high; always low below 5 players</summary>
    public string Confidence { get; set; }
    /// <summary>Median stats of the cohort's players, eg. Farming Fortune</summary>
    public Dictionary<string, double> StatMedians { get; set; }
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
    /// <summary>low, medium or high; measured setups: locked: null, low below 5 players or when extrapolated, composed budget and mid at most medium</summary>
    public string Confidence { get; set; }
    /// <summary>What the numbers assume</summary>
    public List<string> Assumptions { get; set; } = new();
    /// <summary>https sources the setup is based on</summary>
    public List<string> Sources { get; set; } = new();
    /// <summary>Slug of the task this setup unlocks, if any</summary>
    public string UnlocksTask { get; set; }
    /// <summary>The gear pieces</summary>
    public List<PublicGearItem> Items { get; set; } = new();

    /// <summary>Measured endgame only (its late game cohort), locked: null. Distinct players, a minimum when <see cref="PlayersAtLeast"/> is true; null for the composed budget and mid builds</summary>
    public int? Players { get; set; }
    /// <summary>Measured only, locked: null. True below 5 players: the rates are rounded and <see cref="Players"/> is a minimum ("2+")</summary>
    public bool? PlayersAtLeast { get; set; }
    /// <summary>Measured only, locked: null. Player count for display, eg. "2+" when rounded, else the exact count</summary>
    public string PlayersLabel { get; set; }
    /// <summary>Not filled for setups of the gear score model (kept for older clients). Number of play sessions</summary>
    public int? Observations { get; set; }
    /// <summary>Not filled for setups of the gear score model (kept for older clients). Play sessions for display</summary>
    public string ObservationsLabel { get; set; }
    /// <summary>Measured endgame only (its late game cohort), locked: null. Hours played, 2 significant digits when rounded</summary>
    public double? Hours { get; set; }
    /// <summary>Measured only, locked: null. 25th percentile of the players' common coins per hour, null below 5 players</summary>
    public double? CommonP25 { get; set; }
    /// <summary>Measured only, locked: null. 75th percentile of the players' common coins per hour, null below 5 players</summary>
    public double? CommonP75 { get; set; }
    /// <summary>Details only, measured endgame only: median stats of the late game cohort's players, eg. Farming Fortune</summary>
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
    /// <summary>Locked or unknown payback: null. True when full buy cost takes more than 150 hours to recover; budget upgrades with this flag are omitted</summary>
    public bool? PaybackTooLong { get; set; }
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
    /// <summary>Measured only, locked: null. Gear score of the build (see <see cref="PublicTaskSetups.ScoreFormula"/>), set bonuses of full sets included</summary>
    public double? Score { get; set; }
    /// <summary>Measured only, locked: null. Where the score lies among the measured builds of the task (0 .. 100), null without a score distribution</summary>
    public double? ScorePercentile { get; set; }
    /// <summary>Measured only, locked: null. curve: the rate is predicted from the measured rate curve at the score; cohort: the measured rate of the late game cohort (endgame without a curve)</summary>
    public string RateBasis { get; set; }
    /// <summary>Measured only, locked: null. True when the score is outside the observed score range: budget and mid take the curve's rate at the range edge, the endgame reads the curve at most 20 % beyond it</summary>
    public bool? Extrapolated { get; set; }
    /// <summary>Measured only, locked: null. Endgame: true when the rate predicted from the curve was above the late game cohort's published rate and was capped there; false for budget and mid</summary>
    public bool? Capped { get; set; }
    /// <summary>Premium plus only, measured endgame only: the late game cohort the endgame is compared with; its players, hours and quartiles are also in this setup's fields. Null for budget and mid, which are composed and have no players of their own</summary>
    public PublicLateGame LateGame { get; set; }
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
    /// <summary>Measured only, locked: null. Always false: there is no measured baseline whose items would be owned</summary>
    public bool? Owned { get; set; }
    /// <summary>Measured only, locked: null. The score the piece adds, its share of the full set bonus included</summary>
    public double? ScoreContribution { get; set; }
    /// <summary>Measured only, locked: null. Median score percentile (0 .. 100) of the measured builds using the piece, null when unknown</summary>
    public double? UsedAtScorePercentile { get; set; }
    /// <summary>Measured only, locked: null. Distinct players seen with the piece, a minimum when <see cref="PlayersAtLeast"/> is true</summary>
    public int? Players { get; set; }
    /// <summary>Measured only, locked: null. True below 5 players: <see cref="Players"/> is a minimum ("2+")</summary>
    public bool? PlayersAtLeast { get; set; }
    /// <summary>Measured only, locked: null. Player count for display, eg. "2+" when rounded</summary>
    public string PlayersLabel { get; set; }
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
