using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AspNetCoreRateLimit;
using Coflnet.Payments.Client.Api;
using ApiException = Coflnet.Payments.Client.Client.ApiException;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Api.Helper;
using Coflnet.Sky.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using StackExchange.Redis;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
public class PremiumControllerPlayStoreHttpTests
{
    private const string Plan = "l_premium";
    private const string UserId = "42";
    private const string Token = "play-token-kept-private";
    private const string Purchase = """{"purchaseToken":"play-token-kept-private","subscriptionId":"premium","userId":"99","packageName":"other.app"}""";
    private Mock<IGooglePlaySubscriptionApi> payments;
    private Mock<IDatabase> db;
    private FlipControllerBazaarPlanTests.CapturingLogs logs;

    [SetUp]
    public void SetUp()
    {
        payments = new(MockBehavior.Strict);
        db = new(MockBehavior.Strict);
        logs = new();
    }

    [Test]
    public async Task Start_ReturnsTheSaleOfferAndSignsItsId()
    {
        const string offerId = "autumn-sale";
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var host = await StartCheckoutHost(key, offerId, 25);
        using var client = host.GetTestClient();

        using var response = await PostCheckout(client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(body.Value<string>("offerId"), Is.EqualTo(offerId));
        Assert.That(body.Value<int>("discountPercent"), Is.EqualTo(25));
        Assert.That(body.Value<string>("obfuscatedAccountId"), Is.EqualTo(UserId));
        Assert.That(body.Value<string>("playProductId"), Is.EqualTo("premium"));
        Assert.That(body.Value<string>("basePlanId"), Is.EqualTo("monthly"));
        Assert.That(body.Value<string>("accountLabel"), Is.EqualTo("é***@example.com"));

        var expiresAt = body.Value<long>("expiresAt").ToString(CultureInfo.InvariantCulture);
        var payload = $"coflnet-play-checkout-v2\n{UserId}\npremium\nmonthly\n{Plan}\né***@example.com\n{expiresAt}\n";
        var signature = WebEncoders.Base64UrlDecode(body.Value<string>("signature"));
        using var publicKey = ECDsa.Create(key.ExportParameters(false));
        bool Verify(string signedOfferId) => publicKey.VerifyData(Encoding.UTF8.GetBytes(payload + signedOfferId),
            signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.That(Verify(offerId), Is.True);
        Assert.That(Verify("another-sale"), Is.False);
    }

    [TestCase("", 25)]
    [TestCase("-sale", 25)]
    [TestCase("Sale", 25)]
    [TestCase("sale\n", 25)]
    [TestCase("0123456789012345678901234567890123456789012345678901234567890123", 25)]
    [TestCase("autumn-sale", null)]
    [TestCase("autumn-sale", 0)]
    [TestCase("autumn-sale", 100)]
    [TestCase(null, 25)]
    public async Task Start_RejectsMalformedSaleWithoutIssuingASignature(string offerId, int? discountPercent)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var host = await StartCheckoutHost(key, offerId, discountPercent);
        using var client = host.GetTestClient();

        using var response = await PostCheckout(client);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.That(body.Value<string>("slug"), Is.EqualTo("play_checkout_signing_unavailable"));
        Assert.That(body.ContainsKey("signature"), Is.False);
        payments.Verify(api => api.ApiGoogleplaySubscriptionEligibilityGetAsync(UserId, Plan, 0, default), Times.Once);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Complete_WorksAnonymouslyAndDoesNotResolveAnyAuthHeaders(bool sendAuth)
    {
        AllowRequests();
        payments.Setup(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.Is<GooglePlaySubscriptionRequest>(r =>
            r.UserId == null && r.PackageName == "com.coflnet.sky" && r.SubscriptionId == "premium" && r.PurchaseToken == Token),
            0, default)).ReturnsAsync(new GooglePlaySubscriptionResult(true, "active"));
        using var host = await StartHost();
        using var client = host.GetTestClient();
        if (sendAuth)
        {
            client.DefaultRequestHeaders.Add("Authorization", "Bearer ignored-authorization");
            client.DefaultRequestHeaders.Add("GoogleToken", "ignored-google-token");
        }

        using var response = await Post(client, "198.51.100.1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(JObject.Parse(await response.Content.ReadAsStringAsync()).Value<bool>("isValid"), Is.True);
        Assert.That(logs.Entries, Has.None.Contains("Authentication lookup was invoked"));
        Assert.That(logs.Entries, Has.None.Contains(Token));
    }

    [Test]
    public async Task Complete_LimitsTheEleventhRequestAcrossSlugsAndSpoofedHeaders()
    {
        var counts = new ConcurrentDictionary<RedisKey, int>();
        db.Setup(database => database.ScriptEvaluateAsync(PlaySubscriptionCompletionFilter.ReserveQuota,
                It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), CommandFlags.None))
            .ReturnsAsync((string _, RedisKey[] keys, RedisValue[] _, CommandFlags _) =>
                RedisResult.Create(counts.AddOrUpdate(keys[0], 1, (_, count) => count + 1) <= 10 ? 0 : 60));
        CompleteSuccessfully();
        using var host = await StartHost();
        using var client = host.GetTestClient();
        for (var i = 1; i <= 10; i++)
        {
            using var response = await Post(client, "198.51.100.1", $"203.0.113.{i}", slug: $"slug-{i}");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        using var limited = await Post(client, "198.51.100.1", "203.0.113.11");
        using var different = await Post(client, "198.51.100.2", "203.0.113.11");

        Assert.That(limited.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(limited.Headers.RetryAfter?.Delta, Is.EqualTo(TimeSpan.FromMinutes(1)));
        Assert.That(JObject.Parse(await limited.Content.ReadAsStringAsync()).Value<string>("status"), Is.EqualTo("rate_limited"));
        Assert.That(different.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        payments.Verify(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.IsAny<GooglePlaySubscriptionRequest>(), 0, default),
            Times.Exactly(11));
    }

    [TestCase("::ffff:10.0.0.2", "203.0.113.1", "203.0.113.1")]
    [TestCase("10.0.0.2", "::ffff:203.0.113.1", "203.0.113.1")]
    [TestCase("10.0.0.2", "203.0.113.1, 203.0.113.2", "10.0.0.2")]
    [TestCase("10.0.0.2", "fe80::1%1", "10.0.0.2")]
    [TestCase("10.0.0.2", null, "10.0.0.2")]
    [TestCase("198.51.100.10", "203.0.113.1", "198.51.100.10")]
    public void ClientIp_TrustsOnlyASingleValidCloudflareHeaderFromAnApprovedPeer(string remote, string forwarded, string expected)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (forwarded != null)
            context.Request.Headers["CF-Connecting-IP"] = forwarded;
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";

        Assert.That(PremiumController.GetClientIp(context, ["10.0.0.0/24"]), Is.EqualTo(expected));
    }

    [TestCase("10.244.0.2", "198.51.100.1", null, "198.51.100.1")]
    [TestCase("10.244.0.2", "198.51.100.2", null, "198.51.100.2")]
    [TestCase("10.244.0.2", "198.51.100.1", "192.0.2.0/24", "10.244.0.2")]
    [TestCase("192.0.2.2", "198.51.100.1", "192.0.2.0/24", "198.51.100.1")]
    [TestCase("192.0.2.2", "198.51.100.1", null, "192.0.2.2")]
    [TestCase("2001:db8:0:1::1", null, null, "2001:db8:0:1::/64")]
    [TestCase("10.244.0.2", "2001:db8:0:1:ffff::1", null, "2001:db8:0:1::/64")]
    [TestCase("2001:db8:0:2::1", null, null, "2001:db8:0:2::/64")]
    [TestCase("::ffff:198.51.100.1", null, null, "198.51.100.1")]
    public async Task Complete_UsesTheExpectedClientAndSharedQuotaKeys(string remote, string forwarded,
        string trustedProxies, string expectedClient)
    {
        AllowRequests();
        CompleteSuccessfully();
        using var host = await StartHost(trustedProxies);
        using var client = host.GetTestClient();

        using var response = await Post(client, remote, forwarded);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        db.Verify(database => database.ScriptEvaluateAsync(PlaySubscriptionCompletionFilter.ReserveQuota,
            It.Is<RedisKey[]>(keys => keys.SequenceEqual(new RedisKey[]
            {
                $"{{ratelimit:play-subscription-complete}}:clients:{expectedClient}",
                "{ratelimit:play-subscription-complete}:global"
            })), null, CommandFlags.None), Times.Once);
    }

    [Test]
    public async Task Complete_MalformedJsonIsCountedAndItsTokenIsNotLogged()
    {
        AllowRequests();
        using var host = await StartHost();
        using var client = host.GetTestClient();

        using var response = await Post(client, "198.51.100.1", body: """{"purchaseToken":{"play-token-kept-private":""");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(JObject.Parse(body).Value<string>("status"), Is.EqualTo("invalid_purchase"));
        Assert.That(body, Does.Not.Contain(Token));
        Assert.That(logs.Entries, Has.None.Contains(Token));
        db.VerifyAll();
        payments.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Complete_PaymentsExceptionsCannotLogOrReturnTheToken()
    {
        AllowRequests();
        payments.Setup(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.IsAny<GooglePlaySubscriptionRequest>(), 0, default))
            .ThrowsAsync(new ApiException(500, Token, Token));
        using var host = await StartHost();
        using var client = host.GetTestClient();

        using var response = await Post(client, "198.51.100.1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain(Token));
        Assert.That(logs.Entries, Has.None.Contains(Token));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Complete_ProtectsPaymentsWhenRateLimiterIsUnavailable(bool timeout)
    {
        db.Setup(database => database.ScriptEvaluateAsync(PlaySubscriptionCompletionFilter.ReserveQuota,
                It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), CommandFlags.None))
            .ThrowsAsync(timeout ? new RedisTimeoutException(Token, CommandStatus.Sent)
                : new RedisConnectionException(ConnectionFailureType.UnableToConnect, Token));
        using var host = await StartHost();
        using var client = host.GetTestClient();

        using var response = await Post(client, "198.51.100.1");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(JObject.Parse(body).Value<string>("status"), Is.EqualTo("verification_unavailable"));
        Assert.That(body, Does.Not.Contain(Token));
        Assert.That(logs.Entries, Has.None.Contains(Token));
        payments.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Startup_UsesOnlyThePlayQuotaForCompleteAndTheGeneralQuotaForOtherRoutes()
    {
        const int retryAfter = 17;
        db.SetupSequence(database => database.ScriptEvaluateAsync(PlaySubscriptionCompletionFilter.ReserveQuota,
                It.IsAny<RedisKey[]>(), null, CommandFlags.None))
            .ReturnsAsync(RedisResult.Create(0))
            .ReturnsAsync(RedisResult.Create(retryAfter));
        CompleteSuccessfully();
        using var host = await StartHost();
        using var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-Remote-IP", "198.51.100.1");
        client.DefaultRequestHeaders.Add("X-ClientId", "test-client");

        using var otherFirst = await client.GetAsync("/api/blacklist/status");
        using var otherLimited = await client.GetAsync("/api/blacklist/status");
        using var completeFirst = await Post(client, "198.51.100.1");
        using var completeLimited = await Post(client, "198.51.100.1");

        Assert.That(otherFirst.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(otherLimited.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(completeFirst.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(completeLimited.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(completeLimited.Headers.RetryAfter?.Delta, Is.EqualTo(TimeSpan.FromSeconds(retryAfter)));
        Assert.That(JObject.Parse(await completeLimited.Content.ReadAsStringAsync()).Value<string>("status"),
            Is.EqualTo("rate_limited"));
        db.Verify(database => database.ScriptEvaluateAsync(PlaySubscriptionCompletionFilter.ReserveQuota,
            It.IsAny<RedisKey[]>(), null, CommandFlags.None), Times.Exactly(2));
        payments.Verify(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.IsAny<GooglePlaySubscriptionRequest>(), 0, default),
            Times.Once);
    }

    private void CompleteSuccessfully() => payments
        .Setup(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.IsAny<GooglePlaySubscriptionRequest>(), 0, default))
        .ReturnsAsync(new GooglePlaySubscriptionResult(true, "active"));

    private void AllowRequests() => db.Setup(database => database.ScriptEvaluateAsync(PlaySubscriptionCompletionFilter.ReserveQuota,
        It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), CommandFlags.None)).ReturnsAsync(RedisResult.Create(0));

    private Task<IHost> StartCheckoutHost(ECDsa key, string offerId, int? discountPercent)
    {
        payments.Setup(api => api.ApiGoogleplaySubscriptionEligibilityGetAsync(UserId, Plan, 0, default))
            .ReturnsAsync(new GooglePlayEligibility(true, playProductId: "premium", basePlanId: "monthly",
                offerId: offerId, discountPercent: discountPercent));
        return StartHost(signingKey: Convert.ToBase64String(key.ExportPkcs8PrivateKey()));
    }

    private Task<IHost> StartHost(string trustedProxies = null, string signingKey = null)
    {
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(-1, null)).Returns(db.Object);
        return new HostBuilder().ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs))
            .ConfigureAppConfiguration((_, config) =>
            {
                config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
                if (trustedProxies != null)
                    config.AddInMemoryCollection(new Dictionary<string, string> { ["PLAY_CHECKOUT_TRUSTED_PROXIES"] = trustedProxies });
                if (signingKey != null)
                    config.AddInMemoryCollection(new Dictionary<string, string> { [PlayCheckoutSigner.ConfigurationKey] = signingKey });
            })
            .ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services => ConfigureTestServices(services, redis.Object))
                .Configure(ConfigureTestPipeline)).StartAsync();
    }

