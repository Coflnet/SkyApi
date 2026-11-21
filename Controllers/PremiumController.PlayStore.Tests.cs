using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Client;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Api.Helper;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Moq;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
[NonParallelizable]
public class PremiumControllerPlayStoreTests
{
    private const string Plan = "l_premium";
    private const string UserId = "42";
    private const string PurchaseToken = "play-purchase-token";
    private static readonly PremiumController.PlaySubscriptionPurchase Purchase = new(PurchaseToken, "premium");
    private static readonly PlayCheckoutAccountLabel AccountLabel = new((_, _) => Task.FromResult<Coflnet.Sky.McConnect.Model.User>(null),
        _ => Task.FromResult<string>(null), TimeSpan.FromSeconds(2));
    private ECDsa signingKey;
    private Mock<IGooglePlaySubscriptionApi> playApi;

    [SetUp]
    public void SetUp()
    {
        TermsAcceptancePolicy.ResetForTests();
        playApi = new Mock<IGooglePlaySubscriptionApi>(MockBehavior.Strict);
        signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    }

    [TearDown]
    public void TearDown()
    {
        signingKey.Dispose();
        TermsAcceptancePolicy.ResetForTests();
    }

    [TestCase(nameof(PremiumController.StartPlayStoreSubscription), 200, typeof(PremiumController.PlaySubscriptionCheckout))]
    [TestCase(nameof(PremiumController.CompletePlayStoreSubscription), 200, typeof(GooglePlaySubscriptionResult))]
    [TestCase(nameof(PremiumController.CompletePlayStoreSubscription), 400, typeof(GooglePlaySubscriptionResult))]
    [TestCase(nameof(PremiumController.CompletePlayStoreSubscription), 429, typeof(GooglePlaySubscriptionResult))]
    public void Actions_DeclareResponseType(string action, int status, Type responseType)
    {
        var response = typeof(PremiumController).GetMethod(action).GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == status);

