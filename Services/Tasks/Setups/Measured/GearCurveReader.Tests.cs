using AwesomeAssertions;
using Coflnet.Sky.Api.Services.Tasks.Setups.Measured;
using Coflnet.Sky.PlayerState.Client.Model;
using NUnit.Framework;
using static SkyApi.Services.Tasks.Setups.Measured.MeasuredTestData;

namespace SkyApi.Services.Tasks.Setups.Measured;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class GearCurveReaderTests
{
    private static GearRateCurve TwoPoints(double rare = 0, double? min = null, double? max = null)
        => Curve([(100, 1_000_000), (200, 4_000_000)], rare, min, max);

    [Test]
    public void Between_points_the_rate_is_log_linear()
    {
        var rate = GearCurveReader.RateAt(TwoPoints(), 150);

        rate.CoinsPerHour.Should().Be(2_000_000, "the geometric mean of 1M and 4M");
        rate.Clamped.Should().BeFalse();
    }

    [Test]
    public void Rare_drops_are_added_to_the_common_rate()
        => GearCurveReader.RateAt(TwoPoints(rare: 50_000), 150).CoinsPerHour.Should().Be(2_050_000);

    [Test]
    public void Above_the_observed_range_the_score_is_clamped_to_its_edge()
    {
        var rate = GearCurveReader.RateAt(TwoPoints(), 10_000);

        rate.CoinsPerHour.Should().Be(4_000_000);
        rate.Clamped.Should().BeTrue();
    }

    [Test]
    public void Below_the_observed_range_the_score_is_clamped_to_its_edge()
    {
        var rate = GearCurveReader.RateAt(TwoPoints(), 0);

        rate.CoinsPerHour.Should().Be(1_000_000);
        rate.Clamped.Should().BeTrue();
    }

    [Test]
    public void Between_the_last_point_and_the_observed_maximum_the_last_segment_is_followed()
    {
        var rate = GearCurveReader.RateAt(TwoPoints(max: 300), 250);

        rate.CoinsPerHour.Should().Be(8_000_000, "half a segment further doubles the rate again");
        rate.Clamped.Should().BeFalse("the score was observed");
        GearCurveReader.RateAt(TwoPoints(max: 300), 400).CoinsPerHour.Should().Be(16_000_000, "clamped at 300");
    }

    [Test]
    public void Non_positive_rates_are_interpolated_linearly()
        => GearCurveReader.RateAt(Curve([(100, 0), (200, 1_000_000)]), 150).CoinsPerHour.Should().Be(500_000);

    [Test]
    public void Single_point_gives_its_rate_everywhere()
        => GearCurveReader.RateAt(Curve([(100, 1_000_000)]), 500).CoinsPerHour.Should().Be(1_000_000);

    [Test]
    public void No_curve_or_no_points_gives_no_rate()
    {
        GearCurveReader.RateAt(null, 100).Should().BeNull();
        GearCurveReader.RateAt(new GearRateCurve { Points = new() }, 100).Should().BeNull();
    }

    [TestCase(250, 25)]
    [TestCase(275, 37.5)]
    [TestCase(450, 90)]
    [TestCase(500, 95)]
    [TestCase(600, 100)]
    [TestCase(10, 0)]
    public void Percentile_is_linear_between_the_published_percentiles(double score, double percentile)
    {
        var distribution = Distribution(100, 250, 300, 350, 450);
        var curve = Curve([(100, 1), (450, 2)], min: 50, max: 550);

        GearCurveReader.PercentileOf(distribution, curve, score).Should().Be(percentile);
    }

    [Test]
    public void Percentile_without_a_curve_is_clamped_to_the_published_percentiles()
    {
        var distribution = Distribution(100, 250, 300, 350, 450);

        GearCurveReader.PercentileOf(distribution, null, 1_000).Should().Be(90);
        GearCurveReader.PercentileOf(null, null, 1_000).Should().BeNull();
    }
}
