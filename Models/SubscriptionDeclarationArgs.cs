#nullable enable
namespace Coflnet.Sky.Api.Models;

/// <summary>
/// The user's early performance declaration for a subscription checkout. Same fields as the
/// declaration of <see cref="PurchaseArgs"/>; the legal texts are filled in by the server.
/// </summary>
public class SubscriptionDeclarationArgs
{
    /// <summary>Whether the user expressly requested early performance for this order.</summary>
    public bool? immediatePerformanceRequested { get; set; }
    /// <summary>Whether the user acknowledged the consequence of complete performance.</summary>
    public bool? withdrawalConsequenceAcknowledged { get; set; }
    /// <summary>Hash-verified declaration version shown to the user.</summary>
    public string? declarationVersion { get; set; }
    /// <summary>Locale in which the declaration was shown.</summary>
    public string? legalLocale { get; set; }
    /// <summary>Idempotency identifier (a GUID) for this declaration and order.</summary>
    public string? declarationRequestId { get; set; }
}
