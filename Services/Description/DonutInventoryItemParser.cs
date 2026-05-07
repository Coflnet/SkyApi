using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Coflnet.Sky.Core;
using fNbt.Tags;
using Newtonsoft.Json;

#nullable enable

namespace Coflnet.Sky.Api.Services;

internal sealed class DonutInventoryItemParser
{
    private static readonly Regex VisiblePriceRegex = new(@"\$\s*(?<amount>[0-9][0-9,]*(?:\.[0-9]+)?)(?<suffix>[KMBT]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IReadOnlyList<ParsedInventorySlot> Parse(string? fullInventoryNbt)
    {
        if (string.IsNullOrWhiteSpace(fullInventoryNbt))
            return Array.Empty<ParsedInventorySlot>();

        var file = NBT.File(Convert.FromBase64String(fullInventoryNbt));
        var itemList = file.RootTag.Get<NbtList>("i");
        if (itemList == null)
            return Array.Empty<ParsedInventorySlot>();

        return itemList.Select(tag => ParseSlot(tag as NbtCompound)).ToList();
    }

    internal static ParsedInventorySlot ParseSlot(NbtCompound? compound)
    {
        if (compound == null)
            return new ParsedInventorySlot();

        var rawItemId = compound.Get<NbtString>("id")?.StringValue;
        if (string.IsNullOrWhiteSpace(rawItemId) || string.Equals(rawItemId, "minecraft:air", StringComparison.OrdinalIgnoreCase))
            return new ParsedInventorySlot();

        var components = compound.Get<NbtCompound>("components");
        var lore = ParseLore(components) ?? NBT.GetLore(compound)?.OfType<string>().Where(static line => !string.IsNullOrWhiteSpace(line)).ToList();
        var publicBukkitValues = GetPublicBukkitValues(components);

        var item = new DonutItemPriceRequestItem
        {
            ItemId = rawItemId,
            DisplayName = ParseDisplayName(components) ?? NBT.GetName(compound),
            Slot = GetNumericValue(compound, "Slot"),
            Count = ParseCount(compound),
            VisiblePrice = ParseVisiblePrice(lore),
            Lore = lore,
            MapId = ParseMapId(compound),
            CopyId = ParseCopyId(compound),
            SellerUuidHint = publicBukkitValues?.Get<NbtString>("minecraft:gownerctid")?.StringValue,
            ObservedAtUnixMs = GetLongValue(publicBukkitValues, "minecraft:wi"),
            CooldownUntilUnixMs = GetLongValue(publicBukkitValues, "minecraft:checkcdown"),
            AuctionSecurity = GetNumericValue(publicBukkitValues, "minecraft:auctionsecurity"),
            Trim = ParseTrim(compound)
        };

        var enchants = ParseEnchants(compound);
        if (enchants?.Count > 0)
            item.Enchants = enchants;

        return new ParsedInventorySlot { Slot = item.Slot, Item = item };
    }

    private static int ParseCount(NbtCompound compound)
    {
        var lowerCaseCount = GetNumericValue(compound, "count");
        if (lowerCaseCount.HasValue)
            return Math.Max(1, lowerCaseCount.Value);

        var legacyCount = GetNumericValue(compound, "Count");
        return Math.Max(1, legacyCount ?? 1);
    }

    private static string? ParseDisplayName(NbtCompound? components)
    {
        return ParseTextComponent(components?.Get<NbtCompound>("minecraft:custom_name"));
    }

    private static List<string>? ParseLore(NbtCompound? components)
    {
        var loreList = components?.Get<NbtList>("minecraft:lore");
        if (loreList == null)
            return null;

        var lore = loreList
            .Select(ParseTextComponent)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => line!)
            .ToList();

        return lore.Count == 0 ? null : lore;
    }

    private static Dictionary<string, int>? ParseEnchants(NbtCompound compound)
    {
        var componentEnchants = ParseComponentEnchants(compound.Get<NbtCompound>("components"));
        if (componentEnchants?.Count > 0)
            return componentEnchants;

        var legacyEnchants = NBT.GetEnchants(compound)
            ?.ToDictionary(
                enchant => enchant.Key.ToLowerInvariant().Replace("minecraft:", string.Empty),
                enchant => (int)enchant.Value);

        return legacyEnchants?.Count > 0 ? legacyEnchants : null;
    }

