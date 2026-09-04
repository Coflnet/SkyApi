#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Coflnet.Sky.Api.Models;

/// <summary>Represents the user's current agreement status.</summary>
/// <param name="Required">Whether acceptance is required.</param>
/// <param name="CanContinueWithoutAccepting">Whether the user may continue under a prior agreement.</param>
/// <param name="CanStartNewContract">Whether the user may start a new contract.</param>
/// <param name="AgreementId">The agreement ID.</param>
/// <param name="AgreementHash">The agreement root hash.</param>
/// <param name="AgreementUrl">The agreement URL.</param>
/// <param name="Version">The agreement version.</param>
/// <param name="Hash">The accepted content hash.</param>
/// <param name="AcceptedAtUtc">When the agreement was accepted.</param>
/// <param name="EnglishUrl">The English document URL.</param>
/// <param name="GermanUrl">The German document URL.</param>
/// <param name="Documents">The agreement documents.</param>
/// <param name="PremiumPurchaseDeclaration">The localized premium purchase declaration.</param>
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

/// <summary>Represents a legal agreement document.</summary>
/// <param name="Key">The document key.</param>
/// <param name="Title">The localized title.</param>
/// <param name="Version">The document version.</param>
/// <param name="Url">The document URL.</param>
/// <param name="Sha256">The document SHA-256 hash.</param>
/// <param name="AcceptanceHash">The hash used to record acceptance.</param>
/// <param name="Changed">Whether this document caused the agreement package update.</param>
public record LegalAgreementDocument(
    [property: Required] string Key,
    [property: Required] string Title,
    [property: Required] string Version,
    [property: Required] string Url,
    [property: Required] string Sha256,
    [property: Required] string AcceptanceHash,
    bool Changed);

/// <summary>Represents a legal declaration.</summary>
/// <param name="Version">The declaration version.</param>
/// <param name="Locale">The declaration locale.</param>
/// <param name="Text">The declaration text.</param>
public record LegalDeclaration(
    [property: Required] string Version,
    [property: Required] string Locale,
    [property: Required] string Text);

/// <summary>Represents an accept terms request.</summary>
/// <param name="Hash">The accepted agreement hash.</param>
/// <param name="Version">The accepted agreement version.</param>
/// <param name="Source">The acceptance source.</param>
public record AcceptTermsRequest(
    [param: Required] string Hash,
    string? Version = null,
    string? Source = null);