    private void ConfigureTestServices(IServiceCollection services, IConnectionMultiplexer redis)
    {
        services.AddControllers().AddApplicationPart(typeof(PremiumController).Assembly).AddNewtonsoftJson();
        services.PostConfigure<MvcOptions>(options => options.InputFormatters.Insert(0, new PlaySubscriptionPurchaseInputFormatter()));
        services.AddAuthentication("CustomScheme")
            .AddScheme<CustomAuthenticationSchemeOptions, CustomAuthenticationHandler>("CustomScheme", _ => { });
        services.AddAuthorization();
        services.AddCors();
        services.AddSwaggerGen();
        services.AddResponseCaching();
        services.AddResponseCompression();
        services.AddMemoryCache();
        services.AddInMemoryRateLimiting();
        services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
        services.Configure<ClientRateLimitOptions>(options =>
        {
            options.HttpStatusCode = StatusCodes.Status429TooManyRequests;
            options.GeneralRules = [new RateLimitRule { Endpoint = "*", Period = "1m", Limit = 1 }];
        });
        services.AddSingleton(new PremiumTierService(null, null));
        services.AddSingleton(new PlayCheckoutAccountLabel((_, _) => Task.FromResult<Coflnet.Sky.McConnect.Model.User>(null),
            _ => Task.FromResult<string>(null), TimeSpan.FromSeconds(2)));
        services.AddSingleton(Mock.Of<IScrapingDetectionService>());
        services.AddSingleton<GoogletokenService>(_ => throw new InvalidOperationException("Authentication lookup was invoked"));
        services.AddSingleton(redis);
        services.AddSingleton(payments.Object);
        services.AddTransient(sp => new PremiumController(null, null, null, null, null,
            sp.GetRequiredService<ILogger<PremiumController>>(), null, sp.GetRequiredService<IConfiguration>(), null));
        services.AddControllers().AddControllersAsServices();
    }

    private static void ConfigureTestPipeline(IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers["X-Test-Remote-IP"].Single());
            if (context.Request.Headers.TryGetValue("X-Test-Checkout-User", out var userId))
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Email, "éclair@example.com")], "test"));
            await next();
        });
        var services = app.ApplicationServices;
        new Startup(services.GetRequiredService<IConfiguration>()).Configure(app,
            services.GetRequiredService<IWebHostEnvironment>(), services.GetRequiredService<ILogger<Startup>>(), null);
    }

    private static async Task<HttpResponseMessage> PostCheckout(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/premium/subscription/{Plan}/playstore");
        request.Headers.Add("X-Test-Remote-IP", "198.51.100.1");
        request.Headers.Add("X-Test-Checkout-User", UserId);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string remote, string forwarded = null,
        string slug = Plan, string body = Purchase)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/premium/subscription/{slug}/playstore/complete");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Add("X-Test-Remote-IP", remote);
        if (forwarded != null)
        {
            request.Headers.Add("CF-Connecting-IP", forwarded);
            request.Headers.Add("X-Forwarded-For", forwarded);
        }
        return await client.SendAsync(request);
    }
}
#pragma warning restore CS1591
