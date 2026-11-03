using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Mod.Tests;

/// <summary>Tests for <see cref="BazaarTopOrderReader"/>.</summary>
[TestFixture]
public class BazaarTopOrderReaderTests
{
    private static readonly DateTime At = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Custom Price lore of the "How much do you want to pay?" screen from the reported Wolf Tooth order.</summary>
    internal const string WolfToothCustomPriceLore = "§8Buy Order Setup\n\n§7Set the price per unit you're\n§7willing to pay.\n\n"
        + "§aTop Orders:\n"
        + "§8- §6376.9 coins §7each | §a21,408§7x §7in §f1 §7order\n"
        + "§8- §6376.8 coins §7each | §a142,416§7x §7in §f2 §7orders\n"
        + "§8- §6376.7 coins §7each | §a3,100§7x §7in §f1 §7order\n"
        + "§8- §6376.6 coins §7each | §a4,225§7x §7in §f1 §7order\n"
        + "§8- §6376.5 coins §7each | §a640§7x §7in §f1 §7order\n"
        + "§8- §6376.4 coins §7each | §a71§7x §7in §f1 §7order\n"
        + "§8- §6370.1 coins §7each | §a12,000§7x §7in §f1 §7order\n\n"
        + "§eClick to set a custom price!";

    /// <summary>Top Orders lore with a single price level.</summary>
    internal static string TopOrders(string price, string amount = "640", int orders = 1)
        => $"§aTop Orders:\n§8- §6{price} coins §7each | §a{amount}§7x §7in §f{orders} §7order{(orders > 1 ? "s" : "")}";

    /// <summary>A bazaar screen of the given chest size followed by the 36 player inventory slots.</summary>
    internal static List<Item> BazaarScreen(int chestSize, params (int slot, string name, string tag, string lore)[] slots)
    {
        var items = Enumerable.Range(0, chestSize + 36).Select(_ => new Item()).ToList();
        foreach (var (slot, name, tag, lore) in slots)
            items[slot] = new Item { ItemName = name, Tag = tag, Description = lore };
        return items;
    }

    /// <summary>The buy order price screen with the given Custom Price lore.</summary>
    internal static List<Item> BuyPriceScreen(string customPriceLore) => BazaarScreen(36,
        (10, "§aSame as Top Order", null, ""), (12, "§aTop Order +0.1", null, ""), (16, "§aCustom Price", null, customPriceLore));

    [Test]
    public void ParsesBestLevelOfTheCustomPriceLore()
    {
        BazaarTopOrderReader.ParseTopOrders(WolfToothCustomPriceLore, At).Should().Be(new BazaarTopOrder(376.9, At, 21_408, 1, 376.8));
    }

    [Test]
    public void ParsesThousandsSeparators()
    {
        var top = BazaarTopOrderReader.ParseTopOrders("§aTop Orders:\n§8- §61,234.5 coins §7each | §a1,200§7x §7in §f3 §7orders", At);
        top.Should().Be(new BazaarTopOrder(1234.5, At, 1200, 3));
    }

    [Test]
    public void IgnoresSellOffersAndLoreWithoutOrders()
    {
        BazaarTopOrderReader.ParseTopOrders("§aTop Offers:\n§8- §6380.0 coins §7each | §a100§7x §7in §f1 §7offer", At).Should().BeNull();
        BazaarTopOrderReader.ParseTopOrders("§aTop Orders:\n§cNo buy orders!", At).Should().BeNull();
        BazaarTopOrderReader.ParseTopOrders(null, At).Should().BeNull();
    }

    [Test]
    public void ReadsOnlyChestSlotsOfTheBuyPriceScreen()
    {
        BazaarTopOrderReader.ReadBuyPriceScreen(BuyPriceScreen(WolfToothCustomPriceLore), At)?.Price.Should().Be(376.9);
        // the same lore on a player inventory slot is no screen data
        var inventoryOnly = BazaarScreen(36, (10, "§aSame as Top Order", null, ""), (40, "§aCustom Price", null, WolfToothCustomPriceLore));
        BazaarTopOrderReader.ReadBuyPriceScreen(inventoryOnly, At).Should().BeNull();
    }

    /// <summary>The sell offer price screen is never read, even when its lore carries an order list under the same header.</summary>
    [Test]
    public void SellPriceScreenIsIgnored()
    {
        var sell = BazaarScreen(36, (10, "§6Same as Best Offer", null, ""), (12, "§6Best Offer -0.1", null, ""),
            (16, "§6Custom Price", null, WolfToothCustomPriceLore));

        BazaarTopOrderReader.IsBuyPriceScreen(sell).Should().BeFalse();
        BazaarTopOrderReader.ReadBuyPriceScreen(sell, At).Should().BeNull();
    }

    /// <summary>The product page is read from Create Buy Order only, the sell offers of slot 16 never count.</summary>
    [Test]
    public void ProductPageReadsTheBuySideOnly()
    {
        var page = BazaarScreen(54, (13, "§aWolf Tooth", "WOLF_TOOTH", ""), (15, "§aCreate Buy Order", null, TopOrders("376.9")),
            (16, "§6Create Sell Offer", null, "§aTop Offers:\n§8- §6382.0 coins §7each | §a100§7x §7in §f1 §7offer\n" + TopOrders("381.0")));

        BazaarTopOrderReader.ProductPageTag(page).Should().Be("WOLF_TOOTH");
        BazaarTopOrderReader.ReadProductPage(page, At)?.Price.Should().Be(376.9);
        var other = BazaarScreen(54, (13, "§aWolf Tooth", "WOLF_TOOTH", ""), (15, "§aSomething", null, ""));
        BazaarTopOrderReader.ProductPageTag(other).Should().BeNull();
    }

    /// <summary>Enchantment products are ENCHANTED_BOOK by NBT id, the represented auction names the product.</summary>
    [Test]
    public void ProductPagePrefersTheRepresentedTag()
    {
        var page = BazaarScreen(54, (13, "§9Sharpness V", "ENCHANTED_BOOK", ""), (15, "§aCreate Buy Order", null, TopOrders("1,000.0")));
        var represented = page.Select((_, i) => i == 13 ? "ENCHANTMENT_SHARPNESS_5" : null).ToList();

        BazaarTopOrderReader.ProductPageTag(page, represented).Should().Be("ENCHANTMENT_SHARPNESS_5");
        BazaarTopOrderReader.ProductPageTag(page).Should().Be("ENCHANTED_BOOK");
    }
}
