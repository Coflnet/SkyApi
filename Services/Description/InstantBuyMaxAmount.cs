using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// On the "&lt;item&gt; ➜ Instant Buy" screen this suggests the maximum amount the player can
/// afford. The affordable amount is derived from the players purse (loaded from player state) and
/// the real bazaar order book (loaded from SkyBazaar) so it accounts for the order-book walk - a
/// large instant buy fills progressively more expensive sell offers, not just the lowest one. The
/// result is capped by the amount the menu allows and offered as a sign suggestion so right-clicking
/// the Custom Amount button pre-fills it, plus shown as an info line on the screen.
/// </summary>
public class InstantBuyMaxAmount : ICustomModifier
{
    private const string PurseLoadKey = "instantBuyPurse";
    private const string OrderBookLoadKey = "instantBuyOrderBook";

    /// <inheritdoc/>
    public void Modify(ModDescriptionService.PreRequestContainer preRequest)
    {
        if (!IsInstantBuy(preRequest.inventory?.ChestName) || string.IsNullOrWhiteSpace(preRequest.mcName))
            return;
        // load purse and order book in the background so they are ready by the time Apply runs
        preRequest.ToLoad[PurseLoadKey] = LoadPurse(preRequest.mcName);
        var itemTag = FindBazaarItemTag(preRequest.auctionRepresent);
        if (itemTag != null)
            preRequest.ToLoad[OrderBookLoadKey] = LoadSellOrders(itemTag);
    }

    private static async Task<string> LoadPurse(string name)
    {
        try
        {
            var api = DiHandler.GetService<PlayerState.Client.Api.IPlayerStateApi>();
            var currencies = await api.PlayerStatePlayerIdCurrenciesGetAsync(name);
            return currencies?.Purse.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }
        catch (Exception)
        {
            // never let a failed purse load break the whole description computation
            return string.Empty;
        }
    }

    // fetches the live order book and returns the sell offers (the side an instant buy consumes,
    // cheapest first) as a compact json list so Apply can walk it to find the max affordable amount
    private static async Task<string> LoadSellOrders(string itemTag)
    {
        try
        {
            var api = DiHandler.GetService<IOrderBookApi>();
            if (api == null)
                return string.Empty;
            var book = await api.GetOrderBookAsync(itemTag);
            var levels = book?.Sell?
                .Where(o => o != null && o.Amount > 0 && o.PricePerUnit > 0)
                .OrderBy(o => o.PricePerUnit)
                .Select(o => new PriceLevel { Price = o.PricePerUnit, Amount = o.Amount });
            return levels == null ? string.Empty : JsonConvert.SerializeObject(levels);
        }
        catch (Exception)
        {
            // never let a failed order book load break the whole description computation
            return string.Empty;
        }
    }

    /// <inheritdoc/>
    public void Apply(DataContainer data)
    {
        if (!IsInstantBuy(data.inventory?.ChestName))
            return;
        if (data.Loaded == null || !data.Loaded.TryGetValue(PurseLoadKey, out var purseTask))
            return;
        if (!long.TryParse(purseTask.Result, out var purse) || purse <= 0)
            return;

        var customSlot = FindCustomAmountSlot(data);
        if (customSlot < 0)
            return;
        var customItem = data.Items[customSlot];

        // Prefer the real order book: a large instant buy walks up the book, so the average paid
        // per unit is higher than the lowest offer. Fall back to the summary price if unavailable.
        long affordable;
        var accurate = false;
        var sellOrders = LoadedSellOrders(data);
        if (sellOrders != null && sellOrders.Count > 0)
        {
            affordable = MaxAffordable(sellOrders, purse);
            accurate = true;
        }
        else
        {
            var itemTag = FindBazaarItemTag(data.auctionRepresent);
            var pricePerUnit = data.GetItemprice(itemTag, useBuyOrderPrices: true);
            if (pricePerUnit <= 0)
                return;
            affordable = purse / pricePerUnit;
        }
        if (affordable <= 0)
            return;

        var maxFromItem = ParseMaxAmount(customItem);
        var suggested = CapAmount(affordable, maxFromItem);
        if (suggested <= 0)
            return;

        var hover = $"You can afford {McColorCodes.YELLOW}{affordable:N0}{McColorCodes.GRAY} with your purse of {McColorCodes.GOLD}{purse:N0}{McColorCodes.GRAY} coins"
            + $"\n{McColorCodes.DARK_GRAY}" + (accurate ? "(from live order book)" : "(estimated from lowest offer)");
        if (maxFromItem.HasValue && affordable > maxFromItem.Value)
            hover += $"\n{McColorCodes.GRAY}capped at the {McColorCodes.YELLOW}{maxFromItem.Value:N0}{McColorCodes.GRAY} this menu allows";
        hover += $"\n{McColorCodes.AQUA}Right-Click the Custom Amount to fill it in";

        var loreBuilder = new LoreBuilder().AddText(
            $"{McColorCodes.GRAY}[{McColorCodes.GREEN}buy max {McColorCodes.YELLOW}{suggested:N0}{McColorCodes.GRAY}]",
            hover);

        // add as a new appended info-display list (parsed as components), not onto the item slot.
        // The SUGGEST types the amount into the sign; the mod matches the text before ": "
        // against the sign's 4th line, which reads "to order".
        data.mods.Add(new List<DescModification>
        {
            loreBuilder.BuildLine(),
            new DescModification(DescModification.ModType.SUGGEST, 0, $"to order: {suggested}")
        });
    }

