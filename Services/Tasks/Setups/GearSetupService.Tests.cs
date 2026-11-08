using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Api.Services.Tasks;
using Coflnet.Sky.Api.Services.Tasks.Setups;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace SkyApi.Services.Tasks.Setups;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class GearSetupServiceTests
{
    private class TestClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private Mock<ITaskGearApi> gearApi;
    private TestClock clock;
    private GearSetupService service;

    [SetUp]
    public void Setup()
    {
        gearApi = new();
        clock = new();
        service = new(gearApi.Object, clock, Mock.Of<ILogger<GearSetupService>>());
        SetupGet().ReturnsAsync(() => [new TaskGearSetups { TaskName = "A" }, new TaskGearSetups { TaskName = "B" }]);
    }

    private Moq.Language.Flow.ISetup<ITaskGearApi, Task<List<TaskGearSetups>>> SetupGet()
        => gearApi.Setup(s => s.TaskGearSetupsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()));

    private void VerifyFetches(Times times)
        => gearApi.Verify(s => s.TaskGearSetupsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), times);

    [Test]
    public async Task Second_call_within_two_minutes_is_served_from_the_cache()
    {
        await service.GetAllAsync();
        clock.Now += TimeSpan.FromSeconds(119);
        await service.GetAllAsync();

        VerifyFetches(Times.Once());
    }

    [Test]
    public async Task Value_is_refreshed_after_two_minutes()
    {
        await service.GetAllAsync();
        clock.Now += TimeSpan.FromMinutes(2);
        await service.GetAllAsync();

        VerifyFetches(Times.Exactly(2));
    }

    [Test]
    public async Task Upstream_failure_serves_the_stale_value()
    {
        var first = await service.GetAllAsync();
        clock.Now += TimeSpan.FromMinutes(5);
        SetupGet().ThrowsAsync(new InvalidOperationException("down"));

        (await service.GetAllAsync()).Should().BeSameAs(first);
    }

    [Test]
    public async Task Upstream_failure_without_stale_value_is_unavailable()
    {
        SetupGet().ThrowsAsync(new InvalidOperationException("down"));

        var thrown = await FluentActions.Awaiting(() => service.GetAllAsync()).Should().ThrowAsync<CoflnetException>();

        thrown.Which.Slug.Should().Be("gear_setups_unavailable");
    }

    [Test]
    public async Task Unreadable_body_is_a_failure_and_keeps_the_stale_value()
    {
        var first = await service.GetAllAsync();
        clock.Now += TimeSpan.FromMinutes(5);
        SetupGet().ReturnsAsync((List<TaskGearSetups>)null);

        (await service.GetAllAsync()).Should().BeSameAs(first);
    }

    [Test]
    public async Task Empty_list_does_not_replace_a_non_empty_value()
    {
        var first = await service.GetAllAsync();
        clock.Now += TimeSpan.FromMinutes(5);
        SetupGet().ReturnsAsync(() => []);

        (await service.GetAllAsync()).Should().BeSameAs(first);
    }

    [Test]
    public async Task GetByTaskName_finds_the_task_or_returns_null()
    {
        (await service.GetByTaskNameAsync("B")).TaskName.Should().Be("B");
        (await service.GetByTaskNameAsync("nope")).Should().BeNull();
    }
}

public class TaskSetupsServiceTests
{
    private Mock<IPublicTaskService> tasks;
    private Mock<IGearSetupService> gear;
    private Mock<IItemResaleService> resale;
    private TaskSetupsService service;
    private int concurrent;
    private int maxConcurrent;

    [SetUp]
    public void Setup()
    {
        tasks = new();
        gear = new();
        resale = new();
        tasks.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PublicTaskEstimate> { new() { Name = "Task A", Slug = "task-a" } });
        gear.Setup(g => g.GetByTaskNameAsync("Task A", It.IsAny<CancellationToken>())).ReturnsAsync(
            PublicSetupsMapperTests.Gear(
                PublicSetupsMapperTests.Setup("baseline", "baseline", 0),
                PublicSetupsMapperTests.Setup("a", "upgrade", 10_000, PublicSetupsMapperTests.Item("X"), PublicSetupsMapperTests.Item("Y"))));
        resale.Setup(r => r.GetQuoteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .Returns(async (string tag, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
            {
                var now = Interlocked.Increment(ref concurrent);
                maxConcurrent = Math.Max(maxConcurrent, now);
                await Task.Delay(30);
                Interlocked.Decrement(ref concurrent);
                return PublicSetupsMapperTests.Quote(tag, 100, 90);
            });
        service = Measured.MeasuredTestData.Service(tasks.Object, gear.Object, resale.Object, new MemoryCache(new MemoryCacheOptions()));
    }

    [Test]
    public async Task Unknown_slug_is_task_not_found()
    {
        var thrown = await FluentActions.Awaiting(() => service.GetPublicAsync("nope")).Should().ThrowAsync<CoflnetException>();

        thrown.Which.Slug.Should().Be("task_not_found");
    }

    [Test]
    public async Task Task_without_profile_is_gear_setups_not_found()
    {
        tasks.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PublicTaskEstimate> { new() { Name = "No gear", Slug = "no-gear" } });

        var thrown = await FluentActions.Awaiting(() => service.GetPremiumAsync("no-gear")).Should().ThrowAsync<CoflnetException>();

        thrown.Which.Slug.Should().Be("gear_setups_not_found");
    }

