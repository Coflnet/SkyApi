using NUnit.Framework;
using FluentAssertions;

namespace Coflnet.Sky.Api.Services.Description.Tests;

[TestFixture]
public class InstantBuyMaxAmountTests
{
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

    [Test]
    public void ParseMaxAmount_ReadsAmountLabel()
    {
        InstantBuyMaxAmount.ParseMaxAmountFromText("§7Buy Order Quantity\n\n§7Your amount: §a333x").Should().Be(333);
    }

    [Test]
    public void ParseMaxAmount_ReadsWithThousandsSeparator()
    {
        InstantBuyMaxAmount.ParseMaxAmountFromText("§7Your amount: §a71,680x").Should().Be(71_680);
    }

    [Test]
    public void ParseMaxAmount_NullWhenNoAmount()
    {
        InstantBuyMaxAmount.ParseMaxAmountFromText("§7Custom Amount\n§7Right-Click to edit!").Should().BeNull();
    }

    [Test]
    public void CapAmount_CapsAtItemMax()
    {
        // afford 1000 but the menu only allows 333
        InstantBuyMaxAmount.CapAmount(1000, 333).Should().Be(333);
    }

    [Test]
    public void CapAmount_UsesAffordableWhenBelowMax()
    {
        InstantBuyMaxAmount.CapAmount(200, 333).Should().Be(200);
    }

    [Test]
    public void CapAmount_NoCapWhenMaxUnknown()
    {
        InstantBuyMaxAmount.CapAmount(1000, null).Should().Be(1000);
    }
}
