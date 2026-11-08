using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Measured;

/// <summary>What is bought as one: a single piece, or a full set whose set bonus needs all its pieces</summary>
/// <param name="Score">The score the unit adds: a single piece without its set bonus share, a set with the full bonus</param>
public sealed record GearUnit(IReadOnlyList<PricedPiece> Pieces, double Score)
{
    public IEnumerable<string> Slots => Pieces.Select(p => p.Piece.Slot);
    /// <summary>Only composable pieces are in a unit, so every cost is known</summary>
    public double Cost => Pieces.Sum(p => p.Cost ?? 0);
}

/// <summary>The composable units and, per armor slot, the cheapest composable piece to fill an empty slot with</summary>
public sealed record ComposableCatalog(IReadOnlyList<GearUnit> Units, IReadOnlyDictionary<string, GearUnit> ArmorFillers);

/// <summary>A composed build, at most one unit per slot</summary>
public sealed record ComposedBuild(IReadOnlyList<GearUnit> Units)
{
    public static readonly ComposedBuild Empty = new([]);
    public double Score => Units.Sum(u => u.Score);
    public double Cost => Units.Sum(u => u.Cost);
    public IEnumerable<PricedPiece> Pieces => Units.SelectMany(u => u.Pieces);

    /// <summary>This build with <paramref name="unit"/>, every unit sharing a slot with it is replaced</summary>
    public ComposedBuild With(GearUnit unit)
    {
        var slots = unit.Slots.ToHashSet();
        return new ComposedBuild(Units.Where(u => !u.Slots.Any(slots.Contains)).Append(unit).ToList());
    }
}

/// <summary>
/// Composes builds from the priced piece catalog. The score of a build is the sum of its units' scores, which is how
/// PlayerState scores a weighted-sum formula (Σ weight × stat total); the combat formula is a log of a product, its
/// contributions do not add up, so combat builds are not composed (<see cref="CanCompose"/>).
/// <list type="bullet">
/// <item>Only pieces with an exact price are composable: no lower bound, not transient, tradeable (an untradeable item
/// can't be bought within a coin budget).</item>
/// <item>A single piece scores its contribution minus its share of the set bonuses it belongs to; when the share is not
/// an added stat it can't be told apart, then the piece is only bought within its full set.</item>
/// <item>A set unit has, per slot, the member with the highest contribution or the cheapest member (two units when they
/// differ). It needs a composable member in every slot of the set (<see cref="SetSlots"/>): the members' contributions
/// include their share of the full set bonus, a partial set is only the sum of its singles. Its score is the sum of the
/// members' contributions.</item>
/// <item>A tier's build (<see cref="ComposeTier"/>) always has a tool: it starts from the tier below when that has one, else
/// from the composable tool with the highest score that fits the band (no such tool, no build). After the greedy every
/// empty armor slot gets the cheapest composable piece of the slot when it still fits the band and the ceiling.</item>
/// <item>Greedy: from the start build, repeatedly take the unit that adds the most score per extra coin (replacing what
/// is in its slots) while the build stays within the cost limit and below the score ceiling (the late game cohort's
/// score, so a composed build never stands in for the endgame); a replacement that adds score and saves coins goes first.</item>
/// </list>
/// </summary>
public static class BuildComposer
{
    public const string WeightedSum = "weighted-sum";
    public const string AddMode = "add";
    public const int MaxSteps = 64;
    public const string ToolSlot = "tool";
    /// <summary>Every set of PlayerState's effects table is an armor set, some have an extra piece (eg. a gauntlet)</summary>
    public static readonly IReadOnlyList<string> ArmorSlots = ["helmet", "chestplate", "leggings", "boots"];
    private const double Epsilon = 1e-9;

    public static bool CanCompose(GearScoreFormulaInfo formula) => formula?.Kind == WeightedSum;

