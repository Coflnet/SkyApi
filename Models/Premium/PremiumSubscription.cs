
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
    /// <summary>Gets or sets the payment amount.</summary>
    public string PaymentAmount { get; set; }
    /// <summary>Gets or sets the renews at.</summary>
    public DateTime RenewsAt { get; set; }
    /// <summary>Gets or sets the created at.</summary>
    public DateTime CreatedAt { get; set; }
}
