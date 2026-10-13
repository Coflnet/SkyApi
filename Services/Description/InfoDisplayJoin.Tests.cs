using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Api.Services.Mod;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Core;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Description.Tests;

/// <summary>Tests for <see cref="InfoDisplayJoin"/> and its use for the goal guide.</summary>
[TestFixture]
public class InfoDisplayJoinTests
{
    private const int Slots = 3;

    private static List<List<DescModification>> Slotted()
        => Enumerable.Range(0, Slots).Select(i => new List<DescModification> { new($"slot{i}") }).ToList();

    private static List<DescModification> Panel() => new()
    {
        new("recommend 1"),
        new("recommend 2"),
        new(DescModification.ModType.SUGGEST, 0, "starting bid: 1m"),
    };

    private static List<DescModification> Goal() => new() { new("Step: sell") };

    /// <summary>The goal lines come first, then a separator, then the whole previous panel including the SUGGEST entry.</summary>
    [Test]
    public void JoinsGoalLinesBeforePreviousPanel()
    {
        var mods = Slotted();
        var panel = Panel();
        mods.Add(panel);
        var from = mods.Count;
        mods.Add(Goal());

        InfoDisplayJoin.Apply(mods, Slots, from);

        mods.Should().HaveCount(Slots + 1);
        mods.Take(Slots).Select(l => l.Single().Value).Should().Equal("slot0", "slot1", "slot2");
        var last = mods.Last();
        last.Select(m => m.Value).Should().Equal("Step: sell", "", "recommend 1", "recommend 2", "starting bid: 1m");
        last.Last().Type.Should().Be(DescModification.ModType.SUGGEST);
        last.Skip(2).Should().Equal(panel);
    }

    /// <summary>Of several non-goal panels only the last one is absorbed, earlier ones stay untouched.</summary>
    [Test]
    public void OnlyLastPreviousPanelIsAbsorbed()
    {
        var mods = Slotted();
        var first = new List<DescModification> { new("first panel") };
        mods.Add(first);
        mods.Add(Panel());
        var from = mods.Count;
        mods.Add(Goal());

        InfoDisplayJoin.Apply(mods, Slots, from);

        mods.Should().HaveCount(Slots + 2);
        mods[Slots].Should().BeSameAs(first);
        mods.Last().Select(m => m.Value).Should().StartWith(new[] { "Step: sell", "" }).And.Contain("recommend 1");
    }

    /// <summary>The re-enable placeholder goes behind the panel so a disabled guide does not push it down.</summary>
    [Test]
    public void PlaceholderIsAppendedAfterPanel()
    {
        var mods = Slotted();
        mods.Add(Panel());
        var from = mods.Count;
        InfoDisplayDisable.AddReenablePlaceholder(new DataContainer { mods = mods }, "GoalGuideModifier");

        InfoDisplayJoin.Apply(mods, Slots, from, appendAfterPanel: true);

        mods.Should().HaveCount(Slots + 1);
        var last = mods.Last();
        last.Should().HaveCount(4);
        last.Take(3).Select(m => m.Value).Should().Equal("recommend 1", "recommend 2", "starting bid: 1m");
        last[3].Value.Should().Contain("loreDisableInfoIn rm GoalGuideModifier");
    }

    /// <summary>Without an extra list before the goal, the goal list stays alone.</summary>
    [Test]
    public void NoPreviousExtraList_Unchanged()
    {
        var mods = Slotted();
        var from = mods.Count;
        var goal = Goal();
        mods.Add(goal);

        InfoDisplayJoin.Apply(mods, Slots, from);

        mods.Should().HaveCount(Slots + 1);
        mods.Last().Should().BeSameAs(goal);
    }

