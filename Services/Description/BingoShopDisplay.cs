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
    public new void Apply(DataContainer data)
    {
        // Per-item: coins per Bingo Point (with prerequisite deduction)
        base.Apply(data);

        var entries = new List<(string Name, double PerPoint, int Index)>();
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

            if (!HasValue(desc, out int points, out int lineId) || points <= 0)
                continue;

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

            var effective = Math.Max(0, (long)price.Median - prereqValue);
            var perPoint = points > 0 ? effective / (double)points : 0;
            var name = items[i]?.ItemName ?? data.auctionRepresent[i].auction?.ItemName ?? data.auctionRepresent[i].auction?.Tag;
            if (perPoint > 0 && name != null)
                entries.Add((name, perPoint, i));
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
            display.Add(new(line.ToString()));
        }
    }

    protected override void ProcessLine(DataContainer data, int i, string[] desc, PriceEstimate price)
    {
        if (price == null || price.Median == 0)
            return;

        if (!HasValue(desc, out int points, out int lineId) || points <= 0)
            return;

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

        var effectiveValue = (double)((long)price.Median - prereqValue);
        var prefix = price.ItemKey == price.MedianKey ? "" : "~";
        var perPoint = effectiveValue / points;
        var formattedPrice = $"{McColorCodes.AQUA}{prefix}{data.modService.FormatNumber((float)perPoint)}";
        ReplaceLine(data, i, lineId, formattedPrice);
    }
}
