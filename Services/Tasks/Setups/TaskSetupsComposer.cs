using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Api.Services.Tasks.Setups.Measured;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Services.Tasks.Setups;

/// <summary>Values of <see cref="PublicGearSetup.Basis"/></summary>
public static class SetupBasis
{
    public const string Measured = "measured";
    public const string Estimate = "estimate";
}

/// <summary>
/// Everything a task's setups views are built from, shared by the public and the details view.
/// Holds community data only (and numbers of locked tiers), it must never be returned as is.
/// </summary>
/// <param name="Estimates">The curated setups with modelled numbers, null when no profile is researched</param>
/// <param name="Degraded">True when the gear model, estimates or prices were missing because of a failure</param>
/// <param name="CommunityRate">The task's community coins per hour, null when unknown</param>
/// <param name="ScoreFormula">The gear model's score formula, null without a gear model</param>
public record SetupsModel(string Slug, string TaskName, string Category, DateTime? MeasuredAt, TaskGearSetups Estimates,
    TierPlan Plan, IReadOnlyList<GearModifierEffect> ModifierEffects, IReadOnlyDictionary<string, string> TaskSlugs, bool Degraded,
    double? CommunityRate = null, GearScoreFormulaInfo ScoreFormula = null);

/// <summary>Who a view is built for. Every access has its own cached view, the endgame tier is only in <see cref="PremiumPlus"/>.</summary>
public enum SetupsAccess
{
    /// <summary>Anyone: baseline and budget numbers, mid locked, endgame a bare placeholder</summary>
    Public,
    /// <summary>Premium: details of every tier except endgame, which is a bare placeholder</summary>
    Premium,
    /// <summary>Premium plus: everything including the endgame tier</summary>
    PremiumPlus,
}

/// <summary>
/// Builds the public and the details view from a <see cref="SetupsModel"/>. A tier's numbers, player counts, modifiers
/// and acquisition are only copied when the tier is free (<see cref="PublicSetupsMapper.FreeTiers"/>) or the details view is
/// built, stat medians for the details view and modifier effects only for premium plus (pooled transitions of the endgame players can describe endgame gear).
/// The late game cohort only ever travels on the endgame tier. The endgame tier (measured or estimated) is only
/// described for <see cref="SetupsAccess.PremiumPlus"/>, everyone else gets <see cref="EndgamePlaceholder"/> that carries
/// nothing derived from the gear. This is the single place that decides.
/// </summary>
public static class TaskSetupsComposer
{
    public const string PremiumPlusTier = "premium_plus";

    public static PublicTaskSetups Compose(SetupsModel model, IReadOnlyDictionary<string, ItemResaleQuote> quotes, SetupsAccess access)
    {
        var details = access != SetupsAccess.Public;
        var result = new PublicTaskSetups
        {
            Slug = model.Slug,
            TaskName = model.Estimates?.TaskName ?? model.TaskName,
            ProfileName = model.Estimates?.ProfileName,
            Category = string.IsNullOrEmpty(model.Category) ? null : model.Category,
            MeasuredAt = model.MeasuredAt,
            Baseline = MapChoice(model.Plan.Baseline, model, quotes, access),
            Setups = OneEndgamePlaceholder(model.Plan.Setups.Select(c => MapChoice(c, model, quotes, access)).Where(s => s != null)),
            ModifierEffects = access == SetupsAccess.PremiumPlus ? MapEffects(model.ModifierEffects) : null,
            ScoreFormula = MapFormula(model.ScoreFormula),
        };
        if (details || result.Baseline is not { Locked: true })
        {
            result.BaselineCoinsPerHour = model.Plan.BaselineCoinsPerHour;
            result.ComparisonCoinsPerHour = model.Plan.ComparisonCoinsPerHour;
        }
        result.Locked = result.Setups.Append(result.Baseline).Any(s => s is { Locked: true });
        return result;
    }

