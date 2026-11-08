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
using static SkyApi.Services.Tasks.Setups.Measured.MeasuredTestData;

namespace SkyApi.Services.Tasks.Setups;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class TaskSetupsServiceMeasuredTests
{
    private Mock<IPublicTaskService> tasks;
    private Mock<IGearSetupService> gear;
    private Mock<IItemResaleService> resale;
    private Mock<IGearClusterService> clusters;

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
        clusters = new();
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

    private static TaskGearSetups ClusterEstimates()
    {
        var estimates = ListEstimates();
        estimates.ProfileName = "From clusters";
        return estimates;
    }

    private void Lookup(ClusterLookup lookup)
        => clusters.Setup(c => c.GetAsync("Task A", It.IsAny<CancellationToken>())).ReturnsAsync(lookup);

    private TaskSetupsService Create(IMemoryCache cache = null, TimeProvider time = null)
        => Service(tasks.Object, gear.Object, resale.Object, cache, clusters.Object, Measured.MeasuredTestData.Acquisition(_ => 2_000_000).Object, time);

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
    public async Task Failed_clusters_fall_back_to_the_estimates_list_with_a_short_cache()
    {
        Lookup(ClusterLookup.Unavailable);
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
        Lookup(ClusterLookup.NotFound);

        var view = await Create().GetPublicAsync("task-a");

        view.Degraded.Should().BeFalse();
        view.Setups.ProfileName.Should().Be("From list");
    }

    [Test]
    public async Task Clusters_with_estimates_do_not_ask_the_list()
    {
        Lookup(new ClusterLookup(Clusters(ClusterEstimates(), Cluster("BASE", 1_000_000), Cluster("BUDGET", 1_500_000, helmet: "H2")), false));

        var view = await Create().GetPublicAsync("task-a");

        view.Degraded.Should().BeFalse();
        view.Setups.ProfileName.Should().Be("From clusters");
        view.Setups.Category.Should().Be("farming");
        view.Setups.Baseline.Basis.Should().Be("measured");
        view.Setups.Setups.Single(s => s.Tier == "budget").Basis.Should().Be("measured");
        view.Setups.Setups.Single(s => s.Tier == "mid").Basis.Should().Be("estimate");
        gear.VerifyNoOtherCalls();
    }

    /// <summary>Regression: right after a PlayerState restart every view was rebuilt every 15 seconds for up to an hour</summary>
    [Test]
    public async Task Clusters_not_derived_yet_serve_estimates_with_the_normal_cache()
    {
        var notDerived = Clusters(ClusterEstimates());
        notDerived.ComputedAt = null;
        Lookup(new ClusterLookup(notDerived, false));

        var view = await Create().GetPublicAsync("task-a");

        view.Degraded.Should().BeFalse();
        view.Setups.Setups.Should().OnlyContain(s => s.Basis == "estimate");
    }