    public static bool IsComposable(PricedPiece piece)
        => piece?.Piece?.Catalog != null && piece.Cost is > 0 && !piece.LowerBound && !piece.Transient && !piece.NoMarket;

    public static List<GearUnit> Units(IEnumerable<PricedPiece> pieces, GearScoreFormulaInfo formula)
    {
        var all = (pieces ?? []).Where(p => p?.Piece?.Catalog != null).ToList();
        var composable = all.Where(IsComposable).ToList();
        var singles = composable.Select(p => Single(p, formula)).Where(u => u != null);
        return singles.Concat(Sets(composable, all)).ToList();
    }

    public static ComposableCatalog Catalog(IEnumerable<PricedPiece> pieces, GearScoreFormulaInfo formula)
    {
        var list = (pieces ?? []).ToList();
        var fillers = list.Where(IsComposable).Where(p => ArmorSlots.Contains(p.Piece.Slot))
            .GroupBy(p => p.Piece.Slot)
            .ToDictionary(g => g.Key, g => Filler(g.OrderBy(p => p.Cost).ThenByDescending(Contribution).First(), formula));
        return new ComposableCatalog(Units(list, formula), fillers);
    }

    /// <summary>
    /// The build of one tier within <paramref name="maxCost"/> and below <paramref name="scoreCeiling"/>: from
    /// <paramref name="previous"/> (the tier below) when it has a tool, else from the best tool that fits; then the greedy,
    /// then empty armor slots filled with their cheapest piece. Null when no composable tool fits.
    /// </summary>
    public static ComposedBuild ComposeTier(ComposableCatalog catalog, double maxCost, ComposedBuild previous = null, double? scoreCeiling = null)
    {
        var ceiling = scoreCeiling ?? double.PositiveInfinity;
        var start = previous != null && HasTool(previous) ? previous : SeedTool(catalog.Units, maxCost, ceiling);
        if (start == null)
            return null;
        var build = Compose(catalog.Units, maxCost, start, scoreCeiling);
        return FillArmor(build, catalog.ArmorFillers, maxCost, ceiling);
    }

    public static bool HasTool(ComposedBuild build) => build.Units.Any(u => u.Slots.Contains(ToolSlot));

    /// <summary>The tool with the highest score that fits, the best score per coin among equal scores</summary>
    private static ComposedBuild SeedTool(IReadOnlyList<GearUnit> units, double maxCost, double ceiling)
    {
        var tool = units.Where(u => u.Pieces.Count == 1 && u.Pieces[0].Piece.Slot == ToolSlot && u.Cost <= maxCost && u.Score < ceiling)
            .OrderByDescending(u => u.Score).ThenByDescending(u => u.Score / u.Cost).FirstOrDefault();
        return tool == null ? null : ComposedBuild.Empty.With(tool);
    }

    private static ComposedBuild FillArmor(ComposedBuild build, IReadOnlyDictionary<string, GearUnit> fillers, double maxCost, double ceiling)
    {
        foreach (var slot in ArmorSlots)
        {
            if (build.Units.Any(u => u.Slots.Contains(slot)) || !fillers.TryGetValue(slot, out var filler))
                continue;
            var next = build.With(filler);
            if (next.Cost <= maxCost && next.Score < ceiling)
                build = next;
        }
        return build;
    }

    /// <summary>A piece filling an empty slot scores what it adds alone (never below 0; 0 when its set share can't be told apart)</summary>
    private static GearUnit Filler(PricedPiece piece, GearScoreFormulaInfo formula)
    {
        var share = SetShareScore(piece.Piece.Catalog, formula);
        var score = share == null ? 0 : Math.Max(0, Contribution(piece) - share.Value);
        return new GearUnit([piece], score);
    }