    private static Dictionary<string, int>? ParseComponentEnchants(NbtCompound? components)
    {
        var enchantments = components?.Get<NbtCompound>("minecraft:enchantments");
        if (enchantments == null)
            return null;

        var parsed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in enchantments.Tags)
        {
            if (tag is not NbtTag enchantTag || enchantTag.Name == null)
                continue;

            var level = GetNumericValue(enchantments, enchantTag.Name);
            if (!level.HasValue)
                continue;

            parsed[NormalizeNamespacedValue(enchantTag.Name)] = level.Value;
        }

        return parsed.Count == 0 ? null : parsed;
    }

    internal static int? ParseMapId(NbtCompound compound)
    {
        var components = compound.Get<NbtCompound>("components");
        var directMapId = GetNumericValue(components, "minecraft:map_id");
        if (directMapId.HasValue)
            return directMapId;

        return ParseCopyId(compound)
            ?? GetNumericValue(GetPublicBukkitValues(components), "minecraft:map_id");
    }

    internal static int? ParseCopyId(NbtCompound compound)
    {
        return GetNumericValue(GetPublicBukkitValues(compound.Get<NbtCompound>("components")), "minecraft:copyid");
    }

    internal static DonutItemTrim? ParseTrim(NbtCompound compound)
    {
        return ParseComponentTrim(compound.Get<NbtCompound>("components"))
            ?? ParseLegacyTrim(compound.Get<NbtCompound>("tag"));
    }

    private static DonutItemTrim? ParseComponentTrim(NbtCompound? components)
    {
        var trim = components?.Get<NbtCompound>("minecraft:trim");
        return CreateTrim(
            trim?.Get<NbtString>("material")?.StringValue,
            trim?.Get<NbtString>("pattern")?.StringValue);
    }

    private static DonutItemTrim? ParseLegacyTrim(NbtCompound? tag)
    {
        var trim = tag?.Get<NbtCompound>("Trim");
        return CreateTrim(
            trim?.Get<NbtString>("material")?.StringValue,
            trim?.Get<NbtString>("pattern")?.StringValue);
    }

    private static DonutItemTrim? CreateTrim(string? material, string? pattern)
    {
        var normalizedMaterial = NormalizeTrimValue(material);
        var normalizedPattern = NormalizeTrimValue(pattern);
        if (string.IsNullOrWhiteSpace(normalizedMaterial) && string.IsNullOrWhiteSpace(normalizedPattern))
            return null;

        return new DonutItemTrim
        {
            Material = normalizedMaterial,
            Pattern = normalizedPattern
        };
    }

    private static string? NormalizeTrimValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().ToLowerInvariant();
        const string namespacePrefix = "minecraft:";
        if (normalized.StartsWith(namespacePrefix, StringComparison.Ordinal))
            normalized = normalized[namespacePrefix.Length..];

        return normalized;
    }

    private static int? GetNumericValue(NbtCompound? compound, string name)
    {
        if (compound == null || string.IsNullOrWhiteSpace(name))
            return null;

        var tag = compound.Tags.FirstOrDefault(tag => string.Equals(tag.Name, name, StringComparison.Ordinal));
        return tag switch
        {
            NbtInt nbtInt => nbtInt.IntValue,
            NbtShort nbtShort => nbtShort.ShortValue,
            NbtByte nbtByte => nbtByte.ByteValue,
            NbtLong nbtLong => (int)nbtLong.LongValue,
            _ => null
        };
    }

    private static long? GetLongValue(NbtCompound? compound, string name)
    {
        if (compound == null || string.IsNullOrWhiteSpace(name))
            return null;

        var tag = compound.Tags.FirstOrDefault(tag => string.Equals(tag.Name, name, StringComparison.Ordinal));
        return tag switch
        {
            NbtLong nbtLong => nbtLong.LongValue,
            NbtInt nbtInt => nbtInt.IntValue,
            NbtShort nbtShort => nbtShort.ShortValue,
            NbtByte nbtByte => nbtByte.ByteValue,
            _ => null
        };
    }

    private static NbtCompound? GetPublicBukkitValues(NbtCompound? components)
    {
        return components?
            .Get<NbtCompound>("minecraft:custom_data")?
            .Get<NbtCompound>("PublicBukkitValues");
    }

    private static long? ParseVisiblePrice(IEnumerable<string>? lore)
    {
        if (lore == null)
            return null;

        foreach (var line in lore)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var match = VisiblePriceRegex.Match(line);
            if (!match.Success)
                continue;

            if (!decimal.TryParse(match.Groups["amount"].Value.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                continue;

            var multiplier = match.Groups["suffix"].Value.ToUpperInvariant() switch
            {
                "K" => 1_000m,
                "M" => 1_000_000m,
                "B" => 1_000_000_000m,
                "T" => 1_000_000_000_000m,
                _ => 1m
            };

            return (long)Math.Round(amount * multiplier, MidpointRounding.AwayFromZero);
        }

        return null;
    }

    private static string? ParseTextComponent(NbtTag? tag)
    {
        if (tag == null)
            return null;

        return tag switch
        {
            NbtString text => text.StringValue,
            NbtList list => string.Concat(list.Select(ParseTextComponent).Where(static value => !string.IsNullOrEmpty(value))),
            NbtCompound compound => ParseTextComponentCompound(compound),
            _ => null
        };
    }

    private static string? ParseTextComponentCompound(NbtCompound compound)
    {
        var parts = new List<string>();

        var text = compound.Get<NbtString>("text")?.StringValue;
        if (!string.IsNullOrEmpty(text))
            parts.Add(text);

        var translate = compound.Get<NbtString>("translate")?.StringValue;
        if (!string.IsNullOrEmpty(translate) && parts.Count == 0)
            parts.Add(translate);

        var extra = compound.Get<NbtList>("extra");
        if (extra != null)
        {
            foreach (var child in extra)
            {
                var childText = ParseTextComponent(child);
                if (!string.IsNullOrEmpty(childText))
                    parts.Add(childText);
            }
        }

        if (parts.Count == 0)
            return null;

        return string.Concat(parts);
    }

    private static string NormalizeNamespacedValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToLowerInvariant();
        const string namespacePrefix = "minecraft:";
        if (normalized.StartsWith(namespacePrefix, StringComparison.Ordinal))
            normalized = normalized[namespacePrefix.Length..];

        return normalized;
    }
}

