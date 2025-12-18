using System.Collections.Generic;
using Coflnet.Sky.Api.Models.Mod;
using NUnit.Framework;
using FluentAssertions;
using System.Text.RegularExpressions;
using System.Globalization;

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
}
