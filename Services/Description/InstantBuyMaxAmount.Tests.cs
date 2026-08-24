using NUnit.Framework;
using AwesomeAssertions;

namespace Coflnet.Sky.Api.Services.Description.Tests;

/// <summary>Contains instant-buy amount tests.</summary>
[TestFixture]
public class InstantBuyMaxAmountTests
{
    /// <summary>Determines whether only instant-buy screens are matched.</summary>
    [TestCase("Lily Pad ➜ Instant Buy", true)]
    [TestCase("Lily Pad ➜ Instant Buy ", true)]
    [TestCase("Lily Pad ➜ Instant Sell", false)]
    [TestCase("Bazaar ➜ Lily Pad", false)]
    [TestCase("Instant Buy", false)] // no arrow -> not the bazaar screen
    [TestCase(null, false)]
    public void IsInstantBuy_MatchesOnlyBuyScreen(string chestName, bool expected)
    {
        InstantBuyMaxAmount.IsInstantBuy(chestName).Should().Be(expected);
    }

    /// <summary>Parses max amount reads buy up to cap.</summary>
    [Test]
    public void ParseMaxAmount_ReadsBuyUpToCap()
    {
        InstantBuyMaxAmount.ParseMaxAmountFromText("§7Buy Order Quantity\n\n§7Buy up to §a71,680x").Should().Be(71_680);
    }

    /// <summary>Parses max amount ignores current selection.</summary>
    [Test]
    public void ParseMaxAmount_IgnoresCurrentSelection()
    {
        // "Amount:" is the currently selected amount, not a cap - must not be read as the max
        InstantBuyMaxAmount.ParseMaxAmountFromText("§7Amount: §a4,600x\n§7Price: §64,000 coins").Should().BeNull();
    }

    /// <summary>Parses max amount null when no cap.</summary>
    [Test]
    public void ParseMaxAmount_NullWhenNoCap()
    {
        InstantBuyMaxAmount.ParseMaxAmountFromText("§7Custom Amount\n§7Right-Click to edit!").Should().BeNull();
    }

    private static InstantBuyMaxAmount.PriceLevel Level(double price, int amount)
        => new() { Price = price, Amount = amount };

    /// <summary>Performs the max affordable walks the order book operation.</summary>
    [Test]
    public void MaxAffordable_WalksTheOrderBook()
    {
        // 100 @ 10, then 100 @ 20. Purse 2500 with a 4% reserved margin -> budget 2403.8:
        // first level costs 1000 (all 100), leaving 1403.8 -> 70 more at 20 = 170 total
        var orders = new[] { Level(10, 100), Level(20, 100) };
        InstantBuyMaxAmount.MaxAffordable(orders, 2500).Should().Be(170);
    }

    /// <summary>Performs the max affordable stops within first level operation.</summary>
    [Test]
    public void MaxAffordable_StopsWithinFirstLevel()
    {
        // purse 550 with a 4% reserved margin -> budget 528.8 -> 52 at price 10
        var orders = new[] { Level(10, 100), Level(20, 100) };
        InstantBuyMaxAmount.MaxAffordable(orders, 550).Should().Be(52);
    }

    /// <summary>Performs the max affordable capped by available volume operation.</summary>
    [Test]
    public void MaxAffordable_CappedByAvailableVolume()
    {
        // purse far exceeds the book -> limited to total available amount
        var orders = new[] { Level(10, 100), Level(20, 50) };
        InstantBuyMaxAmount.MaxAffordable(orders, 1_000_000).Should().Be(150);
    }

    /// <summary>Performs the cap amount caps at item max operation.</summary>
    [Test]
    public void CapAmount_CapsAtItemMax()
    {
        // afford 1000 but the menu only allows 333
        InstantBuyMaxAmount.CapAmount(1000, 333).Should().Be(333);
    }

    /// <summary>Performs the cap amount uses affordable when below max operation.</summary>
    [Test]
    public void CapAmount_UsesAffordableWhenBelowMax()
    {
        InstantBuyMaxAmount.CapAmount(200, 333).Should().Be(200);
    }

    /// <summary>Performs the cap amount no cap when max unknown operation.</summary>
    [Test]
    public void CapAmount_NoCapWhenMaxUnknown()
    {
        InstantBuyMaxAmount.CapAmount(1000, null).Should().Be(1000);
    }
}
