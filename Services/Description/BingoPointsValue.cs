using Coflnet.Sky.Sniper.Client.Model;
using Coflnet.Sky.Commands.MC;
using System.Linq;
using System.Collections.Generic;

namespace SkyApi.Services.Description;

/// <summary>
/// Displays coins per Bingo Point for Bingo Shop items and accounts for prerequisite item costs.
/// </summary>
public class BingoPointsValue : CurrencyValueDisplay
{
    protected override string ValueSuffix => "Bingo Points";

    protected override string currencyName => "Bingo Point";

    protected override void ProcessLine(Coflnet.Sky.Api.Services.Description.DataContainer data, int i, string[] desc, PriceEstimate price)
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
            // Build a quick lookup from ItemName -> (index, estimate)
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
                    // Avoid self-reference
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
