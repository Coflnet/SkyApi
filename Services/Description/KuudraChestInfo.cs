using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class KuudraChestInfo : ICustomModifier
{
    private const long BasicKeyBaseCoins = 160_000;
    private const long HotKeyBaseCoins = 320_000;
    private const long BurningKeyBaseCoins = 600_000;
    private const long FieryKeyBaseCoins = 1_200_000;
    private const long InfernalKeyBaseCoins = 2_400_000;

    public void Apply(DataContainer data)
    {
        // Total estimated value of chest contents
        var target = data.auctionRepresent.Take(30).ToList();
        Console.WriteLine("Kuudra chest content: " + JsonConvert.SerializeObject(target));
        var claimChestContents = GetClaimChestContentNames(data.auctionRepresent);
        long itemValueSum = 0;

        // Try to determine key type from the claim chest lore first, then fall back to other lore or item names.
        var typeLine = GetDetectedKeyLine(data.auctionRepresent);

        long keyCost = GetKeyCost(typeLine, data);

        // Build a per-item breakdown: iterate over auctionRepresent and list non-null items
        var breakdown = new List<Models.Mod.DescModification>();
        var debugBreakdown = new List<object>();
        int claimContentIndex = 0;
        for (int i = 0; i < 30; i++)
        {
            if (i >= target.Count) continue;
            var entry = target[i];
            var claimContentName = IsPotentialRewardEntry(entry)
                ? claimChestContents.ElementAtOrDefault(claimContentIndex++)
                : null;
            var auc = entry.auction ?? TryCreateSyntheticAuctionFromLore(entry.desc, claimContentName);
            if (auc == null)
            {
                if ((entry.desc?.Length ?? 0) > 0)
                {
                    debugBreakdown.Add(new
                    {
                        Slot = i,
                        RawTag = entry.auction?.Tag,
                        RawItemName = entry.auction?.ItemName,
                        RawCount = entry.auction?.Count ?? 0,
                        ClaimContentName = claimContentName,
                        Count = 0,
                        entry.desc,
                        Skipped = true,
                        Reason = "Unable to parse lore-only entry"
                    });
                }
                continue;
            }

            var resolvedTag = ResolveItemTag(auc);
            var count = GetEffectiveCount(auc);
            var itemName = GetDisplayName(auc, resolvedTag, claimContentName);
            if (string.IsNullOrWhiteSpace(itemName) && string.IsNullOrWhiteSpace(resolvedTag))
            {
                debugBreakdown.Add(new
                {
                    Slot = i,
                    RawTag = auc.Tag,
                    RawItemName = auc.ItemName,
                    RawCount = auc.Count,
                    ClaimContentName = claimContentName,
                    ResolvedTag = resolvedTag,
                    Count = count,
                    entry.desc,
                    Skipped = true,
                    Reason = "Missing item name and tag"
                });
                continue;
            }

            // Determine estimated price for this item
            long est = 0;
            bool usedPriceEstimate = false;
            if (i < (data.PriceEst?.Count ?? 0) && data.PriceEst[i] != null)
            {
                est = data.PriceEst[i].Median;
                usedPriceEstimate = true;
            }
            else if (!string.IsNullOrEmpty(resolvedTag))
            {
                est = data.GetItemprice(resolvedTag) * count;
            }

            // Handle essence
            if (IsEssenceItem(auc.ItemName, resolvedTag))
            {
                var essenceTag = resolvedTag ?? GetEssenceTag(auc.ItemName);
                if (!string.IsNullOrEmpty(essenceTag))
                {
                    var essencePrice = data.GetItemprice(essenceTag);
                    if (essencePrice > 0 || est == 0)
                        est = essencePrice * count;
                    resolvedTag = essenceTag;
                }
            }

            // Multiply by count if present for standard items
            var totalForItem = est;
            itemValueSum += totalForItem;

            debugBreakdown.Add(new
            {
                Slot = i,
                RawTag = entry.auction?.Tag,
                RawItemName = entry.auction?.ItemName,
                RawCount = entry.auction?.Count ?? 0,
                ClaimContentName = claimContentName,
                ResolvedTag = resolvedTag,
                DisplayName = itemName,
                Count = count,
                Synthetic = entry.auction == null,
                UsedPriceEstimate = usedPriceEstimate,
                EstimatedValue = totalForItem,
                entry.desc
            });

            breakdown.Add(new Models.Mod.DescModification(McColorCodes.GRAY + itemName + GetQuantitySuffix(itemName, count) + " " + McColorCodes.WHITE + ModDescriptionService.FormatPriceShort(totalForItem)));
        }

        var hover = new System.Text.StringBuilder();
        if (breakdown.Count > 0)
        {
            hover.AppendLine("Contents breakdown:");
            foreach (var line in breakdown.Take(50))
            {
                var cleaned = System.Text.RegularExpressions.Regex.Replace(line.Value, "§.", "");
                hover.AppendLine(cleaned);
            }
            if (breakdown.Count > 50)
            {
                hover.AppendLine("...and more");
            }
        }
        hover.AppendLine();
        hover.AppendLine("Cost lines:");
        hover.AppendLine($"Key Cost (est): {ModDescriptionService.FormatPriceShort(keyCost)}");
        if (!string.IsNullOrEmpty(typeLine))
        {
            var cleanedTypeLine = System.Text.RegularExpressions.Regex.Replace(typeLine, "§.", "");
            hover.AppendLine($"Detected Key: {cleanedTypeLine}");
        }

        var profit = FlipInstance.ProfitAfterFees(itemValueSum, 0) - keyCost;
        hover.AppendLine();
        hover.AppendLine($"Profit after fees: {ModDescriptionService.FormatPriceShort(profit)}");

        var desc = new List<Models.Mod.DescModification>();
        
        var builderTotal = new LoreBuilder().AddText(McColorCodes.GRAY + "This chest contains items worth " + McColorCodes.WHITE + ModDescriptionService.FormatPriceShort(itemValueSum), hover.ToString());
        desc.Add(new Models.Mod.DescModification(builderTotal.Build()));

        desc.Add(new LoreBuilder().AddText(McColorCodes.GRAY + "It costs " + McColorCodes.WHITE + ModDescriptionService.FormatPriceShort(keyCost)).BuildLine());

        var builderProfit = new LoreBuilder().AddText(McColorCodes.GRAY + $"It would profit you {McColorCodes.WHITE}" + ModDescriptionService.FormatPriceShort(profit), hover.ToString());
        desc.Add(builderProfit.BuildLine());

        desc.Add(new Models.Mod.DescModification(McColorCodes.GRAY + "Please let us know what you think"));
        desc.Add(new Models.Mod.DescModification(McColorCodes.GRAY + "about the estimate on SkyCofl discord!"));

        Console.WriteLine("Kuudra chest output breakdown: " + JsonConvert.SerializeObject(new
        {
            DetectedKey = typeLine,
            KeyCost = keyCost,
            ItemValueSum = itemValueSum,
            Profit = profit,
            ClaimChestContents = claimChestContents,
            Items = debugBreakdown,
            Output = desc.Select(line => line.Value).ToList()
        }));

        data.mods.Add(desc);
    }

    private static string GetDetectedKeyLine(List<(Core.SaveAuction auction, string[] desc)> auctionRepresent)
    {
        if (auctionRepresent == null || auctionRepresent.Count == 0)
            return null;

        var chestLore = auctionRepresent.FirstOrDefault(entry => entry.auction?.Tag == "SKYBLOCK_CLAIM_CHEST").desc;
        var keyLine = FindKuudraKeyLine(chestLore);
        if (!string.IsNullOrEmpty(keyLine))
            return keyLine;

        foreach (var entry in auctionRepresent)
        {
            keyLine = FindKuudraKeyLine(entry.desc);
            if (!string.IsNullOrEmpty(keyLine))
                return keyLine;

            if (ContainsKuudraKey(entry.auction?.ItemName))
                return entry.auction.ItemName;
        }

        return null;
    }

    private static string FindKuudraKeyLine(IEnumerable<string> desc)
    {
        return desc?.FirstOrDefault(ContainsKuudraKey);
    }

    private static bool ContainsKuudraKey(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.IndexOf("Kuudra Key", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsPotentialRewardEntry((Core.SaveAuction auction, string[] desc) entry)
    {
        return entry.auction != null || (entry.desc?.Length ?? 0) > 0;
    }

    private static List<string> GetClaimChestContentNames(List<(Core.SaveAuction auction, string[] desc)> auctionRepresent)
    {
        var claimChestLore = auctionRepresent?.FirstOrDefault(entry => entry.auction?.Tag == "SKYBLOCK_CLAIM_CHEST").desc;
        if (claimChestLore == null)
            return new List<string>();

        bool inContentsSection = false;
        var contentNames = new List<string>();
        foreach (var line in claimChestLore)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var cleanedLine = System.Text.RegularExpressions.Regex.Replace(line, "§.", string.Empty);
            if (cleanedLine.Equals("Contents", StringComparison.OrdinalIgnoreCase))
            {
                inContentsSection = true;
                continue;
            }

            if (cleanedLine.Equals("Cost", StringComparison.OrdinalIgnoreCase))
                break;

            if (inContentsSection)
                contentNames.Add(line);
        }

        return contentNames;
    }

    private static Core.SaveAuction TryCreateSyntheticAuctionFromLore(IEnumerable<string> desc, string claimContentName)
    {
        var claimContentAuction = TryCreateSyntheticAuctionFromClaimContent(claimContentName);
        if (claimContentAuction != null)
            return claimContentAuction;

        if (!TryCreateSyntheticShardFromLore(desc, out var shardAuction))
            return null;

        return shardAuction;
    }

    private static Core.SaveAuction TryCreateSyntheticAuctionFromClaimContent(string claimContentName)
    {
        if (string.IsNullOrWhiteSpace(claimContentName))
            return null;

        if (claimContentName.IndexOf("Attribute Shard", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var attributeKey = NormalizeAttributeKey(claimContentName);
            if (!Coflnet.Sky.Core.Constants.AttributeKeys.Contains(attributeKey))
                return null;

            return new Core.SaveAuction
            {
                Tag = $"ATTRIBUTE_SHARD+{attributeKey};1",
                ItemName = claimContentName,
                Count = 1
            };
        }

        if (!TryResolveShardTag(claimContentName, out var shardTag))
            return null;

        return new Core.SaveAuction
        {
            Tag = shardTag,
            ItemName = claimContentName,
            Count = 1
        };
    }

    private static bool TryCreateSyntheticShardFromLore(IEnumerable<string> desc, out Core.SaveAuction shardAuction)
    {
        shardAuction = null;
        if (!IsLikelyShardLore(desc))
            return false;

        var shardName = ExtractShardNameFromLore(desc);
        if (string.IsNullOrWhiteSpace(shardName))
            return false;

        var attributeKey = NormalizeAttributeKey(shardName);
        if (!Coflnet.Sky.Core.Constants.AttributeKeys.Contains(attributeKey))
            return false;

        shardAuction = new Core.SaveAuction
        {
            Tag = $"ATTRIBUTE_SHARD+{attributeKey};1",
            ItemName = EnsureShardSuffix(shardName),
            Count = 1
        };
        return true;
    }

    private static bool IsLikelyShardLore(IEnumerable<string> desc)
    {
        return desc?.Any(line => !string.IsNullOrWhiteSpace(line)
            && line.IndexOf("Owned:", StringComparison.OrdinalIgnoreCase) >= 0
            && line.IndexOf("Shards", StringComparison.OrdinalIgnoreCase) >= 0) == true;
    }

    private static string ExtractShardNameFromLore(IEnumerable<string> desc)
    {
        var firstLine = desc?.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        if (string.IsNullOrWhiteSpace(firstLine))
            return null;

        return System.Text.RegularExpressions.Regex.Replace(firstLine, @"\s*(?:§.)?\([^)]*\)$", string.Empty).Trim();
    }

    private static string EnsureShardSuffix(string shardName)
    {
        return shardName.IndexOf("Shard", StringComparison.OrdinalIgnoreCase) >= 0
            ? shardName
            : shardName + " Shard";
    }

    private static string NormalizeAttributeKey(string shardName)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(shardName, "§.", string.Empty);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+Attribute Shard$", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return cleaned.Replace(' ', '_').ToLowerInvariant();
    }

    private static string ResolveItemTag(Core.SaveAuction auction)
    {
        if (auction == null)
            return null;

        if (!string.IsNullOrEmpty(auction.Tag))
            return auction.Tag;

        if (IsEssenceItem(auction.ItemName, auction.Tag))
            return GetEssenceTag(auction.ItemName);

        if (TryResolveShardTag(auction.ItemName, out var shardTag))
            return shardTag;

        return null;
    }

    private static bool TryResolveShardTag(string itemName, out string shardTag)
    {
        shardTag = null;
        if (string.IsNullOrWhiteSpace(itemName) || itemName.IndexOf("Shard", StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        return ModDescriptionService.TryGetShardTagFromName(TrimCountSuffix(itemName), out shardTag);
    }

    private static int GetEffectiveCount(Core.SaveAuction auction)
    {
        if (auction == null)
            return 0;

        var parsedCount = TryParseTrailingCount(auction.ItemName);
        if (parsedCount > 0)
            return parsedCount;

        return auction.Count > 0 ? auction.Count : 1;
    }

    private static int TryParseTrailingCount(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return 0;

        var cleaned = System.Text.RegularExpressions.Regex.Replace(itemName, "§.", "");
        var match = System.Text.RegularExpressions.Regex.Match(cleaned, @" x(?<count>[0-9,]+)$");
        if (!match.Success)
            return 0;

        return int.TryParse(match.Groups["count"].Value.Replace(",", ""), out var parsedCount)
            ? parsedCount
            : 0;
    }

    private static string TrimCountSuffix(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            return itemName;

        return System.Text.RegularExpressions.Regex.Replace(itemName, @"\s*§.[xX][0-9,]+$", string.Empty);
    }

    private static bool IsEssenceItem(string itemName, string tag)
    {
        return (!string.IsNullOrEmpty(tag) && tag.StartsWith("ESSENCE_", StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(itemName) && itemName.IndexOf("Essence", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string GetDisplayName(Core.SaveAuction auction, string resolvedTag, string claimContentName)
    {
        if (!string.IsNullOrWhiteSpace(auction?.ItemName))
            return auction.ItemName;

        if (!string.IsNullOrWhiteSpace(claimContentName))
            return claimContentName;

        return resolvedTag ?? auction?.Tag;
    }

    private static string GetQuantitySuffix(string itemName, int count)
    {
        if (count <= 1)
            return string.Empty;

        var cleanedName = string.IsNullOrWhiteSpace(itemName)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(itemName, "§.", "");
        return cleanedName.Contains("x" + count, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : " x" + count;
    }

    private static string GetEssenceTag(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return null;
        var lower = itemName.ToLowerInvariant();
        if (lower.Contains("crimson")) return "ESSENCE_CRIMSON";
        if (lower.Contains("wither")) return "ESSENCE_WITHER";
        if (lower.Contains("spider")) return "ESSENCE_SPIDER";
        if (lower.Contains("undead")) return "ESSENCE_UNDEAD";
        if (lower.Contains("dragon")) return "ESSENCE_DRAGON";
        if (lower.Contains("gold")) return "ESSENCE_GOLD";
        if (lower.Contains("diamond")) return "ESSENCE_DIAMOND";
        if (lower.Contains("ice")) return "ESSENCE_ICE";
        return null; // Add more if needed
    }

    private long GetKeyCost(string typeLine, DataContainer data)
    {
        long starPrice = 0;
        long materialPrice = 0;

        try
        {
            starPrice = data.GetItemprice("CORRUPTED_NETHER_STAR");
            long sandPrice = data.GetItemprice("ENCHANTED_RED_SAND");
            long myceliumPrice = data.GetItemprice("ENCHANTED_MYCELIUM");
            materialPrice = Math.Min(sandPrice, myceliumPrice);
        }
        catch { }

        if (string.IsNullOrEmpty(typeLine))
            return BasicKeyBaseCoins + 2 * materialPrice + 2 * starPrice; // Default to basic Kuudra Key

        if (typeLine.Contains("Infernal", StringComparison.OrdinalIgnoreCase))
            return InfernalKeyBaseCoins + 120 * materialPrice + 2 * starPrice;
        if (typeLine.Contains("Fiery", StringComparison.OrdinalIgnoreCase))
            return FieryKeyBaseCoins + 60 * materialPrice + 2 * starPrice;
        if (typeLine.Contains("Burning", StringComparison.OrdinalIgnoreCase))
            return BurningKeyBaseCoins + 20 * materialPrice + 2 * starPrice;
        if (typeLine.Contains("Hot", StringComparison.OrdinalIgnoreCase))
            return HotKeyBaseCoins + 6 * materialPrice + 2 * starPrice;

        return BasicKeyBaseCoins + 2 * materialPrice + 2 * starPrice; // Basic Kuudra Key
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
    }
}