    /// <summary>
    /// Walks the sell offers (cheapest first) to find the largest amount buyable with the given
    /// purse. Instant buy is not taxed, so the full purse is available; deeper offers just cost
    /// more per unit. Also naturally capped by the total volume on the book.
    /// </summary>
    internal static long MaxAffordable(IReadOnlyList<PriceLevel> ascendingSellOrders, long purse)
    {
        double budget = purse;
        long total = 0;
        foreach (var level in ascendingSellOrders)
        {
            if (level.Price <= 0 || level.Amount <= 0)
                continue;
            var levelCost = level.Price * level.Amount;
            if (levelCost <= budget)
            {
                budget -= levelCost;
                total += level.Amount;
            }
            else
            {
                total += (long)(budget / level.Price);
                return total;
            }
        }
        return total;
    }

    internal static long CapAmount(long affordable, long? maxFromItem)
        => maxFromItem.HasValue ? Math.Min(affordable, maxFromItem.Value) : affordable;

    internal static bool IsInstantBuy(string chestName)
        => chestName != null && chestName.Contains('➜') && chestName.TrimEnd().EndsWith("Instant Buy");

    private static List<PriceLevel> LoadedSellOrders(DataContainer data)
    {
        if (data.Loaded == null || !data.Loaded.TryGetValue(OrderBookLoadKey, out var task))
            return null;
        var json = task.Result;
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonConvert.DeserializeObject<List<PriceLevel>>(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int FindCustomAmountSlot(DataContainer data)
    {
        // slot 16 is the typical location for the Custom Amount button
        if (data.Items.Count > 16 && IsCustomAmount(data.Items[16]))
            return 16;
        for (int i = 0; i < data.Items.Count; i++)
            if (IsCustomAmount(data.Items[i]))
                return i;
        return -1;
    }

    private static bool IsCustomAmount(Item item)
    {
        var name = item?.ItemName;
        if (name == null)
            return false;
        var lower = name.ToLowerInvariant();
        return lower.Contains("custom") && lower.Contains("amount");
    }

    // Picks the bazaar product tag from the screen's items. The product is shown on several preset
    // buttons, so the tag that occurs most often is the actual item being bought.
    private static string FindBazaarItemTag(List<(SaveAuction auction, string[] desc)> auctionRepresent)
    {
        if (auctionRepresent == null)
            return null;
        return auctionRepresent
            .Select(a => a.auction?.Tag)
            .Where(t => !string.IsNullOrEmpty(t))
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();
    }

    private static long? ParseMaxAmount(Item customItem)
        => ParseMaxAmountFromText((customItem?.Description ?? string.Empty) + "\n" + (customItem?.ItemName ?? string.Empty));

    // The hard cap the menu enforces, shown as "Buy up to N" (limited by order-book supply).
    // NOTE: do not use the "Amount: Nx" line here - that is the *currently selected* amount, not a max.
    internal static long? ParseMaxAmountFromText(string text)
    {
        var cleaned = Regex.Replace(text ?? string.Empty, "§.", string.Empty);
        var upToMatch = Regex.Match(cleaned, @"up to\s*([\d,]+)", RegexOptions.IgnoreCase);
        if (upToMatch.Success && long.TryParse(upToMatch.Groups[1].Value.Replace(",", ""), out var max))
            return max;
        return null;
    }

    /// <summary>One price level of the order book: a per-unit price and the amount offered at it.</summary>
    internal class PriceLevel
    {
        public double Price { get; set; }
        public int Amount { get; set; }
    }
}