        Assert.That(response.Type, Is.EqualTo(responseType));
    }

    [Test]
    public void CheckoutSchema_RequiresEveryField()
    {
        var services = new ServiceCollection();
        services.AddControllers().AddNewtonsoftJson();
        services.AddSwaggerGenNewtonsoftSupport();
        services.AddSwaggerGen(options => options.SchemaFilter<RequireNonNullablePropertiesSchemaFilter>());
        using var provider = services.BuildServiceProvider();
        var schemas = new SchemaRepository();
        provider.GetRequiredService<ISchemaGenerator>()
            .GenerateSchema(typeof(PremiumController.PlaySubscriptionCheckout), schemas);

        Assert.That(schemas.Schemas[nameof(PremiumController.PlaySubscriptionCheckout)].Required, Is.EquivalentTo(new[]
            { "obfuscatedAccountId", "playProductId", "basePlanId", "accountLabel", "expiresAt", "signature" }));
    }

    [Test]
    public async Task Start_ReturnsExactSignedCheckout()
    {
        playApi.Setup(api => api.ApiGoogleplaySubscriptionEligibilityGetAsync(UserId, Plan, 0, default))
            .ReturnsAsync(new GooglePlayEligibility(true, playProductId: "premium", basePlanId: "monthly"));

        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var result = await Controller(email: "éclair@example.com").StartPlayStoreSubscription(Plan, playApi.Object, AccountLabel);
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        var checkout = (PremiumController.PlaySubscriptionCheckout)((OkObjectResult)result.Result).Value;
        var json = JObject.FromObject(checkout, JsonSerializer.Create(new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
        }));
        Assert.That(json.Properties().Select(p => p.Name), Is.EquivalentTo(new[]
            { "obfuscatedAccountId", "playProductId", "basePlanId", "accountLabel", "expiresAt", "signature" }));
        Assert.Multiple(() =>
        {
            Assert.That(checkout.ObfuscatedAccountId, Is.EqualTo(UserId));
            Assert.That(checkout.PlayProductId, Is.EqualTo("premium"));
            Assert.That(checkout.BasePlanId, Is.EqualTo("monthly"));
            Assert.That(checkout.AccountLabel, Is.EqualTo("é***@example.com"));
            Assert.That(checkout.ExpiresAt, Is.InRange(before + 600, after + 600));
            Assert.That(checkout.Signature, Does.Match("^[A-Za-z0-9_-]+$"));
        });

        using var publicKey = ECDsa.Create(signingKey.ExportParameters(false));
        var signature = WebEncoders.Base64UrlDecode(checkout.Signature);
        var lines = new[] { "coflnet-play-checkout-v1", UserId, "premium", "monthly", Plan, "é***@example.com",
            checkout.ExpiresAt.ToString(CultureInfo.InvariantCulture) };
        bool Verify(string payload) => publicKey.VerifyData(Encoding.UTF8.GetBytes(payload), signature,
            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.That(Verify(string.Join('\n', lines)), Is.True);
        for (var i = 0; i < lines.Length; i++)
        {
            var tampered = lines.ToArray();
            tampered[i] = i == 6 ? (checkout.ExpiresAt + 1).ToString(CultureInfo.InvariantCulture) : tampered[i] + "x";
            Assert.That(Verify(string.Join('\n', tampered)), Is.False, $"Tampered line {i + 1}");
        }
    }

    [TestCase(null)]
    [TestCase("invalid base64")]
    [TestCase("AQID")]
    [TestCase("wrong curve")]
    [TestCase("trailing bytes")]
    public async Task Start_FailsClosedOnInvalidKey_WhileCompletionStillWorks(string configuredKey)
    {
        if (configuredKey == "wrong curve")
        {
            using var wrongCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            configuredKey = Convert.ToBase64String(wrongCurve.ExportPkcs8PrivateKey());
        }
        if (configuredKey == "trailing bytes")
            configuredKey = Convert.ToBase64String([.. signingKey.ExportPkcs8PrivateKey(), 0]);

        var controller = Controller(configuredKey: configuredKey);
        var result = await controller.StartPlayStoreSubscription(Plan, playApi.Object, AccountLabel);

        Assert.That(result.Result, Is.InstanceOf<ObjectResult>());
        var response = (ObjectResult)result.Result;
        Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
        Assert.That(response.Value, Is.InstanceOf<ErrorResponse>());
        Assert.That(((ErrorResponse)response.Value).Slug, Is.EqualTo("play_checkout_signing_unavailable"));
        playApi.VerifyNoOtherCalls();

        playApi.Setup(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.IsAny<GooglePlaySubscriptionRequest>(), 0, default))
            .ReturnsAsync(new GooglePlaySubscriptionResult(true, "active"));
        var complete = await controller.CompletePlayStoreSubscription(Plan, Purchase, playApi.Object);
        AssertResult(complete, 200, true, "active", null);
    }

    [TestCase(Plan, "active_subscription", "stripe", "billed by stripe")]
    [TestCase(Plan, "active_subscription", null, "already have an active subscription")]
    [TestCase("unknown", "plan_unavailable", null, "unavailable in Google Play")]
    public void Start_UsesEligibilityReasonAsErrorSlug(string plan, string reason, string provider, string message)
    {
        playApi.Setup(api => api.ApiGoogleplaySubscriptionEligibilityGetAsync(UserId, plan, 0, default))
            .ReturnsAsync(new GooglePlayEligibility(false, reason, blockingProvider: provider));

        var error = Assert.ThrowsAsync<CoflnetException>(() => Controller().StartPlayStoreSubscription(plan, playApi.Object, AccountLabel));

        Assert.That(error.Slug, Is.EqualTo(reason));
        Assert.That(error.Message, Does.Contain(message));
    }

    [Test]
    public async Task Start_RequiresCurrentTermsWhenEnforced()
    {
        TermsAcceptancePolicy.Initialize(new("skycofl", "current", "", "https://example.com/terms",
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1), []));

        var result = await Controller(enforceTerms: true).StartPlayStoreSubscription(Plan, playApi.Object, AccountLabel);

        Assert.That(result.Result, Is.InstanceOf<ObjectResult>());
        var response = (ObjectResult)result.Result;
        Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status428PreconditionRequired));
        Assert.That(JObject.FromObject(response.Value).Value<string>("slug"), Is.EqualTo("terms_acceptance_required"));
        playApi.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Start_RejectsMissingUser()
    {
        var controller = Controller(authenticated: false);
        var start = await controller.StartPlayStoreSubscription(Plan, playApi.Object, AccountLabel);

        Assert.That(start.Result, Is.TypeOf<UnauthorizedObjectResult>());
        playApi.VerifyNoOtherCalls();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("premium\u0000")]
    [TestCase("premium\n")]
    [TestCase("premium\u007f")]
    public async Task Complete_UsesPlanWhenSubscriptionIdIsInvalid(string subscriptionId)
    {
        playApi.Setup(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.Is<GooglePlaySubscriptionRequest>(r =>
            r.SubscriptionId == Plan && r.PurchaseToken == PurchaseToken), 0, default))
            .ReturnsAsync(new GooglePlaySubscriptionResult(true, "active"));

        var result = await Controller().CompletePlayStoreSubscription(Plan, new(PurchaseToken, subscriptionId), playApi.Object);

        AssertResult(result, 200, true, "active", null);
    }

    [Test]
    public async Task Complete_UsesPlanWhenSubscriptionIdIsOversized()
    {
        await Complete_UsesPlanWhenSubscriptionIdIsInvalid(new string('a', 129));
    }

    [Test]
    public async Task Complete_ForwardsSubscriptionIdAtLengthLimit()
    {
        var subscriptionId = new string('a', 128);
        playApi.Setup(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.Is<GooglePlaySubscriptionRequest>(r =>
            r.SubscriptionId == subscriptionId), 0, default)).ReturnsAsync(new GooglePlaySubscriptionResult(true, "active"));

        AssertResult(await Controller().CompletePlayStoreSubscription(Plan, new(PurchaseToken, subscriptionId), playApi.Object),
            200, true, "active", null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("token value")]
    [TestCase("token\tvalue")]
    [TestCase("token\nvalue")]
    [TestCase("token\u0000value")]
    [TestCase("token\u007fvalue")]
    [TestCase("tokené")]
    [TestCase("token 😀")]
    public async Task Complete_RejectsInvalidPurchaseToken(string token)
    {
        var result = await Controller().CompletePlayStoreSubscription(Plan, new(token, "premium"), playApi.Object);

        AssertResult(result, 400, false, "invalid_purchase", "A valid Play purchase token is required.");
        playApi.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Complete_RejectsOversizedToken()
    {
        await Complete_RejectsInvalidPurchaseToken(new string('a', 4097));
    }

    [TestCase("abcdefghijklmnopqrstuvwxyz.AO-J1_s-8~5==")]
    [TestCase("aBc/+19==")]
    [TestCase("token?other=value")]
    [TestCase("token\"value")]
    public async Task Complete_AcceptsOpaqueAsciiToken(string token)
    {
        playApi.Setup(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.Is<GooglePlaySubscriptionRequest>(r =>
            r.PurchaseToken == token), 0, default)).ReturnsAsync(new GooglePlaySubscriptionResult(true, "active"));

        AssertResult(await Controller(authenticated: false).CompletePlayStoreSubscription(Plan, new(token, "premium"), playApi.Object),
            200, true, "active", null);
    }

    [Test]
    public async Task Complete_AcceptsAllPrintableAsciiAndMaximumLength()
    {
        var printableAscii = string.Concat(Enumerable.Range('!', '~' - '!' + 1).Select(value => (char)value));
        await Complete_AcceptsOpaqueAsciiToken(printableAscii);
        await Complete_AcceptsOpaqueAsciiToken(new string('a', 4096));
    }

    [TestCase("active", null, 200, "active")]
    [TestCase("cancelled", null, 200, "cancelled")]
    [TestCase("past_due", null, 200, "past_due")]
    [TestCase("unpaid", null, 200, "unpaid")]
    [TestCase("paused", null, 200, "paused")]
    [TestCase("pending", null, 200, "pending")]
    [TestCase("processing", null, 409, "processing")]
    [TestCase("verification_unavailable", null, 503, "verification_unavailable")]
    [TestCase(null, "verification_unavailable", 503, "verification_unavailable")]
    [TestCase("unsupported_state", null, 503, "unsupported_state")]
    [TestCase(null, "unsupported_state", 503, "unsupported_state")]
    [TestCase(null, "user_mismatch", 400, "user_mismatch")]
    [TestCase(null, "missing_user", 400, "missing_user")]
    [TestCase("missing_user", null, 400, "missing_user")]
    [TestCase("user_mismatch", null, 400, "user_mismatch")]
    [TestCase(null, "plan_unavailable", 400, "plan_unavailable")]
    [TestCase("expired", "invalid_purchase", 400, "invalid_purchase")]
    [TestCase("expired", null, 400, "expired")]
    [TestCase(null, null, 400, "invalid_purchase")]
    public async Task Complete_MapsPaymentsResults(string status, string error, int httpStatus, string expectedStatus)
    {
        playApi.Setup(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.IsAny<GooglePlaySubscriptionRequest>(), 0, default))
            .ReturnsAsync(new GooglePlaySubscriptionResult(false, status, error));

        var result = await Controller().CompletePlayStoreSubscription(Plan, Purchase, playApi.Object);

        AssertResult(result, httpStatus, false, expectedStatus, error);
    }

    [Test]
    public async Task Complete_MapsPaymentsConflictToProcessing()
    {
        playApi.Setup(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.IsAny<GooglePlaySubscriptionRequest>(), 0, default))
            .ThrowsAsync(new ApiException(409, "Payments request failed",
                """{"isValid":false,"status":"processing","errorMessage":null}"""));

        var result = await Controller().CompletePlayStoreSubscription(Plan, Purchase, playApi.Object);

        AssertResult(result, 409, false, "processing", null);
    }

    [TestCase(404)]
    [TestCase(408)]
    [TestCase(429)]
    [TestCase(401)]
    [TestCase(500)]
    [TestCase(0)]
    public async Task Complete_MapsOtherPaymentsApiExceptionsToRetryableResponse(int errorCode)
    {
        playApi.Setup(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.IsAny<GooglePlaySubscriptionRequest>(), 0, default))
            .ThrowsAsync(new ApiException(errorCode, "Payments request failed", """{"error":"Request failed"}"""));

        var result = await Controller().CompletePlayStoreSubscription(Plan, Purchase, playApi.Object);

        AssertResult(result, 503, false, "verification_unavailable", "verification_unavailable");
    }

    [Test]
    public async Task Complete_MapsNetworkFailureToRetryableResponse()
    {
        playApi.Setup(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.IsAny<GooglePlaySubscriptionRequest>(), 0, default))
            .ThrowsAsync(new HttpRequestException("Connection failed"));

        var result = await Controller().CompletePlayStoreSubscription(Plan, Purchase, playApi.Object);

        AssertResult(result, 503, false, "verification_unavailable", "verification_unavailable");
    }

    private PremiumController Controller(bool authenticated = true, bool enforceTerms = false,
        string email = "", string configuredKey = "test key")
    {
        var context = new DefaultHttpContext();
        if (authenticated)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId),
                new Claim(ClaimTypes.Email, email)], "test"));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["LEGAL:ENFORCE_CURRENT_TERMS"] = enforceTerms.ToString(),
                [PlayCheckoutSigner.ConfigurationKey] = configuredKey == "test key"
                    ? Convert.ToBase64String(signingKey.ExportPkcs8PrivateKey()) : configuredKey
            })
            .Build();
        return new PremiumController(null, null, null, null, null, null, null, config, null)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static void AssertResult(ActionResult<GooglePlaySubscriptionResult> result, int httpStatus, bool isValid,
        string status, string error)
    {
        Assert.That(result.Result, Is.InstanceOf<ObjectResult>());
        var response = (ObjectResult)result.Result;
        var body = JObject.FromObject(response.Value);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(httpStatus));
            Assert.That(body.Value<bool?>("isValid"), Is.EqualTo(isValid));
            Assert.That(body.Value<string>("status"), Is.EqualTo(status));
            Assert.That(body.ContainsKey("errorMessage"), Is.True);
            Assert.That(body.Value<string>("errorMessage"), Is.EqualTo(error));
        });
    }

    [Test]
    public void PurchaseStringFormNeverContainsTheToken()
    {
        var purchase = new PremiumController.PlaySubscriptionPurchase("secret-purchase-token", "premium");

        Assert.That(purchase.ToString(), Does.Not.Contain("secret-purchase-token"));
    }
}
#pragma warning restore CS1591
