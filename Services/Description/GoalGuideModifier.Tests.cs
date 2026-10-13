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

    private void WithStep(StepKind kind = StepKind.BuyOrder, long qty = 64, bool cookie = true)
    {
        var step = new GoalStep { Id = "s1", Kind = kind, ItemTag = Tag, Quantity = qty };
        source.Setup(s => s.GetGuideContext("Ekwav")).Returns(new GoalGuideContext(step, kind, "Enchanted Diamond", "Enchanted Diamond", qty, cookie));
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
}
