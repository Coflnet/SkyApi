using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Coflnet.Sky.Api.Models;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services;

[NonParallelizable]
public class TermsAcceptancePolicyTests
{
    private static readonly DateTime EffectiveAtUtc =
        new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [SetUp]
    public void SetUp() => TermsAcceptancePolicy.Initialize(Agreement(), new(
        "premium-start-v1",
        new Dictionary<string, string>
        {
            ["en"] = "English declaration",
            ["de"] = "Deutsche Erklärung"
        }));

    [TearDown]
    public void TearDown() => TermsAcceptancePolicy.ResetForTests();

    [Test]
    public void Status_exposes_root_and_complete_localized_bundle()
    {
        var acceptedAt = EffectiveAtUtc.AddMinutes(1);

        var status = TermsAcceptancePolicy.GetStatus(
            true,
            acceptedAt,
            EffectiveAtUtc,
            locale: "de-DE");

        Assert.Multiple(() =>
        {
            Assert.That(status.Required, Is.False);
            Assert.That(status.CanStartNewContract, Is.True);
            Assert.That(status.AgreementId, Is.EqualTo("skycofl"));
            Assert.That(status.AgreementHash, Is.EqualTo(new string('a', 64)));
            Assert.That(status.AgreementUrl, Does.EndWith("root.json"));
            Assert.That(status.EnglishUrl,
                Is.EqualTo("https://coflnet.com/legal/versions"));
            Assert.That(status.GermanUrl, Is.EqualTo(status.EnglishUrl));
            Assert.That(status.AcceptedAtUtc, Is.EqualTo(acceptedAt));
            Assert.That(status.Documents.Select(item => item.Key), Is.EqualTo(
                new[] { "terms", "commerceTerms", "aiTerms", "skycoflTerms" }));
            Assert.That(status.Documents, Has.All.Property("Url").Contains("-de"));
            Assert.That(status.PremiumPurchaseDeclaration.Locale, Is.EqualTo("de"));
        });
    }

    [Test]
    public void Current_root_hash_controls_contract_eligibility()
    {
        Assert.That(TermsAcceptancePolicy.IsCurrent(true), Is.True);
        Assert.That(TermsAcceptancePolicy.IsCurrent(false), Is.False);
        Assert.That(TermsAcceptancePolicy.CanStartNewContract(false, EffectiveAtUtc), Is.False);
        Assert.That(TermsAcceptancePolicy.RequiresCurrentAcceptance(false, EffectiveAtUtc), Is.True);
    }

    [Test]
    public void Agreement_is_not_required_before_effective_time()
    {
        var before = EffectiveAtUtc.AddTicks(-1);

        Assert.That(TermsAcceptancePolicy.RequiresCurrentAcceptance(false, before), Is.False);
        Assert.That(TermsAcceptancePolicy.CanStartNewContract(false, before), Is.True);
    }

    [Test]
    public void Missing_verified_root_fails_closed_for_new_contracts()
    {
        TermsAcceptancePolicy.ResetForTests();

        Assert.That(TermsAcceptancePolicy.RequiresCurrentAcceptance(false), Is.True);
        Assert.That(TermsAcceptancePolicy.CanStartNewContract(false), Is.False);
    }

    [Test]
    public void Acceptance_validation_is_attached_to_the_record_parameter()
    {
        var constructor = typeof(AcceptTermsRequest).GetConstructors().Single();
        var parameter = constructor.GetParameters()
            .Single(item => item.Name == nameof(AcceptTermsRequest.Hash));
        var property = typeof(AcceptTermsRequest)
            .GetProperty(nameof(AcceptTermsRequest.Hash));

        Assert.Multiple(() =>
        {
            Assert.That(parameter.GetCustomAttributes(typeof(RequiredAttribute), false),
                Has.Length.EqualTo(1));
            Assert.That(property!.GetCustomAttributes(typeof(RequiredAttribute), false),
                Is.Empty);
        });
    }

    [TestCase("web-login-de", "de-DE", "web-login-de")]
    [TestCase("web-login-en", "en-US", "web-login-en")]
    [TestCase("web-login-de", "en-US", "web-premium-en")]
    [TestCase("anything", "de-DE", "web-premium-de")]
    public void Acceptance_source_is_limited_to_the_authenticated_web_surface(
        string requested,
        string locale,
        string expected) =>
        Assert.That(
            TermsAcceptancePolicy.NormalizeAcceptanceSource(requested, locale),
            Is.EqualTo(expected));

    private static LegalAgreementSnapshot Agreement()
    {
        var keys = new[] { "terms", "commerceTerms", "aiTerms", "skycoflTerms" };
        return new(
            "skycofl",
            "2030-01-01",
            new string('a', 64),
            "https://coflnet.com/legal/agreements/root.json",
            EffectiveAtUtc,
            EffectiveAtUtc,
            keys.Select(key => new LegalAgreementDocumentSnapshot(
                key,
                $"{key} title",
                "2030-01-01",
                new string('b', 64),
                EffectiveAtUtc,
                EffectiveAtUtc,
                new Dictionary<string, LegalLocaleSnapshot>
                {
                    ["en"] = new($"https://coflnet.com/{key}-en", new string('c', 64)),
                    ["de"] = new($"https://coflnet.com/{key}-de", new string('d', 64))
                })).ToArray());
    }
}
