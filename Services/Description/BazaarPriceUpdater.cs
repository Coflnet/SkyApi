using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
#nullable enable
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Globalization;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// Updates bazaar prices by parsing order book descriptions from in-game screens.
/// </summary>
public class BazaarPriceUpdater : ICustomModifier
{
    /// <inheritdoc/>
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

        var (auction, _) = data.auctionRepresent[13];
        var itemTag = auction?.Tag ?? data.Items[13].Tag;

        var buyOrders = data.Items.Count > 15 ? data.Items[15] : null;
        var sellOffers = data.Items.Count > 16 ? data.Items[16] : null;

        double? topBuyPrice = ParsePrice(buyOrders?.Description);
        double? cheapestSellPrice = ParsePrice(sellOffers?.Description);

        if (topBuyPrice.HasValue || cheapestSellPrice.HasValue)
        {
            data.modService.UpdateBazaarPrice(itemTag, topBuyPrice, cheapestSellPrice);
        }
        // Start the HTTP request now; the player-state Kafka consumer is not on this path.
        _ = UploadOrderBook(itemTag, buyOrders?.Description, sellOffers?.Description, DateTime.UtcNow,
            logger: DiHandler.GetService<ILogger<BazaarPriceUpdater>>());
        PublishInstaSellIntentIfApplicable(data, itemTag);

        // Create a clickable link to open SkyCofl history for this item
        var loreBuilder = new LoreBuilder()
            .AddText("§7[§bopen on SkyCofl website§7]",
                     "Click to view price history",
                     $"https://sky.coflnet.com/item/{itemTag}");

        var display = new List<DescModification>
        {
            new(loreBuilder.Build())
        };

        if (!string.IsNullOrEmpty(itemTag))
        {
            var isBookmarked = data.inventory.Settings.BazaarBookmarks?.Contains(itemTag) ?? false;
            var bookmarkBuilder = new LoreBuilder();
            if (isBookmarked)
                bookmarkBuilder.AddText("§7[§cremove bookmark§7]",
                                        "Remove this item from your bazaar bookmarks shown in the bazaar menu",
                                        $"/cofl set loreBazaarBookmarks rm {itemTag}");
            else
                bookmarkBuilder.AddText("§7[§aadd bookmark§7]",
                                        "Bookmark this item to show it in the bazaar menu",
                                        $"/cofl set loreBazaarBookmarks {itemTag}");
            display.Add(new(bookmarkBuilder.Build()));
        }

        data.mods.Add(display);
    }

    /// <summary>
    /// Parses a coin price from a description string.
    /// </summary>
    /// <param name="description">The description text containing a coin price.</param>
    /// <returns>The parsed price, or null if no price was found.</returns>
    public static double? ParsePrice(string? description)
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

    internal static List<OrderEntry> ParseOrders(string? description) =>
        Regex.Matches(description ?? "", @"§6([\d,]+(?:\.\d+)?) coins[^\n]*?§a([\d,]+)§7x")
            .Select(m => new OrderEntry {
                PricePerUnit = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                Amount = int.Parse(m.Groups[2].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture)
            }).ToList();

    internal static async Task UploadOrderBook(string tag, string? buy, string? sell, DateTime observedAt,
        IOrderBookApi? api = null, ILogger<BazaarPriceUpdater>? logger = null)
    {
        logger ??= NullLogger<BazaarPriceUpdater>.Instance;
        using var span = BazaarTelemetry.Source.StartActivity("bazaar.price.upload");
        span?.SetTag("bazaar.item_tag", tag);
        span?.SetTag("bazaar.observed_at", observedAt.ToString("O"));
        var started = Stopwatch.GetTimestamp();
        var outcome = "failure";
        try
        {
            var remaining = observedAt.AddSeconds(10) - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                outcome = "expired";
                return;
            }
            using var timeout = new System.Threading.CancellationTokenSource(remaining);
            var buys = ParseOrders(buy);
            var sells = ParseOrders(sell);
            if (buys.Count == 0 && sells.Count == 0)
            {
                outcome = "empty";
                return;
            }
            var applied = await (api ?? DiHandler.GetService<IOrderBookApi>()).UpdateOrderBookAsync(new OrderBookUpdate {
                ItemTag = tag, Timestamp = observedAt, BuyOrders = buys, SellOrders = sells
            }, cancellationToken: timeout.Token);
            outcome = applied ? "accepted" : "rejected";
        }
        catch (OperationCanceledException) { outcome = "expired"; }
        catch (Exception e)
        {
            span?.SetStatus(ActivityStatusCode.Error, e.GetType().Name);
            logger.LogError(e, "Could not send Bazaar price observation for {ItemTag} at {ObservedAt:o}; TraceId {TraceId}", tag, observedAt, Activity.Current?.TraceId.ToString());
        }
        finally
        {
            BazaarTelemetry.Uploads.WithLabels(outcome).Inc();
            span?.SetTag("bazaar.result", outcome);
            if (logger.IsEnabled(LogLevel.Trace))
                logger.LogTrace("Bazaar price upload {ItemTag} at {ObservedAt:o}: {Result} in {ElapsedMs} ms; TraceId {TraceId}",
                tag, observedAt, outcome, Stopwatch.GetElapsedTime(started).TotalMilliseconds, Activity.Current?.TraceId.ToString());
        }
    }

    private static void PublishInstaSellIntentIfApplicable(DataContainer data, string? itemTag)
    {
        if (string.IsNullOrWhiteSpace(itemTag)
            || string.IsNullOrWhiteSpace(data.accountInfo?.UserId)
            || !HasImmediateSellIntent(data, itemTag, out var inventoryAmount))
        {
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                var redis = DiHandler.GetService<IConnectionMultiplexer>();
                if (redis == null)
                {
                    return;
                }

                var dedupeKey = $"bazaar:intent:{data.accountInfo.UserId}:{itemTag}";
                var db = redis.GetDatabase();
                var wasNew = await db.StringSetAsync(dedupeKey, "1", TimeSpan.FromSeconds(15), when: When.NotExists);
                if (!wasNew)
                {
                    return;
                }

                var signal = new BazaarSignalEvent
                {
                    Type = BazaarSignalTypes.InstaSellIntent,
                    ItemTag = itemTag,
                    UserId = data.accountInfo.UserId,
                    InventoryAmount = inventoryAmount,
                    Timestamp = DateTime.UtcNow,
                    Source = "SkyApi"
                };

                await redis.GetSubscriber().PublishAsync(
                    RedisChannel.Literal(BazaarSignalChannels.LiveSignals),
                    JsonConvert.SerializeObject(signal));
            }
            catch (Exception ex)
            {
                var logger = DiHandler.GetService<Microsoft.Extensions.Logging.ILogger<BazaarPriceUpdater>>();
                logger?.LogError(ex, "Failed to publish bazaar insta-sell intent for {tag}", itemTag);
            }
        });
    }

    private static bool HasImmediateSellIntent(DataContainer data, string itemTag, out int inventoryAmount)
    {
        inventoryAmount = 0;
        var hasSellButton = data.Items.Count > 11
            && data.Items[11]?.ItemName?.Contains("Sell Instantly", StringComparison.OrdinalIgnoreCase) == true;
        if (!hasSellButton)
        {
            return false;
        }

        for (var slot = 54; slot < data.Items.Count; slot++)
        {
            var item = data.Items[slot];
            if (item?.Tag == itemTag)
            {
                inventoryAmount += Math.Max(1, (int)item.Count);
            }
        }

        return inventoryAmount > 0;
    }

    /// <inheritdoc/>
    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        // No pre-request modifications needed for this modifier
    }
}
