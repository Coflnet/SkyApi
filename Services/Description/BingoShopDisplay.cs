using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Sniper.Client.Model;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// shows Coins per Bingo Point on each item (subtracting prerequisite items), and
/// adds an info panel listing the best options by coins per point.
/// </summary>
public class BingoShopDisplay : SkyApi.Services.Description.CurrencyValueDisplay
{
    protected override string ValueSuffix => "Bingo Points";
    protected override string currencyName => "Bingo Point";

    // Hide base Apply to run both the per-item processing and the summary info
    public override void Apply(DataContainer data)
    {
        // First, determine the user's current bingo rank
        int userCurrentRank = GetUserCurrentBingoRank(data);
        
        // Per-item: coins per Bingo Point (with prerequisite deduction and rank upgrade costs)
        base.Apply(data);

        var entries = new List<(string Name, double PerPoint, int Index, bool IsRankUpgrade)>();
        var items = data.Items;
        if (items == null)
            return;

        var nameToIndex = new Dictionary<string, int>();
        for (int j = 0; j < items.Count; j++)
        {
            var n = items[j]?.ItemName;
            if (!string.IsNullOrEmpty(n) && !nameToIndex.ContainsKey(n))
                nameToIndex[n] = j;
        }

        for (int i = 0; i < Math.Min(data.auctionRepresent.Count, 54); i++)
        {
            var desc = data.auctionRepresent[i].desc;
            var price = data.PriceEst?[i];
            if (desc == null || price == null || desc.Length == 0)
                continue;

            // Check if this is a rank upgrade item
            var itemName = items[i]?.ItemName ?? data.auctionRepresent[i].auction?.ItemName;
            if (itemName != null && itemName.Contains("Upgrade Bingo Rank"))
            {
                var rankUpgradeCost = ExtractBingoRankUpgradeCost(desc);
                if (rankUpgradeCost > 0)
                {
                    var rankPerPoint = EstimateRankUpgradeValue(rankUpgradeCost, userCurrentRank + 1);
                    entries.Add((itemName, rankPerPoint, i, true));
                }
                continue;
            }

            if (!HasValue(desc, out int points, out int lineId) || points <= 0)
                continue;

            // Calculate rank upgrade costs needed to purchase this item
            int requiredRank = GetRequiredBingoRank(desc);
            int rankUpgradeCosts = CalculateRankUpgradeCosts(userCurrentRank, requiredRank);

            long prereqValue = 0;
            // scan below for prerequisite items
            for (int l = lineId + 1; l <= desc.Length; l++)
            {
                var idx = l - 1;
                if (idx < 0 || idx >= desc.Length)
                    break;
                var line = desc[idx];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (nameToIndex.TryGetValue(line, out var preIdx))
                {
                    if (preIdx == i) continue;
                    var preEst = (preIdx >= 0 && preIdx < data.PriceEst?.Count) ? data.PriceEst[preIdx] : null;
                    if (preEst != null && preEst.Median > 0)
                        prereqValue += (long)preEst.Median;
                }
            }

            var totalPointCost = points + rankUpgradeCosts;
            var effective = Math.Max(0, (long)price.Median - prereqValue);
            var perPoint = totalPointCost > 0 ? effective / (double)totalPointCost : 0;
            var name = itemName ?? data.auctionRepresent[i].auction?.Tag;
            if (perPoint > 0 && name != null)
                entries.Add((name, perPoint, i, false));
        }

        var top = entries.OrderByDescending(e => e.PerPoint).Take(3).ToList();
        if (top.Count == 0)
            return;

        var display = new List<DescModification>();
        data.mods.Add(display);
        display.Add(new($"{McColorCodes.GOLD}SkyC{McColorCodes.AQUA}ofl {McColorCodes.GRAY}● §7Best Bingo Points options:"));
        foreach (var e in top)
        {
            var line = new StringBuilder();
            line.Append("§a● §6");
            line.Append(e.Name);
            line.Append(" §7- ");
            line.Append(McColorCodes.AQUA);
            line.Append(data.modService.FormatNumber((float)e.PerPoint));
            line.Append(" §7per point");
            if (e.IsRankUpgrade)
                line.Append(" (unlock value)");

            // Create detailed breakdown for hover text
            var breakdown = CreateCalculationBreakdown(data, e, userCurrentRank);
            var builder = new LoreBuilder()
                .AddText(line.ToString(), breakdown, null);
            display.Add(new(builder.Build()));
        }
    }

