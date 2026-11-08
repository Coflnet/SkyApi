using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Measured;

/// <summary>Values of <see cref="PricedBuild.RateBasis"/></summary>
public static class RateBases
{
    /// <summary>The rate is read from the measured rate curve at the build's score</summary>
    public const string Curve = "curve";
    /// <summary>No curve: the rate is the measured late game cohort's</summary>
    public const string Cohort = "cohort";
}

/// <summary>
/// A measured build: catalog pieces composed for a cost band, or PlayerState's recommended endgame, with its priced
/// pieces and the rate the gear model gives for its score.
/// </summary>
/// <param name="Score">Gear score of the build: the pieces' contributions, set bonuses of full sets included</param>
/// <param name="ScorePercentile">Where the score lies in the task's score distribution (0 .. 100), null without one</param>
/// <param name="RateBasis">See <see cref="RateBases"/></param>
/// <param name="Extrapolated">The score is outside the observed score range: the rate is the curve's at the range edge (endgame: at most 20 % beyond it)</param>
/// <param name="Capped">Endgame only: the curve's rate was above the late game cohort's published rate and was capped there (PlayerState caps it)</param>
/// <param name="Confidence">low or medium (predictions are never high), the late game cohort's for the endgame</param>
/// <param name="LateGame">Endgame only: the measured late game cohort (top score percentiles), null for composed builds</param>
public record PricedBuild(IReadOnlyList<PricedPiece> Pieces, double Rate, double Score, double? ScorePercentile, string RateBasis,
    bool Extrapolated, string Confidence, bool Capped = false, GearLateGame LateGame = null)
{
    public bool Priced => Pieces.All(p => p.Priced);
    /// <summary>What every item costs together, null when an item has no price</summary>
    public double? FullCost => Priced ? Pieces.Sum(p => p.Cost ?? 0) : null;
    /// <summary>True when <see cref="FullCost"/> is only a lower bound</summary>
    public bool LowerBound => Pieces.Any(p => p.LowerBound);
    /// <summary>An item's price lookup failed: the build sits this build out instead of being shown with a wrong price</summary>
    public bool TransientlyUnpriced => Pieces.Any(p => p.Transient);

    public HashSet<string> PieceIds() => Pieces.Select(p => p.Piece.Id).ToHashSet();

    /// <summary>True when both builds consist of the same catalog pieces</summary>
    public bool SameBuildAs(PricedBuild other) => other != null && PieceIds().SetEquals(other.PieceIds());
}
