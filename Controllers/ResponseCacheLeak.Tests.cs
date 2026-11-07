using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Helper;
using Coflnet.Sky.Api.Models;
using Microsoft.Extensions.Caching.Memory;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Client.Model;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

/// <summary>
/// Responses that depend on the token, api key or tier must never be shared through the response cache
/// (keyed by path only) or a CDN (keyed by url, ignores Vary), so they have to be no-store.
/// </summary>
[TestFixture]
public class ResponseCacheLeakTests
{
    private const string VictimKey = "victim-key";
    private const string VictimServer = "victim-private-server";

    /// <summary>The api key victim gets 200, a keyless call to the same url must not get the victim data.</summary>
    [TestCase("/api/player/extracted")]
    [TestCase("/api/player/bazaar/flips")]
    public async Task Api_key_endpoints_do_not_serve_the_victim_to_keyless_callers(string url)
    {
        using var host = CreateKeyHost();
        using var client = host.GetTestClient();

        var victim = await Get(client, url, ("X-Api-Key", VictimKey));
        var keyless = await Get(client, url);

        var keylessBody = await keyless.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(victim.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            AssertNeverShared(victim);
            Assert.That(keyless.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK), keylessBody);
            Assert.That(keylessBody, Does.Not.Contain(VictimServer));
        });
    }

    /// <summary>The leaderboard weeks must not share a cache entry and the premium+ week is no-store.</summary>
    [Test]
    public async Task Leaderboard_current_week_is_not_served_from_the_public_week_entry()
    {
        var weeks = new List<DateTime>();
        using var host = CreateHost(out _, builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<ILeaderboardService>(new RecordingLeaderboard(weeks))));
        using var client = host.GetTestClient();

        var publicWeek = await Get(client, "/api/leaderboard/profit?weekOffset=-1");
        var publicAgain = await Get(client, "/api/leaderboard/profit?weekOffset=-1");
        var current = await Get(client, "/api/leaderboard/profit?weekOffset=0", ("GoogleToken", "premium"));
        var currentAnonymous = await Get(client, "/api/leaderboard/profit?weekOffset=0");

        Assert.Multiple(() =>
        {
            Assert.That(publicWeek.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(publicWeek.Headers.CacheControl?.Public, Is.True, "the public week stays cacheable");
            Assert.That(publicAgain.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(current.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            AssertNeverShared(current);
            Assert.That(currentAnonymous.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
            Assert.That(weeks, Has.Count.EqualTo(2), "public week once (second call cached), current week once");
        });
    }

    /// <summary>The premium fusion answer is no-store, others keep getting the placeholder.</summary>
    [Test]
    public async Task Fusion_multistep_does_not_share_the_premium_answer()
    {
        var api = Stub<IBazaarFlipperApi>((method, _) => method.Name == nameof(IBazaarFlipperApi.FusionMultistepGetAsync)
            ? Task.FromResult(new List<FuseFlip> { new() { Output = "premium-only-output" } })
            : EmptyValue(method.ReturnType));
        using var host = CreateHost(out _, builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(api)));
        using var client = host.GetTestClient();

        var premium = await Get(client, "/api/flip/fusion/multistep", ("GoogleToken", "premium"));
        var basic = await Get(client, "/api/flip/fusion/multistep", ("GoogleToken", "basic"));

        var basicBody = await basic.Content.ReadAsStringAsync();
        var premiumBody = await premium.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(premiumBody, Does.Contain("premium-only-output"));
            AssertNeverShared(premium);
            Assert.That(basic.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the website relies on the placeholder");
            Assert.That(basicBody, Does.Contain("Requires starter premium").And.Not.Contain("premium-only-output"));
        });
    }

    /// <summary>These are premium only or personalised, the attribute is applied for the whole route.</summary>
    [TestCase(typeof(FlipController), nameof(FlipController.GetUnknownFlips))]
    [TestCase(typeof(FlipController), nameof(FlipController.GetAttributeFlips))]
    [TestCase(typeof(FlipController), nameof(FlipController.GetMultiStepFusionFlips))]
    [TestCase(typeof(PricesController), nameof(PricesController.GetYearHistory))]
    [TestCase(typeof(AuctionsController), nameof(AuctionsController.GetArchived))]
    [TestCase(typeof(PlayerController), nameof(PlayerController.GetPlayerSate))]
    [TestCase(typeof(PlayerController), nameof(PlayerController.GetPlayerBazaarFlips))]
    public void Gated_actions_are_marked_no_store(Type controller, string action)
    {
        var cache = controller.GetMethod(action)!.GetCustomAttribute<ResponseCacheAttribute>();

        Assert.That(cache, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(cache!.NoStore, Is.True);
            Assert.That(cache.Location, Is.EqualTo(ResponseCacheLocation.None));
        });
    }

    /// <summary>The forge flips depend on the uuid, the cache key has to include it.</summary>
    [Test]
    public void Forge_flips_vary_by_uuid()
    {
        var cache = typeof(FlipController).GetMethod(nameof(FlipController.GetForgeFlips))!
            .GetCustomAttribute<ResponseCacheAttribute>();

        Assert.That(cache!.VaryByQueryKeys, Does.Contain("uuid"));
    }

    /// <summary>Beyond the free range the analysis is premium only and no-store.</summary>
    [TestCase(7, null)]
    [TestCase(30, "no-store")]
    [TestCase(365, "no-store")]
    public async Task Analysis_is_no_store_beyond_the_free_range(int days, string expected)
    {
        var controller = NewController<PricesController>("premium");

        var header = await CacheControlAfter(controller, nameof(PricesController.GetAnalysis),
            "ITEM", days, null, controller.Premium, null);

        Assert.That(header, Is.EqualTo(expected));
    }

    /// <summary>A basic user is rejected and gets no cacheable premium answer.</summary>
    [Test]
    public async Task Analysis_beyond_the_free_range_stays_rejected_for_basic_users()
    {
        var controller = NewController<PricesController>("basic");

        var thrown = await ThrownBy(controller, nameof(PricesController.GetAnalysis),
            "ITEM", 365, null, controller.Premium, null);

        Assert.That(thrown, Is.InstanceOf<CoflnetException>());
    }

    /// <summary>Stats over more than a week are premium and no-store, the free week stays cacheable.</summary>
    [Test]
    public async Task Flip_stats_are_no_store_only_for_the_premium_range()
    {
        var premiumRange = NewController<FlipController>("premium");
        var freeRange = NewController<FlipController>("basic");
        var rejected = NewController<FlipController>("basic");
        var end = DateTime.UtcNow;

        var premiumHeader = await CacheControlAfter(premiumRange, nameof(FlipController.GetStats), "uuid", end.AddDays(-30), end);
        var freeHeader = await CacheControlAfter(freeRange, nameof(FlipController.GetStats), "uuid", end.AddDays(-3), end);
        var thrown = await ThrownBy(rejected, nameof(FlipController.GetStats), "uuid", end.AddDays(-30), end);

        Assert.Multiple(() =>
        {
            Assert.That(premiumHeader, Is.EqualTo("no-store"));
            Assert.That(freeHeader, Is.Null);
            Assert.That(thrown, Is.InstanceOf<CoflnetException>());
        });
    }

    /// <summary>Pages beyond 10 are premium+ and no-store.</summary>
    [Test]
    public async Task Overview_pages_beyond_ten_are_no_store()
    {
        var premium = NewController<AuctionsController>("premium");
        var basic = NewController<AuctionsController>("basic");
        var free = NewController<AuctionsController>("basic");

        var premiumHeader = await CacheControlAfter(premium, "ToPreview", Page(11), 1, null);
        var basicThrown = await ThrownBy(basic, "ToPreview", Page(11), 1, null);
        var freeHeader = await CacheControlAfter(free, "ToPreview", Page(10), 1, null);

        Assert.Multiple(() =>
        {
            Assert.That(premiumHeader, Is.EqualTo("no-store"));
            Assert.That(basicThrown, Is.InstanceOf<CoflnetException>());
            Assert.That(freeHeader, Is.Null);
        });
    }

    /// <summary>The bazaar history older than a year is prem+ and no-store.</summary>
    [Test]
    public async Task Bazaar_history_is_no_store_for_the_premium_plus_range()
    {
        var premium = NewController<BazaarController>("premium");
        var basic = NewController<BazaarController>("basic");
        var free = NewController<BazaarController>("basic");
        var old = DateTime.UtcNow.AddDays(-400);
        var recent = DateTime.UtcNow.AddHours(-3);

        var premiumHeader = await CacheControlAfter(premium, nameof(BazaarController.HistoryGraph),
            premium.Premium, "TAG", old, old.AddHours(2));
        var basicThrown = await ThrownBy(basic, nameof(BazaarController.HistoryGraph),
            basic.Premium, "TAG", old, old.AddHours(2));
        var freeHeader = await CacheControlAfter(free, nameof(BazaarController.HistoryGraph),
            free.Premium, "TAG", recent, recent.AddHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(premiumHeader, Is.EqualTo("no-store"));
            Assert.That(basicThrown, Is.InstanceOf<CoflnetException>());
            Assert.That(freeHeader, Is.Null);
        });
    }

    /// <summary>A warm server cache is only reachable after the tier gate.</summary>
    [Test]
    public async Task Fusion_multistep_is_cached_server_side_for_premium_only()
    {
        var calls = 0;
        var api = Stub<IBazaarFlipperApi>((method, _) =>
        {
            if (method.Name != nameof(IBazaarFlipperApi.FusionMultistepGetAsync))
                return EmptyValue(method.ReturnType);
            calls++;
            return Task.FromResult(new List<FuseFlip> { new() { Output = "premium-only-output" } });
        });
        using var host = CreateHost(out _, builder => builder.ConfigureTestServices(services =>
            services.AddSingleton(api)));
        using var client = host.GetTestClient();

        await Get(client, "/api/flip/fusion/multistep", ("GoogleToken", "premium"));
        var second = await Get(client, "/api/flip/fusion/multistep", ("GoogleToken", "premium"));
        var basic = await Get(client, "/api/flip/fusion/multistep", ("GoogleToken", "basic"));

        var secondBody = await second.Content.ReadAsStringAsync();
        var basicBody = await basic.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(secondBody, Does.Contain("premium-only-output"));
            Assert.That(basicBody, Does.Not.Contain("premium-only-output"));
        });
    }

    /// <summary>The cache helper runs the factory once per key, regardless of filter order.</summary>
    [Test]
    public async Task Shared_result_cache_runs_the_factory_once_per_normalized_key()
    {
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var calls = 0;
        Task<int> Factory() => Task.FromResult(++calls);
        var a = SharedResultCache.Key("p", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" }, "TAG", 30);
        var b = SharedResultCache.Key("p", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }, "TAG", 30);
        var other = SharedResultCache.Key("p", new Dictionary<string, string> { ["a"] = "1" }, "TAG", 30);

        await cache.GetOrCreateShared(a, TimeSpan.FromMinutes(1), Factory);
        await cache.GetOrCreateShared(b, TimeSpan.FromMinutes(1), Factory);
        await cache.GetOrCreateShared(other, TimeSpan.FromMinutes(1), Factory);
        await ((Microsoft.Extensions.Caching.Memory.IMemoryCache)null).GetOrCreateShared(a, TimeSpan.FromMinutes(1), Factory);

        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo(b));
            Assert.That(calls, Is.EqualTo(3), "a once, other once, the null cache always runs");
        });
    }

    /// <summary>With a warm cache premium users get the cached value, basic users are still rejected.</summary>
    [Test]
    public async Task Warm_server_cache_does_not_bypass_the_tier_gate()
    {
        using var cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var filters = new Dictionary<string, string>();
        var archive = new Dictionary<string, string> { ["EndAfter"] = "1", ["EndBefore"] = "2" };
        var analysis = new PricesService.AdvancedAnalysisResult();
        var year = new PricesService.PriceStatistics();
        var flips = (IEnumerable<AttributeFlip>)new List<AttributeFlip>();
        var archived = new ArchiveResponse();
        cache.Set(SharedResultCache.Key("price-analysis", filters, "ITEM", 30), analysis);
        cache.Set(SharedResultCache.Key("price-year", filters, "ITEM"), year);
        cache.Set(SharedResultCache.Key("archive-overview", archive, "ITEM"), archived);
        cache.Set("attribute-flips|profit", flips);

        var premiumAnalysis = NewController<PricesController>("premium");
        var basicAnalysis = NewController<PricesController>("basic");
        var premiumYear = NewController<PricesController>("premium");
        var basicYear = NewController<PricesController>("basic");
        var premiumArchive = NewController<AuctionsController>("premium");
        var basicArchive = NewController<AuctionsController>("basic");
        var premiumAttr = NewController<FlipController>("premium");
        var basicAttr = NewController<FlipController>("basic");

        var results = new[]
        {
            await Run(premiumAnalysis, nameof(PricesController.GetAnalysis), "ITEM", 30, filters, premiumAnalysis.Premium, cache),
            await Run(premiumYear, nameof(PricesController.GetYearHistory), "ITEM", filters, premiumYear.Premium, null, cache),
            await Run(premiumArchive, nameof(AuctionsController.GetArchived), "ITEM", archive, cache),
            await Run(premiumAttr, nameof(FlipController.GetAttributeFlips), "profit", cache)
        };
        var rejected = new[]
        {
            await Run(basicAnalysis, nameof(PricesController.GetAnalysis), "ITEM", 30, filters, basicAnalysis.Premium, cache),
            await Run(basicYear, nameof(PricesController.GetYearHistory), "ITEM", filters, basicYear.Premium, null, cache),
            await Run(basicArchive, nameof(AuctionsController.GetArchived), "ITEM", archive, cache),
            await Run(basicAttr, nameof(FlipController.GetAttributeFlips), "profit", cache)
        };

        Assert.Multiple(() =>
        {
            Assert.That(results.Select(r => r.Error), Is.All.Null);
            Assert.That(results.Select(r => r.Result), Is.EqualTo(new object[] { analysis, year, archived, flips }));
            Assert.That(rejected.Select(r => r.Error), Is.All.InstanceOf<CoflnetException>());
        });
    }

    /// <summary>A premium refresh must not be answered from a cached non premium response.</summary>
    [Test]
    public async Task Reverse_npc_refresh_is_no_store_for_premium_only()
    {
        var npc = Stub<Crafts.Client.Api.INpcApi>((method, _) => EmptyValue(method.ReturnType));
        var premium = NewController<FlipController>("premium");
        var basic = NewController<FlipController>("basic");
        var noRefresh = NewController<FlipController>("premium");

        var premiumHeader = await CacheControlAfter(premium, nameof(FlipController.GetReverseNpcFlips), npc, true);
        var basicHeader = await CacheControlAfter(basic, nameof(FlipController.GetReverseNpcFlips), npc, true);
        var plainHeader = await CacheControlAfter(noRefresh, nameof(FlipController.GetReverseNpcFlips), npc, false);

        Assert.Multiple(() =>
        {
            Assert.That(premiumHeader, Is.EqualTo("no-store"));
            Assert.That(basicHeader, Is.Null);
            Assert.That(plainHeader, Is.Null);
        });
    }

    private static async Task<(object Result, Exception Error)> Run<T>(ControllerUnderTestHandle<T> handle, string action, params object[] args)
        where T : ControllerBase
    {
        var method = typeof(T).GetMethod(action, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            var task = (Task)method.Invoke(handle.Controller, args)!;
            await task;
            return (task.GetType().GetProperty("Result")!.GetValue(task), null);
        }
        catch (Exception e)
        {
            return (null, e);
        }
    }

    private static Dictionary<string, string> Page(int page) => new() { ["page"] = page.ToString() };

    private static void AssertNeverShared(HttpResponseMessage response)
    {
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True, "no-store keeps Cloudflare from caching it");
        Assert.That(response.Headers.CacheControl?.Public, Is.False);
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string url, params (string, string)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-ClientId", "cache-regression-test");
        foreach (var (name, value) in headers)
            request.Headers.Add(name, value);
        return await client.SendAsync(request);
    }

    private static Microsoft.Extensions.Hosting.IHost CreateKeyHost()
    {
        var state = new StateObject
        {
            ExtractedInfo = new ExtractedInfo { CurrentServer = VictimServer }
        };
        var stateApi = Stub<IPlayerStateApi>((method, _) => method.Name == nameof(IPlayerStateApi.PlayerStatePlayerIdGetAsync)
            ? Task.FromResult(state)
            : EmptyValue(method.ReturnType));
        return FlipControllerCacheTests.CreateHost(out _, builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ApiKeyService>(new VictimKeyService());
            services.AddSingleton(stateApi);
            services.AddSingleton(FlipControllerCacheTests.CreateProxy<IBazaarProfitApi>());
        }));
    }

    private static Microsoft.Extensions.Hosting.IHost CreateHost(
        out FlipControllerCacheTests.TestPremiumTierService premium, Action<IWebHostBuilder> configure) =>
        FlipControllerCacheTests.CreateHost(out premium, configure);

    private static object EmptyValue(Type returnType)
    {
        if (returnType == typeof(Task))
            return Task.CompletedTask;
        var result = returnType.GenericTypeArguments[0];
        var value = result.IsValueType ? Activator.CreateInstance(result) : null;
        return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(result).Invoke(null, [value]);
    }

    private static T Stub<T>(Func<MethodInfo, object[], object> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, StubProxy>();
        ((StubProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    /// <summary>Forwards every call to a delegate.</summary>
    public class StubProxy : DispatchProxy
    {
        /// <summary>Handles the call.</summary>
        public Func<MethodInfo, object[], object> Handler { get; set; }

        /// <summary>Performs the invoke operation.</summary>
        protected override object Invoke(MethodInfo targetMethod, object[] args) => Handler(targetMethod, args);
    }

    private sealed class VictimKeyService : ApiKeyService
    {
        public VictimKeyService() : base(null) { }

        public override Task<ApiKeyInfoResponse> GetKeyInfo(ControllerBase controller)
        {
            if (controller.Request.Headers["X-Api-Key"] != VictimKey)
                throw new CoflnetException("unauthorized", "API key not provided");
            return Task.FromResult(new ApiKeyInfoResponse { MinecraftUuid = Guid.NewGuid().ToString() });
        }
    }

    private sealed class RecordingLeaderboard(List<DateTime> weeks) : ILeaderboardService
    {
        public Task<IEnumerable<LeaderboardService.LeaderboardEntry>> GetTopFlippers(
            string boardName = "sky-flipers", DateTime weekStartDate = default, int page = 0, int count = 10)
        {
            weeks.Add(weekStartDate);
            return Task.FromResult<IEnumerable<LeaderboardService.LeaderboardEntry>>(
                [new() { PlayerId = "uuid", PlayerName = "name", Score = 1 }]);
        }

        public Task HideAccount(string userId, string accountId, DateTime expiresAt) => Task.CompletedTask;

        public Task UnHideAccount(string userId, string accountId) => Task.CompletedTask;
    }

    private static ControllerUnderTestHandle<T> NewController<T>(string token) where T : ControllerBase
    {
        var controller = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        var context = new DefaultHttpContext();
        context.Request.Headers["GoogleToken"] = token;
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        var premium = new FlipControllerCacheTests.TestPremiumTierService();
        var field = typeof(T).GetField("premiumTierService", BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(controller, premium);
        return new ControllerUnderTestHandle<T>(controller, premium);
    }

    private sealed record ControllerUnderTestHandle<T>(T Controller, FlipControllerCacheTests.TestPremiumTierService Premium)
        where T : ControllerBase;

    private static async Task<string> CacheControlAfter<T>(ControllerUnderTestHandle<T> handle, string action, params object[] args)
        where T : ControllerBase
    {
        await ThrownBy(handle, action, args);
        var header = handle.Controller.Response.Headers.CacheControl.ToString();
        return string.IsNullOrEmpty(header) ? null : header;
    }

    /// <summary>
    /// Runs the action, the dependencies behind the tier gate are missing so it ends in an exception.
    /// Returns the exception thrown by the gate itself, if any.
    /// </summary>
    private static async Task<Exception> ThrownBy<T>(ControllerUnderTestHandle<T> handle, string action, params object[] args)
        where T : ControllerBase
    {
        var method = typeof(T).GetMethod(action, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            await (Task)method.Invoke(handle.Controller, args)!;
            return null;
        }
        catch (Exception e)
        {
            return e is TargetInvocationException { InnerException: not null } wrapped ? wrapped.InnerException : e;
        }
    }
}
