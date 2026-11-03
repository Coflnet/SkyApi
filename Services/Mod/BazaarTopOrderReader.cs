using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Description;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services.Mod;

/// <summary>
/// Reads the best buy order of a bazaar item from the "Top Orders" lore of an open bazaar screen: the Create Buy Order
/// button of the product page and the buttons of the buy order price screen list the order book. Sell side screens are never read.
/// </summary>
public static class BazaarTopOrderReader
{
    private const string TopOrdersHeader = "Top Orders:";
    private const int ProductSlot = 13;
    private const int CreateBuyOrderSlot = 15;
    // the buy price screen offers these, the sell price screen "Same as Best Offer" and "Best Offer -0.1"
    private static readonly string[] BuyPriceScreenButtons = ["Top Order +0.1", "Same as Top Order"];
    private static readonly Regex OrderCount = new(@"in §f([\d,]+) §7orders?", RegexOptions.Compiled);
    private static readonly Regex ColorCode = new("§.", RegexOptions.Compiled);

    /// <summary>
    /// Tag of the product page item, null when the screen is not a product page with a Create Buy Order button.
    /// The tag of the represented auction wins over the NBT id (enchantment books are ENCHANTMENT_* products).
    /// </summary>
    public static string ProductPageTag(IList<Item> items, IReadOnlyList<string> representedTags = null)
    {
        if (items == null || ChestSize(items) <= CreateBuyOrderSlot || !NameContains(items[CreateBuyOrderSlot], "Create Buy Order"))
            return null;
        var tag = representedTags?.ElementAtOrDefault(ProductSlot);
        if (string.IsNullOrEmpty(tag))
            tag = items[ProductSlot]?.Tag;
        return string.IsNullOrEmpty(tag) ? null : tag;
    }

    /// <summary>Best buy order of a product page, read from the Create Buy Order button only.</summary>
    public static BazaarTopOrder? ReadProductPage(IList<Item> items, DateTime observedAt)
        => ChestSize(items) > CreateBuyOrderSlot ? ParseTopOrders(items[CreateBuyOrderSlot]?.Description, observedAt) : null;

    /// <summary>True for the "How much do you want to pay?" screen of a buy order, false for the sell offer price screen.</summary>
    public static bool IsBuyPriceScreen(IList<Item> items)
        => ChestSlots(items).Any(item => BuyPriceScreenButtons.Any(button => NameContains(item, button)));

    /// <summary>Best buy order listed on the buy order price screen, null on any other screen.</summary>
    public static BazaarTopOrder? ReadBuyPriceScreen(IList<Item> items, DateTime observedAt)
    {
        if (!IsBuyPriceScreen(items))
            return null;
        return ChestSlots(items).Select(item => ParseTopOrders(item?.Description, observedAt)).FirstOrDefault(top => top != null);
    }

    /// <summary>Best price level of the order lines directly following the "Top Orders:" header, with the level below it.</summary>
    public static BazaarTopOrder? ParseTopOrders(string lore, DateTime observedAt)
    {
        if (string.IsNullOrEmpty(lore))
            return null;
        var lines = lore.Split('\n');
        var header = Array.FindIndex(lines, l => Strip(l).Trim() == TopOrdersHeader);
        if (header < 0)
            return null;
        var levels = lines.Skip(header + 1).Select(ParseLevel).TakeWhile(l => l != null).Select(l => l.Value)
            .OrderByDescending(l => l.Price).ToList();
        if (levels.Count == 0)
            return null;
        var top = levels[0];
        double? next = levels.Count > 1 ? levels[1].Price : null;
        return new BazaarTopOrder(top.Price, observedAt, top.Amount, top.Orders, next);
    }

    private static (double Price, long Amount, int Orders)? ParseLevel(string line)
    {
        var entry = BazaarPriceUpdater.ParseOrders(line).FirstOrDefault();
        if (entry == null)
            return null;
        var count = OrderCount.Match(line);
        var orders = count.Success ? int.Parse(count.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture) : 0;
        return (entry.PricePerUnit, entry.Amount, orders);
    }

    private static bool NameContains(Item item, string text) => Strip(item?.ItemName).Contains(text, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Item> ChestSlots(IList<Item> items) => items?.Take(ChestSize(items)) ?? Enumerable.Empty<Item>();

    // mirrors GoalService.ExtractCounts: 46 or fewer items are the player inventory, otherwise the last 36 are player slots
    private static int ChestSize(IList<Item> items) => items == null || items.Count <= 46 ? 0 : items.Count - 36;

    private static string Strip(string text) => ColorCode.Replace(text ?? "", "");
}
