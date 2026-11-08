using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Coflnet.Sky.Api.Services.Tasks.Setups;
using NUnit.Framework;

namespace SkyApi.Services.Tasks.Setups;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class ResaleMathTests
{
    private static List<DailyPoint> Series(params double[] values)
        => values.Select((v, i) => new DailyPoint(v, 10, i)).ToList();

    private static List<DailyPoint> Flat(double value, int count = 30, double volume = 10)
        => Enumerable.Range(0, count).Select(i => new DailyPoint(value, volume, i)).ToList();

    [TestCase(999_999d, 999_999 * 0.99)]
    [TestCase(1_000_000d, 990_000)]
    [TestCase(1_000_001d, 1_000_001 - 10_000.01 - 1)]
    [TestCase(10_000_000d, 10_000_000 - 200_000 - 100_000)]
    [TestCase(100_000_000d, 100_000_000 - 2_000_000 - 1_000_000)]
    [TestCase(100_000_001d, 100_000_001 - 2_500_000.025 - 1_000_000.01)]
    public void NetAh_AppliesTierBoundaries(double price, double expected)
        => MarketTax.NetAh(price).Should().BeApproximately(expected, 0.5);

    [Test]
    public void AhCollectionTax_NeverTakesSellerBelowOneMillion()
    {
        MarketTax.AhCollectionTax(1_005_000).Should().BeApproximately(5_000, 0.01);
        MarketTax.AhCollectionTax(1_000_000).Should().Be(0);
    }

    [Test]
    public void NetBazaar_TakesOnePointTwoFivePercent()
        => MarketTax.NetBazaar(1000).Should().BeApproximately(987.5, 0.0001);

    [Test]
    public void Cv_OfConstantSeries_IsZero()
        => ResaleMath.Cv(Flat(500)).Should().BeApproximately(0, 1e-9);

    [Test]
    public void Cv_OfTwoValues_IsStdevOverMean()
        => ResaleMath.Cv(Series(90, 110)).Should().BeApproximately(0.1, 1e-9);

    [Test]
    public void Trend_OfExponentialGrowth_MatchesWeeklyRate()
    {
        var points = Enumerable.Range(0, 20).Select(i => new DailyPoint(100 * Math.Pow(1.01, i), 5, i)).ToList();
        ResaleMath.TrendPerWeek(points).Should().BeApproximately(Math.Pow(1.01, 7) - 1, 1e-9);
    }

    [Test]
    public void Trend_UsesRealDayGaps()
    {
        var points = new List<DailyPoint> { new(100, 1, 0), new(121, 1, 14) };
        ResaleMath.TrendPerWeek(points).Should().BeApproximately(0.1, 1e-9);
    }

    [Test]
    public void Trend_OfDecline_IsNegative()
        => ResaleMath.TrendPerWeek(Series(100, 95, 90, 85, 80)).Should().BeLessThan(0);

    [Test]
    public void P10_IsNearestRank()
    {
        ResaleMath.NearestRankPercentile(Enumerable.Range(1, 10).Select(i => (double)i), 10).Should().Be(1);
        ResaleMath.NearestRankPercentile(Enumerable.Range(1, 11).Select(i => (double)i), 10).Should().Be(2);
        ResaleMath.NearestRankPercentile(Array.Empty<double>(), 10).Should().BeNull();
    }

    [Test]
    public void VolumePerDay_DiffersForAhAndBazaar()
    {
        ResaleMath.VolumePerDay(Flat(1, 30, 3), false).Should().BeApproximately(3, 1e-9);
        ResaleMath.VolumePerDay(Flat(1, 30, 700), true).Should().BeApproximately(100, 1e-9);
    }

    [TestCase(5d, 1.0)]
    [TestCase(4.9d, 0.97)]
    [TestCase(1d, 0.97)]
    [TestCase(0.99d, 0.92)]
    [TestCase(0.2d, 0.92)]
    [TestCase(0.19d, 0.85)]
    public void LiquidityFactor_FollowsAhSteps(double vpd, double expected)
        => ResaleMath.LiquidityFactor(vpd, false).Should().Be(expected);

    [Test]
    public void LiquidityFactor_Bazaar_OnlyPenalisesThinWeeklyVolume()
    {
        ResaleMath.LiquidityFactor(100d / 7, true).Should().Be(1.0);
        ResaleMath.LiquidityFactor(1, true).Should().Be(0.85);
    }