    [Test]
    public async Task Clusters_without_estimates_and_a_failing_list_still_serve_the_measured_setups()
    {
        Lookup(new ClusterLookup(Clusters(null, Cluster("BASE", 1_000_000)), false));
        gear.Setup(g => g.GetByTaskNameAsync("Task A", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Coflnet.Sky.Core.CoflnetException("gear_setups_unavailable", "down"));

        var view = await Create().GetPublicAsync("task-a");

        view.Degraded.Should().BeTrue();
        view.Setups.Baseline.Basis.Should().Be("measured");
        view.Setups.ProfileName.Should().BeNull();
    }

    [Test]
    public async Task Premium_and_premium_plus_views_have_their_own_cache_keys_and_endgame_content()
    {
        Lookup(new ClusterLookup(Clusters(ClusterEstimates(), Cluster("BASE", 1_000_000)), false));
        var service = Create(new MemoryCache(new MemoryCacheOptions()));

        var premiumPlus = await service.GetPremiumPlusAsync("task-a");
        var premium = await service.GetPremiumAsync("task-a");

        new[] { TaskSetupsService.PublicKey("task-a"), TaskSetupsService.PremiumKey("task-a"), TaskSetupsService.PremiumPlusKey("task-a") }
            .Should().OnlyHaveUniqueItems();
        premium.Should().NotBeSameAs(premiumPlus);
        premium.Setups.Setups.Where(s => s.Tier == "endgame").Should().OnlyContain(s => s.Locked && s.RequiredTier == "premium_plus" && s.Items.Count == 0);
    }

    [Test]
    public async Task Both_views_share_one_model_build_and_have_their_own_cache_keys()
    {
        Lookup(new ClusterLookup(Clusters(ClusterEstimates(), Cluster("BASE", 1_000_000)), false));
        var (cache, entries) = CapturingCache();
        var service = Create(new MemoryCache(new MemoryCacheOptions()));

        await service.GetPublicAsync("task-a");
        await service.GetPremiumAsync("task-a");

        clusters.Verify(c => c.GetAsync("Task A", It.IsAny<CancellationToken>()), Times.Once);
        await Create(cache.Object).GetPremiumAsync("task-a");
        entries.Select(e => e.Key).Should().BeEquivalentTo(new object[] { TaskSetupsService.ModelKey("task-a"), TaskSetupsService.PremiumKey("task-a") });
        new[] { TaskSetupsService.PublicKey("task-a"), TaskSetupsService.PremiumKey("task-a"), TaskSetupsService.ModelKey("task-a"), TaskSetupsService.PublicKey("task-b") }
            .Should().OnlyHaveUniqueItems();
    }

    [Test]
    public async Task Task_without_any_setup_is_not_found_and_not_asked_again_right_away()
    {
        Lookup(ClusterLookup.NotFound);
        gear.Setup(g => g.GetByTaskNameAsync("Task A", It.IsAny<CancellationToken>())).ReturnsAsync((TaskGearSetups)null);
        var service = Create();

        for (var i = 0; i < 2; i++)
        {
            var thrown = await FluentActions.Awaiting(() => service.GetPublicAsync("task-a")).Should().ThrowAsync<Coflnet.Sky.Core.CoflnetException>();
            thrown.Which.Slug.Should().Be("gear_setups_not_found");
        }
        clusters.Verify(c => c.GetAsync("Task A", It.IsAny<CancellationToken>()), Times.Once);
    }
    [Test]
    public async Task Stale_model_is_served_while_it_is_rebuilt_in_the_background()
    {
        Lookup(new ClusterLookup(Clusters(ClusterEstimates(), Cluster("BASE", 1_000_000)), false));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = Create(cache, time);
        await service.GetPublicAsync("task-a");
        var slowRebuild = new TaskCompletionSource<ClusterLookup>(TaskCreationOptions.RunContinuationsAsynchronously);
        clusters.Setup(c => c.GetAsync("Task A", It.IsAny<CancellationToken>())).Returns(slowRebuild.Task);
        time.Advance(TaskSetupsService.PremiumCacheDuration + TimeSpan.FromSeconds(1));
        cache.Remove(TaskSetupsService.PublicKey("task-a"));

        var stale = await service.GetPublicAsync("task-a").WaitAsync(TimeSpan.FromSeconds(5));

        stale.Setups.Baseline.Basis.Should().Be("measured", "the stale model answers right away");
        await WaitUntil(() => clusters.Invocations.Count == 2);
        slowRebuild.SetResult(new ClusterLookup(Clusters(ClusterEstimates()), false));
        await WaitUntil(() => !ModelHasClusters(cache));
        cache.Remove(TaskSetupsService.PublicKey("task-a"));
        (await service.GetPublicAsync("task-a")).Setups.Baseline.Basis.Should().Be("estimate", "the refreshed model has no clusters");
    }

    /// <summary>Regression: an estimates only rebuild after a PlayerState blip replaced the good measured model</summary>
    [Test]
    public async Task Degraded_refresh_keeps_a_good_stale_model_until_it_expires()
    {
        Lookup(new ClusterLookup(Clusters(ClusterEstimates(), Cluster("BASE", 1_000_000)), false));
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = Create(cache, time);
        await service.GetPublicAsync("task-a");
        Lookup(ClusterLookup.Unavailable);

        time.Advance(TaskSetupsService.PremiumCacheDuration + TimeSpan.FromSeconds(1));
        await RefreshInBackground(service, cache, time, invocations: 2);
        ModelHasClusters(cache).Should().BeTrue("the degraded rebuild must not replace the good model");
        ModelFreshUntil(cache).Should().Be(time.GetUtcNow() + TaskSetupsService.DegradedCacheDuration, "the good model is retried soon");

        time.Advance(TaskSetupsService.StaleModelDuration + TaskSetupsService.PremiumCacheDuration);
        await RefreshInBackground(service, cache, time, invocations: 3);
        ModelHasClusters(cache).Should().BeFalse("once the good model expired the degraded one is accepted");
    }

    /// <summary>Serves the stale model, which starts a background rebuild, and waits until that rebuild stored its result</summary>
    private async Task RefreshInBackground(TaskSetupsService service, IMemoryCache cache, FakeTimeProvider time, int invocations)
    {
        cache.Remove(TaskSetupsService.PublicKey("task-a"));
        await service.GetPublicAsync("task-a");
        await WaitUntil(() => clusters.Invocations.Count == invocations && ModelFreshUntil(cache) > time.GetUtcNow());
    }

    private static DateTimeOffset ModelFreshUntil(IMemoryCache cache)
    {
        var stamped = cache.Get(TaskSetupsService.ModelKey("task-a"));
        return (DateTimeOffset)stamped!.GetType().GetProperty("FreshUntil")!.GetValue(stamped)!;
    }

    private static bool ModelHasClusters(IMemoryCache cache)
    {
        var stamped = cache.Get(TaskSetupsService.ModelKey("task-a"));
        var model = (SetupsModel)stamped!.GetType().GetProperty("Model")!.GetValue(stamped);
        return model!.Plan.Baseline.IsMeasured;
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
        var gate = new TaskCompletionSource<ClusterLookup>(TaskCreationOptions.RunContinuationsAsynchronously);
        clusters.Setup(c => c.GetAsync("Task A", It.IsAny<CancellationToken>())).Returns(gate.Task);
        var acquisition = Measured.MeasuredTestData.Acquisition(_ => 2_000_000);
        var service = Service(tasks.Object, gear.Object, resale.Object, null, clusters.Object, acquisition.Object);

        var calls = Enumerable.Range(0, 8).Select(i => i % 2 == 0 ? service.GetPublicAsync("task-a") : service.GetPremiumAsync("task-a")).ToList();
        gate.SetResult(new ClusterLookup(Clusters(ClusterEstimates(), Cluster("BASE", 1_000_000), Cluster("BUDGET", 1_500_000, helmet: "H2")), false));
        var views = await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(10));

        clusters.Verify(c => c.GetAsync("Task A", It.IsAny<CancellationToken>()), Times.Once);
        acquisition.Verify(a => a.GetAsync(It.IsAny<Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition.GearItemSpec>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
        views.Where((_, i) => i % 2 == 0).Select(v => v.Setups).Distinct().Should().ContainSingle();
        views.Where((_, i) => i % 2 == 1).Select(v => v.Setups).Distinct().Should().ContainSingle();
    }
}

public class GearClusterServiceTests
{
    private Mock<ITaskGearApi> api;
    private FakeTimeProvider time;
    private GearClusterService service;

    [SetUp]
    public void Setup()
    {
        api = new();
        time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero));
        service = new GearClusterService(api.Object, time, Mock.Of<ILogger<GearClusterService>>());
    }

