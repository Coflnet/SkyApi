using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.Shared.Goals;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.Settings.Client.Api;
using Coflnet.Sky.Sniper.Client.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services.Mod.Tests;

/// <summary>Tests for <see cref="GoalService"/>.</summary>
[TestFixture]
public class GoalServiceTests
{
    private Mock<IModUserPublisher> publisher;
    private Mock<ISettingsApi> settingsApi;
    private Mock<ICraftsApi> craftsApi;
    private Mock<IForgeApi> forgeApi;
    private GoalList list;
    private GoalService service;

    /// <summary>Builds a service with an in-memory goal list.</summary>
    [SetUp]
    public void Setup()
    {
        (service, publisher, settingsApi, craftsApi, forgeApi, list) = Create();
    }

    /// <summary>Creates a service wired to mocks, shared with the controller tests.</summary>
    internal static (GoalService, Mock<IModUserPublisher>, Mock<ISettingsApi>, Mock<ICraftsApi>, Mock<IForgeApi>, GoalList) Create()
    {
        var publisher = new Mock<IModUserPublisher>();
        var settingsApi = new Mock<ISettingsApi>();
        settingsApi.Setup(s => s.GetSettingWithHttpInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(HttpStatusCode.NoContent, null));
        var settings = new SettingsService(new Mock<IConfiguration>().Object, Mock.Of<ILogger<SettingsService>>(), settingsApi.Object);
        var cookies = new Mock<IBoosterCookieService>();
        cookies.Setup(c => c.Get(It.IsAny<string>())).ReturnsAsync(CookieState.Unknown);
        var crafts = new Mock<ICraftsApi>();
        var forge = new Mock<IForgeApi>();
        var list = new GoalList();
        var service = new GoalService(settings, publisher.Object, cookies.Object, Mock.Of<IPlayerStateApi>(),
            Mock.Of<IAuctionApi>(), new GoalPlanner(crafts.Object, forge.Object), new ServiceCollection().BuildServiceProvider(),
            Mock.Of<ILogger<GoalService>>())
        {
            HolderFactory = _ => Task.FromResult(new GoalListHolder(() => list)),
            BazaarTagSource = () => new HashSet<string> { "BZ" }
        };
        return (service, publisher, settingsApi, crafts, forge, list);
    }

    private static Item I(string tag, int count) => new() { Tag = tag, Count = (byte)count };

    private static List<Item> Inventory(int size, params (int index, string tag, int count)[] items)
    {
        var result = Enumerable.Range(0, size).Select(_ => new Item()).ToList();
        foreach (var (index, tag, count) in items)
            result[index] = I(tag, count);
        return result;
    }

    private static Goal BuyGoal(string tag = "X", long qty = 2) => new()
    {
        Title = "g",
        RootItemTag = tag,
        Quantity = qty,
        Root = new GoalStep { Kind = StepKind.BuyOrder, ItemTag = tag, Quantity = qty }
    };

    /// <summary>A 46 slot screen only counts the player slots 9..44.</summary>
    [Test]
    public void ExtractCounts_SmallInventory_UsesSlots9To44()
    {
        var items = Inventory(46, (5, "A", 1), (9, "B", 2), (44, "B", 3), (45, "C", 4), (10, "SKYBLOCK_MENU", 1));
        GoalService.ExtractCounts(items).Should().BeEquivalentTo(new Dictionary<string, long> { ["B"] = 5 });
    }

    /// <summary>A container screen only counts the last 36 slots.</summary>
    [Test]
    public void ExtractCounts_Container_UsesLast36()
    {
        var items = Inventory(90, (10, "A", 1), (53, "A", 9), (54, "B", 2), (89, "B", 3));
        GoalService.ExtractCounts(items).Should().BeEquivalentTo(new Dictionary<string, long> { ["B"] = 5 });
    }

    /// <summary>DoneAt is stored once and an unchanged inventory is not pushed again.</summary>
    [Test]
    public async Task OnInventory_PersistsDoneOnce_PushesOnce()
    {
        list.Goals.Add(BuyGoal());
        list.FocusId = list.Goals[0].Id;
        var items = Inventory(46, (10, "X", 2));

        await service.OnInventory("u1", "Steve", items);
        await service.OnInventory("u1", "Steve", items);

        list.Goals[0].Root.DoneAt.Should().NotBeNull();
        settingsApi.Verify(s => s.UpdateSettingAsync("u1", "goals", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        publisher.Verify(p => p.DisplayForUser("u1", It.IsAny<object>()), Times.Once);
    }

    /// <summary>Without goals nothing is evaluated or pushed.</summary>
    [Test]
    public async Task OnInventory_NoGoals_DoesNothing()
    {
        await service.OnInventory("u1", "Steve", Inventory(46, (10, "X", 2)));
        publisher.Verify(p => p.DisplayForUser(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    /// <summary>Open buy orders mark the matching todo buy step as ordered.</summary>
    [Test]
    public async Task OnBazaarOrders_SetsOrderedAt()
    {
        var goal = BuyGoal("X", 5);
        list.Goals.Add(goal);
        await service.OnBazaarOrders("u1", new[] { ("X", true, 5L), ("Y", false, 1L) });
        goal.Root.OrderedAt.Should().NotBeNull();
        settingsApi.Verify(s => s.UpdateSettingAsync("u1", "goals", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Sell orders do not count as ordered.</summary>
    [Test]
    public async Task OnBazaarOrders_SellOrder_Ignored()
    {
        var goal = BuyGoal("X", 5);
        list.Goals.Add(goal);
        await service.OnBazaarOrders("u1", new[] { ("X", false, 5L) });
        goal.Root.OrderedAt.Should().BeNull();
    }

    /// <summary>Removing the last goal clears the hud.</summary>
    [Test]
    public async Task Remove_LastGoal_PushesClear()
    {
        var goal = BuyGoal();
        list.Goals.Add(goal);
        list.FocusId = goal.Id;

        (await service.Remove("u1", goal.Id)).Should().BeTrue();

        list.Goals.Should().BeEmpty();
        publisher.Verify(p => p.DisplayForUser("u1", It.Is<InfoDisplayDto>(d => d.Clear && d.Id == 3)), Times.Once);
    }

    /// <summary>Unknown ids are reported.</summary>
    [Test]
    public async Task Remove_Unknown_ReturnsFalse()
    {
        (await service.Remove("u1", "nope")).Should().BeFalse();
    }

    /// <summary>The panel lists the actionable bazaar steps of the focus goal with the search command.</summary>
    [Test]
    public async Task GetPanel_ListsActionableBazaarSteps()
    {
        var goal = BuyGoal("X", 7);
        list.Goals.Add(goal);
        list.FocusId = goal.Id;
        await service.OnInventory("u1", "Steve", Inventory(46));

        var panel = service.GetPanel("Steve");

        panel.Entries.Should().ContainSingle();
        panel.Entries[0].Amount.Should().Be(7);
        panel.Entries[0].Command.Should().StartWith("/cofl bzorder search 7 ");
    }
}
