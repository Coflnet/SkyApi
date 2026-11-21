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
public class PremiumSubscriptionExtensionTests
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

    private static SubscriptionDeclarationArgs Requested() => new()
    {
        immediatePerformanceRequested = true,
        withdrawalConsequenceAcknowledged = true,
        declarationVersion = "v7",
        legalLocale = "en",
        declarationRequestId = RequestId
    };

    [Test]
    public void Request_MapsTheDeclarationLikeTheCheckout()
    {
        var request = PremiumController.BuildExtensionRequest("sub_1", Requested(), "de", EarlyStart, Agreement, Withdrawal);
        var checkout = SubscriptionDeclarationMapper.ToDeclaration(Requested(), "de", EarlyStart, Agreement, Withdrawal);

        Assert.That(request.Declaration.ToJson(), Is.EqualTo(checkout.ToJson()));
        Assert.That(request.Declaration.DeclarationSha256, Is.EqualTo("sha-en"));
    }

    [Test]
    public void Request_BuildsEscapedReturnAndCancelUrls()
    {
        var request = PremiumController.BuildExtensionRequest("sub 1&x", Requested(), "en", EarlyStart, Agreement, Withdrawal);

        Assert.That(request.ReturnUrl, Is.EqualTo("https://sky.coflnet.com/account?extended=sub%201%26x"));
        Assert.That(request.CancelUrl, Is.EqualTo("https://sky.coflnet.com/account?extend=sub%201%26x"));
    }

    [Test]
    public void Request_WithoutDeclarationIsRejectedWithTheDeclarationSlug()
    {
        var ex = Assert.Throws<CoflnetException>(() =>
            PremiumController.BuildExtensionRequest("sub_1", null, "en", EarlyStart, Agreement, Withdrawal));

        Assert.That(ex.Slug, Is.EqualTo("service_performance_declaration_required"));
    }

    private static SubscriptionExtensionRequest Dummy() => new(new SubscriptionDeclaration(locale: "en", requestId: RequestId), "r", "c");

    [Test]
    public async Task Extend_ForwardsTheResult()
    {
        var until = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var api = new Mock<ISubscriptionApi>();
        api.Setup(a => a.ApiSubscriptionUUserIdSubscriptionIdExtendPostAsync("7", "sub_1", It.IsAny<SubscriptionExtensionRequest>(), 0, default))
            .ReturnsAsync(new SubscriptionExtensionResult(false, "https://www.paypal.com/approve", until, until.AddDays(91)));

        var result = await PremiumController.RequestExtension(api.Object, "7", "sub_1", Dummy());

        Assert.That(result.Extended, Is.False);
        Assert.That(result.ApprovalUrl, Is.EqualTo("https://www.paypal.com/approve"));
        Assert.That(result.TermEndsAt, Is.EqualTo(until));
        Assert.That(result.ExtendedUntil, Is.EqualTo(until.AddDays(91)));
    }

    private static Mock<ISubscriptionApi> Throwing(int status, string body)
    {
        var api = new Mock<ISubscriptionApi>();
        api.Setup(a => a.ApiSubscriptionUUserIdSubscriptionIdExtendPostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SubscriptionExtensionRequest>(), 0, default))
            .ThrowsAsync(new ApiException(status, "error", body));
        return api;
    }

    [TestCase("{\"Message\":\"extension_unavailable\"}", "extension_unavailable")]
    [TestCase("{\"Message\":\"Subscription not found\"}", "extension_failed")]
    [TestCase("{\"Message\":\"This subscription can not be extended for another term.\"}", "extension_failed")]
    public void Extend_MapsPaymentsClientErrorsToSlugAndMessage(string body, string slug)
    {
        var api = Throwing(400, body);

        var ex = Assert.ThrowsAsync<CoflnetException>(() =>
            PremiumController.RequestExtension(api.Object, "7", "sub_1", Dummy()));

        Assert.That(ex.Slug, Is.EqualTo(slug));
        Assert.That(ex.Message, Does.Contain(System.Text.RegularExpressions.Regex.Match(body, "\"Message\":\"([^\"]+)").Groups[1].Value));
    }

    [Test]
    public void Extend_HidesPaymentsServerErrors()
    {
        var api = Throwing(500, "{\"Message\":\"boom\",\"TraceId\":\"abc\",\"Host\":\"payments-0\"}");

        var ex = Assert.ThrowsAsync<CoflnetException>(() =>
            PremiumController.RequestExtension(api.Object, "7", "sub_1", Dummy()));

        Assert.That(ex.Slug, Is.EqualTo("extension_failed"));
        Assert.That(ex.Message, Does.Not.Contain("boom").And.Not.Contain("payments-0"));
    }

    [TestCase("https://www.paypal.com/checkoutnow?token=1", true)]
    [TestCase("https://paypal.com/x", true)]
    [TestCase("http://www.paypal.com/x", false)]
    [TestCase("https://evilpaypal.com/x", false)]
    [TestCase("https://paypal.com.evil.example/x", false)]
    [TestCase("javascript:alert(1)", false)]
    public void ApprovalUrl_MustBeHttpsPayPal(string url, bool expected)
    {
        Assert.That(PremiumController.IsPayPalUrl(url), Is.EqualTo(expected));
    }

    [Test]
    public void Extend_RejectsAnApprovalUrlOutsidePayPal()
    {
        var until = DateTime.UtcNow;
        var api = new Mock<ISubscriptionApi>();
        api.Setup(a => a.ApiSubscriptionUUserIdSubscriptionIdExtendPostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SubscriptionExtensionRequest>(), 0, default))
            .ReturnsAsync(new SubscriptionExtensionResult(false, "https://evil.example/approve", until, until));

        var ex = Assert.ThrowsAsync<CoflnetException>(() =>
            PremiumController.RequestExtension(api.Object, "7", "sub_1", Dummy()));

        Assert.That(ex.Slug, Is.EqualTo("extension_failed"));
    }

    [Test]
    public void Request_WithAChangedDeclarationVersionThrowsTheCheckoutMessage()
    {
        var args = Requested();
        args.declarationVersion = "v6";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PremiumController.BuildExtensionRequest("sub_1", args, "en", EarlyStart, Agreement, Withdrawal));

        Assert.That(ex.Message, Is.EqualTo("The Premium declaration changed. Review it and try again."));
    }

    [Test]
    public void Extend_MapsDeclarationErrorsLikeTheCheckout()
    {
        const string slug = "service_performance_declaration_required";
        var api = new Mock<ISubscriptionApi>();
        api.Setup(a => a.ApiSubscriptionUUserIdSubscriptionIdExtendPostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SubscriptionExtensionRequest>(), 0, default))
            .ThrowsAsync(new ApiException(400, "Bad Request", $"{{\"Message\":\"{slug}\"}}"));

        var ex = Assert.ThrowsAsync<CoflnetException>(() =>
            PremiumController.RequestExtension(api.Object, "7", "sub_1", Dummy()));

        Assert.That(ex.Slug, Is.EqualTo(slug));
    }

    [Test]
    public void List_MapsTheExtensionFields()
    {
        var term = new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var subscription = new UserSubscription(
            externalId: "sub_1", currency: "EUR", currentTermEndsAt: term, canExtendLongTerm: true,
            extensionPrice: 12.5, extensionTaxTreatment: "included", renewalPrice: 4.25, extendedUntil: term.AddDays(91));

        var mapped = PremiumController.ToPremiumSubscription(subscription);

        Assert.Multiple(() =>
        {
            Assert.That(mapped.Currency, Is.EqualTo("EUR"));
            Assert.That(mapped.CurrentTermEndsAt, Is.EqualTo(term));
            Assert.That(mapped.CanExtendLongTerm, Is.True);
            Assert.That(mapped.ExtensionPrice, Is.EqualTo(12.5m));
            Assert.That(mapped.ExtensionTaxTreatment, Is.EqualTo("included"));
            Assert.That(mapped.RenewalPrice, Is.EqualTo(4.25m));
            Assert.That(mapped.ExtendedUntil, Is.EqualTo(term.AddDays(91)));
        });
    }

    [Test]
    public void List_MapsTheProviderManagementUrl()
    {
        const string url = "https://play.google.com/store/account/subscriptions?sku=premium&package=com.coflnet.sky";
        var subscription = new UserSubscription { ManageUrl = url };

        Assert.That(PremiumController.ToPremiumSubscription(subscription).ManageUrl, Is.EqualTo(url));
    }

    [TestCase(2419200L, 4)]
    [TestCase(7862400L, 13)]
    [TestCase(31536000L, 52)]
    [TestCase(1000L, null)]
    public void List_MapsTheTermWeeksFromTheProduct(long ownershipSeconds, int? weeks)
    {
        var subscription = new UserSubscription(product: new Product(ownershipSeconds: ownershipSeconds));

        Assert.That(PremiumController.ToPremiumSubscription(subscription).TermWeeks, Is.EqualTo(weeks));
    }

    [Test]
    public void List_HasNoTermWeeksWithoutProduct()
    {
        Assert.That(PremiumController.ToPremiumSubscription(new UserSubscription()).TermWeeks, Is.Null);
    }
}