    private Moq.Language.Flow.ISetup<ITaskGearApi, Task<TaskGearClusters>> SetupGet()
        => api.Setup(a => a.TaskGearClustersTaskNameGetAsync("Task A", It.IsAny<int>(), It.IsAny<CancellationToken>()));

    [Test]
    public async Task Returns_the_clusters()
    {
        SetupGet().ReturnsAsync(new TaskGearClusters { TaskName = "Task A" });

        var lookup = await service.GetAsync("Task A");

        lookup.Failed.Should().BeFalse();
        lookup.Clusters.TaskName.Should().Be("Task A");
    }

    [Test]
    public async Task Not_found_is_no_failure()
    {
        SetupGet().ThrowsAsync(new ApiException(404, "task_not_found"));

        (await service.GetAsync("Task A")).Should().Be(ClusterLookup.NotFound);
    }

    [Test]
    public async Task Failure_backs_off_for_a_minute()
    {
        SetupGet().ThrowsAsync(new ApiException(500, "boom"));

        (await service.GetAsync("Task A")).Should().Be(ClusterLookup.Unavailable);
        time.Advance(TimeSpan.FromSeconds(59));
        (await service.GetAsync("Task A")).Should().Be(ClusterLookup.Unavailable);
        api.Verify(a => a.TaskGearClustersTaskNameGetAsync("Task A", It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);

        SetupGet().ReturnsAsync(new TaskGearClusters { TaskName = "Task A" });
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
        time.Advance(GearClusterService.Timeout);

        (await lookup.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(ClusterLookup.Unavailable);
    }

    [Test]
    public async Task Unreadable_body_is_a_failure()
    {
        SetupGet().ReturnsAsync((TaskGearClusters)null);

        (await service.GetAsync("Task A")).Should().Be(ClusterLookup.Unavailable);
    }
}
