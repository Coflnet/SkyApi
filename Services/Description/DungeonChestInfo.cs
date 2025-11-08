using System.Linq;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

public class DungeonChestInfo : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        // Total estimated value of chest contents (first 5 rows x 9 as before)
        var target = data.auctionRepresent.Take(5 * 9).ToList();
        Console.WriteLine("Dungeon chest content: " + JsonConvert.SerializeObject(target));
        long itemValueSum = 0;

        // Parse coin cost from the cost slot and detect whether a Dungeon Chest Key is listed
        int coins = GetCostFromDungeonChest(target);
        var costSlot = target.ElementAtOrDefault(31).desc ?? Array.Empty<string>();
        bool hasKey = costSlot.Any(c => c?.IndexOf("Dungeon Chest Key", StringComparison.OrdinalIgnoreCase) >= 0);
        long keyCost = 0;
        if (hasKey)
        {
            // Get bazaar cost / market price for the DUNGEON_CHEST_KEY product and add to total cost
            try
            {
                keyCost = data.GetItemprice("DUNGEON_CHEST_KEY");
                coins += (int)keyCost;
            }
            catch
            {
                // If for some reason GetItemprice fails, ignore and proceed without key cost
                keyCost = 0;
            }
        }

        // Build a per-item breakdown: iterate over auctionRepresent and list non-null items
        var breakdown = new List<Models.Mod.DescModification>();
        for (int i = 0; i < 9 * 3; i++)
        {
            var entry = target[i];
            var auc = entry.auction;
            if (auc == null) continue;

            // Determine estimated price for this item (use price estimate list or fallback)
            long est = 0;
            if (i < (data.PriceEst?.Count ?? 0) && data.PriceEst[i] != null)
            {
                est = data.PriceEst[i].Median;
            }
            else
            {
                var count = auc.Count <= 0 ? 1 : auc.Count;
                est = data.GetItemprice(auc.Tag) * count;
            }

            // Multiply by count if present
            var totalForItem = est;
            itemValueSum += totalForItem;

            // Create a readable item name (fall back to tag when itemName is null/empty)
            var itemName = string.IsNullOrWhiteSpace(auc.ItemName) ? auc.Tag : auc.ItemName;
            breakdown.Add(new Models.Mod.DescModification(McColorCodes.GRAY + itemName + (auc.Count > 1 ? " x" + auc.Count : "") + " " + McColorCodes.WHITE + ModDescriptionService.FormatPriceShort(totalForItem)));
        }

        var desc = new List<Models.Mod.DescModification>()
        {
            new(McColorCodes.GRAY + "This chest contains items worth " + McColorCodes.WHITE + ModDescriptionService.FormatPriceShort(itemValueSum)),
        };

        // Insert per-item breakdown (limit to a reasonable number to avoid huge lore)
        if (breakdown.Count > 0)
        {
            desc.Add(new Models.Mod.DescModification(McColorCodes.GRAY + "Contents breakdown:"));
            // add up to first 20 entries to keep description concise
            foreach (var line in breakdown.Take(20))
            {
                desc.Add(line);
            }
            if (breakdown.Count > 20)
            {
                desc.Add(new Models.Mod.DescModification(McColorCodes.GRAY + "...and more"));
            }
        }

        // Show cost lines: chest coins, optional key cost, and total
        desc.Add(new Models.Mod.DescModification(McColorCodes.GRAY + "It costs " + McColorCodes.WHITE + ModDescriptionService.FormatPriceShort(coins)));
        if (hasKey)
        {
            desc.Add(new Models.Mod.DescModification(McColorCodes.GRAY + "Includes Dungeon Chest Key (est) " + McColorCodes.WHITE + ModDescriptionService.FormatPriceShort(keyCost)));
        }

        desc.Add(new Models.Mod.DescModification(McColorCodes.GRAY + $"It would profit you {McColorCodes.WHITE}" + ModDescriptionService.FormatPriceShort(FlipInstance.ProfitAfterFees(itemValueSum, coins))));
        desc.Add(new Models.Mod.DescModification(McColorCodes.GRAY + "Please let us know what you think"));
        desc.Add(new Models.Mod.DescModification(McColorCodes.GRAY + "about the estimate on SkyCofl discord!"));
        Console.WriteLine("Dungeon chest mods: " + JsonConvert.SerializeObject(desc));

        data.mods.Add(desc);
    }

    public static int GetCostFromDungeonChest(List<(Core.SaveAuction auction, string[] desc)> target)
    {
        if (target == null || target.Count <= 31) return 0;
        var costSlot = target[31].desc ?? Array.Empty<string>();

        // Try to find a line that contains a coins number and parse the number safely while stripping color codes
        var coinsLine = costSlot.FirstOrDefault(c => !string.IsNullOrEmpty(c) && c.IndexOf("coin", StringComparison.OrdinalIgnoreCase) >= 0);
        if (coinsLine == null) return 0;

        try
        {
            // remove Minecraft color codes like '§6' by deleting the section sign and the following char
            var cleaned = System.Text.RegularExpressions.Regex.Replace(coinsLine, "§.", "");

            // find the first group of digits/commas
            var m = System.Text.RegularExpressions.Regex.Match(cleaned, "([0-9,]+)");
            if (!m.Success) return 0;

            var num = m.Groups[1].Value.Replace(",", "");
            if (long.TryParse(num, out var coins))
            {
                // clamp to int range since method returns int
                if (coins > int.MaxValue) return int.MaxValue;
                if (coins < int.MinValue) return int.MinValue;
                return (int)coins;
            }
        }
        catch
        {
            // ignore parse errors
        }

        return 0;
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        // none
    }
}