    [TestCase(30, 0.04, -0.01, 6.0, StabilityLabel.Stable)]
    [TestCase(30, 0.04, -0.03, 6.0, StabilityLabel.Moderate)]
    [TestCase(30, 0.10, 0.0, 1.5, StabilityLabel.Moderate)]
    [TestCase(30, 0.10, -0.06, 6.0, StabilityLabel.Volatile)]
    [TestCase(30, 0.30, 0.0, 6.0, StabilityLabel.Volatile)]
    [TestCase(30, 0.01, 0.0, 0.1, StabilityLabel.Illiquid)]
    [TestCase(5, 0.01, 0.0, 9.0, StabilityLabel.Unknown)]
    public void Label_Classifies(int points, double cv, double trend, double vpd, StabilityLabel expected)
        => ResaleMath.Label(points, true, cv, trend, vpd).Should().Be(expected);

    [Test]
    public void Label_WithoutPrice_IsUnknown()
        => ResaleMath.Label(30, false, 0.01, 0, 10).Should().Be(StabilityLabel.Unknown);

    [Test]
    public void StableBase_NeverExceedsSellNow_AndUsesLargerOfP10AndCvHaircut()
    {
        // cv haircut: 1000 * (1 - 0.2) = 800, net p10 = 900 -> 900
        ResaleMath.Resale(1000, 0.2, 912.4, 0, 10, false).Should().BeApproximately(MarketTax.NetAh(912.4), 0.01);
        // p10 below the cv haircut -> haircut wins
        ResaleMath.Resale(1000, 0.2, 100, 0, 10, false).Should().BeApproximately(800, 0.01);
        // p10 above sellNow -> capped at sellNow
        ResaleMath.Resale(1000, 0.0, 5000, 0, 10, false).Should().BeApproximately(1000, 0.01);
    }

    [Test]
    public void Resale_CapsCvHaircutAtFiftyPercent()
        => ResaleMath.Resale(1000, 3.0, null, 0, 10, false).Should().BeApproximately(500, 0.01);

    [Test]
    public void Resale_AppliesTrendOnlyWhenFalling()
    {
        ResaleMath.Resale(1000, 0, 1000, 0.5, 10, false).Should().BeApproximately(1000, 0.01);
        ResaleMath.Resale(1000, 0, 1000, -0.1, 10, false).Should().BeApproximately(900, 0.01);
        ResaleMath.Resale(1000, 0, 1000, -0.9, 10, false).Should().BeApproximately(600, 0.01);
    }

    [Test]
    public void SellNow_UndoesTheAhHaircut_SoTaxIsNotPaidTwice()
    {
        // PricesService reports 980_000 for a 1_000_000 BIN
        var sellNow = ResaleMath.SellNow(new ItemQuote(1_000_000, 980_000, false, true), new List<DailyPoint>());
        sellNow.Should().BeApproximately(MarketTax.NetAh(1_000_000), 0.01);
        sellNow.Should().BeGreaterThan(MarketTax.NetAh(980_000));
    }

    [Test]
    public void SellNow_KeepsPricesThatNeverHadTheHaircut()
    {
        // filtered summary, no-BIN fallback and craft cost capped prices are real sell prices already
        var sellNow = ResaleMath.SellNow(new ItemQuote(1_100_000, 1_000_000, false, false), new List<DailyPoint>());
        sellNow.Should().BeApproximately(MarketTax.NetAh(1_000_000), 0.01);
    }

    [Test]
    public void SellNow_UsesRecentMedianWhenLower()
    {
        var points = Flat(500_000, 10);
        var sellNow = ResaleMath.SellNow(new ItemQuote(1_000_000, 980_000, false, true), points);
        sellNow.Should().BeApproximately(MarketTax.NetAh(500_000), 0.01);
    }

    [Test]
    public void SellNow_IgnoresMedianWithFewerThanThreePoints()
    {
        var sellNow = ResaleMath.SellNow(new ItemQuote(1_000_000, 980_000, false, true), Flat(500_000, 2));
        sellNow.Should().BeApproximately(MarketTax.NetAh(1_000_000), 0.01);
    }

    [Test]
    public void SellNow_Bazaar_UsesBazaarTaxOnly()
        => ResaleMath.SellNow(new ItemQuote(110, 100, true, false), new List<DailyPoint>()).Should().BeApproximately(98.75, 1e-9);

    [Test]
    public void SellNow_WithoutSellPrice_IsNull()
        => ResaleMath.SellNow(new ItemQuote(100, 0, false, true), Flat(100)).Should().BeNull();

    [Test]
    public void Evaluate_StableAhItem_ProducesStableLabelAndResaleBelowBuy()
    {
        var result = ResaleMath.Evaluate("X", new ItemQuote(1_000_000, 980_000, false, true), Flat(1_000_000, 30, 300));
        result.Label.Should().Be(StabilityLabel.Stable);
        result.Cv.Should().BeApproximately(0, 1e-9);
        result.Resale.Should().BeApproximately(MarketTax.NetAh(1_000_000), 0.01);
        result.Resale.Should().BeLessThan(result.Buy!.Value);
    }

