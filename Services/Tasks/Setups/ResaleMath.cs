using System;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.Api.Services.Tasks.Setups;

/// <summary>Pure resale ("borrow") economics for gear: what is left after buying an item and selling it back</summary>
public static class ResaleMath
{
    public const int HistoryDays = 30;
    public const int MinPointsForStats = 7;
    public const double SignificantCostShare = 0.2;
    private const double MaxCvHaircut = 0.5;
    private const double MaxTrendLoss = 0.4;
    private const double BazaarMinWeeklyVolume = 100;

    public static double? Cv(IReadOnlyList<DailyPoint> points)
    {
        if (points.Count < 2)
            return null;
        var mean = points.Average(p => p.Value);
        if (mean <= 0)
            return null;
        var variance = points.Sum(p => (p.Value - mean) * (p.Value - mean)) / points.Count;
        return Math.Sqrt(variance) / mean;
    }

    /// <summary>Relative price change per week from a log-linear OLS fit over the day index</summary>
    public static double? TrendPerWeek(IReadOnlyList<DailyPoint> points)
    {
        var usable = points.Where(p => p.Value > 0).ToList();
        if (usable.Count < 2)
            return null;
        var days = DayIndexes(usable);
        var logs = usable.Select(p => Math.Log(p.Value)).ToList();
        var meanX = days.Average();
        var denominator = days.Sum(x => (x - meanX) * (x - meanX));
        if (denominator == 0)
            return null;
        var slope = days.Zip(logs, (x, y) => (x - meanX) * y).Sum() / denominator;
        return Math.Exp(7 * slope) - 1;
    }

    private static List<double> DayIndexes(IReadOnlyList<DailyPoint> points)
    {
        if (points.Any(p => p.Day < 0))
            return Enumerable.Range(0, points.Count).Select(i => (double)i).ToList();
        return points.Select(p => (double)p.Day).ToList();
    }

