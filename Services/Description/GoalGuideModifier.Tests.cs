using Coflnet.Sky.Bazaar.Client.Model;
using System.Collections.Immutable;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Mod;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Core;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Description.Tests;

/// <summary>Tests for <see cref="GoalGuideModifier"/>.</summary>
[TestFixture]
public class GoalGuideModifierTests
{
    private const string Tag = "ENCHANTED_DIAMOND";
    private Mock<IGoalGuideSource> source;
    private GoalGuideModifier modifier;

    /// <summary>Creates the modifier with a mocked goal source.</summary>
    [SetUp]
    public void Setup()
    {
        source = new Mock<IGoalGuideSource>();
        source.Setup(s => s.MarkOrdered(It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        modifier = new GoalGuideModifier(() => source.Object);
    }

    private void WithStep(StepKind kind = StepKind.BuyOrder, long qty = 64, bool cookie = true, AuctionOffer offer = null, double? unitPrice = null)
    {
        var step = new GoalStep { Id = "s1", Kind = kind, ItemTag = Tag, Quantity = qty, UnitPrice = unitPrice };
        source.Setup(s => s.GetGuideContext("Ekwav")).Returns(new GoalGuideContext(step, kind, "Enchanted Diamond", "Enchanted Diamond", qty, cookie, offer));
    }

    private static DataContainer Screen(string title, params (int slot, string name, string tag)[] slots)
    {
        var size = 54;
        var items = Enumerable.Range(0, size).Select(_ => new Item()).ToList();
        foreach (var (slot, name, tag) in slots)
            items[slot] = new Item { ItemName = name, Tag = tag, Description = "" };
        return new DataContainer
        {
            inventory = new InventoryDataWithSettings { ChestName = title, Version = 4 },
            Items = items,
            auctionRepresent = items.Select(i => (new SaveAuction { ItemName = i.ItemName, Tag = i.Tag }, new string[0])).ToList(),
            mods = Enumerable.Range(0, size).Select(_ => new List<DescModification>()).ToList(),
            mcName = "Ekwav"
        };
    }

    private static int? Highlighted(DataContainer data)
    {
        var slots = Enumerable.Range(0, 54).Where(i => data.mods[i].Any(m => m.Type == DescModification.ModType.HIGHLIGHT && m.Line == -1 && m.Value == "00FF00")).ToList();
        return slots.Count == 0 ? null : slots.Single();
    }

    private static string Panel(DataContainer data) => string.Join("\n", data.mods.Skip(54).SelectMany(l => l).Select(m => m.Value));

    /// <summary>The product page highlights Create Buy Order by name.</summary>
    [Test]
    public void ProductPage_HighlightsCreateBuyOrder()
    {
        WithStep();
        var data = Screen("Bazaar ➜ Enchanted Diamond", (10, "§aBuy Instantly", null), (13, "§aEnchanted Diamond", Tag), (15, "§6Create Buy Order", null), (16, "§6Create Sell Offer", null));
        modifier.Apply(data);
        Highlighted(data).Should().Be(15);
        Panel(data).Should().Contain("Step: ").And.Contain("Create Buy Order (64x Enchanted Diamond)");
    }

    /// <summary>A product page of another item is not highlighted.</summary>
    [Test]
    public void ProductPage_OtherItem_NoHighlight()
    {
        WithStep();
        var data = Screen("Bazaar ➜ Coal", (10, "§aBuy Instantly", null), (13, "§aCoal", "COAL"), (15, "§6Create Buy Order", null));
        modifier.Apply(data);
        Highlighted(data).Should().BeNull();
        Panel(data).Should().Contain("/cofl bzorder 64 Enchanted Diamond");
    }

    /// <summary>The amount screen outlines Custom Amount and offers the quantity on the sign.</summary>
    [Test]
    public void AmountScreen_HighlightsCustomAmountAndAddsFillSign()
    {
        WithStep();
        var data = Screen("How many do you want?", (10, "§aBuy a stack!", Tag), (16, "§aCustom Amount", null));
        modifier.Apply(data);
        Highlighted(data).Should().Be(16);
        Panel(data).Should().Contain("fillsign:").And.Contain("to order").And.Contain("\\u0022value\\u0022:\\u002264\\u0022");
    }

    /// <summary>When the name is missing the known slot index is used.</summary>
    [Test]
    public void AmountScreen_NameMissing_FallsBackToSlot16()
    {
        WithStep();
        var data = Screen("How many do you want?", (10, "§aBuy a stack!", Tag), (16, "§aSomething else", null));
        modifier.Apply(data);
        Highlighted(data).Should().Be(16);
        var data2 = Screen("How many do you want?", (10, "§aBuy a stack!", Tag), (14, "§aCustom Amount", null));
        modifier.Apply(data2);
        Highlighted(data2).Should().Be(14); // name wins over the index
    }

    /// <summary>The price screen highlights the +0.1 option.</summary>
    [Test]
    public void PriceScreen_HighlightsTopOrderPlus()
    {
        WithStep();
        var data = Screen("How much do you want to pay?", (10, "§aSame as Top Order", null), (12, "§aTop Order +0.1", null), (16, "§aCustom Price", null));
        modifier.Apply(data);
        Highlighted(data).Should().Be(12);
    }

    /// <summary>The confirm screen highlights the confirm item and marks the step ordered.</summary>
    [Test]
    public void ConfirmScreen_HighlightsAndMarksOrdered()
    {
        WithStep();
        var data = Screen("Confirm Buy Order", (13, "§aConfirm Buy Order", Tag), (30, "§aGo Back", null), (31, "§cCancel Buy Order", null));
        modifier.Apply(data);
        Highlighted(data).Should().Be(13);
        source.Verify(s => s.MarkOrdered("Ekwav", "s1"), Times.Once);
    }

    /// <summary>Confirm screens of a different product are not marked.</summary>
    [Test]
    public void ConfirmScreen_OtherProduct_NotMarked()
    {
        WithStep();
        var data = Screen("Confirm Buy Order", (13, "§aCoal", "COAL"));
        data.bazaarPrices = System.Collections.Immutable.ImmutableDictionary<string, Coflnet.Sky.Bazaar.Client.Model.ItemPrice>.Empty.Add("COAL", new());
        modifier.Apply(data);
        source.Verify(s => s.MarkOrdered(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>The BIN view highlights Buy Item Right Now for auction steps.</summary>
    [Test]
    public void BinAuctionView_HighlightsBuyNow()
    {
        WithStep(StepKind.Auction, 1);
        var data = Screen("BIN Auction View", (13, "§aEnchanted Diamond", Tag), (31, "§6Buy Item Right Now", null));
        modifier.Apply(data);
        Highlighted(data).Should().Be(31);
    }

    /// <summary>The auction hint names the price and the profit change and links the full uuid.</summary>
    [Test]
    public void AuctionStep_HintShowsPriceAndProfitChange()
    {
        WithStep(StepKind.Auction, 1, offer: new AuctionOffer("70e3559483ce4da8b1c2d3e4f5a6b7c8", 2_900_000), unitPrice: 2_500_000);
        var data = Screen("Chest", (0, "§fStone", "STONE"));
        modifier.Apply(data);
        Panel(data).Should().Contain("Buy Enchanted Diamond on the auction house (2.9m, 400k less profit than planned)")
            .And.Contain("Click to view the cheapest auction");
    }

    /// <summary>A short sniper id is never turned into a view command.</summary>
    [Test]
    public void AuctionStep_ShortId_OpensAuctionHouse()
    {
        WithStep(StepKind.Auction, 1, offer: new AuctionOffer("70e3559483ce4da8b", 2_500_000), unitPrice: 2_500_000);
        var data = Screen("Chest", (0, "§fStone", "STONE"));
        modifier.Apply(data);
        var panel = Newtonsoft.Json.JsonConvert.SerializeObject(data.mods.Skip(54));
        panel.Should().Contain("/ah").And.NotContain("viewauction");
        Panel(data).Should().Contain("(2.5m)").And.NotContain("profit");
    }

    /// <summary>The guide takes the first actionable step, which is the auction step because the evaluator orders it first.</summary>
    [Test]
    public void AuctionComesFirst_GuideFollowsAuctionStep()
    {
        var goal = new Goal
        {
            Root = new GoalStep
            {
                Id = "r", Kind = StepKind.Craft, ItemTag = "R", Quantity = 1,
                Children =
                {
                    new GoalStep { Id = "bz", Kind = StepKind.InstaBuy, ItemTag = "BZ", Quantity = 8 },
                    new GoalStep { Id = "ah", Kind = StepKind.Auction, ItemTag = Tag, Quantity = 1 },
                }
            }
        };
        var progress = GoalProgressEvaluator.Evaluate(goal, new Dictionary<string, long>(), new Dictionary<string, long>(), new HashSet<string>(), DateTime.UtcNow);
        var first = GoalHud.MergeSameItem(progress.Actionable).First();
        first.Id.Should().Be("ah");
        first.Kind.Should().Be(StepKind.Auction);
    }

    /// <summary>An unrelated chest only gets the hint, no highlight.</summary>
    [Test]
    public void UnrelatedChest_NoHighlight_OnlyHint()
    {
        WithStep();
        var data = Screen("Chest", (0, "§fStone", "STONE"));
        modifier.Apply(data);
        Highlighted(data).Should().BeNull();
        Panel(data).Should().Contain("Open the bazaar page");
        source.Verify(s => s.MarkOrdered(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>Without goals nothing is emitted.</summary>
    [Test]
    public void NoGoals_EmitsNothing()
    {
        source.Setup(s => s.GetGuideContext(It.IsAny<string>())).Returns((GoalGuideContext)null);
        var data = Screen("How many do you want?", (16, "§aCustom Amount", null));
        modifier.Apply(data);
        data.mods.Count.Should().Be(54);
        Highlighted(data).Should().BeNull();
    }

    /// <summary>A failing source never throws out of Apply.</summary>
    [Test]
    public void SourceThrows_DoesNotThrow()
    {
        source.Setup(s => s.GetGuideContext(It.IsAny<string>())).Throws<System.Exception>();
        var act = () => modifier.Apply(Screen("Chest"));
        act.Should().NotThrow();
    }

    /// <summary>The catch-all pattern matches next to the screen specific modifiers.</summary>
    [Test]
    public void CatchAllPattern_MatchesEveryTitle()
    {
        ChestNamePatterns.AnyChest.IsMatch("How many do you want?").Should().BeTrue();
        ChestNamePatterns.AnyChest.IsMatch("Crafting").Should().BeTrue();
    }

    /// <summary>Without a booster cookie the sell hint says where to go and has no /ah or /bz command.</summary>
    [Test]
    public void Sell_NoCookie_NoCommand()
    {
        WithStep(StepKind.Sell, 1, cookie: false);
        var data = Screen("SkyBlock Menu");
        modifier.Apply(data);
        var line = Panel(data);
        line.Should().Contain("List Enchanted Diamond").And.Contain("at the Auction Master");
        line.ToLowerInvariant().Should().NotContain("onclick");
    }

    /// <summary>Bazaar items are sold at the bazaar without a cookie.</summary>
    [Test]
    public void Sell_NoCookie_BazaarItem_SaysBazaar()
    {
        WithStep(StepKind.Sell, 1, cookie: false);
        var data = Screen("SkyBlock Menu");
        data.bazaarPrices = new Dictionary<string, ItemPrice> { [Tag] = new ItemPrice() }.ToImmutableDictionary();
        modifier.Apply(data);
        Panel(data).Should().Contain("at the Bazaar");
        Panel(data).ToLowerInvariant().Should().NotContain("onclick");
    }

    /// <summary>With a cookie the sell hint still opens /ah.</summary>
    [Test]
    public void Sell_WithCookie_OpensAh()
    {
        WithStep(StepKind.Sell, 1, cookie: true);
        var data = Screen("SkyBlock Menu");
        modifier.Apply(data);
        Panel(data).Should().Contain("/ah");
        Panel(data).ToLowerInvariant().Should().Contain("onclick");
    }

    /// <summary>The auction hint without offer uuid has no /ah fallback without a cookie.</summary>
    [Test]
    public void Auction_NoCookie_NoUuid_NoAhCommand()
    {
        WithStep(StepKind.Auction, 1, cookie: false, offer: new AuctionOffer("70e3559483ce4da8b", 2_500_000));
        var data = Screen("SkyBlock Menu");
        modifier.Apply(data);
        Panel(data).Should().Contain("at the Auction Master");
        Panel(data).ToLowerInvariant().Should().NotContain("onclick");
    }
}