    /// <summary>
    /// The build the greedy reaches from <paramref name="start"/> within <paramref name="maxCost"/>, its score below
    /// <paramref name="scoreCeiling"/> (no ceiling when null)
    /// </summary>
    public static ComposedBuild Compose(IReadOnlyList<GearUnit> units, double maxCost, ComposedBuild start = null, double? scoreCeiling = null)
    {
        var build = start ?? ComposedBuild.Empty;
        var ceiling = scoreCeiling ?? double.PositiveInfinity;
        for (var step = 0; step < MaxSteps; step++)
        {
            var next = BestMove(units, build, maxCost, ceiling);
            if (next == null)
                return build;
            build = next;
        }
        return build;
    }

    /// <summary>The score of the piece's set bonus shares, null when a share is not an added stat</summary>
    public static double? SetShareScore(GearCatalogPiece entry, GearScoreFormulaInfo formula)
    {
        var shares = (entry.Effects ?? new()).Where(e => e is { SetShare: true }).ToList();
        if (shares.Any(e => e.Mode != AddMode))
            return null;
        return shares.Sum(e => e.Value * (e.Stats ?? new()).Sum(s => formula?.Weights?.GetValueOrDefault(s) ?? 0));
    }

    private static GearUnit Single(PricedPiece piece, GearScoreFormulaInfo formula)
    {
        var share = SetShareScore(piece.Piece.Catalog, formula);
        if (share == null)
            return null;
        var score = piece.Piece.Catalog.ScoreContribution - share.Value;
        return score > Epsilon ? new GearUnit([piece], score) : null;
    }

    /// <summary>The slots a full set fills: the armor slots and every other slot a catalog member of the set has</summary>
    public static HashSet<string> SetSlots(string setId, IEnumerable<PricedPiece> catalog)
    {
        var slots = ArmorSlots.ToHashSet(StringComparer.Ordinal);
        slots.UnionWith(catalog.Where(p => SetIds(p).Contains(setId)).Select(p => p.Piece.Slot));
        return slots;
    }

    private static IEnumerable<GearUnit> Sets(List<PricedPiece> composable, List<PricedPiece> catalog)
    {
        foreach (var setId in composable.SelectMany(SetIds).Distinct(StringComparer.Ordinal))
        {
            var bySlot = composable.Where(p => SetIds(p).Contains(setId)).GroupBy(p => p.Piece.Slot).ToList();
            if (!SetSlots(setId, catalog).SetEquals(bySlot.Select(g => g.Key)))
                continue;
            var best = SetUnit(bySlot.Select(g => g.OrderByDescending(Contribution).ThenBy(p => p.Cost).First()));
            var cheapest = SetUnit(bySlot.Select(g => g.OrderBy(p => p.Cost).ThenByDescending(Contribution).First()));
            yield return best;
            if (!cheapest.Pieces.SequenceEqual(best.Pieces))
                yield return cheapest;
        }
    }

    private static GearUnit SetUnit(IEnumerable<PricedPiece> members)
    {
        var list = members.ToList();
        return new GearUnit(list, list.Sum(Contribution));
    }

    private static double Contribution(PricedPiece piece) => piece.Piece.Catalog.ScoreContribution;

    private static IEnumerable<string> SetIds(PricedPiece piece)
        => (piece.Piece.Catalog.Effects ?? new()).Where(e => e is { SetShare: true } && e.Id != null).Select(e => e.Id);

    private static ComposedBuild BestMove(IReadOnlyList<GearUnit> units, ComposedBuild build, double maxCost, double ceiling)
        => units.Where(u => !build.Units.Contains(u))
            .Select(u => build.With(u))
            .Where(next => next.Cost <= maxCost && next.Score > build.Score + Epsilon && next.Score < ceiling)
            .OrderByDescending(next => Efficiency(build, next))
            .ThenByDescending(next => next.Score)
            .FirstOrDefault();

    /// <summary>Score gained per extra coin, a replacement that saves coins first</summary>
    private static double Efficiency(ComposedBuild from, ComposedBuild to)
    {
        var cost = to.Cost - from.Cost;
        return cost <= 0 ? double.PositiveInfinity : (to.Score - from.Score) / cost;
    }
}
