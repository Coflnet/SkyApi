#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Coflnet.Sky.Api.Models;

public record TermsStatus(
    bool Required,
    bool CanContinueWithoutAccepting,
    bool CanStartNewContract,
    [property: Required] string AgreementId,
    [property: Required] string AgreementHash,
    [property: Required] string AgreementUrl,
    [property: Required] string Version,
    [property: Required] string Hash,
    DateTime? AcceptedAtUtc,
    [property: Required] string EnglishUrl,
    [property: Required] string GermanUrl,
    [property: Required] IReadOnlyList<LegalAgreementDocument> Documents,
    LegalDeclaration? PremiumPurchaseDeclaration);

public record LegalAgreementDocument(
    [property: Required] string Key,
    [property: Required] string Title,
    [property: Required] string Version,
    [property: Required] string Url,
    [property: Required] string Sha256,
    [property: Required] string AcceptanceHash);

public record LegalDeclaration(
    [property: Required] string Version,
    [property: Required] string Locale,
    [property: Required] string Text);

public record AcceptTermsRequest(
    [param: Required] string Hash,
    string? Version = null,
    string? Source = null);
