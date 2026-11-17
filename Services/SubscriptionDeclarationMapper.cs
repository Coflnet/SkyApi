using System;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Api.Models;

namespace Coflnet.Sky.Api.Services;

/// <summary>
/// Maps the declaration of a subscription checkout to the Payments evidence, like the declaration of the
/// prepaid purchase: the user only sends the choice, the legal texts and hashes come from the manifest.
/// </summary>
public static class SubscriptionDeclarationMapper
{
    /// <summary>de for German locales, en otherwise; the requested locale wins over the request fallback.</summary>
    public static string ResolveLegalLocale(string requested, string requestFallback)
    {
        var locale = string.IsNullOrWhiteSpace(requested) ? requestFallback : requested;
        return locale != null && locale.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
    }

    /// <summary>The user requested early performance and acknowledged its consequence.</summary>
    public static bool IsRequested(SubscriptionDeclarationArgs args) =>
        args?.immediatePerformanceRequested == true && args.withdrawalConsequenceAcknowledged == true;

    /// <summary>
    /// Like the prepaid purchase, only a body with a request id is a declaration; Payments requires the id.
    /// </summary>
    public static bool IsDeclaration(SubscriptionDeclarationArgs args) =>
        !string.IsNullOrWhiteSpace(args?.declarationRequestId);

    /// <summary>
    /// Builds the declaration for TopUpOptions; null without a declaration (no body or no request id) so
    /// Payments applies its own enforcement. A declaration that does not request early performance carries no
    /// legal text, Payments rejects or tolerates it according to its enforcement switch.
    /// </summary>
    public static SubscriptionDeclaration ToDeclaration(
        SubscriptionDeclarationArgs args,
        string requestLocale,
        LegalDeclarationSnapshot declaration,
        LegalAgreementSnapshot agreement,
        LegalDocumentSnapshot withdrawal)
    {
        if (!IsDeclaration(args))
            return null;
        var locale = ResolveLegalLocale(args.legalLocale, requestLocale);
        var result = new SubscriptionDeclaration(locale: locale, requestId: args.declarationRequestId)
        {
            ImmediatePerformanceRequested = args.immediatePerformanceRequested ?? false,
            WithdrawalConsequenceAcknowledged = args.withdrawalConsequenceAcknowledged ?? false
        };
        if (!IsRequested(args))
            return result;
        AddEvidence(result, args, locale, declaration, agreement, withdrawal);
        return result;
    }

    private static void AddEvidence(
        SubscriptionDeclaration result,
        SubscriptionDeclarationArgs args,
        string locale,
        LegalDeclarationSnapshot declaration,
        LegalAgreementSnapshot agreement,
        LegalDocumentSnapshot withdrawal)
    {
        if (declaration == null)
            throw new InvalidOperationException("The Premium declaration is unavailable.");
        if (agreement == null)
            throw new InvalidOperationException("The SkyCofl agreement identity is unavailable.");
        if (withdrawal == null)
            throw new InvalidOperationException("The withdrawal identity is unavailable.");
        if (!string.Equals(args.declarationVersion, declaration.Version, StringComparison.Ordinal))
            throw new InvalidOperationException("The Premium declaration changed. Review it and try again.");
        result.DeclarationVersion = declaration.Version;
        result.DeclarationText = declaration.Locales[locale];
        result.DeclarationSha256 = declaration.Sha256[locale];
        result.AgreementId = agreement.Id;
        result.AgreementHash = agreement.Hash;
        result.WithdrawalVersion = withdrawal.Version;
        result.WithdrawalSha256 = withdrawal.Sha256[locale];
    }

    private static readonly string[] PaymentsDeclarationSlugs =
    {
        "service_performance_declaration_required",
        "invalid_service_performance_declaration"
    };

    /// <summary>The declaration error slug in a Payments error message, null for other errors.</summary>
    public static string DeclarationErrorSlug(string paymentsMessage)
    {
        if (string.IsNullOrEmpty(paymentsMessage))
            return null;
        return Array.Find(PaymentsDeclarationSlugs, s => paymentsMessage.Contains(s, StringComparison.Ordinal));
    }
}