    /// <summary>
    /// Processes individual lines to show coins per bingo point including rank upgrade costs
    /// </summary>

    protected override void ProcessLine(DataContainer data, int i, string[] desc, PriceEstimate price)
    {
        if (price == null || price.Median == 0)
            return;

        if (!HasValue(desc, out int points, out int lineId) || points <= 0)
            return;

        // Get user's current bingo rank for calculation
        int userCurrentRank = GetUserCurrentBingoRank(data);
        
        // Calculate rank upgrade costs needed to purchase this item
        int requiredRank = GetRequiredBingoRank(desc);
        int rankUpgradeCosts = CalculateRankUpgradeCosts(userCurrentRank, requiredRank);

        // Calculate prerequisite value from other required items listed under the cost
        long prereqValue = 0;
        var items = data.Items;
        if (items != null)
        {
            var nameToIndex = new Dictionary<string, int>();
            for (int j = 0; j < items.Count; j++)
            {
                var n = items[j]?.ItemName;
                if (!string.IsNullOrEmpty(n) && !nameToIndex.ContainsKey(n))
                    nameToIndex[n] = j;
            }

            // Scan following lines for prerequisite item names that exist in the same inventory
            for (int l = lineId + 1; l <= desc.Length; l++)
            {
                var idx = l - 1;
                if (idx < 0 || idx >= desc.Length)
                    break;
                var line = desc[idx];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (nameToIndex.TryGetValue(line, out var preIdx))
                {
                    if (preIdx == i)
                        continue;
                    var preEst = (preIdx >= 0 && preIdx < data.PriceEst?.Count) ? data.PriceEst[preIdx] : null;
                    if (preEst != null && preEst.Median > 0)
                        prereqValue += (long)preEst.Median;
                }
            }
        }

        var totalPointCost = points + rankUpgradeCosts;
        var effectiveValue = (double)((long)price.Median - prereqValue);
        var prefix = price.ItemKey == price.MedianKey ? "" : "~";
        var perPoint = totalPointCost > 0 ? effectiveValue / totalPointCost : 0;
        var formattedPrice = $"{McColorCodes.AQUA}{prefix}{data.modService.FormatNumber((float)perPoint)}";
        if (rankUpgradeCosts > 0)
            formattedPrice += $" §8(+{rankUpgradeCosts} rank cost)";
        ReplaceLine(data, i, lineId, formattedPrice);
    }

    /// <summary>
    /// Determines the user's current bingo rank from the "Upgrade Bingo Rank" item in the inventory
    /// </summary>
    private int GetUserCurrentBingoRank(DataContainer data)
    {
        var items = data.Items;
        if (items == null) return 0;

        for (int i = 0; i < Math.Min(data.auctionRepresent.Count, 54); i++)
        {
            var itemName = items[i]?.ItemName;
            if (itemName != null && itemName.Contains("Upgrade Bingo Rank"))
            {
                var desc = data.auctionRepresent[i].desc;
                if (desc == null) continue;

                foreach (var line in desc)
                {
                    // Look for "Your Rank: §cNone" or "Your Rank: §aⒷ Bingo Rank I" etc
                    if (line.Contains("Your Rank:"))
                    {
                        if (line.Contains("§cNone"))
                            return 0;
                        
                        // Extract rank number from patterns like "§aⒷ Bingo Rank I", "§9Ⓑ Bingo Rank II", etc
                        var rankMatch = Regex.Match(line, @"Bingo Rank (I{1,3}|IV)");
                        if (rankMatch.Success)
                        {
                            var roman = rankMatch.Groups[1].Value;
                            return roman switch
                            {
                                "I" => 1,
                                "II" => 2,
                                "III" => 3,
                                "IV" => 4,
                                _ => 0
                            };
                        }
                    }
                }
            }
        }
        return 0; // Default if no rank info found
    }

