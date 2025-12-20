using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using System.Collections.Concurrent;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Bazaar.Client.Api;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services.Description;

public class BazaarPriceUpdater : ICustomModifier
{
    // For tests: observe what gets posted without relying on DI mocking.
    public static ConcurrentDictionary<string, (List<Bazaar.Client.Model.OrderEntry> buy, List<Bazaar.Client.Model.OrderEntry> sell)> LastPostedOrderBooks = new();
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
        ExtractAndUploadOrderBook(itemTag, buyOrders.Description, sellOffers.Description);

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
    
    public static (double buy, double sell) ExtractAndUploadOrderBook(string tag, string buyDescription, string sellDescription)
    {
        // Parse buy/sell descriptions into order entries
        var buyList = new List<Bazaar.Client.Model.OrderEntry>();
        var sellList = new List<Bazaar.Client.Model.OrderEntry>();

        if (!string.IsNullOrEmpty(buyDescription))
        {
            var matches = Regex.Matches(buyDescription, @"§6([\d,]+(?:\.\d+)?) coins(?:.*?§a([\d,]+)§7x)?");
            foreach (Match m in matches)
            {
                if (double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                {
                    var amount = 1;
                    if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value.Replace(",", ""), out var a))
                        amount = a;
                    buyList.Add(new Bazaar.Client.Model.OrderEntry() { Amount = amount, PricePerUnit = price, Timestamp = DateTime.UtcNow });
                }
            }
        }

        if (!string.IsNullOrEmpty(sellDescription))
        {
            var matches = Regex.Matches(sellDescription, @"§6([\d,]+(?:\.\d+)?) coins(?:.*?§a([\d,]+)§7x)?");
            foreach (Match m in matches)
            {
                if (double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                {
                    var amount = 1;
                    if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value.Replace(",", ""), out var a))
                        amount = a;
                    sellList.Add(new Bazaar.Client.Model.OrderEntry() { Amount = amount, PricePerUnit = price, Timestamp = DateTime.UtcNow });
                }
            }
        }

        // Determine top prices
        var topBuy = buyList.Any() ? buyList.Max(o => o.PricePerUnit) : 0.0;
        var cheapestSell = sellList.Any() ? sellList.Min(o => o.PricePerUnit) : 0.0;

        // Post the orderbook in the background so we don't block description parsing
        Task.Run(() =>
        {
            try
            {
                // store posted payload for tests to inspect
                LastPostedOrderBooks[tag] = (buyList, sellList);

                // Try to obtain the API from DI; if DI isn't initialized (tests), skip the remote post.
                try
                {
                    var orderBookApi = DiHandler.GetService<IOrderBookApi>();
                    if (orderBookApi != null)
                    {
                        // Post the structured lists (server expects list of entries for the orderbook).
                        orderBookApi.UpdateOrderBookAsync(new()
                        {
                            ItemTag = tag,
                            BuyOrders = buyList,
                            SellOrders = sellList
                        }).GetAwaiter().GetResult();
                    }
                }
                catch
                {
                    // ignore DI/service provider errors in tests
                }
            }
            catch (Exception ex)
            {
                var logger = DiHandler.GetService<Microsoft.Extensions.Logging.ILogger<BazaarPriceUpdater>>();
                logger?.LogError(ex, "Failed to update orderbook for {tag}", tag);
            }
        });

        return (topBuy, cheapestSell);
    }

    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        // No pre-request modifications needed for this modifier
    }
}
