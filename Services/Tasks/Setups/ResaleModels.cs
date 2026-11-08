namespace Coflnet.Sky.Api.Services.Tasks.Setups;

public enum StabilityLabel
{
    Stable,
    Moderate,
    Volatile,
    Illiquid,
    Unknown
}

/// <summary>
/// Current prices of one item, Sell is what the market currently pays.
/// <paramref name="SellIncludesAhHaircut"/> is true only when Sell is lowest BIN times 0.98 (PricesService BIN path),
/// the haircut is only undone then.
/// </summary>
public record ItemQuote(double? Buy, double? Sell, bool IsBazaar, bool SellIncludesAhHaircut = false);

/// <summary>A price from <see cref="IItemPriceSource"/> and whether its Sell carries the PricesService 0.98 haircut</summary>
public record PriceSnapshot(Coflnet.Sky.CurrentPrice Price, bool SellIncludesAhHaircut);

/// <summary>One day of price history. Day is an ascending day number, negative means use list position</summary>
public record DailyPoint(double Value, double Volume, int Day = -1);

public record ItemResaleQuote(
    string Tag,
    double? Buy,
    double? SellNow,
    double? Resale,
    double? Cv,
    double? TrendPerWeek,
    double? P10,
    double VolumePerDay,
    StabilityLabel Label,
    bool Degraded = false)
{
    /// <summary>Quote without data. <see cref="Degraded"/> marks quotes that are missing because a price lookup failed or ran out of time</summary>
    public static ItemResaleQuote Unknown(string tag) => new(tag, null, null, null, null, null, null, 0, StabilityLabel.Unknown);
}

public record SetupResale(
    double? BuyCost,
    double? Resale,
    double? BorrowCost,
    double? ResaleRatio,
    double? PaybackBorrowHours,
    double? PaybackFullHours,
    double? NetGainAfter10h,
    double? NetGainAfter50h,
    StabilityLabel Stability,
    bool PriceIncomplete);
