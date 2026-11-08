using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;

/// <summary>A gem a slot has to hold at least</summary>
/// <param name="Quality">ROUGH, FLAWED, FINE, FLAWLESS or PERFECT</param>
/// <param name="Type">Gem type, eg PERIDOT</param>
/// <param name="ItemTag">Gem item tag, eg FINE_PERIDOT_GEM</param>
public record GemRequirement(string Quality, string Type, string ItemTag)
{
    public static readonly string[] Qualities = { "ROUGH", "FLAWED", "FINE", "FLAWLESS", "PERFECT" };

    /// <summary>1 for ROUGH up to 5 for PERFECT, 0 for anything else</summary>
    public static int Rank(string quality) => Array.IndexOf(Qualities, quality?.ToUpperInvariant()) + 1;

    /// <summary>Parses QUALITY_TYPE_GEM, null when the tag has another shape</summary>
    public static GemRequirement Parse(string itemTag)
    {
        var parts = itemTag?.ToUpperInvariant().Split('_');
        if (parts == null || parts.Length < 3 || parts[^1] != "GEM" || Rank(parts[0]) == 0)
            return null;
        return new(parts[0], string.Join('_', parts[1..^1]), string.Join('_', parts));
    }
}

/// <summary>
/// Normalized spec of a gear piece or pet: upper case tags, parsed enums, requirements of 0 dropped.
/// Modifiers that could not be parsed end up in <see cref="Unsupported"/> so they are never silently ignored.
/// </summary>
public class GearTarget
{
    public string Tag { get; init; }
    public bool IsPet { get; init; }
    /// <summary>Minimum rarity of a gear piece</summary>
    public Tier? MinRarity { get; init; }
    /// <summary>Exact rarity of a pet</summary>
    public Tier? PetTier { get; init; }
    public ItemReferences.Reforge? Reforge { get; init; }
    public int Stars { get; init; }
    public int HotPotatoCount { get; init; }
    public bool Recombobulated { get; init; }
    public SortedDictionary<Enchantment.EnchantmentType, int> Enchants { get; init; } = new();
    public SortedDictionary<string, GemRequirement> Gems { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, string> Parts { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> Counters { get; init; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, long> Applied { get; init; } = new(StringComparer.Ordinal);
    public int PetLevel { get; init; }
    public string PetItem { get; init; }
    /// <summary>Modifiers of the spec that are not understood, as kind:name</summary>
    public List<string> Unsupported { get; init; } = new();

    /// <summary>Enchant levels are stored as a byte</summary>
    public const int MaxEnchantLevel = byte.MaxValue;

    public static GearTarget From(GearItemSpec spec)
    {
        var unsupported = new List<string>();
        return new GearTarget
        {
            Tag = NormalizeTag(spec.Tag),
            MinRarity = ParseEnum<Tier>(spec.Rarity, "rarity", unsupported),
            Reforge = ParseReforge(spec.Reforge, unsupported),
            Stars = Math.Max(0, spec.Stars ?? 0),
            HotPotatoCount = Math.Max(0, spec.HotPotatoCount ?? 0),
            Recombobulated = spec.Recombobulated == true,
            Enchants = ParseEnchants(spec.Enchants, unsupported),
            Gems = ParseGems(spec.Gems, unsupported),
            Parts = Clean(spec.Parts),
            Counters = Positive(spec.Counters),
            Applied = Positive(spec.Applied?.ToDictionary(a => a.Key, a => (long)a.Value)),
            Unsupported = unsupported
        };
    }

    public static GearTarget From(GearPetSpec pet)
    {
        var unsupported = new List<string>();
        return new GearTarget
        {
            Tag = PetTag(pet.Type),
            IsPet = true,
            PetTier = ParseEnum<Tier>(pet.Tier, "tier", unsupported),
            PetLevel = Math.Max(0, pet.Level),
            PetItem = string.IsNullOrWhiteSpace(pet.HeldItem) ? null : NormalizeTag(pet.HeldItem),
            Unsupported = unsupported
        };
    }

    /// <summary>Stable key of every requirement, equal targets have equal keys</summary>
    public string Key => string.Join('|', new[]
    {
        Tag, IsPet ? "pet" : "item", $"r={MinRarity}", $"t={PetTier}", $"rf={Reforge}", $"s={Stars}", $"h={HotPotatoCount}",
        $"rc={Recombobulated}", "e=" + Join(Enchants), "g=" + Join(Gems.ToDictionary(g => g.Key, g => g.Value.ItemTag)),
        "p=" + Join(Parts), "c=" + Join(Counters), "a=" + Join(Applied), $"pl={PetLevel}", $"pi={PetItem}",
        "u=" + string.Join(',', Unsupported.OrderBy(u => u, StringComparer.Ordinal))
    });

    private static string Join<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> pairs)
        => string.Join(',', pairs.Select(p => $"{p.Key}:{p.Value}").OrderBy(p => p, StringComparer.Ordinal));

    private static string NormalizeTag(string tag) => tag?.Trim().ToUpperInvariant();

    private static T? ParseEnum<T>(string value, string kind, List<string> unsupported) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var name = value.Trim().Replace(' ', '_');
        // TryParse also accepts numbers ("3" or "999"), only real names count
        if (!long.TryParse(name, out _) && Enum.TryParse<T>(name, true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;
        unsupported.Add($"{kind}:{value}");
        return null;
    }

    private static ItemReferences.Reforge? ParseReforge(string value, List<string> unsupported)
    {
        var reforge = ParseEnum<ItemReferences.Reforge>(value, "reforge", unsupported);
        if (reforge is ItemReferences.Reforge.None or ItemReferences.Reforge.Unknown or ItemReferences.Reforge.Any)
            return null;
        return reforge;
    }

    private static SortedDictionary<Enchantment.EnchantmentType, int> ParseEnchants(Dictionary<string, int> enchants, List<string> unsupported)
    {
        var result = new SortedDictionary<Enchantment.EnchantmentType, int>();
        foreach (var (name, level) in enchants ?? new())
        {
            if (level <= 0)
                continue;
            var type = ParseEnum<Enchantment.EnchantmentType>(name, "enchant", unsupported);
            if (type != null)
                result[type.Value] = Math.Min(MaxEnchantLevel, Math.Max(level, result.GetValueOrDefault(type.Value)));
        }
        return result;
    }

    private static SortedDictionary<string, GemRequirement> ParseGems(Dictionary<string, string> gems, List<string> unsupported)
    {
        var result = new SortedDictionary<string, GemRequirement>(StringComparer.Ordinal);
        foreach (var (slot, tag) in gems ?? new())
        {
            if (string.IsNullOrWhiteSpace(slot) || string.IsNullOrWhiteSpace(tag))
                continue;
            var gem = GemRequirement.Parse(tag);
            if (gem == null)
                unsupported.Add($"gem:{slot}={tag}");
            else
                result[slot.Trim().ToUpperInvariant()] = gem;
        }
        return result;
    }

    private static SortedDictionary<string, string> Clean(Dictionary<string, string> parts)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in parts ?? new())
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                result[key.Trim().ToLowerInvariant()] = NormalizeTag(value);
        return result;
    }

    private static SortedDictionary<string, long> Positive(Dictionary<string, long> counters)
    {
        var result = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var (key, value) in counters ?? new())
            if (!string.IsNullOrWhiteSpace(key) && value > 0)
                result[key.Trim()] = Math.Max(value, result.GetValueOrDefault(key.Trim()));
        return result;
    }

    private static string PetTag(string type)
    {
        var tag = NormalizeTag(type);
        if (string.IsNullOrEmpty(tag))
            return null;
        return tag.StartsWith("PET_") ? tag : "PET_" + tag;
    }
}