    /// <summary>
    /// Extracts the required bingo rank from item descriptions that contain "You must be §xⒷ Bingo Rank X"
    /// </summary>
    private int GetRequiredBingoRank(string[] desc)
    {
        foreach (var line in desc)
        {
            if (line.Contains("You must be") && line.Contains("Bingo Rank"))
            {
                var rankMatch = Regex.Match(line, @"Bingo Rank (I{1,3}|IV)");
                if (rankMatch.Success)
                {
                    var roman = rankMatch.Groups[1].Value;
                    return roman switch
                    {
                        "I" => 1,
                        "II" => 2,
                        "III" => 3,
                        "IV" => 4,
                        _ => 0
                    };
                }
            }
        }
        return 0; // No rank requirement
    }

    /// <summary>
    /// Calculates the total bingo points needed to upgrade from current rank to required rank
    /// Rank upgrade costs: 50, 100, 150, 200 for ranks I, II, III, IV respectively
    /// </summary>
    private int CalculateRankUpgradeCosts(int currentRank, int requiredRank)
    {
        if (requiredRank <= currentRank) return 0;

        int totalCost = 0;
        var rankCosts = new[] { 50, 100, 150, 200 }; // Costs for ranks I, II, III, IV

        for (int rank = currentRank + 1; rank <= requiredRank; rank++)
        {
            if (rank >= 1 && rank <= 4)
                totalCost += rankCosts[rank - 1];
        }

        return totalCost;
    }

