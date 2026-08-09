using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Api.Services;
using Coflnet.Payments.Client.Model;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
[NonParallelizable]
public class PremiumDeclarationCompatibilityTests
{
    [Test]
    public void MissingRequestIdKeepsLegacyPurchasePath()
    {
        var request = new PurchaseArgs();

        Assert.Multiple(() =>
        {
            Assert.That(
                PremiumController.UsesDeclaredPurchase(request),
                Is.False);
            Assert.That(request.immediatePerformanceRequested, Is.Null);
            Assert.That(
                request.withdrawalConsequenceAcknowledged,
                Is.Null);
        });
    }

    [Test]
    public void RequestIdSelectsDeclaredPurchasePath()
    {
        var request = new PurchaseArgs
        {
            declarationRequestId = "08a595f8-0fe4-48f1-8a37-e596cae89287"
        };

        Assert.That(
            PremiumController.UsesDeclaredPurchase(request),
            Is.True);
    }

    [Test]
    public void GeneratedDeclarationRequestAcceptsRequiredConstructorValues()
    {
        var request = new ServicePurchaseRequest(
            reference: "premium-request",
            locale: "en",
            requestId: "request-id");

        Assert.Multiple(() =>
        {
            Assert.That(request.Reference, Is.EqualTo("premium-request"));
            Assert.That(request.Locale, Is.EqualTo("en"));
            Assert.That(request.RequestId, Is.EqualTo("request-id"));
        });
    }

    [Test]
    public void TermsEnforcementIsOffByDefaultAndBlocksOnlyWhenEnabled()
    {
        TermsAcceptancePolicy.Initialize(new(
            "skycofl",
            "2030-01-01",
            new string('a', 64),
            "https://coflnet.com/legal/agreements/root.json",
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddDays(-1),
            []));
        var shadow = new ConfigurationBuilder().Build();
        var enforced = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["LEGAL:ENFORCE_CURRENT_TERMS"] = "true"
            })
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(PremiumController.MustRejectNewContract(false, shadow),
                Is.False);
            Assert.That(PremiumController.MustRejectDeclaredPurchase(false),
                Is.True);
            Assert.That(PremiumController.MustRejectNewContract(false, enforced),
                Is.True);
        });

        TermsAcceptancePolicy.Initialize(new(
            "skycofl",
            "2031-01-01",
            new string('b', 64),
            "https://coflnet.com/legal/agreements/future.json",
            DateTime.UtcNow.AddDays(1),
            DateTime.UtcNow.AddDays(1),
            []));
        Assert.That(PremiumController.MustRejectDeclaredPurchase(false),
            Is.True,
            "Declared purchases require exact current acceptance independently of rollout enforcement.");
    }
}
#pragma warning restore CS1591
