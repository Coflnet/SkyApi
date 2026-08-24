using System;
using System.Linq;
using Coflnet.Sky.Api.Models;

namespace Coflnet.Sky.Api.Services;

/// <summary>Evaluates current agreement acceptance rules.</summary>
public static class TermsAcceptancePolicy
{
    private static LegalAgreementSnapshot current;
    private static LegalDeclarationSnapshot premiumEarlyStart;

    /// <summary>Gets the current agreement id.</summary>
    public static string CurrentAgreementId => current?.Id ?? "skycofl";
    /// <summary>Gets the current version.</summary>
    public static string CurrentVersion => current?.Version ?? "";
    /// <summary>Gets the current hash.</summary>
    public static string CurrentHash => current?.Hash ?? "";
    /// <summary>Gets the current agreement url.</summary>
    public static string CurrentAgreementUrl => current?.Url ?? "https://coflnet.com/legal/versions";
    /// <summary>Gets the current version effective at utc.</summary>
    public static DateTime? CurrentVersionEffectiveAtUtc => current?.EffectiveFromUtc;
    /// <summary>Gets the english url.</summary>
    public static string EnglishUrl => "https://coflnet.com/legal/versions";
    /// <summary>Gets the german url.</summary>
    public static string GermanUrl => "https://coflnet.com/legal/versions";

    /// <summary>Initializes the current legal agreement state.</summary>
    public static void Initialize(
        LegalAgreementSnapshot snapshot,
        LegalDeclarationSnapshot declaration = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        current = snapshot;
        premiumEarlyStart = declaration;
    }

    internal static void ResetForTests()
    {
        current = null;
        premiumEarlyStart = null;
    }

    /// <summary>Determines whether the user accepted the current agreement.</summary>
    public static bool IsCurrent(bool hasCurrentAgreement) =>
        current != null && hasCurrentAgreement;

    /// <summary>Determines whether the current agreement is effective.</summary>
    public static bool IsEffective(DateTime? utcNow = null, bool forceEffective = false) =>
        current != null
        && (forceEffective || (utcNow ?? DateTime.UtcNow) >= current.EffectiveFromUtc);

    /// <summary>Determines whether current acceptance is required.</summary>
    public static bool RequiresCurrentAcceptance(
        bool hasCurrentAgreement,
        DateTime? utcNow = null,
        bool forceEffective = false) =>
        current == null
        || (IsEffective(utcNow, forceEffective) && !hasCurrentAgreement);

    /// <summary>Determines whether the user may start a new contract.</summary>
    public static bool CanStartNewContract(
        bool hasCurrentAgreement,
        DateTime? utcNow = null,
        bool forceEffective = false) =>
        current != null
        && (!IsEffective(utcNow, forceEffective) || hasCurrentAgreement);

    /// <summary>Gets status.</summary>
    public static TermsStatus GetStatus(
        bool hasCurrentAgreement,
        DateTime? acceptedAtUtc = null,
        DateTime? utcNow = null,
        bool forceEffective = false,
        string locale = "en",
        bool canContinueWithoutAccepting = true)
    {
        var language = NormalizeLocale(locale);
        var declaration = premiumEarlyStart?.Locales.TryGetValue(language, out var text) == true
            ? new LegalDeclaration(premiumEarlyStart.Version, language, text)
            : null;
        var documents = current?.Documents.Select(document =>
        {
            var localized = document.Locales[language];
            return new LegalAgreementDocument(
                document.Key,
                document.Title,
                document.Version,
                localized.Url,
                localized.Sha256,
                document.AcceptanceHash);
        }).ToArray() ?? [];

        return new(
            RequiresCurrentAcceptance(hasCurrentAgreement, utcNow, forceEffective),
            canContinueWithoutAccepting,
            CanStartNewContract(hasCurrentAgreement, utcNow, forceEffective),
            CurrentAgreementId,
            CurrentHash,
            CurrentAgreementUrl,
            CurrentVersion,
            CurrentHash,
            IsCurrent(hasCurrentAgreement) ? acceptedAtUtc : null,
            EnglishUrl,
            GermanUrl,
            documents,
            declaration);
    }

    /// <summary>Normalizes locale.</summary>
    public static string NormalizeLocale(string locale) =>
        locale?.StartsWith("de", StringComparison.OrdinalIgnoreCase) == true
            ? "de"
            : "en";

    /// <summary>Normalizes acceptance source.</summary>
    public static string NormalizeAcceptanceSource(string requested, string locale)
    {
        var language = NormalizeLocale(locale);
        var login = $"web-login-{language}";
        return requested == login ? login : $"web-premium-{language}";
    }

}