    [Test]
    public void Evaluate_NoHistory_IsUnknownButStillHasConservativeResale()
    {
        var result = ResaleMath.Evaluate("X", new ItemQuote(1000, 980, false, true), new List<DailyPoint>());
        result.Label.Should().Be(StabilityLabel.Unknown);
        result.Resale.Should().NotBeNull();
        result.Resale.Should().BeLessThan(result.SellNow!.Value);
    }

    private static ItemResaleQuote Item(string tag, double? buy, double? resale, StabilityLabel label = StabilityLabel.Stable)
        => new(tag, buy, buy, resale, 0, 0, 0, 10, label);

    [Test]
    public void Aggregate_SumsItemsAndComputesBorrowAndPayback()
    {
        var items = new[] { Item("A", 1000, 900), Item("B", 3000, 2000) };
        var result = ResaleMath.Aggregate(items, new HashSet<string>(), 100);
        result.BuyCost.Should().Be(4000);
        result.Resale.Should().Be(2900);
        result.BorrowCost.Should().Be(1100);
        result.ResaleRatio.Should().BeApproximately(0.725, 1e-9);
        result.PaybackBorrowHours.Should().BeApproximately(11, 1e-9);
        result.PaybackFullHours.Should().BeApproximately(40, 1e-9);
        result.NetGainAfter10h.Should().BeApproximately(-100, 1e-9);
        result.NetGainAfter50h.Should().BeApproximately(3900, 1e-9);
        result.PriceIncomplete.Should().BeFalse();
    }

    [Test]
    public void Aggregate_BorrowCostIsNeverNegative()
    {
        var result = ResaleMath.Aggregate(new[] { Item("A", 1000, 1500) }, new HashSet<string>(), 10);
        result.BorrowCost.Should().Be(0);
        result.PaybackBorrowHours.Should().Be(0);
    }

    [TestCase(0d)]
    [TestCase(-5d)]
    public void Aggregate_PaybackIsNullWhenDeltaNotPositive(double delta)
    {
        var result = ResaleMath.Aggregate(new[] { Item("A", 1000, 500) }, new HashSet<string>(), delta);
        result.PaybackBorrowHours.Should().BeNull();
        result.PaybackFullHours.Should().BeNull();
        result.NetGainAfter10h.Should().BeApproximately(delta * 10 - 500, 1e-9);
    }

    [Test]
    public void Aggregate_WithoutDelta_HasNoPaybackOrNetGain()
    {
        var result = ResaleMath.Aggregate(new[] { Item("A", 1000, 500) }, new HashSet<string>(), null);
        result.PaybackBorrowHours.Should().BeNull();
        result.NetGainAfter50h.Should().BeNull();
        result.BorrowCost.Should().Be(500);
    }

    [Test]
    public void Aggregate_SubtractsBaselineTags()
    {
        var items = new[] { Item("OWNED", 5000, 4000), Item("NEW", 1000, 800) };
        var result = ResaleMath.Aggregate(items, new HashSet<string> { "OWNED" }, 100);
        result.BuyCost.Should().Be(1000);
        result.Resale.Should().Be(800);
    }

    [Test]
    public void Aggregate_MissingPrice_IsIncompleteWithNullNumbers()
    {
        var items = new[] { Item("A", 1000, 900), Item("B", null, null, StabilityLabel.Unknown) };
        var result = ResaleMath.Aggregate(items, new HashSet<string>(), 100);
        result.PriceIncomplete.Should().BeTrue();
        result.BuyCost.Should().BeNull();
        result.BorrowCost.Should().BeNull();
        result.PaybackBorrowHours.Should().BeNull();
        result.NetGainAfter10h.Should().BeNull();
        result.Stability.Should().Be(StabilityLabel.Unknown);
    }

    [Test]
    public void Aggregate_StabilityIgnoresItemsBelowTwentyPercentOfCost()
    {
        var items = new[]
        {
            Item("BIG", 9000, 8000, StabilityLabel.Moderate),
            Item("SMALL", 1000, 100, StabilityLabel.Volatile)
        };
        ResaleMath.Aggregate(items, new HashSet<string>(), 1).Stability.Should().Be(StabilityLabel.Moderate);
    }

    [Test]
    public void Aggregate_StabilityIsWorstSignificantLabel()
    {
        var items = new[]
        {
            Item("A", 5000, 4000, StabilityLabel.Stable),
            Item("B", 5000, 4000, StabilityLabel.Volatile)
        };
        ResaleMath.Aggregate(items, new HashSet<string>(), 1).Stability.Should().Be(StabilityLabel.Volatile);
    }

    [Test]
    public void Aggregate_OnlyBaselineItems_IsFreeAndStable()
    {
        var result = ResaleMath.Aggregate(new[] { Item("A", 5, 4) }, new HashSet<string> { "A" }, 10);
        result.BuyCost.Should().Be(0);
        result.Stability.Should().Be(StabilityLabel.Stable);
    }
}

