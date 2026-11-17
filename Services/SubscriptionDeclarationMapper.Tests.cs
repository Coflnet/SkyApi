using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Api.Models;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services;

public class SubscriptionDeclarationMapperTests
{
    private const string RequestId = "08a595f8-0fe4-48f1-8a37-e596cae89287";
    private static readonly LegalDeclarationSnapshot Declaration = new(
        "v7",
        new Dictionary<string, string> { ["en"] = "english text", ["de"] = "deutscher Text" },
        new Dictionary<string, string> { ["en"] = "sha-en", ["de"] = "sha-de" });
    private static readonly LegalAgreementSnapshot Agreement = new(
        "skycofl", "2030-01-01", "agreement-hash", "https://coflnet.com/a", DateTime.UtcNow, DateTime.UtcNow, []);
    private static readonly LegalDocumentSnapshot Withdrawal = new(
        "w3", new Dictionary<string, string> { ["en"] = "w-en", ["de"] = "w-de" });

    private static SubscriptionDeclarationArgs Requested(string version = "v7", string locale = null) => new()
    {
        immediatePerformanceRequested = true,
        withdrawalConsequenceAcknowledged = true,
        declarationVersion = version,
        legalLocale = locale,
        declarationRequestId = RequestId
    };

    private static SubscriptionDeclaration Map(SubscriptionDeclarationArgs args, string requestLocale = "DE") =>
        SubscriptionDeclarationMapper.ToDeclaration(args, requestLocale, Declaration, Agreement, Withdrawal);

    [Test]
    public void MissingBodyMapsToNoDeclaration()
    {
        Assert.That(Map(null), Is.Null);
    }

    /// <summary>
    /// Like the prepaid purchase, only a body with a request id is a declaration. Payments requires the id and
    /// rejects the checkout otherwise, so an empty JSON object must behave like no body.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void BodyWithoutRequestIdMapsToNoDeclaration(string requestId)
    {
        var args = Requested();
        args.declarationRequestId = requestId;

        Assert.That(Map(args), Is.Null);
        Assert.That(Map(new SubscriptionDeclarationArgs()), Is.Null);
    }

    [Test]
    public void RequestedDeclarationCarriesTheManifestEvidence()
    {
        var result = Map(Requested(locale: "en"));

        Assert.Multiple(() =>
        {
            Assert.That(result.ImmediatePerformanceRequested, Is.True);
            Assert.That(result.WithdrawalConsequenceAcknowledged, Is.True);
            Assert.That(result.Locale, Is.EqualTo("en"));
            Assert.That(result.RequestId, Is.EqualTo(RequestId));
            Assert.That(result.DeclarationVersion, Is.EqualTo("v7"));
            Assert.That(result.DeclarationText, Is.EqualTo("english text"));
            Assert.That(result.DeclarationSha256, Is.EqualTo("sha-en"));
            Assert.That(result.AgreementId, Is.EqualTo("skycofl"));
            Assert.That(result.AgreementHash, Is.EqualTo("agreement-hash"));
            Assert.That(result.WithdrawalVersion, Is.EqualTo("w3"));
            Assert.That(result.WithdrawalSha256, Is.EqualTo("w-en"));
        });
    }

    [TestCase(null, "DE", "de")]
    [TestCase("de-AT", "US", "de")]
    [TestCase("fr", "DE", "en")]
    [TestCase(null, "US", "en")]
    public void LocaleFollowsTheRequestedThenTheRequestCountry(string requested, string requestLocale, string expected)
    {
        var result = Map(Requested(locale: requested), requestLocale);

        Assert.That(result.Locale, Is.EqualTo(expected));
        Assert.That(result.DeclarationText, Is.EqualTo(Declaration.Locales[expected]));
    }

    [Test]
    public void StaleDeclarationVersionIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => Map(Requested(version: "v6")));
    }

    [Test]
    public void UnavailableManifestIsRejectedForARequestedDeclaration()
    {
        Assert.Throws<InvalidOperationException>(() => SubscriptionDeclarationMapper.ToDeclaration(
            Requested(), "DE", null, Agreement, Withdrawal));
    }

    [Test]
    public void DeclinedDeclarationCarriesNoLegalTextSoPaymentsDecides()
    {
        var result = Map(new SubscriptionDeclarationArgs { declarationRequestId = RequestId });

        Assert.Multiple(() =>
        {
            Assert.That(result.ImmediatePerformanceRequested, Is.False);
            Assert.That(result.WithdrawalConsequenceAcknowledged, Is.False);
            Assert.That(result.DeclarationText, Is.Null);
            Assert.That(result.AgreementId, Is.Null);
        });
    }

    [Test]
    public void OnlyOneCheckboxIsNotARequestAndIsForwardedForPaymentsToReject()
    {
        var result = Map(new SubscriptionDeclarationArgs { immediatePerformanceRequested = true, declarationRequestId = RequestId });

        Assert.That(result.ImmediatePerformanceRequested, Is.True);
        Assert.That(result.WithdrawalConsequenceAcknowledged, Is.False);
        Assert.That(result.DeclarationText, Is.Null);
    }

    [TestCase("{\"Message\":\"service_performance_declaration_required\"}", "service_performance_declaration_required")]
    [TestCase("service_performance_declaration_required", "service_performance_declaration_required")]
    [TestCase("invalid_service_performance_declaration", "invalid_service_performance_declaration")]
    [TestCase("country mismatch", null)]
    [TestCase(null, null)]
    public void DeclarationErrorsKeepTheirSlug(string message, string expected)
    {
        Assert.That(SubscriptionDeclarationMapper.DeclarationErrorSlug(message), Is.EqualTo(expected));
    }

    [TestCase("stripe")]
    [TestCase("paypal")]
    [TestCase("lemonsqueezy")]
    public async Task EveryProviderReceivesTheDeclarationInTheOptions(string provider)
    {
        var declaration = Map(Requested());
        var options = new TopUpOptions { ServicePerformanceDeclaration = declaration };
        var api = new Mock<ITopUpApiAsync>();
        api.Setup(a => a.TopUpStripeSubscribePostAsync("1", "plan", It.IsAny<TopUpOptions>(), 0, default)).ReturnsAsync(new TopUpIdResponse());
        api.Setup(a => a.TopUpPaypalSubscribePostAsync("1", "plan", It.IsAny<TopUpOptions>(), 0, default)).ReturnsAsync(new TopUpIdResponse());
        api.Setup(a => a.TopUpLemonsqueezySubscribePostAsync("1", "plan", It.IsAny<TopUpOptions>(), 0, default)).ReturnsAsync(new TopUpIdResponse());

        await SubscriptionCheckoutRouter.Subscribe(api.Object, provider, "1", "plan", options);

        Assert.That(api.Invocations, Has.Count.EqualTo(1));
        var sent = (TopUpOptions)api.Invocations[0].Arguments[2];
        Assert.That(sent.ServicePerformanceDeclaration, Is.SameAs(declaration));
    }
}
