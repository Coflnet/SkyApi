using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Api.Services;

namespace Coflnet.Sky.Api.Services.Description;

public class BazaarPriceUpdater : ICustomModifier
{
    public void Apply(DataContainer data)
    {
        // Check if the chest name contains ➜ symbol to filter for bazaar item screens
        if (data.inventory.ChestName == null || !data.inventory.ChestName.Contains("➜"))
            return;

        // Check if slot 10 (0-indexed) exists and contains "Buy Instantly" to confirm it's a bazaar item screen
        if (data.Items.Count <= 10 || data.Items[10] == null)
            return;

        var slot10Item = data.Items[10];
        if (slot10Item.ItemName == null || !slot10Item.ItemName.Contains("Buy Instantly"))
            return;

        // Check if slot 13 (0-indexed) exists to get the item tag
        if (data.Items.Count <= 13 || data.Items[13] == null || string.IsNullOrEmpty(data.Items[13].Tag))
            return;

        var slot13Item = data.Items[13];
        var itemTag = slot13Item.Tag;
        
        var buyOrders = data.Items.Count > 15 ? data.Items[15] : null;
        var sellOffers = data.Items.Count > 16 ? data.Items[16] : null;

        double? topBuyPrice = ParsePrice(buyOrders?.Description);
        double? cheapestSellPrice = ParsePrice(sellOffers?.Description);

        if (topBuyPrice.HasValue || cheapestSellPrice.HasValue)
        {
            data.modService.UpdateBazaarPrice(itemTag, topBuyPrice, cheapestSellPrice);
        }

        // Create a clickable link to open SkyCofl history for this item
        var loreBuilder = new LoreBuilder()
            .AddText("§7[§bopen on SkyCofl website§7]", 
                     "Click to view price history", 
                     $"https://sky.coflnet.com/item/{itemTag}");

        var display = new List<DescModification>
        {
            new(loreBuilder.Build())
        };

        data.mods.Add(display);
    }

    public static double? ParsePrice(string description)
    {
        if (string.IsNullOrEmpty(description)) return null;
        
        // Match the first price in the "Top Orders" or "Top Offers" list
        // Format: §8- §64,362.4 coins
        var match = Regex.Match(description, @"§6([\d,.]+) coins");
        if (match.Success)
        {
            if (double.TryParse(match.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out double price))
            {
                return price;
            }
        }
        return null;
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        // No pre-request modifications needed for this modifier
    }
}
