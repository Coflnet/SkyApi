using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Client;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Core;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
public class PremiumSubscriptionTermSwitchTests
{
    private const string RequestId = "08a595f8-0fe4-48f1-8a37-e596cae89287";
    private static readonly LegalDeclarationSnapshot EarlyStart = new(
        "v7",
        new Dictionary<string, string> { ["en"] = "english text" },
        new Dictionary<string, string> { ["en"] = "sha-en" });
    private static readonly LegalAgreementSnapshot Agreement = new(
        "skycofl", "2030-01-01", "agreement-hash", "https://coflnet.com/a", DateTime.UtcNow, DateTime.UtcNow, []);
    private static readonly LegalDocumentSnapshot Withdrawal = new(
        "w3", new Dictionary<string, string> { ["en"] = "w-en" });

    private static SubscriptionTermSwitchArgs Args(decimal? expected = 12.5m) => new()
    {
        Weeks = 52,
        ExpectedChargedNow = expected,
        Declaration = new SubscriptionDeclarationArgs
        {
            immediatePerformanceRequested = true,
            withdrawalConsequenceAcknowledged = true,
            declarationVersion = "v7",
            legalLocale = "en",
            declarationRequestId = RequestId
        }
    };

    private static SubscriptionTermSwitchRequest Dummy() =>
        new(52, new SubscriptionDeclaration(locale: "en", requestId: RequestId), 12.5, "r", "c");

