using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using ITaskApi = Coflnet.Sky.PlayerState.Client.Api.ITaskApi;
using Coflnet.Sky.PlayerState.Client.Client;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SkyApi.Services.Tasks;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class PublicTaskServiceTests
{
    private class TestClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public readonly List<ManualTimer> Timers = new();
        public override DateTimeOffset GetUtcNow() => Now;

        public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);
            Timers.Add(timer);
            return timer;
        }
    }

    /// <summary>A timer that only fires when the test says so</summary>
    private class ManualTimer : ITimer
    {
        private readonly TimerCallback callback;
        private readonly object state;
        public TimeSpan DueTime { get; private set; }

        public ManualTimer(TimerCallback callback, object state, TimeSpan dueTime)
        {
            this.callback = callback;
            this.state = state;
            DueTime = dueTime;
        }

        public void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime;
            return true;
        }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private Mock<ITaskApi> taskApi;
    private Mock<IItemsApi> itemsApi;
    private TestClock clock;
    private Coflnet.Sky.Api.Services.Tasks.PublicTaskService service;

    [SetUp]
    public void Setup()
    {
        taskApi = new();
        itemsApi = new();
        clock = new();
        service = new(taskApi.Object, itemsApi.Object, clock, Mock.Of<ILogger<Coflnet.Sky.Api.Services.Tasks.PublicTaskService>>());
        SetupEstimates().ReturnsAsync(() => new List<TaskEstimate> { Estimate("A") });
    }

    private Moq.Language.Flow.ISetup<ITaskApi, Task<List<TaskEstimate>>> SetupEstimates()
        => taskApi.Setup(s => s.TaskEstimatesGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()));

    private void VerifyFetches(Times times)
        => taskApi.Verify(s => s.TaskEstimatesGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), times);

    private static TaskEstimate Estimate(string name) => new() { TaskName = name, CoinsPerHour = 5 };

    [TestCase("T4 Voidglooms (FD)", "t4-voidglooms-fd")]
    [TestCase("  --Hello   World!! ", "hello-world")]
    [TestCase("***", "task")]
    public void Slugify_CollapsesAndTrims(string name, string expected)
    {
        Coflnet.Sky.Api.Services.Tasks.PublicTaskMapper.Slugify(name).Should().Be(expected);
    }

    [Test]
    public void MapAll_CollidingSlugsGetSuffix()
    {
        var result = Coflnet.Sky.Api.Services.Tasks.PublicTaskMapper.MapAll(
            new[] { Estimate("Foo Bar"), Estimate("foo-bar"), Estimate("FOO bar!"), Estimate("Foo Bar 2") }, null);
        result.Select(r => r.Slug).Should().Equal("foo-bar", "foo-bar-2", "foo-bar-3", "foo-bar-2-2");
        result.Select(r => r.Slug).Should().OnlyHaveUniqueItems();
    }

    [TestCase(TaskType.NUMBER_0, "active")]
    [TestCase(TaskType.NUMBER_1, "passive")]
    [TestCase(TaskType.NUMBER_2, "limited")]
    [TestCase(null, "active")]
    public void MapType_MapsGeneratedEnum(TaskType? type, string expected)
    {
        Coflnet.Sky.Api.Services.Tasks.PublicTaskMapper.MapType(type).Should().Be(expected);
    }

    [Test]
    public void Upstream_NumericTypeDeserializesIntoGeneratedEnum()
    {
        // PlayerState serializes TaskType as a number, the generated model must accept that wire format
        var list = JsonConvert.DeserializeObject<List<TaskEstimate>>("[{\"taskName\":\"a\",\"type\":1},{\"taskName\":\"b\",\"type\":2},{\"taskName\":\"c\"}]");
        list.Select(t => Coflnet.Sky.Api.Services.Tasks.PublicTaskMapper.MapType(t.Type)).Should().Equal("passive", "limited", "active");
    }

    [Test]
    public void Map_ExcludesInternalFieldsAndOnClick()
    {
        var upstream = JsonConvert.DeserializeObject<TaskEstimate>(@"{
            ""taskName"":""X"",""type"":0,""traceId"":""secret"",""statBucket"":3,""rawCoinsPerHour"":9,
            ""personalTrackedMinutes"":4,""coinsPerHour"":7,
            ""steps"":[{""number"":1,""text"":""go"",""onClick"":""/warp x""}],
            ""drops"":[{""itemTag"":""STONE"",""ratePerHour"":1,""priceEach"":2,""contributionPerHour"":2}],
            ""bestZones"":[{""zone"":""z"",""coinsPerHour"":1,""players"":2}]}");
        var mapped = Coflnet.Sky.Api.Services.Tasks.PublicTaskMapper.Map(upstream, "x",
            new Dictionary<string, string> { ["STONE"] = "Stone" });

        var json = JObject.Parse(JsonConvert.SerializeObject(mapped, new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        }));
        var text = json.ToString();
        foreach (var excluded in new[] { "traceId", "statBucket", "personalTrackedMinutes", "rawCoinsPerHour", "onClick", "secret" })
            text.Should().NotContain(excluded);
        json["coinsPerHour"].Value<double>().Should().Be(7);
        json["drops"][0]["itemName"].Value<string>().Should().Be("Stone");
        json["steps"][0]["text"].Value<string>().Should().Be("go");
    }

    [Test]
    public void Map_PassesRequirementsThroughWithoutPlayerFields()
    {
        var upstream = JsonConvert.DeserializeObject<TaskEstimate>(@"{
            ""taskName"":""X"",
            ""requirements"":[
              {""label"":""Mining 12"",""kind"":""skill_level"",""key"":""mining"",""target"":12,""hard"":true,""met"":true,""current"":40,""currentText"":""40""},
              {""label"":""Catacombs 20"",""kind"":""catacombs_level"",""target"":20,""hard"":false}]}");
        var mapped = Coflnet.Sky.Api.Services.Tasks.PublicTaskMapper.Map(upstream, "x", new Dictionary<string, string>());

        mapped.Requirements.Should().HaveCount(2);
        mapped.Requirements[0].Should().BeEquivalentTo(new PublicTaskRequirement
        {
            Label = "Mining 12", Kind = "skill_level", Key = "mining", Target = 12, Hard = true
        });
        mapped.Requirements[1].Hard.Should().BeFalse();
        var json = JObject.Parse(JsonConvert.SerializeObject(mapped, new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        }));
        var requirementKeys = json["requirements"].SelectMany(r => ((JObject)r).Properties().Select(p => p.Name)).Distinct();
        requirementKeys.Should().BeEquivalentTo("label", "kind", "key", "target", "hard");
        json.ToString().Should().NotContain("\"met\"").And.NotContain("\"current\"").And.NotContain("currentText");
    }

    [Test]
    public void Map_TaskWithoutRequirementsGetsEmptyList()
    {
        var mapped = Coflnet.Sky.Api.Services.Tasks.PublicTaskMapper.Map(new TaskEstimate { TaskName = "X" }, "x", new Dictionary<string, string>());
        mapped.Requirements.Should().BeEmpty();
    }

    [Test]
    public void Map_UnknownItemNameIsNull()
    {
        var upstream = new TaskEstimate { TaskName = "X", Drops = new() { new() { ItemTag = "NOPE" } } };
        var mapped = Coflnet.Sky.Api.Services.Tasks.PublicTaskMapper.Map(upstream, "x", new Dictionary<string, string>());
        mapped.Drops[0].ItemName.Should().BeNull();
    }

    [Test]
    public async Task GetAll_CachesWithinDuration()
    {
        await service.GetAllAsync();
        clock.Now += TimeSpan.FromSeconds(60);
        await service.GetAllAsync();
        VerifyFetches(Times.Once());
        clock.Now += TimeSpan.FromSeconds(61);
        await service.GetAllAsync();
        VerifyFetches(Times.Exactly(2));
    }

    [Test]
    public async Task GetAll_UpstreamFailureServesStaleValue()
    {
        var first = await service.GetAllAsync();
        clock.Now += TimeSpan.FromMinutes(5);
        SetupEstimates().ThrowsAsync(new Exception("down"));

        var second = await service.GetAllAsync();

        second.Should().BeSameAs(first);
        (await service.GetBySlugAsync("a")).Should().NotBeNull();
    }

    [Test]
    public async Task GetAll_UpstreamFailureWithoutStaleValueThrowsUnavailable()
    {
        SetupEstimates().ThrowsAsync(new Exception("down"));
        var act = () => service.GetAllAsync();
        (await act.Should().ThrowAsync<CoflnetException>()).Which.Slug.Should().Be("tasks_unavailable");
    }

    [Test]
    public async Task GetBySlug_UnknownReturnsNull()
    {
        (await service.GetBySlugAsync("missing")).Should().BeNull();
    }

    [Test]
    public async Task GetAll_ConcurrentCallsDuringOutageWithStaleValue_ReturnQuicklyAndFetchOnce()
    {
        await service.GetAllAsync();
        clock.Now += TimeSpan.FromMinutes(5);
        var gate = new TaskCompletionSource();
        SetupEstimates().Returns(async () =>
        {
            await gate.Task;
            throw new Exception("down");
        });

        var calls = Enumerable.Range(0, 20).Select(_ => service.GetAllAsync()).ToList();
        var waiters = calls.Skip(1).ToList();
        // everyone except the refreshing caller completes while upstream is still hanging
        await Task.WhenAny(Task.WhenAll(waiters), Task.Delay(2000));
        waiters.Count(w => w.IsCompletedSuccessfully).Should().BeGreaterThanOrEqualTo(19);
        gate.SetResult();
        await Task.WhenAll(calls);

        VerifyFetches(Times.Exactly(2));
    }

    [Test]
    public async Task GetAll_AfterFailureBacksOffUntilRetryWindow()
    {
        await service.GetAllAsync();
        clock.Now += TimeSpan.FromMinutes(5);
        SetupEstimates().ThrowsAsync(new Exception("down"));
        await service.GetAllAsync();
        await service.GetAllAsync();
        clock.Now += TimeSpan.FromSeconds(10);
        await service.GetAllAsync();
        VerifyFetches(Times.Exactly(2));
        clock.Now += TimeSpan.FromSeconds(10);
        await service.GetAllAsync();
        VerifyFetches(Times.Exactly(3));
    }

    [Test]
    public async Task GetAll_FailureWithoutStaleValueBacksOff()
    {
        SetupEstimates().ThrowsAsync(new Exception("down"));
        for (var i = 0; i < 3; i++)
            await FluentActions.Awaiting(() => service.GetAllAsync()).Should().ThrowAsync<CoflnetException>();
        VerifyFetches(Times.Once());
    }

    /// <summary>Hangs until the passed token is cancelled, then fails the way the RestSharp based client does</summary>
    private void SetupHangingUntilCancelled(List<CancellationToken> seen = null)
    {
        SetupEstimates().Returns(async (int _, CancellationToken token) =>
        {
            seen?.Add(token);
            await Task.Delay(Timeout.Infinite, token).ContinueWith(_ => { });
            throw new ApiException(0, "Error calling TaskEstimatesGet: The operation was canceled.");
        });
    }

    [Test]
    public async Task GetAll_UpstreamTimeoutWithoutStaleValueThrowsUnavailable()
    {
        SetupHangingUntilCancelled();
        var call = service.GetAllAsync();

        clock.Timers.Should().ContainSingle().Which.DueTime.Should().Be(TimeSpan.FromSeconds(10));
        call.IsCompleted.Should().BeFalse();
        clock.Timers[0].Fire();

        (await FluentActions.Awaiting(() => call).Should().ThrowAsync<CoflnetException>()).Which.Slug.Should().Be("tasks_unavailable");
    }

    [Test]
    public async Task GetAll_UpstreamTimeoutWithStaleValueServesStale()
    {
        var first = await service.GetAllAsync();
        clock.Now += TimeSpan.FromMinutes(5);
        SetupHangingUntilCancelled();
        var call = service.GetAllAsync();

        clock.Timers.Last().Fire();

        (await call).Should().BeSameAs(first);
    }

    [Test]
    public async Task GetAll_CallerAbortWithoutStaleValue_CancelsUpstreamAndDoesNotBackOff()
    {
        var seen = new List<CancellationToken>();
        SetupHangingUntilCancelled(seen);
        using var aborted = new CancellationTokenSource();
        var call = service.GetAllAsync(aborted.Token);

        aborted.Cancel();

        await FluentActions.Awaiting(() => call).Should().ThrowAsync<OperationCanceledException>();
        seen.Should().ContainSingle().Which.IsCancellationRequested.Should().BeTrue();
        // an aborted request is not an upstream failure, the next caller fetches right away
        SetupEstimates().ReturnsAsync(new List<TaskEstimate> { Estimate("A") });
        (await service.GetAllAsync()).Should().ContainSingle();
        VerifyFetches(Times.Exactly(2));
    }

    [Test]
    public async Task GetAll_WithStaleValue_RefreshIsNotTiedToCallerAbort()
    {
        await service.GetAllAsync();
        clock.Now += TimeSpan.FromMinutes(5);
        var seen = new List<CancellationToken>();
        SetupEstimates().ReturnsAsync((int _, CancellationToken token) =>
        {
            seen.Add(token);
            return new List<TaskEstimate> { Estimate("B") };
        });
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();

        var result = await service.GetAllAsync(aborted.Token);

        result.Single().Name.Should().Be("B");
        seen.Should().ContainSingle().Which.IsCancellationRequested.Should().BeFalse();
    }

    [Test]
    public async Task GetAll_NullBodyWithStaleValue_KeepsStaleAndBacksOff()
    {
        var first = await service.GetAllAsync();
        clock.Now += TimeSpan.FromMinutes(5);
        SetupEstimates().ReturnsAsync((List<TaskEstimate>)null);

        (await service.GetAllAsync()).Should().BeSameAs(first);
        (await service.GetAllAsync()).Should().BeSameAs(first);
        VerifyFetches(Times.Exactly(2));
    }

    [Test]
    public async Task GetAll_NullBodyWithoutStaleValue_ThrowsUnavailable()
    {
        SetupEstimates().ReturnsAsync((List<TaskEstimate>)null);
        (await FluentActions.Awaiting(() => service.GetAllAsync()).Should().ThrowAsync<CoflnetException>())
            .Which.Slug.Should().Be("tasks_unavailable");
    }

    [Test]
    public async Task GetAll_EmptyListWithNonEmptyStaleValue_KeepsStaleAndBacksOff()
    {
        var first = await service.GetAllAsync();
        clock.Now += TimeSpan.FromMinutes(5);
        SetupEstimates().ReturnsAsync(new List<TaskEstimate>());

        (await service.GetAllAsync()).Should().BeSameAs(first);
        (await service.GetAllAsync()).Should().BeSameAs(first);
        VerifyFetches(Times.Exactly(2));
        (await service.GetBySlugAsync("a")).Should().NotBeNull();
    }

    [Test]
    public async Task GetAll_EmptyListWithoutStaleValue_IsCachedSuccess()
    {
        SetupEstimates().ReturnsAsync(new List<TaskEstimate>());

        (await service.GetAllAsync()).Should().BeEmpty();
        (await service.GetAllAsync()).Should().BeEmpty();
        VerifyFetches(Times.Once());
    }
}
