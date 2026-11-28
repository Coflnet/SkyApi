using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Services.Tasks;

/// <summary>
/// Maps upstream task estimates to the public website model.
/// Step onClick is dropped entirely, it only holds slash commands or in game actions.
/// </summary>
public static class PublicTaskMapper
{
    private static readonly Regex NonAlphanumeric = new("[^a-z0-9]+", RegexOptions.Compiled);

    public static List<PublicTaskEstimate> MapAll(IEnumerable<TaskEstimate> upstream, IReadOnlyDictionary<string, string> itemNames)
    {
        var usedSlugs = new HashSet<string>();
        return upstream.Select(t => Map(t, UniqueSlug(t.TaskName, usedSlugs), itemNames)).ToList();
    }

    public static PublicTaskEstimate Map(TaskEstimate t, string slug, IReadOnlyDictionary<string, string> itemNames)
    {
        return new PublicTaskEstimate
        {
            Name = t.TaskName,
            Slug = slug,
            Category = t.Category,
            Description = t.Description,
            Type = MapType(t.Type),
            CoinsPerHour = t.CoinsPerHour,
            Source = t.Source,
            CommunityTrackedHours = t.CommunityTrackedHours,
            Contributors = t.Contributors,
            CurrentDoers = t.CurrentDoers,
            DoersChange20m = t.DoersChange20m,
            Island = t.Island,
            Where = t.Where,
            Warp = t.Warp,
            WikiUrl = t.WikiUrl,
            WhereWikiUrl = t.WhereWikiUrl,
            Steps = (t.Steps ?? new()).Select(s => new PublicTaskStep { Number = s.Number, Text = s.Text }).ToList(),
            Drops = (t.Drops ?? new()).Select(d => MapDrop(d, itemNames)).ToList(),
            Requirements = (t.Requirements ?? new()).Select(MapRequirement).ToList(),
            BestZones = (t.BestZones ?? new()).Select(z => new PublicTaskZone { Zone = z.Zone, CoinsPerHour = z.CoinsPerHour, Players = z.Players }).ToList(),
            RequiredEvent = t.RequiredEvent,
            Available = IsAvailable(t),
            AvailabilityNote = t.AvailabilityNote,
            NextAvailableAt = t.NextAvailableAt,
        };
    }

    /// <summary>
    /// Only a task with a required event can be unavailable. An upstream without the availability fields
    /// deserializes available as false, so without the event it is always available.
    /// </summary>
    public static bool IsAvailable(TaskEstimate t) => t.RequiredEvent == null || t.Available;

    /// <summary>
    /// Whether the list shows the task: available ones, and unavailable ones whose event started by its own schedule
    /// (the cached availability is older than that, e.g. while the upstream is down).
    /// </summary>
    public static bool IsListed(PublicTaskEstimate t, DateTime utcNow) => t.Available || t.NextAvailableAt?.ToUniversalTime() <= utcNow;

    private static PublicTaskRequirement MapRequirement(TaskRequirementInfo r) => new()
    {
        Label = r.Label,
        Kind = r.Kind,
        Key = r.Key,
        Target = r.Target,
        Hard = r.Hard,
    };

    private static PublicTaskDrop MapDrop(TaskDropRate d, IReadOnlyDictionary<string, string> itemNames)
    {
        string name = null;
        if (d.ItemTag != null && itemNames != null)
            itemNames.TryGetValue(d.ItemTag, out name);
        return new PublicTaskDrop
        {
            ItemTag = d.ItemTag,
            ItemName = name,
            RatePerHour = d.RatePerHour,
            PriceEach = d.PriceEach,
            ContributionPerHour = d.ContributionPerHour,
        };
    }

    /// <summary>Maps the upstream TaskType (0 Active, 1 Passive, 2 Limited) to active, passive or limited</summary>
    public static string MapType(TaskType? type) => type switch
    {
        TaskType.NUMBER_1 => "passive",
        TaskType.NUMBER_2 => "limited",
        _ => "active"
    };

    public static string Slugify(string name)
    {
        var slug = NonAlphanumeric.Replace((name ?? "").ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "task" : slug;
    }

    /// <summary>Slugifies and appends -2, -3, ... until the slug is not in <paramref name="used"/>; adds the result to it</summary>
    public static string UniqueSlug(string name, ISet<string> used)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        for (var i = 2; !used.Add(slug); i++)
            slug = $"{baseSlug}-{i}";
        return slug;
    }
}
