using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Api.Services.Tasks;
using Coflnet.Sky.Api.Services.Tasks.Setups;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Client;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using SkyApi.Services.Tasks.Setups.Measured;
using static SkyApi.Services.Tasks.Setups.Measured.MeasuredTestData;

namespace SkyApi.Services.Tasks.Setups;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class TaskSetupsServiceMeasuredTests
{
    private Mock<IPublicTaskService> tasks;
    private Mock<IGearSetupService> gear;
    private Mock<IItemResaleService> resale;
    private Mock<IGearModelService> models;

    [SetUp]
    public void Setup()
    {
        tasks = new();
        tasks.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PublicTaskEstimate> { new() { Name = "Task A", Slug = "task-a" } });
        gear = new();
        gear.Setup(g => g.GetByTaskNameAsync("Task A", It.IsAny<CancellationToken>())).ReturnsAsync(ListEstimates());
        resale = new();
        resale.Setup(r => r.GetQuoteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tag, IReadOnlyDictionary<string, string> _, CancellationToken _) => Quote(tag, 1000));
        models = new();
    }

    /// <summary>The estimates of the gear setups list, profile name "From list" tells them apart</summary>
    private static TaskGearSetups ListEstimates()
    {
        var estimates = PublicSetupsMapperTests.Gear(
            PublicSetupsMapperTests.Setup("baseline", "baseline", 0),
            PublicSetupsMapperTests.Setup("budget", "upgrade", 10_000, PublicSetupsMapperTests.Item("X")),
            PublicSetupsMapperTests.Setup("mid", "upgrade", 2_000_000, PublicSetupsMapperTests.Item("Y")));
        estimates.ProfileName = "From list";
        return estimates;
    }

    private static TaskGearSetups ModelEstimates()
    {
        var estimates = ListEstimates();
        estimates.ProfileName = "From model";
        return estimates;
    }

    /// <summary>The four tier model: measured budget, mid and endgame</summary>
    private static GearModelLookup Measured(TaskGearSetups estimates) => new(FourTierModel.Model(estimates), false);

    /// <summary>A model with nothing measured yet, only the estimates</summary>
    private static TaskGearModel EstimatesOnly(TaskGearSetups estimates)
        => MeasuredTestData.Model(estimates, null, []);

    private void Lookup(GearModelLookup lookup)
        => models.Setup(c => c.GetAsync("Task A", It.IsAny<CancellationToken>())).ReturnsAsync(lookup);

    private TaskSetupsService Create(IMemoryCache cache = null, TimeProvider time = null)
        => Service(tasks.Object, gear.Object, resale.Object, cache, models.Object, FourTierModel.Acquisition().Object, time);

    private static (Mock<IMemoryCache>, List<(object Key, Mock<ICacheEntry> Entry)>) CapturingCache()
    {
        var entries = new List<(object, Mock<ICacheEntry>)>();
        var cache = new Mock<IMemoryCache>();
        cache.Setup(c => c.CreateEntry(It.IsAny<object>())).Returns((object key) =>
        {
            var entry = new Mock<ICacheEntry>();
            entry.SetupAllProperties();
            entries.Add((key, entry));
            return entry.Object;
        });
        return (cache, entries);
    }

    [Test]
    public async Task Failed_model_falls_back_to_the_estimates_list_with_a_short_cache()
    {
        Lookup(GearModelLookup.Unavailable);
        var (cache, entries) = CapturingCache();

        var view = await Create(cache.Object).GetPublicAsync("task-a");

        view.Degraded.Should().BeTrue();
        view.Setups.ProfileName.Should().Be("From list");
        view.Setups.Setups.Should().OnlyContain(s => s.Basis == "estimate");
        entries.Single(e => Equals(e.Key, TaskSetupsService.PublicKey("task-a"))).Entry.Object.AbsoluteExpirationRelativeToNow
            .Should().Be(TaskSetupsService.DegradedCacheDuration);
        entries.Single(e => Equals(e.Key, TaskSetupsService.ModelKey("task-a"))).Entry.Object.AbsoluteExpirationRelativeToNow!.Value
            .Should().BeCloseTo(TaskSetupsService.DegradedCacheDuration + TaskSetupsService.StaleModelDuration, TimeSpan.FromSeconds(1),
                "a stale model is served while it is rebuilt");
    }

    [Test]
    public async Task Unknown_task_upstream_uses_the_list_and_keeps_the_normal_cache()
    {
        Lookup(GearModelLookup.NotFound);

        var view = await Create().GetPublicAsync("task-a");

        view.Degraded.Should().BeFalse();
        view.Setups.ProfileName.Should().Be("From list");
    }

    [Test]
    public async Task Model_with_estimates_does_not_ask_the_list()
    {
        Lookup(Measured(ModelEstimates()));

        var view = await Create().GetPublicAsync("task-a");

        view.Degraded.Should().BeFalse();
        view.Setups.ProfileName.Should().Be("From model");
        view.Setups.Category.Should().Be("farming");
        view.Setups.MeasuredAt.Should().NotBeNull();
        view.Setups.ScoreFormula.Expression.Should().Be("1 × Farming Fortune");
        view.Setups.Baseline.Basis.Should().Be("estimate", "there is no measured baseline");
        view.Setups.Setups.Single(s => s.Tier == "budget").Basis.Should().Be("measured");
        view.Setups.Setups.Single(s => s.Tier == "budget").CoinsPerHour.Should().Be(FourTierModel.BudgetRate);
        view.Setups.Setups.Single(s => s.Tier == "mid").Basis.Should().Be("measured");
        gear.VerifyNoOtherCalls();
    }

    /// <summary>Regression: right after a PlayerState restart every view was rebuilt every 15 seconds for up to an hour</summary>
    [Test]
    public async Task Model_not_derived_yet_serves_estimates_with_the_normal_cache()
    {
        var notDerived = EstimatesOnly(ModelEstimates());
        notDerived.ComputedAt = null;
        Lookup(new GearModelLookup(notDerived, false));

        var view = await Create().GetPublicAsync("task-a");

        view.Degraded.Should().BeFalse();
        view.Setups.Setups.Should().OnlyContain(s => s.Basis == "estimate");
    }

    [Test]
    public async Task Model_without_estimates_and_a_failing_list_still_serves_the_measured_setups()
    {
        Lookup(Measured(null));
        gear.Setup(g => g.GetByTaskNameAsync("Task A", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Coflnet.Sky.Core.CoflnetException("gear_setups_unavailable", "down"));

        var view = await Create().GetPublicAsync("task-a");

        view.Degraded.Should().BeTrue();
        view.Setups.Baseline.Should().BeNull();
        view.Setups.Setups.Single(s => s.Tier == "budget").Basis.Should().Be("measured");
        view.Setups.ProfileName.Should().BeNull();
    }

    [Test]
    public async Task Premium_and_premium_plus_views_have_their_own_cache_keys_and_endgame_content()
    {
        Lookup(Measured(ModelEstimates()));
        var service = Create(new MemoryCache(new MemoryCacheOptions()));

        var premiumPlus = await service.GetPremiumPlusAsync("task-a");
        var premium = await service.GetPremiumAsync("task-a");

        new[] { TaskSetupsService.PublicKey("task-a"), TaskSetupsService.PremiumKey("task-a"), TaskSetupsService.PremiumPlusKey("task-a") }
            .Should().OnlyHaveUniqueItems();
        premium.Should().NotBeSameAs(premiumPlus);
        premium.Setups.Setups.Where(s => s.Tier == "endgame").Should().OnlyContain(s => s.Locked && s.RequiredTier == "premium_plus" && s.Items.Count == 0);
        premiumPlus.Setups.Setups.Single(s => s.Tier == "endgame").LateGame.Players.Should().Be(FourTierModel.SecretPlayers);
    }

    [Test]
    public async Task Both_views_share_one_model_build_and_have_their_own_cache_keys()
    {
        Lookup(Measured(ModelEstimates()));
        var (cache, entries) = CapturingCache();
        var service = Create(new MemoryCache(new MemoryCacheOptions()));

        await service.GetPublicAsync("task-a");
        await service.GetPremiumAsync("task-a");

        models.Verify(c => c.GetAsync("Task A", It.IsAny<CancellationToken>()), Times.Once);
        await Create(cache.Object).GetPremiumAsync("task-a");
        entries.Select(e => e.Key).Should().BeEquivalentTo(new object[] { TaskSetupsService.ModelKey("task-a"), TaskSetupsService.PremiumKey("task-a") });
        new[] { TaskSetupsService.PublicKey("task-a"), TaskSetupsService.PremiumKey("task-a"), TaskSetupsService.ModelKey("task-a"), TaskSetupsService.PublicKey("task-b") }
            .Should().OnlyHaveUniqueItems();
    }

    private void WithoutProfile(TaskGearModel model)
    {
        tasks.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PublicTaskEstimate> { new() { Name = "Task A", Slug = "task-a", CoinsPerHour = 2_000_000 } });
        Lookup(new GearModelLookup(model, false));
        gear.Setup(g => g.GetByTaskNameAsync("Task A", It.IsAny<CancellationToken>())).ReturnsAsync((TaskGearSetups)null);
    }

    /// <summary>Regression: Treasure Hoarder, Glacite Walker, Chill, Ender Dragon and Ghost have a gear model but no curated profile and were 404</summary>
    [Test]
    public async Task Model_without_a_curated_profile_gets_its_measured_endgame()
    {
        var endHoe = FourTierModel.EndHoe();
        WithoutProfile(MeasuredTestData.Model(null, null, [endHoe], Recommended(500, 5_000_000, endHoe), LateGame(5_000_000)));
        var service = Create();

        var premiumPlus = (await service.GetPremiumPlusAsync("task-a")).Setups;
        var publicView = (await service.GetPublicAsync("task-a")).Setups;

        premiumPlus.BaselineCoinsPerHour.Should().Be(2_000_000, "the community rate is the baseline");
        var endgame = premiumPlus.Setups.Single();
        endgame.Basis.Should().Be("measured");
        endgame.CoinsPerHour.Should().Be(5_000_000);
        publicView.Setups.Single().Id.Should().Be("locked-endgame");
    }

    [Test]
    public async Task Model_with_only_a_distribution_and_no_profile_stays_not_found()
    {
        WithoutProfile(MeasuredTestData.Model(null, null, [], distribution: Distribution(1, 2, 3, 4, 5)));

        var thrown = await FluentActions.Awaiting(() => Create().GetPublicAsync("task-a")).Should().ThrowAsync<Coflnet.Sky.Core.CoflnetException>();

        thrown.Which.Slug.Should().Be("gear_setups_not_found");
    }

    [Test]
    public async Task Task_without_any_setup_is_not_found_and_not_asked_again_right_away()
    {
        Lookup(GearModelLookup.NotFound);
        gear.Setup(g => g.GetByTaskNameAsync("Task A", It.IsAny<CancellationToken>())).ReturnsAsync((TaskGearSetups)null);
        var service = Create();

        for (var i = 0; i < 2; i++)
        {
            var thrown = await FluentActions.Awaiting(() => service.GetPublicAsync("task-a")).Should().ThrowAsync<Coflnet.Sky.Core.CoflnetException>();
            thrown.Which.Slug.Should().Be("gear_setups_not_found");
        }
        models.Verify(c => c.GetAsync("Task A", It.IsAny<CancellationToken>()), Times.Once);
    }
    [Test]
    public async Task Stale_model_is_served_while_it_is_rebuilt_in_the_background()
    {
        Lookup(Measured(ModelEstimates()));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = Create(cache, time);
        await service.GetPublicAsync("task-a");
        var slowRebuild = new TaskCompletionSource<GearModelLookup>(TaskCreationOptions.RunContinuationsAsynchronously);
        models.Setup(c => c.GetAsync("Task A", It.IsAny<CancellationToken>())).Returns(slowRebuild.Task);
        time.Advance(TaskSetupsService.PremiumCacheDuration + TimeSpan.FromSeconds(1));
        cache.Remove(TaskSetupsService.PublicKey("task-a"));

        var stale = await service.GetPublicAsync("task-a").WaitAsync(TimeSpan.FromSeconds(5));

        stale.Setups.Setups.Single(s => s.Tier == "budget").Basis.Should().Be("measured", "the stale model answers right away");
        await WaitUntil(() => models.Invocations.Count == 2);
        slowRebuild.SetResult(new GearModelLookup(EstimatesOnly(ModelEstimates()), false));
        await WaitUntil(() => !ModelIsMeasured(cache));
        cache.Remove(TaskSetupsService.PublicKey("task-a"));
        (await service.GetPublicAsync("task-a")).Setups.Setups.Single(s => s.Tier == "budget").Basis.Should().Be("estimate", "the refreshed model has nothing measured");
    }

    /// <summary>Regression: an estimates only rebuild after a PlayerState blip replaced the good measured model</summary>
    [Test]
    public async Task Degraded_refresh_keeps_a_good_stale_model_until_it_expires()
    {
        Lookup(Measured(ModelEstimates()));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = Create(cache, time);
        await service.GetPublicAsync("task-a");
        Lookup(GearModelLookup.Unavailable);

        time.Advance(TaskSetupsService.PremiumCacheDuration + TimeSpan.FromSeconds(1));
        await RefreshInBackground(service, cache, time, invocations: 2);
        ModelIsMeasured(cache).Should().BeTrue("the degraded rebuild must not replace the good model");
        ModelFreshUntil(cache).Should().Be(time.GetUtcNow() + TaskSetupsService.DegradedCacheDuration, "the good model is retried soon");

        time.Advance(TaskSetupsService.StaleModelDuration + TaskSetupsService.PremiumCacheDuration);
        await RefreshInBackground(service, cache, time, invocations: 3);
        ModelIsMeasured(cache).Should().BeFalse("once the good model expired the degraded one is accepted");
    }

    /// <summary>Serves the stale model, which starts a background rebuild, and waits until that rebuild stored its result</summary>
    private async Task RefreshInBackground(TaskSetupsService service, IMemoryCache cache, FakeTimeProvider time, int invocations)
    {
        cache.Remove(TaskSetupsService.PublicKey("task-a"));
        await service.GetPublicAsync("task-a");
        await WaitUntil(() => models.Invocations.Count == invocations && ModelFreshUntil(cache) > time.GetUtcNow());
    }

    private static DateTimeOffset ModelFreshUntil(IMemoryCache cache)
    {
        var stamped = cache.Get(TaskSetupsService.ModelKey("task-a"));
        return (DateTimeOffset)stamped!.GetType().GetProperty("FreshUntil")!.GetValue(stamped)!;
    }

    private static bool ModelIsMeasured(IMemoryCache cache)
    {
        var stamped = cache.Get(TaskSetupsService.ModelKey("task-a"));
        var model = (SetupsModel)stamped!.GetType().GetProperty("Model")!.GetValue(stamped);
        return model!.Plan.Setups.Any(s => s.IsMeasured);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        condition().Should().BeTrue();
    }

    [Test]
    public async Task Concurrent_cold_callers_of_both_views_share_one_measured_build()
    {
        var gate = new TaskCompletionSource<GearModelLookup>(TaskCreationOptions.RunContinuationsAsynchronously);
        models.Setup(c => c.GetAsync("Task A", It.IsAny<CancellationToken>())).Returns(gate.Task);
        var acquisition = FourTierModel.Acquisition();
        var service = Service(tasks.Object, gear.Object, resale.Object, null, models.Object, acquisition.Object);

        var calls = Enumerable.Range(0, 8).Select(i => i % 2 == 0 ? service.GetPublicAsync("task-a") : service.GetPremiumAsync("task-a")).ToList();
        gate.SetResult(Measured(ModelEstimates()));
        var views = await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(10));

        models.Verify(c => c.GetAsync("Task A", It.IsAny<CancellationToken>()), Times.Once);
        acquisition.Verify(a => a.GetAsync(It.IsAny<Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition.GearItemSpec>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
        views.Where((_, i) => i % 2 == 0).Select(v => v.Setups).Distinct().Should().ContainSingle();
        views.Where((_, i) => i % 2 == 1).Select(v => v.Setups).Distinct().Should().ContainSingle();
    }
}

