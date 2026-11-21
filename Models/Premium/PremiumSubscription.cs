
namespace Coflnet.Sky.Api.Models;

/// <summary>Represents a premium subscription.</summary>
public class PremiumSubscription
{
    /// <summary>Gets or sets the external id.</summary>
    public string ExternalId { get; set; }
    /// <summary>Gets or sets the ends at.</summary>
    public DateTime? EndsAt { get; set; }
    /// <summary>Gets or sets the product name.</summary>
    public string ProductName { get; set; }
    /// <summary>Number of assignable slots renewed by this subscription; zero for personal access.</summary>
    public int SlotCount { get; set; }
    /// <summary>Gets or sets the payment amount.</summary>
    public string PaymentAmount { get; set; }
    /// <summary>Gets or sets the renews at.</summary>
    public DateTime RenewsAt { get; set; }
    /// <summary>Gets or sets the created at.</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Payment provider that bills this subscription (lemonsqueezy, stripe, paypal, googleplay).</summary>
    public string ProviderSlug { get; set; }
    /// <summary>Provider page where the customer can manage this subscription, if available.</summary>
    public string ManageUrl { get; set; }
    /// <summary>Whether a cancelled subscription can be reactivated; false for example for PayPal.</summary>
    public bool CanResume { get; set; }
    /// <summary>Whether the plan can be switched to another tier.</summary>
    public bool CanChangePlan { get; set; }
    /// <summary>Currency of the prices of this subscription (ISO code).</summary>
    public string Currency { get; set; }
    /// <summary>When the term that is currently paid for ends.</summary>
    public DateTime CurrentTermEndsAt { get; set; }
    /// <summary>Whether the subscription can be renewed for another long term (13 or 52 weeks).</summary>
    public bool CanExtendLongTerm { get; set; }
    /// <summary>Price of renewing for another term, in the subscription currency.</summary>
    public decimal ExtensionPrice { get; set; }
    /// <summary>Whether the extension price includes tax: included, net or none.</summary>
    public string ExtensionTaxTreatment { get; set; }
    /// <summary>Price of the 4 week continuation, in the subscription currency.</summary>
    public decimal RenewalPrice { get; set; }
    /// <summary>Until when the subscription is already extended, null when it is not.</summary>
    public DateTime? ExtendedUntil { get; set; }
    /// <summary>Length of the current term in weeks (4, 13 or 52), null when unknown.</summary>
    public int? TermWeeks { get; set; }
}
