namespace Coflnet.Sky.Api.Models;

/// <summary>Result of renewing a subscription for another term.</summary>
public class SubscriptionExtensionResponse
{
    /// <summary>True when the term was extended immediately (Stripe).</summary>
    public bool Extended { get; set; }
    /// <summary>Url to redirect the buyer to for approval (PayPal), null when already extended.</summary>
    public string ApprovalUrl { get; set; }
    /// <summary>When the current term ends.</summary>
    public DateTime TermEndsAt { get; set; }
    /// <summary>Until when the subscription is paid after the extension.</summary>
    public DateTime ExtendedUntil { get; set; }
}
