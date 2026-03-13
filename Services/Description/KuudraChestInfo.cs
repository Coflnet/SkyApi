using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class KuudraChestInfo : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        // Total estimated value of chest contents
        var target = data.auctionRepresent.Take(30).ToList();
        long itemValueSum = 0;

        // Try to determine key type
        var typeLine = data.auctionRepresent.ElementAtOrDefault(31).desc?.LastOrDefault() ?? 
                       data.auctionRepresent.FirstOrDefault(i => i.auction?.ItemName?.Contains("Kuudra Key", StringComparison.OrdinalIgnoreCase) == true).auction?.ItemName;

        long keyCost = GetKeyCost(typeLine, data);

        // Build a per-item breakdown: iterate over auctionRepresent and list non-null items
        var breakdown = new List<Models.Mod.DescModification>();
        for (int i = 0; i < 30; i++)
        {
            if (i >= target.Count) continue;
            var entry = target[i];
            var auc = entry.auction;
            if (auc == null || string.IsNullOrWhiteSpace(auc.ItemName) || auc.ItemName.Trim() == "") continue;

            // Determine estimated price for this item
            long est = 0;
            if (i < (data.PriceEst?.Count ?? 0) && data.PriceEst[i] != null)
            {
                est = data.PriceEst[i].Median;
            }
            else if (!string.IsNullOrEmpty(auc.Tag))
            {
                var count = auc.Count <= 0 ? 1 : auc.Count;
                est = data.GetItemprice(auc.Tag) * count;
            }

            // Handle essence
            if (auc.ItemName.Contains("Essence", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(auc.ItemName.Split('x').LastOrDefault()?.Trim(), out var essenceCount))
                {
                    var tag = auc.Tag ?? GetEssenceTag(auc.ItemName);
                    if (!string.IsNullOrEmpty(tag))
                    {
                        var essencePrice = data.GetItemprice(tag);
                        est = essencePrice * essenceCount;
                    }
                }
            }

            // Multiply by count if present for standard items
            var totalForItem = est;
            itemValueSum += totalForItem;

            // Create a readable item name (fall back to tag when itemName is null/empty)
            var itemName = string.IsNullOrWhiteSpace(auc.ItemName) ? auc.Tag : auc.ItemName;
            breakdown.Add(new Models.Mod.DescModification(McColorCodes.GRAY + itemName + (auc.Count > 1 && !itemName.Contains("x" + auc.Count) ? " x" + auc.Count : "") + " " + McColorCodes.WHITE + ModDescriptionService.FormatPriceShort(totalForItem)));
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

        data.mods.Add(desc);
    }

    private string GetEssenceTag(string itemName)
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
            return 200_000 + 2 * materialPrice + 2 * starPrice; // Default to basic Kuudra Key

        if (typeLine.Contains("Infernal", StringComparison.OrdinalIgnoreCase))
            return 3_000_000 + 120 * materialPrice + 2 * starPrice;
        if (typeLine.Contains("Fiery", StringComparison.OrdinalIgnoreCase))
            return 1_500_000 + 60 * materialPrice + 2 * starPrice;
        if (typeLine.Contains("Burning", StringComparison.OrdinalIgnoreCase))
            return 750_000 + 20 * materialPrice + 2 * starPrice;
        if (typeLine.Contains("Hot", StringComparison.OrdinalIgnoreCase))
            return 400_000 + 6 * materialPrice + 2 * starPrice;

        return 200_000 + 2 * materialPrice + 2 * starPrice; // Basic Kuudra Key
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
    }
}