    /// <summary>When the goal added nothing the previous panel stays as it is.</summary>
    [Test]
    public void GoalAddedNothing_Unchanged()
    {
        var mods = Slotted();
        var panel = Panel();
        mods.Add(panel);

        InfoDisplayJoin.Apply(mods, Slots, mods.Count);

        mods.Should().HaveCount(Slots + 1);
        mods.Last().Should().BeSameAs(panel);
    }

    private static (DataContainer data, Mock<IGoalGuideSource> source) CreateBinScreen(bool disabled = false)
    {
        var items = Enumerable.Range(0, 54).Select(_ => new Item { ItemName = "x", Description = "" }).ToList();
        var settings = new DescriptionSetting();
        if (disabled)
            settings.DisableInfoIn = new HashSet<string> { nameof(GoalGuideModifier) };
        var data = new DataContainer
        {
            inventory = new InventoryDataWithSettings { ChestName = "Create BIN Auction", Version = 4, Settings = settings },
            Items = items,
            auctionRepresent = items.Select(i => (new SaveAuction(), new string[0])).ToList(),
            mods = Enumerable.Range(0, 54).Select(_ => new List<DescModification>()).ToList(),
            mcName = "Ekwav"
        };
        var step = new GoalStep { Id = "s1", Kind = StepKind.Sell, ItemTag = "NECRON_LEGGINGS", Quantity = 1 };
        var source = new Mock<IGoalGuideSource>();
        source.Setup(s => s.GetGuideContext("Ekwav")).Returns(new GoalGuideContext(step, StepKind.Sell, "Necron's Leggings", "Necron's Leggings", 1, true));
        return (data, source);
    }

    /// <summary>
    /// Regression: on the Create BIN Auction screen the goal Step line replaced the price recommendation
    /// and its sign suggestion. Runs the real modifier step of <see cref="ModDescriptionService"/>
    /// (the modifier loop itself needs the whole service, this is the step it executes per modifier).
    /// </summary>
    [Test]
    public void CreateBinAuction_GoalStepKeepsRecommendationAndSuggestion()
    {
        var (data, source) = CreateBinScreen();
        data.mods.Add(Panel());

        ModDescriptionService.ApplyModifier(data, new GoalGuideModifier(() => source.Object), 54);

        data.mods.Should().HaveCount(55);
        var last = data.mods.Last();
        last.First().Value.Should().Contain("Step: ");
        last.First().Value.Should().Contain("loreDisableInfoIn GoalGuideModifier");
        last.Select(m => m.Value).Should().Contain(new[] { "recommend 1", "recommend 2" });
        last.Should().Contain(m => m.Type == DescModification.ModType.SUGGEST && m.Value == "starting bid: 1m");
    }

    /// <summary>Regression: the re-enable placeholder of a disabled goal guide must not hide the panel either.</summary>
    [Test]
    public void CreateBinAuction_DisabledGoalGuideKeepsPanelAndPlaceholder()
    {
        var (data, source) = CreateBinScreen(disabled: true);
        data.mods.Add(Panel());

        ModDescriptionService.ApplyModifier(data, new GoalGuideModifier(() => source.Object), 54);

        data.mods.Should().HaveCount(55);
        var last = data.mods.Last();
        last.Take(3).Select(m => m.Value).Should().Equal("recommend 1", "recommend 2", "starting bid: 1m");
        last[3].Value.Should().Contain("loreDisableInfoIn rm GoalGuideModifier");
    }

    /// <summary>Modifiers that do not opt in keep adding their own list.</summary>
    [Test]
    public void OtherModifiers_StillAddOwnList()
    {
        var (data, _) = CreateBinScreen();
        data.mods.Add(Panel());
        var modifier = new Mock<ICustomModifier>();
        modifier.SetupGet(m => m.DisableInfoName).Returns((string)null);
        modifier.Setup(m => m.Apply(data)).Callback(() => data.mods.Add(new() { new("other") }));

        ModDescriptionService.ApplyModifier(data, modifier.Object, 54);

        data.mods.Should().HaveCount(56);
    }
}
