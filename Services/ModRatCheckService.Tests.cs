using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Controller;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services;

#pragma warning disable CS1591
public class ModRatCheckServiceTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static IDistributedCache NewCache() => new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static ModController NewController(ModRatCheckService service)
    {
        // RatCheck only touches ratCheckService and HttpContext, so every other dependency can stay null
        var controller = new ModController(null, null, null, null, null, null, null, null, null, null, null)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return controller;
    }

    private class Backend(HttpStatusCode status, string body, string contentType = "application/json") : HttpMessageHandler
    {
        public int Calls;
        public string LastRequestedPath;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequestedPath = request.RequestUri.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType) } }
            });
        }
    }

    private static (ModRatCheckService service, Backend backend) NewService(HttpStatusCode status, string body, IDistributedCache cache = null, string contentType = "application/json")
    {
        var backend = new Backend(status, body, contentType);
        var client = new HttpClient(backend) { BaseAddress = new Uri("https://isthisarat.com/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(ModRatCheckService.HttpClientName)).Returns(client);
        return (new ModRatCheckService(factory.Object, cache ?? NewCache()), backend);
    }

    [TestCase("too-short")]
    [TestCase("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")] // 64 chars but not hex
    [TestCase(null)]
    public async Task InvalidHash_RejectsWithoutCallingUpstream(string hash)
    {
        var (service, backend) = NewService(HttpStatusCode.OK, "{\"rat\":\"no\",\"md5return\":\"x\"}");
        var controller = NewController(service);

        var result = await controller.RatCheck(hash, service);

        var badRequest = result as BadRequestObjectResult;
        Assert.That(badRequest, Is.Not.Null);
        Assert.That(((ErrorResponse)badRequest.Value).Slug, Is.EqualTo("invalid_hash"));
        Assert.That(backend.Calls, Is.EqualTo(0), "an invalid hash must never reach the upstream service");
    }

    [Test]
    public async Task ValidHash_ReturnsUpstreamVerdict_AndSecondLookupIsServedFromCache()
    {
        var (service, backend) = NewService(HttpStatusCode.OK, "{\"rat\":\"no\",\"md5return\":\"deadbeef\"}");

        var first = await service.CheckAsync(Hash);
        var second = await service.CheckAsync(Hash);

        Assert.That(first.IsAvailable, Is.True);
        Assert.That(first.Response.Rat, Is.EqualTo("no"));
        Assert.That(first.Response.Md5Return, Is.EqualTo("deadbeef"));
        Assert.That(second.IsAvailable, Is.True);
        Assert.That(second.Response.Md5Return, Is.EqualTo("deadbeef"));
        Assert.That(backend.Calls, Is.EqualTo(1), "the second lookup for the same hash must be served from the cache");
        Assert.That(backend.LastRequestedPath, Is.EqualTo($"/api/signature/{Hash}"));
    }

    [Test]
    public async Task ControllerNormalizesHashToLowercaseBeforeCallingUpstream()
    {
        var (service, backend) = NewService(HttpStatusCode.OK, "{\"rat\":\"no\",\"md5return\":\"deadbeef\"}");
        var controller = NewController(service);

        var result = await controller.RatCheck(Hash.ToUpperInvariant(), service);

        var ok = result as OkObjectResult;
        Assert.That(ok, Is.Not.Null);
        Assert.That(((RatCheckingResponse)ok.Value).Rat, Is.EqualTo("no"));
        Assert.That(backend.LastRequestedPath, Is.EqualTo($"/api/signature/{Hash}"), "the upstream lookup must use the normalized lowercase hash");
    }

    [Test]
    public async Task CloudflareChallengeResponse_MapsToUnavailable_AndIsNotCachedAsSuccess()
    {
        var (service, backend) = NewService(HttpStatusCode.Forbidden,
            "<html><head><title>Just a moment...</title></head><body>cf-challenge</body></html>", contentType: "text/html");

        var first = await service.CheckAsync(Hash);
        var second = await service.CheckAsync(Hash);

        Assert.That(first.IsAvailable, Is.False);
        Assert.That(first.Response, Is.Null);
        Assert.That(second.IsAvailable, Is.False, "a challenge response must never be cached as a success");
        Assert.That(backend.Calls, Is.EqualTo(1), "repeated lookups while negative-cached must not hammer the upstream again");
    }

    [Test]
    public async Task UnavailableResult_ControllerReturns503WithUnavailableSlug()
    {
        var (service, _) = NewService(HttpStatusCode.Forbidden, "<html>cf-challenge</html>", contentType: "text/html");
        var controller = NewController(service);

        var result = await controller.RatCheck(Hash, service);

        var objectResult = result as ObjectResult;
        Assert.That(objectResult, Is.Not.Null);
        Assert.That(objectResult.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(((ErrorResponse)objectResult.Value).Slug, Is.EqualTo("ratcheck_unavailable"));
        Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"),
            "an unavailable answer must not be cached by the cdn/browser for the success duration");
    }

    [Test]
    public async Task InvalidHash_IsNotCacheable()
    {
        var (service, _) = NewService(HttpStatusCode.OK, "{\"rat\":\"No\",\"md5return\":\"x\"}");
        var controller = NewController(service);

        await controller.RatCheck("too-short", service);

        Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [TestCase("{}")]
    [TestCase("{\"error\":\"rate limited\"}")]
    [TestCase("{\"rat\":\"\",\"md5return\":\"x\"}")]
    [TestCase("null")]
    public async Task JsonWithoutVerdict_MapsToUnavailable(string body)
    {
        var (service, _) = NewService(HttpStatusCode.OK, body);

        var result = await service.CheckAsync(Hash);

        Assert.That(result.IsAvailable, Is.False, "a response without a verdict must not be reported as a scan result");
        Assert.That(result.Response, Is.Null);
    }
}
#pragma warning restore CS1591