    /// <summary>Nearest-rank percentile, same method as PricesService.NearestRankPercentile</summary>
    public static double? NearestRankPercentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0)
            return null;
        var rank = Math.Clamp((int)Math.Ceiling(percentile / 100.0 * sorted.Count), 1, sorted.Count);
        return sorted[rank - 1];
    }

    public static double VolumePerDay(IReadOnlyList<DailyPoint> points, bool isBazaar)
    {
        if (points.Count == 0)
            return 0;
        // bazaar volume is a moving weekly figure, auction volume is sales per day
        return isBazaar ? points.Average(p => p.Volume) / 7.0 : points.Sum(p => p.Volume) / HistoryDays;
    }

    public static double LiquidityFactor(double volumePerDay, bool isBazaar)
    {
        if (isBazaar)
            return volumePerDay * 7 < BazaarMinWeeklyVolume ? 0.85 : 1.0;
        if (volumePerDay >= 5) return 1.0;
        if (volumePerDay >= 1) return 0.97;
        return volumePerDay >= 0.2 ? 0.92 : 0.85;
    }

    /// <summary>Only declines are punished: a rising price is not assumed to continue</summary>
    public static double TrendFactor(double? trendPerWeek)
        => Math.Clamp(1 + Math.Min(0, trendPerWeek ?? 0), 1 - MaxTrendLoss, 1);

    public static StabilityLabel Label(int pointCount, bool hasPrice, double? cv, double? trendPerWeek, double volumePerDay)
    {
        if (pointCount < MinPointsForStats || !hasPrice || cv == null)
            return StabilityLabel.Unknown;
        var trend = trendPerWeek ?? 0;
        if (volumePerDay < 0.2) return StabilityLabel.Illiquid;
        if (cv <= 0.05 && trend >= -0.02 && volumePerDay >= 5) return StabilityLabel.Stable;
        if (cv <= 0.15 && trend >= -0.05 && volumePerDay >= 1) return StabilityLabel.Moderate;
        return StabilityLabel.Volatile;
    }

    public static bool IsBorrowable(StabilityLabel label) => label is StabilityLabel.Stable or StabilityLabel.Moderate;

    /// <summary>
    /// Coins received when selling the item right now. When the current sell price contains the 0.98 haircut of
    /// PricesService (<see cref="ItemQuote.SellIncludesAhHaircut"/>) it is undone so the real fees are not paid twice.
    /// Prices that never had the haircut (filtered summaries, the no-BIN fallback, craft cost capped) are used as is.
    /// </summary>
    public static double? SellNow(ItemQuote quote, IReadOnlyList<DailyPoint> points)
    {
        if (quote.Sell is not > 0)
            return null;
        if (quote.IsBazaar)
            return MarketTax.NetBazaar(quote.Sell.Value);
        var listPrice = quote.SellIncludesAhHaircut ? quote.Sell.Value / MarketTax.PricesServiceAhHaircut : quote.Sell.Value;
        var recent = points.TakeLast(7).Select(p => p.Value).ToList();
        if (recent.Count >= 3)
            listPrice = Math.Min(listPrice, Median(recent));
        return MarketTax.NetAh(listPrice);
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static double Net(double price, bool isBazaar) => isBazaar ? MarketTax.NetBazaar(price) : MarketTax.NetAh(price);

    /// <summary>Resale value: a price the item can be sold for with some certainty, not the best case</summary>
    public static double? Resale(double? sellNow, double? cv, double? p10, double? trendPerWeek, double volumePerDay, bool isBazaar)
    {
        if (sellNow is not { } now)
            return null;
        var cvHaircut = now * (1 - Math.Min(cv ?? MaxCvHaircut, MaxCvHaircut));
        var floor = p10 is { } low ? Math.Max(Net(low, isBazaar), cvHaircut) : cvHaircut;
        var stableBase = Math.Min(now, floor);
        return stableBase * TrendFactor(trendPerWeek) * LiquidityFactor(volumePerDay, isBazaar);
    }

    public static ItemResaleQuote Evaluate(string tag, ItemQuote quote, IReadOnlyList<DailyPoint> points)
    {
        var buy = quote.Buy is > 0 ? quote.Buy : null;
        var cv = Cv(points);
        var trend = TrendPerWeek(points);
        var p10 = NearestRankPercentile(points.Select(p => p.Value), 10);
        var vpd = VolumePerDay(points, quote.IsBazaar);
        var sellNow = SellNow(quote, points);
        var resale = Resale(sellNow, cv, p10, trend, vpd, quote.IsBazaar);
        var label = Label(points.Count, sellNow != null, cv, trend, vpd);
        return new ItemResaleQuote(tag, buy, sellNow, resale, cv, trend, p10, vpd, label);
    }

    /// <summary>
    /// Aggregates the items a setup adds on top of the baseline into buy, resale and payback numbers.
    /// <paramref name="baselineTags"/> matches by plain tag; callers with variant priced items (pets) filter owned
    /// items by tag and filters themselves and pass an empty set.
    /// </summary>
    public static SetupResale Aggregate(IEnumerable<ItemResaleQuote> items, ISet<string> baselineTags, double? deltaCoinsPerHour)
    {
        var added = items.Where(i => !baselineTags.Contains(i.Tag)).ToList();
        var incomplete = added.Any(i => i.Buy == null || i.Resale == null);
        var stability = WorstSignificantLabel(added);
        if (incomplete)
            return new SetupResale(null, null, null, null, null, null, null, null, stability, true);
        var buyCost = added.Sum(i => i.Buy!.Value);
        var resale = added.Sum(i => i.Resale!.Value);
        var borrowCost = Math.Max(0, buyCost - resale);
        return new SetupResale(
            buyCost, resale, borrowCost,
            buyCost > 0 ? resale / buyCost : null,
            Payback(borrowCost, deltaCoinsPerHour),
            Payback(buyCost, deltaCoinsPerHour),
            NetGain(deltaCoinsPerHour, 10, borrowCost),
            NetGain(deltaCoinsPerHour, 50, borrowCost),
            stability, false);
    }

    private static double? Payback(double cost, double? delta)
        => delta is > 0 ? cost / delta : null;

    private static double? NetGain(double? delta, double hours, double borrowCost)
        => delta is { } d ? d * hours - borrowCost : null;

    /// <summary>Worst label among items that make up at least 20% of the cost</summary>
    private static StabilityLabel WorstSignificantLabel(List<ItemResaleQuote> items)
    {
        var total = items.Sum(i => i.Buy ?? 0);
        var significant = items.Where(i => i.Buy == null || (total > 0 && i.Buy / total >= SignificantCostShare)).ToList();
        if (significant.Count == 0)
            return items.Count == 0 ? StabilityLabel.Stable : StabilityLabel.Unknown;
        return significant.Max(i => i.Label);
    }
}
