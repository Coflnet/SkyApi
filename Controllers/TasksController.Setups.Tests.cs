using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Api.Controller;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Api.Services.Tasks;
using Coflnet.Sky.Api.Services.Tasks.Setups;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.Core;
using GearCluster = Coflnet.Sky.PlayerState.Client.Model.GearCluster;
using SkyApi.Services.Tasks.Setups;
using SkyApi.Services.Tasks.Setups.Measured;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using GearSetups = Coflnet.Sky.PlayerState.Client.Model.TaskGearSetups;

namespace SkyApi.Controllers;
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
public class TasksControllerSetupsTests
{
    private const double SecretBuyCost = 123_456_789;
    private const double FreeBuyCost = 5_000;

    /// <summary>Token header values: premium, premium_plus, starter, basic (logged in) or anything else (invalid)</summary>
    private sealed class TestTierService : PremiumTierService
    {
        public TestTierService() : base(null, null) { }

        public override Task<(GoogleUser user, string tier)> GetUserAndTier(ControllerBase controller)
        {
            var token = controller.Request.Headers[HeaderName].ToString();
            if (token == "slowpayments")
                throw new TaskCanceledException("HttpClient timeout");
            if (token == "paymentsdown")
                throw new InvalidOperationException("payments unavailable");
            if (token == "garbage")
                throw new CoflnetException("invalid_token", "rejected by google");
            (GoogleUser, string) result = token switch
            {
                "" => (null, "anonymous"),
                "premium" => (new GoogleUser { Id = 1 }, "premium"),
                "premium_plus" => (new GoogleUser { Id = 2 }, "premium_plus"),
                "starter" => (new GoogleUser { Id = 3 }, "starter_premium"),
                "basic" => (new GoogleUser { Id = 4 }, "logged_in"),
                _ => (null, "anonymous"),
            };
            return Task.FromResult(result);
        }
    }

