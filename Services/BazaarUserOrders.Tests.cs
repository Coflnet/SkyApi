using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Controller;
using Coflnet.Sky.PlayerState.Client.Api;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using StackExchange.Redis;

namespace Coflnet.Sky.Api.Services;

public class BazaarUserOrdersTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task ReturnsEngineFillsAndConfidenceForAuthenticatedPlayer(bool estimate)
    {
        var redis = new Mock<IConnectionMultiplexer>();
        var database = new Mock<IDatabase>();
        var state = new Mock<IPlayerStateApi>();
        redis.Setup(r => r.GetDatabase(-1, null)).Returns(database.Object);
        var payload = JsonConvert.SerializeObject(new {
            UserId = "1", ItemNames = new { WHEAT = "Wheat" }, Orders = new[] {
                new { ItemId = "WHEAT", PlayerName = "Ekwav", Amount = 64, Filled = 32, IsEstimate = estimate, IsSell = true, IsExpired = true, Claimed = 16 },
                new { ItemId = "WHEAT", PlayerName = "OtherPlayer", Amount = 20, Filled = 20, IsEstimate = false, IsSell = false, IsExpired = false, Claimed = 0 }
            }
        });
        database.Setup(d => d.StringGetAsync("bazaar:orders:v1:1", CommandFlags.None)).ReturnsAsync((RedisValue)payload);
        state.Setup(s => s.PlayerStatePlayerIdBazaarGetAsync("Ekwav", 0, default)).ReturnsAsync(new List<Coflnet.Sky.PlayerState.Client.Model.Offer>());
        var result = await new BazaarUserOrders(redis.Object, state.Object).Get("1", "Ekwav");
        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result.Single().FilledAmount, Is.EqualTo(32));
        Assert.That(result.Single().IsEstimate, Is.EqualTo(estimate));
        Assert.That(result.Single().IsExpired, Is.True);
        Assert.That(result.Single().ClaimedAmount, Is.EqualTo(16));
        Assert.That(result.Single().ItemName, Is.EqualTo("Wheat"));
        var serialized = JObject.FromObject(result.Single());
        Assert.That((long)serialized["filledAmount"], Is.EqualTo(32));
        Assert.That((bool)serialized["isEstimate"], Is.EqualTo(estimate));
    }

    [Test]
    public void LiveUserOrderResponseIsNeverPubliclyCached()
    {
        var cache = (ResponseCacheAttribute)typeof(PlayerController).GetMethod(nameof(PlayerController.GetPlayerOrders))
            .GetCustomAttributes(typeof(ResponseCacheAttribute), false).Single();
        Assert.That(cache.NoStore, Is.True);
        Assert.That(cache.Location, Is.EqualTo(ResponseCacheLocation.None));
    }
    private class Backend(HttpStatusCode status, string body = "{}") : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.That(request.RequestUri.AbsolutePath, Is.EqualTo("/OrderBook/user/1"));
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    public async Task OlderOrLoadingBazaarPreservesTheExistingOrdersEndpoint(HttpStatusCode status)
    {
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(-1, null)).Returns(Mock.Of<IDatabase>());
        var state = new Mock<IPlayerStateApi>();
        state.Setup(s => s.PlayerStatePlayerIdBazaarGetAsync("Ekwav", 0, default)).ReturnsAsync(new List<PlayerState.Client.Model.Offer> {
            new() { ItemTag = "WHEAT", ItemName = "Wheat", Amount = 64, IsSell = true, PricePerUnit = 10 }
        });
        using var backend = new Backend(status);
        using var client = new HttpClient(backend) { BaseAddress = new Uri("http://bazaar/") };
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(c => c.CreateClient("BazaarOrders")).Returns(client);
        var orders = await new BazaarUserOrders(redis.Object, state.Object, clients.Object).Get("1", "Ekwav");
        Assert.That(orders.Single().ItemTag, Is.EqualTo("WHEAT"));
        Assert.That(orders.Single().IsEstimate, Is.True);
        Assert.That(backend.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task LostRedisCacheRecoversFromAuthorityEvenWhenPlayerHistoryIsUnavailable()
    {
        var redis = new Mock<IConnectionMultiplexer>();
        var db = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(-1, null)).Returns(db.Object);
        db.Setup(d => d.StringGetAsync("bazaar:orders:v1:1", CommandFlags.None)).ThrowsAsync(new RedisServerException("restarting"));
        var state = new Mock<IPlayerStateApi>();
        state.Setup(s => s.PlayerStatePlayerIdBazaarGetAsync("Ekwav", 0, default))
            .ThrowsAsync(new HttpRequestException("history unavailable"));
        using var backend = new Backend(HttpStatusCode.OK,
            "{\"UserId\":\"1\",\"Orders\":[{\"ItemId\":\"WHEAT\",\"PlayerName\":\"Ekwav\",\"Amount\":64,\"Filled\":32,\"IsEstimate\":true}]}");
        using var client = new HttpClient(backend) { BaseAddress = new Uri("http://bazaar/") };
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(c => c.CreateClient("BazaarOrders")).Returns(client);
        var orders = await new BazaarUserOrders(redis.Object, state.Object, clients.Object).Get("1", "Ekwav");
        Assert.That(orders.Single().FilledAmount, Is.EqualTo(32));
        Assert.That(orders.Single().IsEstimate, Is.True);
    }

    [Test]
    public async Task CachedSnapshotLinksToItsPublisherTraceAndRecordsTheCacheHit()
    {
        Activity read = null;
        using var listener = new ActivityListener {
            ShouldListenTo = source => source.Name == BazaarTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = span => read = span
        };
        ActivitySource.AddActivityListener(listener);
        var redis = new Mock<IConnectionMultiplexer>();
        var db = new Mock<IDatabase>();
        redis.Setup(r => r.GetDatabase(-1, null)).Returns(db.Object);
        db.Setup(d => d.StringGetAsync("bazaar:orders:v1:1", CommandFlags.None)).ReturnsAsync((RedisValue)JsonConvert.SerializeObject(new {
            UserId = "1", Revision = 42, Orders = Array.Empty<object>(),
            TraceParent = "00-11111111111111111111111111111111-2222222222222222-01"
        }));
        var state = new Mock<IPlayerStateApi>();
        state.Setup(s => s.PlayerStatePlayerIdBazaarGetAsync("Ekwav", 0, default)).ReturnsAsync(new List<PlayerState.Client.Model.Offer>());
        var before = BazaarTelemetry.Reads.WithLabels("cache_hit").Value;
        Assert.That(await new BazaarUserOrders(redis.Object, state.Object).Get("1", "Ekwav"), Is.Empty);
        Assert.That(read.GetTagItem("bazaar.revision"), Is.EqualTo(42));
        Assert.That(read.Links.Single().Context.TraceId.ToString(), Is.EqualTo("11111111111111111111111111111111"));
        Assert.That(BazaarTelemetry.Reads.WithLabels("cache_hit").Value, Is.EqualTo(before + 1));
    }

}
