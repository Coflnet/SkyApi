using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Measured;

/// <summary>A rate read from the curve and whether the score had to be clamped to the observed score range</summary>
public record CurveRate(double CoinsPerHour, bool Clamped);

/// <summary>
/// Reads PlayerState's published rate curve and score distribution at any score. The curve is published as points at
/// the score percentiles 10 .. 90; between two points the common rate is interpolated log-linearly (the curve is a fit of
/// ln(common rate) over the score), beyond the outer points the outer segment is followed up to the observed score range
/// (<see cref="GearRateCurve.ScoreMin"/> .. <see cref="GearRateCurve.ScoreMax"/>) and clamped there. The pooled rare
/// drop value per hour is added to every common rate.
/// </summary>
public static class GearCurveReader
{
    /// <summary>The rate at <paramref name="score"/>, null without a curve point</summary>
    public static CurveRate RateAt(GearRateCurve curve, double score)
    {
        var points = Points(curve);
        if (points.Count == 0)
            return null;
        var (low, high) = Range(curve, points);
        var clamped = Math.Clamp(score, low, high);
        var common = points.Count == 1 ? points[0].CommonCoinsPerHour : Common(points, clamped);
        return new CurveRate(Math.Round(Math.Max(0, common) + curve.RareCoinsPerHour), clamped != score);
    }

    /// <summary>
    /// Where <paramref name="score"/> lies in the distribution (0 .. 100), linear between the published percentiles,
    /// the curve's observed score range standing in for the 0th and 100th; null without a distribution
    /// </summary>
    public static double? PercentileOf(GearScoreSummary distribution, GearRateCurve curve, double score)
    {
        if (distribution == null)
            return null;
        var knots = PercentileKnots(distribution, curve);
        if (score <= knots[0].Score)
            return knots[0].Percentile;
        if (score >= knots[^1].Score)
            return knots[^1].Percentile;
        var upper = knots.FindIndex(k => k.Score >= score);
        return Math.Round(Lerp(knots[upper - 1], knots[upper], score), 1);
    }

    private static List<GearCurvePoint> Points(GearRateCurve curve)
        => (curve?.Points ?? new()).Where(p => p != null).GroupBy(p => p.Score).Select(g => g.First()).OrderBy(p => p.Score).ToList();

    private static (double Low, double High) Range(GearRateCurve curve, List<GearCurvePoint> points)
        => (Math.Min(curve.ScoreMin, points[0].Score), Math.Max(curve.ScoreMax, points[^1].Score));

    /// <summary>The segment holding the score, the outer segment for a score beyond the outer points</summary>
    private static double Common(List<GearCurvePoint> points, double score)
    {
        var upper = points.FindIndex(p => p.Score >= score);
        upper = Math.Clamp(upper < 0 ? points.Count - 1 : upper, 1, points.Count - 1);
        return Interpolate(points[upper - 1], points[upper], score);
    }

    /// <summary>Log-linear when both rates are positive, else linear</summary>
    private static double Interpolate(GearCurvePoint a, GearCurvePoint b, double score)
    {
        var t = (score - a.Score) / (b.Score - a.Score);
        if (a.CommonCoinsPerHour <= 0 || b.CommonCoinsPerHour <= 0)
            return a.CommonCoinsPerHour + t * (b.CommonCoinsPerHour - a.CommonCoinsPerHour);
        return Math.Exp(Math.Log(a.CommonCoinsPerHour) + t * (Math.Log(b.CommonCoinsPerHour) - Math.Log(a.CommonCoinsPerHour)));
    }

    private sealed record Knot(double Score, double Percentile);

    private static List<Knot> PercentileKnots(GearScoreSummary d, GearRateCurve curve)
    {
        var knots = new List<Knot> { new(d.ScoreP10, 10), new(d.ScoreP25, 25), new(d.ScoreP50, 50), new(d.ScoreP75, 75), new(d.ScoreP90, 90) };
        if (curve != null && curve.ScoreMin < d.ScoreP10)
            knots.Insert(0, new Knot(curve.ScoreMin, 0));
        if (curve != null && curve.ScoreMax > d.ScoreP90)
            knots.Add(new Knot(curve.ScoreMax, 100));
        return knots;
    }

    private static double Lerp(Knot a, Knot b, double score)
        => b.Score <= a.Score ? b.Percentile : a.Percentile + (score - a.Score) / (b.Score - a.Score) * (b.Percentile - a.Percentile);
}
