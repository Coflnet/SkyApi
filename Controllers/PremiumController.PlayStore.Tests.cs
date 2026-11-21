using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Client;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Moq;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
[NonParallelizable]
public class PremiumControllerPlayStoreTests
{
    private const string Plan = "l_premium";
    private const string UserId = "42";
    private const string PurchaseToken = "play-purchase-token";
    private static readonly PremiumController.PlaySubscriptionPurchase Purchase = new(PurchaseToken, "premium");
    private Mock<IGooglePlaySubscriptionApi> playApi;

    [SetUp]
    public void SetUp()
    {
        TermsAcceptancePolicy.ResetForTests();
        playApi = new Mock<IGooglePlaySubscriptionApi>(MockBehavior.Strict);
    }

    [TearDown]
    public void TearDown() => TermsAcceptancePolicy.ResetForTests();

    [TestCase(nameof(PremiumController.StartPlayStoreSubscription), typeof(PremiumController.PlaySubscriptionCheckout))]
    [TestCase(nameof(PremiumController.CompletePlayStoreSubscription), typeof(GooglePlaySubscriptionResult))]
    public void Actions_DeclareSuccessfulResponseType(string action, Type responseType)
    {
        var response = typeof(PremiumController).GetMethod(action).GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == StatusCodes.Status200OK);

        Assert.That(response.Type, Is.EqualTo(responseType));
    }

    [Test]
    public async Task Start_UsesAuthenticatedIdAndReturnsPlayBillingIdentifiers()
    {
        playApi.Setup(api => api.ApiGoogleplaySubscriptionEligibilityGetAsync(UserId, Plan, 0, default))
            .ReturnsAsync(new GooglePlayEligibility(true, playProductId: "premium", basePlanId: "monthly"));

        var result = await Controller().StartPlayStoreSubscription(Plan, playApi.Object);

        Assert.That(result.Result, Is.TypeOf<OkObjectResult>());
        Assert.That(((OkObjectResult)result.Result).Value,
            Is.EqualTo(new PremiumController.PlaySubscriptionCheckout(UserId, "premium", "monthly")));
    }

    [TestCase(Plan, "active_subscription", "stripe", "billed by stripe")]
    [TestCase(Plan, "active_subscription", null, "already have an active subscription")]
    [TestCase("unknown", "plan_unavailable", null, "unavailable in Google Play")]
    public void Start_UsesEligibilityReasonAsErrorSlug(string plan, string reason, string provider, string message)
    {
        playApi.Setup(api => api.ApiGoogleplaySubscriptionEligibilityGetAsync(UserId, plan, 0, default))
            .ReturnsAsync(new GooglePlayEligibility(false, reason, blockingProvider: provider));

        var error = Assert.ThrowsAsync<CoflnetException>(() => Controller().StartPlayStoreSubscription(plan, playApi.Object));

        Assert.That(error.Slug, Is.EqualTo(reason));
        Assert.That(error.Message, Does.Contain(message));
    }

    [Test]
    public async Task Start_RequiresCurrentTermsWhenEnforced()
    {
        TermsAcceptancePolicy.Initialize(new("skycofl", "current", "", "https://example.com/terms",
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1), []));

        var result = await Controller(enforceTerms: true).StartPlayStoreSubscription(Plan, playApi.Object);

        Assert.That(result.Result, Is.InstanceOf<ObjectResult>());
        var response = (ObjectResult)result.Result;
        Assert.That(response.StatusCode, Is.EqualTo(StatusCodes.Status428PreconditionRequired));
        Assert.That(JObject.FromObject(response.Value).Value<string>("slug"), Is.EqualTo("terms_acceptance_required"));
        playApi.VerifyNoOtherCalls();
    }

    [Test]
    public async Task BothActions_RejectMissingUser()
    {
        var controller = Controller(authenticated: false);
        var start = await controller.StartPlayStoreSubscription(Plan, playApi.Object);
        var complete = await controller.CompletePlayStoreSubscription(Plan, Purchase, playApi.Object);

        Assert.That(start.Result, Is.TypeOf<UnauthorizedObjectResult>());
        Assert.That(complete.Result, Is.TypeOf<UnauthorizedObjectResult>());
        playApi.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Complete_UsesAuthenticatedIdAndFixedPackageEvenWhenBodySuppliesItsOwn()
    {
        var purchase = JsonConvert.DeserializeObject<PremiumController.PlaySubscriptionPurchase>(
            """{"purchaseToken":"play-purchase-token","subscriptionId":"premium_plus","userId":"99","packageName":"other.app"}""");
        playApi.Setup(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.Is<GooglePlaySubscriptionRequest>(r =>
            r.UserId == UserId && r.PackageName == "com.coflnet.sky" && r.SubscriptionId == "premium_plus"
            && r.PurchaseToken == PurchaseToken), 0, default)).ReturnsAsync(new GooglePlaySubscriptionResult(true, "active"));

        // This account would fail the start gate, and the product ID differs from the checkout plan.
        var result = await Controller(enforceTerms: true).CompletePlayStoreSubscription(Plan, purchase, playApi.Object);

        AssertResult(result, 200, true, "active", null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public async Task Complete_UsesPlanWhenSubscriptionIdIsMissing(string subscriptionId)
    {
        playApi.Setup(api => api.ApiGoogleplaySubscriptionCompletePostAsync(It.Is<GooglePlaySubscriptionRequest>(r =>
            r.SubscriptionId == Plan && r.PurchaseToken == PurchaseToken), 0, default))
            .ReturnsAsync(new GooglePlaySubscriptionResult(true, "active"));

        var result = await Controller().CompletePlayStoreSubscription(Plan, new(PurchaseToken, subscriptionId), playApi.Object);

        AssertResult(result, 200, true, "active", null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public async Task Complete_RejectsEmptyPurchaseToken(string token)
    {
        var result = await Controller().CompletePlayStoreSubscription(Plan, new(token, "premium"), playApi.Object);

        AssertResult(result, 400, false, "invalid_purchase", "A purchase token is required.");
        playApi.VerifyNoOtherCalls();
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

    private static PremiumController Controller(bool authenticated = true, bool enforceTerms = false)
    {
        var context = new DefaultHttpContext();
        if (authenticated)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId)], "test"));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["LEGAL:ENFORCE_CURRENT_TERMS"] = enforceTerms.ToString() })
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