    /// <summary>Several hidden endgame estimates would tell how many there are and repeat the id, so only the first stays</summary>
    private static List<PublicGearSetup> OneEndgamePlaceholder(IEnumerable<PublicGearSetup> setups)
    {
        var placeholderSeen = false;
        return setups.Where(s =>
        {
            if (s.RequiredTier == null)
                return true;
            var first = !placeholderSeen;
            placeholderSeen = true;
            return first;
        }).ToList();
    }

    public static bool IsOpen(TierChoice choice, SetupsAccess access)
        => access != SetupsAccess.Public || (choice?.Tier != null && PublicSetupsMapper.FreeTiers.Contains(choice.Tier));

    public static bool IsEndgame(TierChoice choice)
        => string.Equals(choice?.Tier, SetupTierSelector.Endgame, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the tier must not be described to this access at all</summary>
    public static bool IsHidden(TierChoice choice, SetupsAccess access)
        => access != SetupsAccess.PremiumPlus && IsEndgame(choice);

    /// <summary>
    /// Stands in for the endgame tier of callers below premium plus. Everything else stays at its default (no name, items,
    /// basis, sources or numbers): anything filled in could be derived from the gear of the few players behind the tier.
    /// </summary>
    public static PublicGearSetup EndgamePlaceholder() => new()
    {
        Locked = true,
        Id = "locked-" + SetupTierSelector.Endgame,
        Tier = SetupTierSelector.Endgame,
        RequiredTier = PremiumPlusTier,
    };

    /// <summary>The estimated setups that get numbers in a view, their items need resale quotes</summary>
    public static List<GearSetupEstimate> QuotedEstimates(TierPlan plan, SetupsAccess access)
        => plan.Setups.Where(c => c is { IsMeasured: false, IsUnrated: false } && !IsHidden(c, access) && IsOpen(c, access))
            .Select(c => c.Estimate).ToList();

    private static PublicGearSetup MapChoice(TierChoice choice, SetupsModel model, IReadOnlyDictionary<string, ItemResaleQuote> quotes, SetupsAccess access)
    {
        if (choice == null)
            return null;
        if (IsHidden(choice, access))
            return EndgamePlaceholder();
        // premium plus sees every tier, a locked "premium plus only" row would make no sense: without a rate there is no row
        if (choice.IsUnrated)
            return null;
        var open = IsOpen(choice, access);
        if (choice.IsMeasured)
            return MeasuredSetupMapper.Map(choice, model.CommunityRate, open, access != SetupsAccess.Public);
        return PublicSetupsMapper.MapEstimate(choice.Estimate, model.Estimates, model.TaskSlugs, quotes, open, choice.DeltaCoinsPerHour, null);
    }

    /// <summary>PlayerState's formula kind of tasks without a gear score (non-combat "other" tasks)</summary>
    public const string NoScoreFormula = "none";

    /// <summary>The formula is the same for every task of a category, nothing in it comes from players; null for a task without a score</summary>
    private static PublicScoreFormula MapFormula(GearScoreFormulaInfo formula)
    {
        if (formula == null || string.IsNullOrEmpty(formula.Kind) || formula.Kind == NoScoreFormula)
            return null;
        return new PublicScoreFormula { Kind = formula.Kind, Expression = formula.Expression, Weights = formula.Weights ?? new() };
    }

    private static List<PublicModifierEffect> MapEffects(IReadOnlyList<GearModifierEffect> effects)
        => (effects ?? []).Where(e => e != null).Select(e => new PublicModifierEffect
        {
            Changes = (e.Changes ?? new()).Select(c => new PublicGearChange { Component = c.Component, From = c.From, To = c.To }).ToList(),
            Players = MeasuredSetupMapper.DisplayedPlayers(e.Players, e.Rounded),
            PlayersAtLeast = e.Rounded,
            PlayersLabel = MeasuredSetupMapper.PlayersLabel(e.Players, e.Rounded),
            Pairs = e.Pairs,
            Ratio = e.Ratio,
            PercentChange = e.PercentChange,
        }).ToList();
}