    [Test]
    public async Task Public_view_does_not_ask_for_prices_of_locked_tiers()
    {
        var result = await service.GetPublicAsync("task-a");

        result.Setups.Locked.Should().BeTrue();
        result.Degraded.Should().BeFalse();
        resale.VerifyNoOtherCalls();
    }

    private void UseFreeBudgetSetup()
    {
        gear.Setup(g => g.GetByTaskNameAsync("Task A", It.IsAny<CancellationToken>())).ReturnsAsync(
            PublicSetupsMapperTests.Gear(
                PublicSetupsMapperTests.Setup("baseline", "baseline", 0),
                PublicSetupsMapperTests.Setup("budget", "upgrade", 10_000, PublicSetupsMapperTests.Item("X")),
                PublicSetupsMapperTests.Setup("mid", "upgrade", 20_000, PublicSetupsMapperTests.Item("Y"))));
    }

    [Test]
    public async Task Public_view_prices_the_free_tiers_only()
    {
        UseFreeBudgetSetup();

        var view = await service.GetPublicAsync("task-a");

        view.Setups.Setups.Single(s => s.Tier == "budget").BuyCost.Should().Be(100);
        view.Setups.Setups.Single(s => s.Tier == "mid").BuyCost.Should().BeNull();
        resale.Verify(r => r.GetQuoteAsync("X", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
        resale.Verify(r => r.GetQuoteAsync("Y", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Public_view_survives_failing_price_lookups_with_incomplete_prices()
    {
        UseFreeBudgetSetup();
        resale.Setup(r => r.GetQuoteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("prices down"));

        var view = await service.GetPublicAsync("task-a");

        view.Degraded.Should().BeTrue();
        var budget = view.Setups.Setups.Single(s => s.Tier == "budget");
        budget.PriceIncomplete.Should().BeTrue();
        budget.BuyCost.Should().BeNull();
        budget.Items.Should().ContainSingle();
        budget.Locked.Should().BeFalse();
    }

    private static (Mock<IMemoryCache>, Mock<ICacheEntry>) CapturingCache()
    {
        var entry = new Mock<ICacheEntry>();
        entry.SetupAllProperties();
        var cache = new Mock<IMemoryCache>();
        cache.Setup(c => c.CreateEntry(It.IsAny<object>())).Returns(entry.Object);
        return (cache, entry);
    }

    [TestCase(false, 120)]
    [TestCase(true, 15)]
    public async Task View_with_a_degraded_quote_is_only_cached_briefly(bool degraded, int expectedSeconds)
    {
        resale.Setup(r => r.GetQuoteAsync("Y", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(degraded ? ItemResaleQuote.Unknown("Y") with { Degraded = true } : PublicSetupsMapperTests.Quote("Y", 100, 90));
        var (cache, entry) = CapturingCache();
        service = Measured.MeasuredTestData.Service(tasks.Object, gear.Object, resale.Object, cache.Object);

        await service.GetPremiumAsync("task-a");
        entry.Object.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
        UseFreeBudgetSetup();
        resale.Setup(r => r.GetQuoteAsync("X", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(degraded ? ItemResaleQuote.Unknown("X") with { Degraded = true } : PublicSetupsMapperTests.Quote("X", 100, 90));
        await service.GetPublicAsync("task-a");

        entry.Object.AbsoluteExpirationRelativeToNow.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    [Test]
    public async Task Permanently_missing_price_keeps_the_normal_cache_time()
    {
        UseFreeBudgetSetup();
        resale.Setup(r => r.GetQuoteAsync("X", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ItemResaleQuote.Unknown("X"));
        var (cache, entry) = CapturingCache();
        service = Measured.MeasuredTestData.Service(tasks.Object, gear.Object, resale.Object, cache.Object);

        var view = await service.GetPublicAsync("task-a");

        view.Setups.Setups.Single(s => s.Tier == "budget").PriceIncomplete.Should().BeTrue();
        view.Degraded.Should().BeFalse();
        entry.Object.AbsoluteExpirationRelativeToNow.Should().Be(TaskSetupsService.PremiumCacheDuration);
    }

    [Test]
    public async Task Concurrent_cold_callers_build_the_premium_view_once()
    {
        var views = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.GetPremiumAsync("task-a")));

        views.Should().OnlyContain(v => ReferenceEquals(v.Setups, views[0].Setups));
        resale.Verify(r => r.GetQuoteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task Premium_view_is_cached_per_slug_and_quotes_are_loaded_in_parallel()
    {
        var first = await service.GetPremiumAsync("task-a");
        var second = await service.GetPremiumAsync("task-a");

        first.Setups.Locked.Should().BeFalse();
        second.Should().BeSameAs(first);
        first.Setups.Setups.Single().BuyCost.Should().Be(200);
        resale.Verify(r => r.GetQuoteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        maxConcurrent.Should().Be(2);
    }
}