public class TaskSetupsServiceCatalogTests
{
    [Test]
    public void Nothing_is_priced_without_a_curve_or_an_endgame()
    {
        var model = MeasuredTestData.Model(null, null, [Entry("tool", "HOE", 100)]);

        TaskSetupsService.PricedCatalog(model).Should().BeEmpty();
        TaskSetupsService.PricedCatalog(null).Should().BeEmpty();
    }

    [Test]
    public void Without_composition_only_the_endgame_pieces_are_priced()
    {
        var model = FourTierModel.Model();
        model.ScoreFormula = Formula("log-damage-product");

        TaskSetupsService.PricedCatalog(model).Select(p => p.Id).Should().BeEquivalentTo("tool:END_HOE", "helmet:MID_HELMET");
    }

    [Test]
    public void At_most_the_best_pieces_per_slot_are_priced_plus_the_endgame_ones()
    {
        var tools = Enumerable.Range(0, TaskSetupsService.MaxPiecesPerSlot + 5).Select(i => Entry("tool", "TOOL_" + i, 100 + i)).ToList();
        var weakEndgameTool = Entry("tool", "WEAK", 1);
        var model = MeasuredTestData.Model(null, FourTierModel.Model().Curve, tools.Append(weakEndgameTool),
            Recommended(100, 1, weakEndgameTool));

        var ids = TaskSetupsService.PricedCatalog(model).Select(p => p.Id).ToList();

        ids.Should().HaveCount(TaskSetupsService.MaxPiecesPerSlot + 1);
        ids[0].Should().Be("tool:WEAK", "the endgame's pieces are priced first");
        ids.Should().Contain($"tool:TOOL_{TaskSetupsService.MaxPiecesPerSlot + 4}").And.NotContain("tool:TOOL_0");
    }

