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
    internal static (GoalService, Mock<IModUserPublisher>, Mock<ISettingsApi>, Mock<ICraftsApi>, Mock<IForgeApi>, GoalList) Create(CookieState cookie = CookieState.Unknown)
    {
        var publisher = new Mock<IModUserPublisher>();
        var settingsApi = new Mock<ISettingsApi>();
        settingsApi.Setup(s => s.GetSettingWithHttpInfoAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(HttpStatusCode.NoContent, null));
        var settings = new SettingsService(new Mock<IConfiguration>().Object, Mock.Of<ILogger<SettingsService>>(), settingsApi.Object);
        var cookies = new Mock<IBoosterCookieService>();
        cookies.Setup(c => c.Get(It.IsAny<string>())).ReturnsAsync(cookie);
        var crafts = new Mock<ICraftsApi>();
        var forge = new Mock<IForgeApi>();
        var list = new GoalList();
        var service = new GoalService(settings, publisher.Object, cookies.Object, Mock.Of<IPlayerStateApi>(),
            new GoalPlanner(crafts.Object, forge.Object), new ServiceCollection().BuildServiceProvider(),
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

    private static Goal TwoSameItemGoal() => new()
    {
        Title = "g",
        RootItemTag = "R",
        Quantity = 1,
        Root = new GoalStep
        {
            Kind = StepKind.Craft,
            ItemTag = "R",
            Quantity = 1,
            Children =
            {
                new GoalStep { Id = "s1", Kind = StepKind.BuyOrder, ItemTag = "X", Quantity = 15 },
                new GoalStep { Id = "s2", Kind = StepKind.BuyOrder, ItemTag = "X", Quantity = 17 },
            }
        }
    };

    /// <summary>Two actionable steps of one item show as one panel row with the summed amount.</summary>
    [Test]
    public async Task GetPanel_MergesSameItem()
    {
        var goal = TwoSameItemGoal();
        list.Goals.Add(goal);
        list.FocusId = goal.Id;
        await service.OnInventory("u1", "Steve", Inventory(46));

        var panel = service.GetPanel("Steve");

        panel.Entries.Should().ContainSingle();
        panel.Entries[0].Amount.Should().Be(32);
        panel.Entries[0].Command.Should().StartWith("/cofl bzorder search 32 ");
    }

    /// <summary>The guide context carries the merged quantity and the id of a real step.</summary>
    [Test]
    public async Task GetGuideContext_UsesMergedQuantity()
    {
        var goal = TwoSameItemGoal();
        list.Goals.Add(goal);
        list.FocusId = goal.Id;
        await service.OnInventory("u1", "Steve", Inventory(46));

        var ctx = service.GetGuideContext("Steve");

        ctx.Quantity.Should().Be(32);
        ctx.Step.Id.Should().Be("s1");
    }

    /// <summary>Ordering the merged line marks every merged step, but not steps that are not actionable.</summary>
    [Test]
    public async Task MarkOrdered_MarksMergedStepsOnly()
    {
        var goal = TwoSameItemGoal();
        goal.Root.Children.Add(new GoalStep { Id = "s3", Kind = StepKind.BuyOrder, ItemTag = "X", Quantity = 4, DoneAt = DateTime.UtcNow });
        list.Goals.Add(goal);
        list.FocusId = goal.Id;
        await service.OnInventory("u1", "Steve", Inventory(46));
        settingsApi.Invocations.Clear();

        await service.MarkOrdered("Steve", "s1");

        goal.Root.Children[0].OrderedAt.Should().NotBeNull();
        goal.Root.Children[1].OrderedAt.Should().NotBeNull();
        goal.Root.Children[2].OrderedAt.Should().BeNull();
        settingsApi.Verify(s => s.UpdateSettingAsync("u1", "goals", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>An unchanged hud is pushed again after the refresh interval, without a new expectation.</summary>
    [Test]
    public async Task OnInventory_RepushesUnchangedHudAfterInterval()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        service.Now = () => now;
        var goal = BuyGoal("X", 7);
        list.Goals.Add(goal);
        list.FocusId = goal.Id;
        var items = Inventory(46);

        await service.OnInventory("u1", "Steve", items);
        now = now.AddSeconds(10);
        await service.OnInventory("u1", "Steve", items);
        publisher.Verify(p => p.DisplayForUser("u1", It.IsAny<object>()), Times.Once);

        now = now.AddSeconds(GoalService.HudRefreshInterval.TotalSeconds + 1);
        await service.OnInventory("u1", "Steve", items);

        publisher.Verify(p => p.DisplayForUser("u1", It.IsAny<object>()), Times.Exactly(2));
        publisher.Verify(p => p.ExpectForUser("u1", It.IsAny<string>(), It.IsAny<int>()), Times.Once);
    }

    private const string FullUuid = "70e3559483ce4da8b1c2d3e4f5a6b7c8";

    private static Goal Necron() => new()
    {
        Title = "Necron's Leggings",
        RootItemTag = "NECRON_LEGGINGS",
        Quantity = 1,
        Root = new GoalStep
        {
            Id = "root", Kind = StepKind.Craft, ItemTag = "NECRON_LEGGINGS", Quantity = 1,
            Children =
            {
                new GoalStep { Id = "bz", Kind = StepKind.InstaBuy, ItemTag = "HANDLE", Quantity = 8 },
                new GoalStep { Id = "ah", Kind = StepKind.Auction, ItemTag = "WITHER_LEGGINGS", Quantity = 1, UnitPrice = 2_500_000 },
            }
        }
    };

    private InfoDisplayDto LastHud() => (InfoDisplayDto)publisher.Invocations.Last(i => i.Method.Name == nameof(IModUserPublisher.DisplayForUser)).Arguments[1];

    /// <summary>The auction line is first, links the full uuid from the lowest bin source and states the lost profit.</summary>
    [Test]
    public async Task Hud_AuctionFirstWithFullUuidFromLowestBinSource()
    {
        service.LowestBinSource = tag => Task.FromResult(tag == "WITHER_LEGGINGS" ? new AuctionOffer(FullUuid, 2_900_000) : null);
        list.Goals.Add(Necron());
        list.FocusId = list.Goals[0].Id;

        await service.OnInventory("u1", "Steve", Inventory(46));

        var hud = LastHud();
        hud.Lines[0].OnClick.Should().MatchRegex("^/viewauction [0-9a-f]{32}$");
        hud.Lines[0].Text.Should().Contain("(auction)").And.EndWith("§c-400k profit");
        hud.Lines[1].Text.Should().StartWith("§7then Buy 8x");
        publisher.Verify(p => p.ExpectForUser("u1", "/viewauction " + FullUuid, It.IsAny<int>()), Times.Once);
    }

    /// <summary>A short (17 character) sniper id is not a valid auction uuid, the line opens the auction house instead.</summary>
    [Test]
    public async Task Hud_ShortAuctionId_FallsBackToAh()
    {
        (service, publisher, settingsApi, craftsApi, forgeApi, list) = Create(CookieState.Active);
        service.LowestBinSource = _ => Task.FromResult(new AuctionOffer("70e3559483ce4da8b", 2_500_000));
        list.Goals.Add(Necron());
        list.FocusId = list.Goals[0].Id;

        await service.OnInventory("u1", "Steve", Inventory(46));

        LastHud().Lines[0].OnClick.Should().Be("/ah");
    }

    /// <summary>Offers are cached for 20 seconds and a changed cheapest auction changes the pushed hud.</summary>
    [Test]
    public async Task Hud_OfferCachedFor20Seconds_ThenRefreshed()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        service.Now = () => now;
        var price = 2_500_000L;
        var lookups = 0;
        service.LowestBinSource = _ => { lookups++; return Task.FromResult(new AuctionOffer(FullUuid, price)); };
        list.Goals.Add(Necron());
        list.FocusId = list.Goals[0].Id;
        var items = Inventory(46);

        await service.OnInventory("u1", "Steve", items);
        price = 3_000_000;
        now = now.AddSeconds(19);
        await service.OnInventory("u1", "Steve", items);
        lookups.Should().Be(1);
        publisher.Verify(p => p.DisplayForUser("u1", It.IsAny<object>()), Times.Once);

        now = now.AddSeconds(2);
        await service.OnInventory("u1", "Steve", items);

        lookups.Should().Be(2);
        publisher.Verify(p => p.DisplayForUser("u1", It.IsAny<object>()), Times.Exactly(2));
        LastHud().Lines[0].Text.Should().Contain("3m");
    }

    /// <summary>Without a bin the line keeps the plain auction text.</summary>
    [Test]
    public async Task Hud_NoOffer_PlainAuctionLine()
    {
        (service, publisher, settingsApi, craftsApi, forgeApi, list) = Create(CookieState.Active);
        service.LowestBinSource = _ => Task.FromResult<AuctionOffer>(null);
        list.Goals.Add(Necron());
        list.FocusId = list.Goals[0].Id;

        await service.OnInventory("u1", "Steve", Inventory(46));

        LastHud().Lines[0].OnClick.Should().Be("/ah");
        LastHud().Lines[0].Text.Should().EndWith("(auction)");
    }

    /// <summary>Regression: without a booster cookie /ah is rejected by Hypixel, the line gets no click and no expect push.</summary>
    [Test]
    public async Task Hud_NoCookie_AuctionWithoutUuid_HasNoAhCommand()
    {
        service.LowestBinSource = _ => Task.FromResult(new AuctionOffer("70e3559483ce4da8b", 2_500_000));
        list.Goals.Add(Necron());
        list.FocusId = list.Goals[0].Id;

        await service.OnInventory("u1", "Steve", Inventory(46));

        LastHud().Lines[0].OnClick.Should().BeNull();
        LastHud().Lines[0].Text.Should().EndWith("at the Auction Master");
        publisher.Verify(p => p.ExpectForUser("u1", "/ah", It.IsAny<int>()), Times.Never);
    }

    /// <summary>Without a cookie a plain auction line says where to go instead of running /ah.</summary>
    [Test]
    public async Task Hud_NoCookie_NoOffer_AuctionLineHasNoCommand()
    {
        service.LowestBinSource = _ => Task.FromResult<AuctionOffer>(null);
        list.Goals.Add(Necron());
        list.FocusId = list.Goals[0].Id;

        await service.OnInventory("u1", "Steve", Inventory(46));

        LastHud().Lines[0].OnClick.Should().BeNull();
        LastHud().Lines[0].Hover.Should().Contain("Auction Master");
        publisher.Verify(p => p.ExpectForUser("u1", "/ah", It.IsAny<int>()), Times.Never);
    }

    /// <summary>The guide reads the cached offer and never looks it up itself.</summary>
    [Test]
    public async Task GetGuideContext_AuctionStepFirstWithCachedOffer()
    {
        var lookups = 0;
        service.LowestBinSource = _ => { lookups++; return Task.FromResult(new AuctionOffer(FullUuid, 2_500_000)); };
        list.Goals.Add(Necron());
        list.FocusId = list.Goals[0].Id;
        await service.OnInventory("u1", "Steve", Inventory(46));
        lookups = 0;

        var ctx = service.GetGuideContext("Steve");

        ctx.Kind.Should().Be(StepKind.Auction);
        ctx.Offer.Should().Be(new AuctionOffer(FullUuid, 2_500_000));
        lookups.Should().Be(0);
    }

    /// <summary>The panel names the pending auction item so the bazaar rows are not bought first.</summary>
    [Test]
    public async Task GetPanel_NamesPendingAuction()
    {
        service.LowestBinSource = _ => Task.FromResult<AuctionOffer>(null);
        list.Goals.Add(Necron());
        list.FocusId = list.Goals[0].Id;
        await service.OnInventory("u1", "Steve", Inventory(46));

        var panel = service.GetPanel("Steve");

        panel.FirstAuctionName.Should().NotBeNullOrEmpty();
        panel.Entries.Should().ContainSingle().Which.Amount.Should().Be(8);
    }

    /// <summary>Without a pending auction the panel has no hint.</summary>
    [Test]
    public async Task GetPanel_NoAuction_NoHint()
    {
        var goal = BuyGoal("X", 7);
        list.Goals.Add(goal);
        list.FocusId = goal.Id;
        await service.OnInventory("u1", "Steve", Inventory(46));

        service.GetPanel("Steve").FirstAuctionName.Should().BeNull();
    }

    private const string Legs = "NECRON_LEGGINGS";

    private Goal SellGoal() => new()
    {
        Title = "Legs",
        RootItemTag = Legs,
        Quantity = 1,
        Root = new GoalStep
        {
            Id = "sell", Kind = StepKind.Sell, ItemTag = Legs, Quantity = 1,
            Children = { new GoalStep { Id = "craft", Kind = StepKind.Craft, ItemTag = Legs, Quantity = 1 } }
        }
    };

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(20);
    }

    private int DisplayPushes => publisher.Invocations.Count(i => i.Method.Name == nameof(IModUserPublisher.DisplayForUser));

    /// <summary>A list change made outside this pod (chat completed the Sell step) is shown on the hud once.</summary>
    [Test]
    public async Task ListChangedOutside_PushesCompleteLineOnce()
    {
        GoalListHolder holder = null;
        service.HolderFactory = _ => Task.FromResult(holder = new GoalListHolder(() => list));
        var goal = SellGoal();
        list.Goals.Add(goal);
        list.FocusId = goal.Id;
        await service.OnInventory("u1", "Steve", Inventory(46, (10, Legs, 1)));
        goal.Root.DoneAt.Should().BeNull();
        var before = DisplayPushes;

        goal.Root.DoneAt = DateTime.UtcNow;
        holder.RaiseChanged();
        await Eventually(() => DisplayPushes > before);
        holder.RaiseChanged();
        await Task.Delay(200);

        DisplayPushes.Should().Be(before + 1);
        LastHud().Lines.Select(l => l.Text).Should().Contain(t => t.Contains("Goal complete"));
    }

    /// <summary>A change that does not alter the hud pushes nothing.</summary>
    [Test]
    public async Task ListChangedOutside_SameHud_PushesNothing()
    {
        GoalListHolder holder = null;
        service.HolderFactory = _ => Task.FromResult(holder = new GoalListHolder(() => list));
        var goal = SellGoal();
        list.Goals.Add(goal);
        list.FocusId = goal.Id;
        await service.OnInventory("u1", "Steve", Inventory(46, (10, Legs, 1)));
        var before = DisplayPushes;

        holder.RaiseChanged();
        await Task.Delay(300);

        DisplayPushes.Should().Be(before);
    }

    /// <summary>A pod that never showed the hud of the user has no inventory and leaves the push to the pod that did.</summary>
    [Test]
    public async Task ListChangedOutside_HudNeverShownHere_PushesNothing()
    {
        GoalListHolder holder = null;
        service.HolderFactory = _ => Task.FromResult(holder = new GoalListHolder(() => list));
        var goal = SellGoal();
        list.Goals.Add(goal);
        list.FocusId = goal.Id;
        await service.EnsureLoaded("u1", "Steve");
        var before = DisplayPushes;

        goal.Root.DoneAt = DateTime.UtcNow;
        holder.RaiseChanged();
        await Task.Delay(300);

        DisplayPushes.Should().Be(before);
    }

    /// <summary>A failing refresh in the change callback is logged and never thrown.</summary>
    [Test]
    public async Task ListChangedOutside_FailingRefresh_DoesNotThrow()
    {
        GoalListHolder holder = null;
        service.HolderFactory = _ => Task.FromResult(holder = new GoalListHolder(() => list));
        var goal = SellGoal();
        list.Goals.Add(goal);
        list.FocusId = goal.Id;
        await service.OnInventory("u1", "Steve", Inventory(46, (10, Legs, 1)));
        publisher.Setup(p => p.DisplayForUser(It.IsAny<string>(), It.IsAny<object>())).ThrowsAsync(new InvalidOperationException("down"));

        goal.Root.DoneAt = DateTime.UtcNow;
        Assert.DoesNotThrow(() => holder.RaiseChanged());
        await Task.Delay(300);
    }

    private async Task<(GoalListHolder holder, Goal goal)> CompletedSellGoal()
    {
        GoalListHolder holder = null;
        service.HolderFactory = _ => Task.FromResult(holder = new GoalListHolder(() => list));
        var goal = SellGoal();
        list.Goals.Add(goal);
        list.FocusId = goal.Id;
        await service.OnInventory("u1", "Steve", Inventory(46, (10, Legs, 1)));
        goal.Root.DoneAt = DateTime.UtcNow;
        var before = DisplayPushes;
        holder.RaiseChanged();
        await Eventually(() => DisplayPushes > before);
        LastHud().Lines.Select(l => l.Text).Should().Contain(t => t.Contains("Goal complete"));
        return (holder, goal);
    }

    /// <summary>The click lines of a completed goal never trigger a chat hint push.</summary>
    [Test]
    public async Task CompletedGoal_DoesNotPushExpect()
    {
        await CompletedSellGoal();

        publisher.Verify(p => p.ExpectForUser(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    /// <summary>Removing the last goal outside clears the hud once.</summary>
    [Test]
    public async Task OutsideRemoveOfLastGoal_ClearsHudOnce()
    {
        var (holder, goal) = await CompletedSellGoal();
        var before = DisplayPushes;

        list.Goals.Remove(goal);
        list.FocusId = null;
        holder.RaiseChanged();
        await Eventually(() => DisplayPushes > before);
        holder.RaiseChanged();
        await Task.Delay(200);

        DisplayPushes.Should().Be(before + 1);
        LastHud().Clear.Should().BeTrue();
    }

    /// <summary>Removing the focused goal outside shows the remaining goal.</summary>
    [Test]
    public async Task OutsideRemoveOfFocusedGoal_ShowsRemainingGoal()
    {
        var (holder, goal) = await CompletedSellGoal();
        var other = BuyGoal("OTHER", 2);
        other.Title = "Other goal";
        list.Goals.Add(other);
        var before = DisplayPushes;

        list.Goals.Remove(goal);
        // the focus still points to the removed goal
        holder.RaiseChanged();
        await Eventually(() => DisplayPushes > before);

        LastHud().Title.Should().Contain("Other goal");
        LastHud().Clear.Should().BeFalse();
    }

    /// <summary>Resetting a completed goal outside shows its first step again.</summary>
    [Test]
    public async Task OutsideResetOfCompletedGoal_ShowsStepAgain()
    {
        var (holder, goal) = await CompletedSellGoal();
        var before = DisplayPushes;

        GoalProgressEvaluator.Reset(goal);
        holder.RaiseChanged();
        await Eventually(() => DisplayPushes > before);

        var texts = LastHud().Lines.Select(l => l.Text).ToList();
        texts.Should().NotContain(t => t.Contains("Goal complete"));
        texts.Should().NotBeEmpty();
        // the crafted item is still in the last known inventory, so the sell step is next (no cookie: no command)
        texts[0].Should().Contain("Sell");
    }
}