    private static (TaskSetupsService, Mock<IItemResaleService>) CreateSetups(bool hasGear = true, bool hasTask = true,
        Func<string, ItemResaleQuote> quote = null)
    {
        var tasks = new Mock<IPublicTaskService>();
        tasks.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasTask ? [new PublicTaskEstimate { Name = "Task A", Slug = "task-a" }] : []);
        var gear = new Mock<IGearSetupService>();
        if (hasGear)
            gear.Setup(g => g.GetByTaskNameAsync("Task A", It.IsAny<CancellationToken>())).ReturnsAsync(Gear());
        var resale = new Mock<IItemResaleService>();
        resale.Setup(r => r.GetQuoteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tag, IReadOnlyDictionary<string, string> _, CancellationToken _) =>
                quote?.Invoke(tag) ?? DefaultQuote(tag));
        return (SkyApi.Services.Tasks.Setups.Measured.MeasuredTestData.Service(tasks.Object, gear.Object, resale.Object), resale);
    }

    /// <summary>The free budget item is cheap, the mid and endgame items carry the secret number</summary>
    private static ItemResaleQuote DefaultQuote(string tag)
    {
        var price = tag == "NEW" ? FreeBuyCost : SecretBuyCost;
        return new ItemResaleQuote(tag, price, price, price * 0.9, 0.02, 0, price, 20, StabilityLabel.Stable);
    }

    private static GearSetups Gear() => SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Gear(
        SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Setup("baseline", "baseline", 0),
        SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Setup("budget", "upgrade", 10_000,
            SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Item("NEW")),
        SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Setup("mid", "upgrade", 20_000,
            SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Item("MID")),
        SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Setup("endgame", "upgrade", 30_000,
            SkyApi.Services.Tasks.Setups.PublicSetupsMapperTests.Item("END")));

    private static TasksController CreateController(string token = null)
    {
        var http = new DefaultHttpContext();
        if (token != null)
            http.Request.Headers["GoogleToken"] = token;
        return new TasksController(Mock.Of<IPublicTaskService>()) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private static ObjectResult Object(ActionResult<PublicTaskSetups> result) => (ObjectResult)result.Result;

    private static async Task<ObjectResult> Details(string token, TaskSetupsService setups, string slug = "task-a")
    {
        var controller = CreateController(token);
        var result = await controller.GetSetupDetails(slug, setups, new TestTierService());
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
        return Object(result);
    }

    [Test]
    public async Task Details_without_a_token_is_401_login_required()
    {
        var (setups, resale) = CreateSetups();

        var result = await Details(null, setups);

        result.StatusCode.Should().Be(401);
        result.Value.Should().BeOfType<ErrorResponse>().Which.Slug.Should().Be("login_required");
        resale.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Details_with_an_unresolvable_authorization_header_is_401_invalid_token()
    {
        var (setups, _) = CreateSetups();
        var controller = CreateController();
        controller.Request.Headers["Authorization"] = "Bearer unknown";

        var result = Object(await controller.GetSetupDetails("task-a", setups, new TestTierService()));

        result.StatusCode.Should().Be(401);
        result.Value.Should().BeOfType<ErrorResponse>().Which.Slug.Should().Be("invalid_token");
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Test]
    public async Task Details_when_the_tier_check_fails_is_503_not_500()
    {
        var (setups, resale) = CreateSetups();

        var result = await Details("paymentsdown", setups);

        result.StatusCode.Should().Be(503);
        result.Value.Should().BeOfType<ErrorResponse>().Which.Slug.Should().Be("premium_check_unavailable");
        resale.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Details_when_the_payments_call_times_out_is_503()
    {
        var (setups, _) = CreateSetups();

        var result = await Details("slowpayments", setups);

        result.StatusCode.Should().Be(503);
        result.Value.Should().BeOfType<ErrorResponse>().Which.Slug.Should().Be("premium_check_unavailable");
    }

    [Test]
    public async Task Details_with_a_rejected_token_is_401_invalid_token()
    {
        var (setups, _) = CreateSetups();

        var result = await Details("garbage", setups);

        result.StatusCode.Should().Be(401);
        result.Value.Should().BeOfType<ErrorResponse>().Which.Slug.Should().Be("invalid_token");
    }

    [TestCase("basic")]
    [TestCase("starter")]
    public async Task Details_without_premium_is_403_premium_required_and_computes_nothing(string token)
    {
        var (setups, resale) = CreateSetups();

        var result = await Details(token, setups);

        result.StatusCode.Should().Be(403);
        result.Value.Should().BeOfType<ErrorResponse>().Which.Slug.Should().Be("premium_required");
        resale.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Details_with_premium_plus_is_200_with_every_tier_unlocked()
    {
        var (setups, _) = CreateSetups();

        var result = await Details("premium_plus", setups);

        result.StatusCode.Should().Be(200);
        var body = result.Value.Should().BeOfType<PublicTaskSetups>().Which;
        body.Locked.Should().BeFalse();
        body.Setups.Should().OnlyContain(x => !x.Locked);
        body.Setups.Single(x => x.Tier == "mid").BuyCost.Should().Be(SecretBuyCost);
        body.Setups.Single(x => x.Tier == "budget").BuyCost.Should().Be(FreeBuyCost);
        var endgame = body.Setups.Single(x => x.Tier == "endgame");
        endgame.BuyCost.Should().Be(SecretBuyCost);
        endgame.Items.Should().ContainSingle().Which.ItemTag.Should().Be("END");
    }

    [Test]
    public async Task Details_with_premium_unlocks_mid_but_only_a_placeholder_for_endgame()
    {
        var (setups, _) = CreateSetups();

        var result = await Details("premium", setups);

        result.StatusCode.Should().Be(200);
        var body = result.Value.Should().BeOfType<PublicTaskSetups>().Which;
        body.Setups.Single(x => x.Tier == "mid").BuyCost.Should().Be(SecretBuyCost);
        body.Setups.Single(x => x.Tier == "budget").BuyCost.Should().Be(FreeBuyCost);
        var endgame = body.Setups.Single(x => x.Tier == "endgame");
        endgame.Locked.Should().BeTrue();
        endgame.RequiredTier.Should().Be("premium_plus");
        endgame.Items.Should().BeEmpty();
        endgame.BuyCost.Should().BeNull();
        endgame.Name.Should().BeNull();
    }

    [Test]
    public async Task Public_setups_are_200_with_numbers_for_free_tiers_only()
    {
        var (setups, resale) = CreateSetups();
        var controller = CreateController();

        var result = await controller.GetSetups("task-a", setups);

        var body = Object(result).Value.Should().BeOfType<PublicTaskSetups>().Which;
        body.Locked.Should().BeTrue();
        body.Setups.Single(x => x.Tier == "budget").BuyCost.Should().Be(FreeBuyCost);
        body.Setups.Where(x => x.Tier is "mid" or "endgame").Should().OnlyContain(x => x.Locked && x.BuyCost == null && x.CoinsPerHour == null);
        resale.Verify(r => r.GetQuoteAsync("MID", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Never);
        controller.Response.Headers.CacheControl.ToString().Should().NotBe("public, max-age=15");
    }

    [Test]
    public async Task Public_setups_with_a_degraded_free_quote_get_a_short_public_cache()
    {
        var (setups, _) = CreateSetups(quote: tag => ItemResaleQuote.Unknown(tag) with { Degraded = true });
        var controller = CreateController();

        var result = await controller.GetSetups("task-a", setups);

        var body = Object(result).Value.Should().BeOfType<PublicTaskSetups>().Which;
        body.Setups.Single(x => x.Tier == "budget").PriceIncomplete.Should().BeTrue();
        controller.Response.Headers.CacheControl.ToString().Should().Be("public, max-age=15");
    }

    [Test]
    public async Task Public_setups_still_work_when_price_lookups_throw()
    {
        var (setups, resale) = CreateSetups();
        resale.Setup(r => r.GetQuoteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("prices down"));
        var controller = CreateController();

        var result = await controller.GetSetups("task-a", setups);

        var body = Object(result).Value.Should().BeOfType<PublicTaskSetups>().Which;
        var budget = body.Setups.Single(x => x.Tier == "budget");
        budget.PriceIncomplete.Should().BeTrue();
        budget.BuyCost.Should().BeNull();
        budget.Items.Should().ContainSingle();
        body.Setups.Should().HaveCount(3);
        controller.Response.Headers.CacheControl.ToString().Should().Be("public, max-age=15");
    }

    [TestCase(false, true, "task_not_found")]
    [TestCase(true, false, "gear_setups_not_found")]
    public async Task Missing_task_or_profile_is_404_on_both_endpoints(bool hasTask, bool hasGear, string slug)
    {
        var (setups, _) = CreateSetups(hasGear, hasTask);
        var publicController = CreateController();

        var publicResult = Object(await publicController.GetSetups("task-a", setups));
        var detailsResult = await Details("premium", setups);

        foreach (var result in new[] { publicResult, detailsResult })
        {
            result.StatusCode.Should().Be(404);
            result.Value.Should().BeOfType<ErrorResponse>().Which.Slug.Should().Be(slug);
        }
        publicController.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Test]
    public async Task Upstream_outage_is_503()
    {
        var setups = new Mock<ITaskSetupsService>();
        setups.Setup(s => s.GetPublicAsync("task-a", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CoflnetException("gear_setups_unavailable", "down"));
        var controller = CreateController();

        var result = await controller.GetSetups("task-a", setups.Object);

        Object(result).StatusCode.Should().Be(503);
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Test]
    public void Details_action_is_marked_no_store()
    {
        var cache = typeof(TasksController).GetMethod(nameof(TasksController.GetSetupDetails))!.GetCustomAttribute<ResponseCacheAttribute>();

        cache.Should().NotBeNull();
        cache!.NoStore.Should().BeTrue();
        cache.Location.Should().Be(ResponseCacheLocation.None);
    }

    private static Microsoft.Extensions.Hosting.IHost CreateHost()
    {
        var (setups, _) = CreateSetups();
        return FlipControllerCacheTests.CreateHost(out _, builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<PremiumTierService>(new TestTierService());
            services.AddSingleton<ITaskSetupsService>(setups);
        }));
    }

    private static async Task<HttpResponseMessage> Get(HttpClient client, string url, string token = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-ClientId", "cache-regression-test");
        if (token != null)
            request.Headers.Add("GoogleToken", token);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// The response cache is keyed by path only. A premium answer for the details path must never be
    /// replayed to anonymous or non premium callers, and the public path never holds numbers.
    /// </summary>
    [Test]
    public async Task Details_premium_answer_is_not_served_to_anonymous_or_basic_callers()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        const string url = "/api/tasks/task-a/setups/details";

        var premium = await Get(client, url, "premium");
        var anonymous = await Get(client, url);
        var basic = await Get(client, url, "basic");
        var premiumAgain = await Get(client, url, "premium");

        var premiumBody = await premium.Content.ReadAsStringAsync();
        var anonymousBody = await anonymous.Content.ReadAsStringAsync();
        var basicBody = await basic.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(premium.StatusCode, Is.EqualTo(HttpStatusCode.OK), premiumBody);
            Assert.That(premiumBody, Does.Contain(((long)SecretBuyCost).ToString()));
            Assert.That(premium.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(anonymousBody, Does.Not.Contain(((long)SecretBuyCost).ToString()));
            Assert.That(basic.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(basicBody, Does.Not.Contain(((long)SecretBuyCost).ToString()));
            Assert.That(premiumAgain.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    /// <summary>
    /// The server side views of premium and premium plus differ. Whoever asks first, no lower tier and no anonymous caller
    /// may get the endgame items, and premium plus always gets them.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public async Task Premium_plus_endgame_is_never_served_to_premium_or_anonymous_callers(bool premiumPlusFirst)
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();
        const string details = "/api/tasks/task-a/setups/details";
        const string endgameItem = "\"itemTag\":\"END\"";

        var responses = new List<(string Who, HttpResponseMessage Response)>();
        foreach (var who in premiumPlusFirst ? new[] { "premium_plus", "premium", "premium_plus" } : new[] { "premium", "premium_plus", "premium" })
            responses.Add((who, await Get(client, details, who)));
        var anonymousDetails = await Get(client, details);
        var publicAnonymous = await Get(client, "/api/tasks/task-a/setups");
        var publicPremiumPlus = await Get(client, "/api/tasks/task-a/setups", "premium_plus");

        foreach (var (who, response) in responses)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True, "tier dependent views are never shared");
            if (who == "premium_plus")
                Assert.That(body, Does.Contain(endgameItem));
            else
                Assert.That(body, Does.Not.Contain("\"END\""));
        }
        foreach (var response in new[] { anonymousDetails, publicAnonymous, publicPremiumPlus })
            Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("\"END\""));
        var placeholder = ((JArray)JObject.Parse(await publicAnonymous.Content.ReadAsStringAsync())["setups"]!)
            .Single(x => x["tier"]!.Value<string>() == "endgame");
        Assert.That(placeholder["requiredTier"]!.Value<string>(), Is.EqualTo("premium_plus"));
    }

    [Test]
    public async Task Public_setups_never_contain_mid_or_endgame_numbers_even_after_a_premium_call()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        var premium = await Get(client, "/api/tasks/task-a/setups/details", "premium");
        var publicResponse = await Get(client, "/api/tasks/task-a/setups");
        var publicWithToken = await Get(client, "/api/tasks/task-a/setups", "premium");

        Assert.That(await premium.Content.ReadAsStringAsync(), Does.Contain(((long)SecretBuyCost).ToString()));
        foreach (var response in new[] { publicResponse, publicWithToken })
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            Assert.That(body, Does.Not.Contain(((long)SecretBuyCost).ToString()));
            var json = JObject.Parse(body);
            Assert.That(json["locked"]!.Value<bool>(), Is.True);
            var byTier = ((JArray)json["setups"]!).ToDictionary(x => x["tier"]!.Value<string>()!);
            Assert.That(byTier["budget"]["buyCost"]!.Value<double>(), Is.EqualTo(FreeBuyCost));
            Assert.That(byTier["budget"]["locked"]!.Value<bool>(), Is.False);
            foreach (var tier in new[] { "mid", "endgame" })
            {
                Assert.That(byTier[tier]["locked"]!.Value<bool>(), Is.True);
                Assert.That(byTier[tier]["buyCost"]!.Type, Is.EqualTo(JTokenType.Null));
                Assert.That(byTier[tier]["coinsPerHour"]!.Type, Is.EqualTo(JTokenType.Null));
            }
        }
        Assert.That(publicResponse.Headers.CacheControl?.Public, Is.True, "the public view stays cacheable");
        Assert.That(publicResponse.Headers.CacheControl?.MaxAge, Is.EqualTo(TimeSpan.FromSeconds(120)));
    }

    [Test]
    public async Task Public_setups_with_degraded_prices_have_a_short_cache_through_the_pipeline()
    {
        var (setups, _) = CreateSetups(quote: tag => ItemResaleQuote.Unknown(tag) with { Degraded = true });
        using var host = FlipControllerCacheTests.CreateHost(out _, builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<PremiumTierService>(new TestTierService());
            services.AddSingleton<ITaskSetupsService>(setups);
        }));
        using var client = host.GetTestClient();

        var response = await Get(client, "/api/tasks/task-a/setups");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.CacheControl?.Public, Is.True);
        Assert.That(response.Headers.CacheControl?.MaxAge, Is.EqualTo(TimeSpan.FromSeconds(15)));
    }

    [Test]
    public async Task Rejected_token_is_401_not_400_through_the_pipeline()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        var response = await Get(client, "/api/tasks/task-a/setups/details", "garbage");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), await response.Content.ReadAsStringAsync());
        Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
    }

    [Test]
    public async Task Unknown_slug_is_404_through_the_pipeline()
    {
        using var host = CreateHost();
        using var client = host.GetTestClient();

        var publicResponse = await Get(client, "/api/tasks/nope/setups");
        var details = await Get(client, "/api/tasks/nope/setups/details", "premium");

        Assert.Multiple(() =>
        {
            Assert.That(publicResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), publicResponse.Content.ReadAsStringAsync().Result);
            Assert.That(details.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    /// <summary>Measured clusters: free baseline and budget, the mid and endgame carry the sentinels of <see cref="TaskSetupsComposerTests"/></summary>
    private static TaskSetupsService MeasuredSetups()
    {
        var tasks = new Mock<IPublicTaskService>();
        tasks.Setup(t => t.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PublicTaskEstimate { Name = "Task A", Slug = "task-a" }]);
        GearCluster Secret(string tool, double rate, string helmet)
        {
            var cluster = MeasuredTestData.Cluster(tool, rate, players: TaskSetupsComposerTests.SecretPlayers, helmet: helmet,
                toolPiece: MeasuredTestData.Piece(tool, new Dictionary<string, int> { ["harvesting"] = 6 }));
            cluster.Hours = TaskSetupsComposerTests.SecretHours;
            cluster.StatMedians = new Dictionary<string, double> { ["Farming Fortune"] = TaskSetupsComposerTests.SecretStat };
            return cluster;
        }
        var clusters = MeasuredTestData.Clusters(null,
            MeasuredTestData.Cluster("BASE_HOE", 1_000_000),
            MeasuredTestData.Cluster("BUDGET_HOE", 1_500_000),
            Secret("MID_HOE", TaskSetupsComposerTests.MidRate, "MID_HELMET"),
            Secret("END_HOE", TaskSetupsComposerTests.EndRate, "END_HELMET"));
        clusters.ModifierEffects = TaskSetupsComposerTests.Effects;
        var prices = new Dictionary<string, (long Price, string Uuid)>
        {
            ["BASE_HOE"] = (500_000, "77777777777777777777777777777777"),
            ["BUDGET_HOE"] = (5_000_000, TaskSetupsComposerTests.BudgetUuid),
            ["MID_HOE"] = (TaskSetupsComposerTests.MidCost, TaskSetupsComposerTests.MidUuid),
            ["END_HOE"] = (TaskSetupsComposerTests.EndCost, TaskSetupsComposerTests.EndUuid),
        };
        var acquisition = new Mock<IGearAcquisitionService>();
        acquisition.Setup(a => a.GetAsync(It.IsAny<GearItemSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GearItemSpec spec, CancellationToken _) => prices.TryGetValue(spec.Tag, out var p)
                ? MeasuredTestData.Direct(p.Price, p.Uuid)
                : MeasuredTestData.Direct(1_000_000, "66666666666666666666666666666666"));
        var resale = new Mock<IItemResaleService>();
        resale.Setup(r => r.GetQuoteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tag, IReadOnlyDictionary<string, string> _, CancellationToken _) => MeasuredTestData.Quote(tag, 1000));
        return MeasuredTestData.Service(tasks.Object, Mock.Of<IGearSetupService>(), resale.Object,
            clusters: MeasuredTestData.Returning(new ClusterLookup(clusters, false)), acquisition: acquisition.Object);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Public_measured_setups_never_contain_locked_numbers_uuids_or_details_only_data_through_the_pipeline(bool publicFirst)
    {
        var setups = MeasuredSetups();
        using var host = FlipControllerCacheTests.CreateHost(out _, builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<PremiumTierService>(new TestTierService());
            services.AddSingleton<ITaskSetupsService>(setups);
        }));
        using var client = host.GetTestClient();

        var publicResponse = publicFirst ? await Get(client, "/api/tasks/task-a/setups") : null;
        var premium = await Get(client, "/api/tasks/task-a/setups/details", "premium_plus");
        publicResponse ??= await Get(client, "/api/tasks/task-a/setups");
        var publicWithToken = await Get(client, "/api/tasks/task-a/setups", "premium");
        var premiumBody = await premium.Content.ReadAsStringAsync();

        Assert.That(premium.StatusCode, Is.EqualTo(HttpStatusCode.OK), premiumBody);
        Assert.That(premium.Headers.CacheControl?.NoStore, Is.True);
        foreach (var sentinel in TaskSetupsComposerTests.Sentinels)
            Assert.That(premiumBody, Does.Contain(sentinel), "details carry " + sentinel);
        foreach (var response in new[] { publicResponse, publicWithToken })
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            foreach (var sentinel in TaskSetupsComposerTests.Sentinels)
                Assert.That(body, Does.Not.Contain(sentinel), "public leaks " + sentinel);
            Assert.That(body, Does.Contain("/viewauction " + TaskSetupsComposerTests.BudgetUuid), "the budget acquisition is free");
        }
    }
}