    [Test]
    public void Variants_of_one_tag_take_one_place_so_cheaper_tags_are_kept()
    {
        var variants = Enumerable.Range(0, TaskSetupsService.MaxPiecesPerSlot).Select(i =>
        {
            var entry = Entry("tool", "TOP_HOE", 500 + i);
            entry.Id = "tool:TOP_HOE:" + i;
            return entry;
        });
        var model = MeasuredTestData.Model(null, FourTierModel.Model().Curve, variants.Append(Entry("tool", "CHEAP_HOE", 10)));

        var ids = TaskSetupsService.PricedCatalog(model).Select(p => p.Id).ToList();

        ids.Should().BeEquivalentTo("tool:TOP_HOE:5", "tool:CHEAP_HOE");
    }
}

public class GearModelServiceTests
{
    private Mock<ITaskGearApi> api;
    private FakeTimeProvider time;
    private GearModelService service;

    [SetUp]
    public void Setup()
    {
        api = new();
        time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero));
        service = new GearModelService(api.Object, time, Mock.Of<ILogger<GearModelService>>());
    }

    private Moq.Language.Flow.ISetup<ITaskGearApi, Task<TaskGearModel>> SetupGet()
        => api.Setup(a => a.TaskGearModelTaskNameGetAsync("Task A", It.IsAny<int>(), It.IsAny<CancellationToken>()));

    [Test]
    public async Task Returns_the_model_from_the_gear_model_route()
    {
        SetupGet().ReturnsAsync(new TaskGearModel { TaskName = "Task A" });

        var lookup = await service.GetAsync("Task A");

        lookup.Failed.Should().BeFalse();
        lookup.Model.TaskName.Should().Be("Task A");
        api.Verify(a => a.TaskGearClustersTaskNameGetAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Not_found_is_no_failure()
    {
        SetupGet().ThrowsAsync(new ApiException(404, "task_not_found"));

        (await service.GetAsync("Task A")).Should().Be(GearModelLookup.NotFound);
    }

    [Test]
    public async Task Failure_backs_off_for_a_minute()
    {
        SetupGet().ThrowsAsync(new ApiException(500, "boom"));

        (await service.GetAsync("Task A")).Should().Be(GearModelLookup.Unavailable);
        time.Advance(TimeSpan.FromSeconds(59));
        (await service.GetAsync("Task A")).Should().Be(GearModelLookup.Unavailable);
        api.Verify(a => a.TaskGearModelTaskNameGetAsync("Task A", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);

        SetupGet().ReturnsAsync(new TaskGearModel { TaskName = "Task A" });
        time.Advance(TimeSpan.FromSeconds(2));
        (await service.GetAsync("Task A")).Failed.Should().BeFalse();
    }

    [Test]
    public async Task Hanging_upstream_times_out_as_a_failure()
    {
        SetupGet().Returns(async (string _, int _, CancellationToken token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return null;
        });

        var lookup = service.GetAsync("Task A");
        time.Advance(GearModelService.Timeout);

        (await lookup.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(GearModelLookup.Unavailable);
    }

    [Test]
    public async Task Unreadable_body_is_a_failure()
    {
        SetupGet().ReturnsAsync((TaskGearModel)null);

        (await service.GetAsync("Task A")).Should().Be(GearModelLookup.Unavailable);
    }
}
