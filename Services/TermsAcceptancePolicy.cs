using System;
using System.Linq;
using Coflnet.Sky.Api.Models;

namespace Coflnet.Sky.Api.Services;

public static class TermsAcceptancePolicy
{
    private static LegalAgreementSnapshot current;
    private static LegalDeclarationSnapshot premiumEarlyStart;

    public static string CurrentAgreementId => current?.Id ?? "skycofl";
    public static string CurrentVersion => current?.Version ?? "";
    public static string CurrentHash => current?.Hash ?? "";
    public static string CurrentAgreementUrl => current?.Url ?? "https://coflnet.com/legal/versions";
    public static DateTime? CurrentVersionEffectiveAtUtc => current?.EffectiveFromUtc;
    public static string EnglishUrl => "https://coflnet.com/legal/versions";
    public static string GermanUrl => "https://coflnet.com/legal/versions";

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

    public static bool IsCurrent(bool hasCurrentAgreement) =>
        current != null && hasCurrentAgreement;

    public static bool IsEffective(DateTime? utcNow = null, bool forceEffective = false) =>
        current != null
        && (forceEffective || (utcNow ?? DateTime.UtcNow) >= current.EffectiveFromUtc);

    public static bool RequiresCurrentAcceptance(
        bool hasCurrentAgreement,
        DateTime? utcNow = null,
        bool forceEffective = false) =>
        current == null
        || (IsEffective(utcNow, forceEffective) && !hasCurrentAgreement);

    public static bool CanStartNewContract(
        bool hasCurrentAgreement,
        DateTime? utcNow = null,
        bool forceEffective = false) =>
        current != null
        && (!IsEffective(utcNow, forceEffective) || hasCurrentAgreement);

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

    public static string NormalizeLocale(string locale) =>
        locale?.StartsWith("de", StringComparison.OrdinalIgnoreCase) == true
            ? "de"
            : "en";

    public static string NormalizeAcceptanceSource(string requested, string locale)
    {
        var language = NormalizeLocale(locale);
        var login = $"web-login-{language}";
        return requested == login ? login : $"web-premium-{language}";
    }

}
