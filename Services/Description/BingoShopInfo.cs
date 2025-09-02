using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Api.Services.Description;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// Adds a small info display showing the top Bingo Shop options by coins per Bingo Point.
/// </summary>
public class BingoShopInfo : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        // Only when Bingo Shop is open
        if (data.inventory?.ChestName == null || !data.inventory.ChestName.StartsWith("Bingo Shop"))
            return;

        var entries = new List<(string Name, double PerPoint, int Index)>();
        var items = data.Items;
        if (items == null)
            return;

        // Build quick lookups
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

            // Parse required Bingo Points using CurrencyValueDisplay's detection method-like logic
            int points; int lineId;
            if (!TryParsePoints(desc, out points, out lineId) || points <= 0)
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
        display.Add(new(new LoreBuilder().AddText("","Drag by holding right click").Build()));
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        // none needed
    }

    private bool TryParsePoints(IEnumerable<string> description, out int points, out int lineId)
    {
        points = 1;
        lineId = 0;
        const string Suffix = "Bingo Points";
        foreach (string descLine in description)
        {
            lineId++;
            if (!descLine.EndsWith(Suffix))
            {
                continue;
            }
            string commaSanitizedMatch = Regex.Replace(descLine.Substring(2, descLine.Length - Suffix.Length - 3), "(§.|[^0-9])", "");
            if (int.TryParse(commaSanitizedMatch, out points))
            {
                return true;
            }
        }
        return false;
    }
}