    private static Mock<ISubscriptionApi> Throwing(int status, string body)
    {
        var api = new Mock<ISubscriptionApi>();
        api.Setup(a => a.ApiSubscriptionUUserIdSubscriptionIdTermPostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SubscriptionTermSwitchRequest>(), 0, default))
            .ThrowsAsync(new ApiException(status, "error", body));
        return api;
    }

    private static Mock<ISubscriptionApi> Returning(SubscriptionTermSwitchResult result)
    {
        var api = new Mock<ISubscriptionApi>();
        api.Setup(a => a.ApiSubscriptionUUserIdSubscriptionIdTermPostAsync("7", "sub_1", It.IsAny<SubscriptionTermSwitchRequest>(), 0, default))
            .ReturnsAsync(result);
        return api;
    }

    private static SubscriptionTermSwitchResult ResultWithUrl(string url) =>
        new(switched: false, status: "payment_required", approvalUrl: url, weeks: 52, chargedNow: 12.5);

    [Test]
    public void Request_MapsTheDeclarationLikeTheCheckout()
    {
        var request = PremiumController.BuildTermSwitchRequest("sub_1", Args(), "de", EarlyStart, Agreement, Withdrawal);
        var checkout = SubscriptionDeclarationMapper.ToDeclaration(Args().Declaration, "de", EarlyStart, Agreement, Withdrawal);

        Assert.That(request.Declaration.ToJson(), Is.EqualTo(checkout.ToJson()));
        Assert.That(request.Weeks, Is.EqualTo(52));
        Assert.That(request.ExpectedChargedNow, Is.EqualTo(12.5));
    }

    [Test]
    public void Request_BuildsEscapedReturnAndCancelUrls()
    {
        var request = PremiumController.BuildTermSwitchRequest("sub 1&x", Args(), "en", EarlyStart, Agreement, Withdrawal);

        Assert.That(request.ReturnUrl, Is.EqualTo("https://sky.coflnet.com/account?switched=sub%201%26x"));
        Assert.That(request.CancelUrl, Is.EqualTo("https://sky.coflnet.com/account?switch=sub%201%26x"));
    }

    [Test]
    public void Request_WithoutDeclarationIsRejectedWithTheDeclarationSlug()
    {
        var args = Args();
        args.Declaration = null;

        var ex = Assert.Throws<CoflnetException>(() =>
            PremiumController.BuildTermSwitchRequest("sub_1", args, "en", EarlyStart, Agreement, Withdrawal));

        Assert.That(ex.Slug, Is.EqualTo("service_performance_declaration_required"));
    }

    [Test]
    public void Request_WithoutExpectedPriceIsRejected()
    {
        var ex = Assert.Throws<CoflnetException>(() =>
            PremiumController.BuildTermSwitchRequest("sub_1", Args(null), "en", EarlyStart, Agreement, Withdrawal));

        Assert.That(ex.Slug, Is.EqualTo("expected_charged_now_required"));
    }

    [Test]
    public void Request_WithAChangedDeclarationVersionThrowsTheCheckoutMessage()
    {
        var args = Args();
        args.Declaration.declarationVersion = "v6";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PremiumController.BuildTermSwitchRequest("sub_1", args, "en", EarlyStart, Agreement, Withdrawal));

        Assert.That(ex.Message, Is.EqualTo("The Premium declaration changed. Review it and try again."));
    }

    [Test]
    public async Task Switch_ForwardsTheResult()
    {
        var until = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var api = Returning(new SubscriptionTermSwitchResult(
            switched: true, status: "completed", weeks: 52, productSlug: "premium_plus_52", chargedNow: 12.5,
            currency: "EUR", promotionCode: "BW", accessUntil: until, renewalPrice: 4.25, renewalWeeks: 4));

        var result = await PremiumController.RequestTermSwitch(api.Object, "7", "sub_1", Dummy());

        Assert.Multiple(() =>
        {
            Assert.That(result.Switched, Is.True);
            Assert.That(result.Status, Is.EqualTo("completed"));
            Assert.That(result.ApprovalUrl, Is.Null);
            Assert.That(result.ChargedNow, Is.EqualTo(12.5m));
            Assert.That(result.PromotionCode, Is.EqualTo("BW"));
            Assert.That(result.AccessUntil, Is.EqualTo(until));
            Assert.That(result.RenewalPrice, Is.EqualTo(4.25m));
        });
    }

    [TestCase("https://invoice.stripe.com/i/acct_1/live_abc")]
    [TestCase("https://checkout.stripe.com/c/pay/cs_1")]
    [TestCase("https://billing.stripe.com/p/session/x")]
    [TestCase("https://www.paypal.com/checkoutnow?token=1")]
    public async Task Switch_ReturnsStripeAndPayPalApprovalUrls(string url)
    {
        var api = Returning(ResultWithUrl(url));

        var result = await PremiumController.RequestTermSwitch(api.Object, "7", "sub_1", Dummy());

        Assert.That(result.ApprovalUrl, Is.EqualTo(url));
    }

    [TestCase("https://evil.example/approve")]
    [TestCase("http://invoice.stripe.com/i/x")]
    [TestCase("https://stripe.com.evil.example/x")]
    [TestCase("https://evilinvoice.stripe.com/x")]
    [TestCase("https://dashboard.stripe.com/x")]
    [TestCase("javascript:alert(1)")]
    public void Switch_RejectsAnApprovalUrlOutsideStripeAndPayPal(string url)
    {
        var api = Returning(ResultWithUrl(url));

        var ex = Assert.ThrowsAsync<CoflnetException>(() =>
            PremiumController.RequestTermSwitch(api.Object, "7", "sub_1", Dummy()));

        Assert.That(ex.Slug, Is.EqualTo("term_switch_failed"));
    }

    [TestCase("term_switch_unavailable")]
    [TestCase("subscription_not_active")]
    [TestCase("extension_booked")]
    [TestCase("term_not_paid")]
    [TestCase("no_longer_term")]
    [TestCase("term_switch_not_offered")]
    [TestCase("subscription_not_switchable")]
    [TestCase("term_switch_pending")]
    [TestCase("term_switch_price_changed")]
    [TestCase("renewal_due")]
    [TestCase("expected_charged_now_required")]
    [TestCase("invalid_service_performance_declaration")]
    [TestCase("service_performance_declaration_required")]
    public void Switch_PassesKnownPaymentsSlugsThrough(string slug)
    {
        var api = Throwing(400, $"{{\"Message\":\"{slug}\"}}");

        var ex = Assert.ThrowsAsync<CoflnetException>(() =>
            PremiumController.RequestTermSwitch(api.Object, "7", "sub_1", Dummy()));

        Assert.That(ex.Slug, Is.EqualTo(slug));
    }

    [Test]
    public void Switch_MapsUnknownClientErrorsToTheGenericSlug()
    {
        var api = Throwing(400, "{\"Message\":\"Subscription not found\"}");

        var ex = Assert.ThrowsAsync<CoflnetException>(() =>
            PremiumController.RequestTermSwitch(api.Object, "7", "sub_1", Dummy()));

        Assert.That(ex.Slug, Is.EqualTo("term_switch_failed"));
        Assert.That(ex.Message, Does.Contain("Subscription not found"));
    }

    [Test]
    public void Switch_HidesPaymentsServerErrors()
    {
        var api = Throwing(500, "{\"Message\":\"boom\",\"TraceId\":\"abc\",\"Host\":\"payments-0\"}");

        var ex = Assert.ThrowsAsync<CoflnetException>(() =>
            PremiumController.RequestTermSwitch(api.Object, "7", "sub_1", Dummy()));

        Assert.That(ex.Slug, Is.EqualTo("term_switch_failed"));
        Assert.That(ex.Message, Does.Not.Contain("boom").And.Not.Contain("payments-0"));
    }

    [Test]
    public async Task Terms_MapsEveryField()
    {
        var end = new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var option = new SubscriptionTermOption(
            weeks: 52, productSlug: "premium_52", title: "Premium 52 weeks", regularPrice: 40.5, chargedNow: 30.25,
            currency: "EUR", taxTreatment: "included",
            promotion: new SubscriptionTermPromotion("BW", "Blackweek", "percent", 25, end),
            currentAccessEndsAt: end, newAccessEndsAt: end.AddDays(364), carriedOverDays: 12, firstRenewalAt: end.AddDays(364),
            renewsAsProductSlug: "premium_4", renewalPrice: 4.5, renewalWeeks: 4);
        var api = new Mock<ISubscriptionApi>();
        api.Setup(a => a.ApiSubscriptionUUserIdSubscriptionIdTermsGetAsync("7", "sub_1", 0, default))
            .ReturnsAsync(new Coflnet.Payments.Client.Model.SubscriptionTermOptions(
                subscriptionId: "sub_1", currentWeeks: 13, currentAccessEndsAt: end, terms: [option]));

        var result = await PremiumController.RequestTermOptions(api.Object, "7", "sub_1");

        var term = result.Terms[0];
        Assert.Multiple(() =>
        {
            Assert.That(result.SubscriptionId, Is.EqualTo("sub_1"));
            Assert.That(result.CurrentWeeks, Is.EqualTo(13));
            Assert.That(result.CurrentAccessEndsAt, Is.EqualTo(end));
            Assert.That(result.UnavailableReason, Is.Null);
            Assert.That(result.Pending, Is.Null);
            Assert.That(term.Weeks, Is.EqualTo(52));
            Assert.That(term.ProductSlug, Is.EqualTo("premium_52"));
            Assert.That(term.RegularPrice, Is.EqualTo(40.5m));
            Assert.That(term.ChargedNow, Is.EqualTo(30.25m));
            Assert.That(term.TaxTreatment, Is.EqualTo("included"));
            Assert.That(term.Promotion.Code, Is.EqualTo("BW"));
            Assert.That(term.Promotion.Amount, Is.EqualTo(25m));
            Assert.That(term.Promotion.ExpiresAt, Is.EqualTo(end));
            Assert.That(term.NewAccessEndsAt, Is.EqualTo(end.AddDays(364)));
            Assert.That(term.CarriedOverDays, Is.EqualTo(12));
            Assert.That(term.RenewsAsProductSlug, Is.EqualTo("premium_4"));
            Assert.That(term.RenewalPrice, Is.EqualTo(4.5m));
            Assert.That(term.RenewalWeeks, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task Terms_KeepsTheUnavailableReasonAndPending()
    {
        var api = new Mock<ISubscriptionApi>();
        api.Setup(a => a.ApiSubscriptionUUserIdSubscriptionIdTermsGetAsync("7", "sub_1", 0, default))
            .ReturnsAsync(new Coflnet.Payments.Client.Model.SubscriptionTermOptions(
                subscriptionId: "sub_1", unavailableReason: "term_switch_pending",
                pending: ResultWithUrl("https://invoice.stripe.com/i/x"), terms: []));

        var result = await PremiumController.RequestTermOptions(api.Object, "7", "sub_1");

        Assert.That(result.Terms, Is.Empty);
        Assert.That(result.UnavailableReason, Is.EqualTo("term_switch_pending"));
        Assert.That(result.Pending.ApprovalUrl, Is.EqualTo("https://invoice.stripe.com/i/x"));
    }

    [Test]
    public void Terms_RefuseAPendingSwitchWithAForeignPaymentLink()
    {
        var api = new Mock<ISubscriptionApi>();
        api.Setup(a => a.ApiSubscriptionUUserIdSubscriptionIdTermsGetAsync("7", "sub_1", 0, default))
            .ReturnsAsync(new Coflnet.Payments.Client.Model.SubscriptionTermOptions(
                subscriptionId: "sub_1", unavailableReason: "term_switch_pending",
                pending: ResultWithUrl("https://evil.example/pay"), terms: []));

        var ex = Assert.ThrowsAsync<CoflnetException>(() => PremiumController.RequestTermOptions(api.Object, "7", "sub_1"));

        Assert.That(ex.Slug, Is.EqualTo("term_switch_failed"));
    }

    [Test]
    public void Terms_MapsPaymentsErrorsLikeTheSwitch()
    {
        var api = new Mock<ISubscriptionApi>();
        api.Setup(a => a.ApiSubscriptionUUserIdSubscriptionIdTermsGetAsync(It.IsAny<string>(), It.IsAny<string>(), 0, default))
            .ThrowsAsync(new ApiException(400, "error", "{\"Message\":\"subscription_not_active\"}"));

        var ex = Assert.ThrowsAsync<CoflnetException>(() => PremiumController.RequestTermOptions(api.Object, "7", "sub_1"));

        Assert.That(ex.Slug, Is.EqualTo("subscription_not_active"));
    }
}
