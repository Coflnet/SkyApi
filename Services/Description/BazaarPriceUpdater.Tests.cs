using System.Collections.Generic;
using Coflnet.Sky.Api.Models.Mod;
using NUnit.Framework;
using FluentAssertions;
using System.Text.RegularExpressions;
using System.Globalization;
using Moq;
using Coflnet.Sky.Bazaar.Client.Api;

namespace Coflnet.Sky.Api.Services.Description.Tests;

[TestFixture]
public class BazaarPriceUpdaterTests
{
    [Test]
    public void ParseTopBuyOrder_ShouldReturnCorrectPrice()
    {
        var description = "§8Fine Flour\n\n§aTop Orders:\n§8- §64,362.4 coins §7each | §a475§7x §7in §f1 §7order\n§8- §64,362.3 coins §7each | §a58,703§7x §7in §f1 §7order\n\n§eClick to setup Buy Order!";
        
        var price = BazaarPriceUpdater.ParsePrice(description);
        
        price.Should().Be(4362.4);
    }

    [Test]
    public void ParseCheapestSellOffer_ShouldReturnCorrectPrice()
    {
        var description = "§8Fine Flour\n\n§aTop Offers:\n§8- §64,500.0 coins §7each | §a100§7x §7in §f1 §7offer\n\n§eClick to setup Sell Offer!";
        
        var price = BazaarPriceUpdater.ParsePrice(description);
        
        price.Should().Be(4500.0);
    }

    [Test]
    public void ExtractAndUploadOrderBook_PostsParsedOrders()
    {
        var buy = "§aTop Orders:\n§8- §64,362.4 coins §7each | §a475§7x";
        var sell = "§aTop Offers:\n§8- §64,500.0 coins §7each | §a100§7x";

        var (topBuy, cheapestSell) = BazaarPriceUpdater.ExtractAndUploadOrderBook("TEST:1", buy, sell);

        // give background task some time to run and populate the observable store
        System.Threading.Thread.Sleep(50);

        Assert.That(topBuy, Is.EqualTo(4362.4).Within(0.001));
        Assert.That(cheapestSell, Is.EqualTo(4500.0).Within(0.001));

        // verify the background poster recorded the posted orderbook
        Assert.That(BazaarPriceUpdater.LastPostedOrderBooks.TryGetValue("TEST:1", out var entry));
        entry.buy.Should().NotBeNull();
        entry.sell.Should().NotBeNull();
        entry.buy.Count.Should().BeGreaterThan(0);
        entry.sell.Count.Should().BeGreaterThan(0);
    }
}