    /// <summary>
    /// Extracts the bingo points cost from a rank upgrade item description
    /// </summary>
    private int ExtractBingoRankUpgradeCost(string[] desc)
    {
        foreach (var line in desc)
        {
            var match = Regex.Match(line, @"§6(\d+) Bingo Points");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int cost))
                return cost;
        }
        return 0;
    }

    /// <summary>
    /// Creates a detailed breakdown of the calculation for the hover text
    /// </summary>
    private string CreateCalculationBreakdown(DataContainer data, (string Name, double PerPoint, int Index, bool IsRankUpgrade) entry, int userCurrentRank)
    {
        var breakdown = new StringBuilder();
        breakdown.AppendLine($"§6{entry.Name} Calculation Breakdown");
        breakdown.AppendLine();

        if (entry.IsRankUpgrade)
        {
            // For rank upgrade items, show unlock value calculation
            var desc = data.auctionRepresent[entry.Index].desc;
            var rankUpgradeCost = ExtractBingoRankUpgradeCost(desc);
            var rankBeingUnlocked = userCurrentRank + 1;
            
            breakdown.AppendLine("§eRank Upgrade Item:");
            breakdown.AppendLine($"§7Base Cost: §c{rankUpgradeCost} Bingo Points");
            breakdown.AppendLine($"§7Unlocks: §aⒷ Bingo Rank {ToRoman(rankBeingUnlocked)}");
            breakdown.AppendLine($"§7Estimated Unlock Value: §6{data.modService.FormatNumber((float)entry.PerPoint)} coins");
            breakdown.AppendLine();
            breakdown.AppendLine("§8Rank upgrades unlock access to higher tier items");
        }
        else
        {
            // For regular items, show detailed cost breakdown
            var desc = data.auctionRepresent[entry.Index].desc;
            var price = data.PriceEst?[entry.Index];
            
            if (desc != null && price != null && HasValue(desc, out int basePoints, out int lineId))
            {
                // Calculate components
                int requiredRank = GetRequiredBingoRank(desc);
                int rankUpgradeCosts = CalculateRankUpgradeCosts(userCurrentRank, requiredRank);
                
                // Calculate prerequisite costs
                long prereqValue = 0;
                var prereqItems = new List<string>();
                var items = data.Items;
                if (items != null)
                {
                    var nameToIndex = new Dictionary<string, int>();
                    for (int j = 0; j < items.Count; j++)
                    {
                        var n = items[j]?.ItemName;
                        if (!string.IsNullOrEmpty(n) && !nameToIndex.ContainsKey(n))
                            nameToIndex[n] = j;
                    }

                    for (int l = lineId + 1; l <= desc.Length; l++)
                    {
                        var idx = l - 1;
                        if (idx < 0 || idx >= desc.Length) break;
                        var line = desc[idx];
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        if (nameToIndex.TryGetValue(line, out var preIdx) && preIdx != entry.Index)
                        {
                            var preEst = (preIdx >= 0 && preIdx < data.PriceEst?.Count) ? data.PriceEst[preIdx] : null;
                            if (preEst != null && preEst.Median > 0)
                            {
                                prereqValue += (long)preEst.Median;
                                prereqItems.Add($"{line}: {data.modService.FormatNumber((float)preEst.Median)} coins");
                            }
                        }
                    }
                }

                var totalPointCost = basePoints + rankUpgradeCosts;
                var effectiveValue = Math.Max(0, (long)price.Median - prereqValue);

                breakdown.AppendLine("§eItem Purchase Breakdown:");
                breakdown.AppendLine($"§7Item Value: §6{data.modService.FormatNumber((float)price.Median)} coins");
                
                if (prereqValue > 0)
                {
                    breakdown.AppendLine($"§7Prerequisite Cost: §c-{data.modService.FormatNumber((float)prereqValue)} coins");
                    foreach (var prereq in prereqItems)
                        breakdown.AppendLine($"  §8• {prereq}");
                }
                
                breakdown.AppendLine($"§7Effective Value: §a{data.modService.FormatNumber((float)effectiveValue)} coins");
                breakdown.AppendLine();
                
                breakdown.AppendLine("§eBingo Point Costs:");
                breakdown.AppendLine($"§7Base Cost: §b{basePoints} Bingo Points");
                
                if (rankUpgradeCosts > 0)
                {
                    breakdown.AppendLine($"§7Rank Upgrade Cost: §c+{rankUpgradeCosts} Bingo Points");
                    breakdown.AppendLine($"  §8(Current: Rank {ToRoman(userCurrentRank)}, Required: Rank {ToRoman(requiredRank)})");
                }
                
                breakdown.AppendLine($"§7Total Points: §e{totalPointCost} Bingo Points");
                breakdown.AppendLine();
                breakdown.AppendLine($"§aFinal: §6{data.modService.FormatNumber((float)entry.PerPoint)} §7coins per point");
            }
        }

        return breakdown.ToString().TrimEnd('\n', '\r');
    }

    /// <summary>
    /// Converts a number to Roman numerals (1-4 only)
    /// </summary>
    private string ToRoman(int number)
    {
        return number switch
        {
            1 => "I",
            2 => "II", 
            3 => "III",
            4 => "IV",
            _ => number.ToString()
        };
    }

    /// <summary>
    /// Estimates the value of a rank upgrade based on its cost and the rank being unlocked
    /// Higher ranks provide more value as they unlock more content
    /// </summary>
    private double EstimateRankUpgradeValue(int cost, int rankBeingUnlocked)
    {
        // Base value multiplier, adjusted by rank importance
        var baseMultiplier = 2.5;
        var rankMultiplier = 1.0 + (rankBeingUnlocked - 1) * 0.3; // Higher ranks are more valuable
        return cost * baseMultiplier * rankMultiplier;
    }
}