internal sealed class ParsedInventorySlot
{
    public int? Slot { get; init; }

    public DonutItemPriceRequestItem? Item { get; init; }
}

internal sealed class DonutItemPriceRequestItem
{
    [JsonProperty("itemId")]
    public string ItemId { get; init; } = string.Empty;

    [JsonProperty("displayName")]
    public string? DisplayName { get; init; }

    [JsonProperty("slot")]
    public int? Slot { get; init; }

    [JsonProperty("count")]
    public int Count { get; init; } = 1;

    [JsonProperty("visiblePrice")]
    public long? VisiblePrice { get; init; }

    [JsonProperty("enchants")]
    public Dictionary<string, int>? Enchants { get; set; }

    [JsonProperty("lore")]
    public List<string>? Lore { get; init; }

    [JsonProperty("mapId")]
    public int? MapId { get; init; }

    [JsonProperty("copyId")]
    public int? CopyId { get; init; }

    [JsonProperty("sellerUuidHint")]
    public string? SellerUuidHint { get; init; }

    [JsonProperty("observedAtUnixMs")]
    public long? ObservedAtUnixMs { get; init; }

    [JsonProperty("cooldownUntilUnixMs")]
    public long? CooldownUntilUnixMs { get; init; }

    [JsonProperty("auctionSecurity")]
    public int? AuctionSecurity { get; init; }

    [JsonProperty("trim")]
    public DonutItemTrim? Trim { get; init; }
}

internal sealed class DonutItemTrim
{
    [JsonProperty("material")]
    public string? Material { get; init; }

    [JsonProperty("pattern")]
    public string? Pattern { get; init; }
}