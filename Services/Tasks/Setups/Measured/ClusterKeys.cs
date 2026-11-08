using System;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Measured;

/// <summary>
/// Reads PlayerState's setup keys ("L2|armor=A,B,C,D|pet=ELEPHANT:LEGENDARY|tool=X"). Keys are nested: a finer key has
/// every part of its coarser parent, so the parent's observations include the child's.
/// </summary>
public static class ClusterKeys
{
    public static IReadOnlyDictionary<string, string> Parts(string key)
    {
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in (key ?? "").Split('|').Where(s => s.Contains('=')))
        {
            var split = segment.IndexOf('=');
            parts[segment[..split]] = segment[(split + 1)..];
        }
        return parts;
    }

    /// <summary>
    /// True when <paramref name="parent"/> is a coarser key of the same setup as <paramref name="child"/>: the child names
    /// it as its parent, or (a level between them was not published) the child's key has every part of the parent's
    /// </summary>
    public static bool IsAncestor(PricedCluster parent, PricedCluster child)
    {
        if (parent.Cluster.Level >= child.Cluster.Level || string.IsNullOrEmpty(parent.Cluster.Key))
            return false;
        if (child.Cluster.ParentKey == parent.Cluster.Key)
            return true;
        return parent.KeyParts.Count > 0 && parent.KeyParts.All(p => child.KeyParts.TryGetValue(p.Key, out var value) && value == p.Value);
    }

    public static bool AreRelated(PricedCluster a, PricedCluster b)
        => ReferenceEquals(a, b) || IsAncestor(a, b) || IsAncestor(b, a);

    /// <summary>
    /// Drops a parent that has a finer descendant among the candidates which it does not dominate: the finest level wins,
    /// unless the parent is cheaper and better, then the dominated child is no reason to hide it.
    /// Then drops every candidate a relative dominates, so in a family P -> {C1, C2} where P dominates C1 but not C2,
    /// neither P (it has the better child C2) nor C1 (P beats it) can be picked.
    /// </summary>
    public static List<PricedCluster> Finest(IReadOnlyCollection<PricedCluster> candidates)
    {
        var finest = candidates.Where(c => !candidates.Any(other => IsAncestor(c, other) && !Dominates(c, other)));
        return finest.Where(c => !candidates.Any(other => !ReferenceEquals(other, c) && AreRelated(other, c) && Dominates(other, c))).ToList();
    }

    /// <summary>
    /// True when <paramref name="a"/> costs at most as much and earns at least as much as <paramref name="b"/>, better in one.
    /// Without both prices only the rate counts.
    /// </summary>
    public static bool Dominates(PricedCluster a, PricedCluster b)
    {
        if (a.FullCost is not { } costA || b.FullCost is not { } costB)
            return a.Rate > b.Rate;
        return costA <= costB && a.Rate >= b.Rate && (costA < costB || a.Rate > b.Rate);
    }
}
