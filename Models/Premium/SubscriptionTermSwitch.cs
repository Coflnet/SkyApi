using System.Collections.Generic;

namespace Coflnet.Sky.Api.Models;

/// <summary>Body of a request to switch a subscription to a longer term.</summary>
public class SubscriptionTermSwitchArgs
{
    /// <summary>The new term in weeks (13 or 52), one of the offered terms.</summary>
    public int Weeks { get; set; }
    /// <summary>Early performance declaration, the same body the subscription checkout takes.</summary>
    public SubscriptionDeclarationArgs Declaration { get; set; }
    /// <summary>The price shown to the buyer (chargedNow of the chosen term). A switch that would charge another amount is refused.</summary>
    public decimal? ExpectedChargedNow { get; set; }
}

/// <summary>The longer terms a subscription can switch to now.</summary>
public class SubscriptionTermOptionsResponse
{
    /// <summary>External id of the subscription.</summary>
    public string SubscriptionId { get; set; }
    /// <summary>Length of the current term in weeks.</summary>
    public int? CurrentWeeks { get; set; }
    /// <summary>When the access paid so far ends.</summary>
    public DateTime? CurrentAccessEndsAt { get; set; }
    /// <summary>Why no term is offered, null when there are terms.</summary>
    public string UnavailableReason { get; set; }
    /// <summary>A switch waiting for the buyer's payment or approval, null without one.</summary>
    public SubscriptionTermSwitchResponse Pending { get; set; }
    /// <summary>The terms that can be switched to.</summary>
    public List<SubscriptionTermOptionResponse> Terms { get; set; } = [];
}

/// <summary>One longer term: what it costs now and how it continues afterwards.</summary>
public class SubscriptionTermOptionResponse
{
    /// <summary>Length of the term in weeks.</summary>
    public int Weeks { get; set; }
    /// <summary>Slug of the plan the term is.</summary>
    public string ProductSlug { get; set; }
    /// <summary>Display title of the plan.</summary>
    public string Title { get; set; }
    /// <summary>Price of the term without a promotion.</summary>
    public decimal RegularPrice { get; set; }
    /// <summary>What is charged now, the regular price or the lower promotion price.</summary>
    public decimal ChargedNow { get; set; }
    /// <summary>Currency of the prices (ISO code).</summary>
    public string Currency { get; set; }
    /// <summary>How tax relates to the prices: included, net or none.</summary>
    public string TaxTreatment { get; set; }
    /// <summary>The promotion lowering the price, null without one.</summary>
    public SubscriptionTermPromotionResponse Promotion { get; set; }
    /// <summary>When the access paid so far ends; these days are kept.</summary>
    public DateTime CurrentAccessEndsAt { get; set; }
    /// <summary>When the access ends after the switch.</summary>
    public DateTime NewAccessEndsAt { get; set; }
    /// <summary>Whole days of the current term carried over on top of the new term.</summary>
    public int CarriedOverDays { get; set; }
    /// <summary>When the first 4 week cycle after the new term is charged.</summary>
    public DateTime FirstRenewalAt { get; set; }
    /// <summary>Slug of the 4 week plan the subscription continues as.</summary>
    public string RenewsAsProductSlug { get; set; }
    /// <summary>What each 4 week cycle after the new term charges.</summary>
    public decimal RenewalPrice { get; set; }
    /// <summary>Length of the continuation cycle in weeks.</summary>
    public int RenewalWeeks { get; set; }
}

/// <summary>A promotion applied to the first charge of a switch.</summary>
public class SubscriptionTermPromotionResponse
{
    /// <summary>Promotion code.</summary>
    public string Code { get; set; }
    /// <summary>Display name.</summary>
    public string Name { get; set; }
    /// <summary>percent or fixed (cents).</summary>
    public string AmountType { get; set; }
    /// <summary>The amount, percent or cents depending on the amount type.</summary>
    public decimal Amount { get; set; }
    /// <summary>When the promotion ends, null when it does not.</summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Outcome of a switch to a longer term.</summary>
public class SubscriptionTermSwitchResponse
{
    /// <summary>True once the new term is paid and granted.</summary>
    public bool Switched { get; set; }
    /// <summary>completed, payment_required, approval_required or processing.</summary>
    public string Status { get; set; }
    /// <summary>Where to send the buyer to pay or approve (Stripe or PayPal), null otherwise.</summary>
    public string ApprovalUrl { get; set; }
    /// <summary>Length of the new term in weeks.</summary>
    public int Weeks { get; set; }
    /// <summary>Slug of the new plan.</summary>
    public string ProductSlug { get; set; }
    /// <summary>What is charged now.</summary>
    public decimal ChargedNow { get; set; }
    /// <summary>Currency of the prices (ISO code).</summary>
    public string Currency { get; set; }
    /// <summary>How tax relates to the prices: included, net or none.</summary>
    public string TaxTreatment { get; set; }
    /// <summary>Code of the promotion applied, null without one.</summary>
    public string PromotionCode { get; set; }
    /// <summary>When the access ends once the switch is paid.</summary>
    public DateTime AccessUntil { get; set; }
    /// <summary>When the first 4 week cycle after the new term is charged.</summary>
    public DateTime FirstRenewalAt { get; set; }
    /// <summary>Slug of the 4 week plan the subscription continues as.</summary>
    public string RenewsAsProductSlug { get; set; }
    /// <summary>What each 4 week cycle after the new term charges.</summary>
    public decimal RenewalPrice { get; set; }
    /// <summary>Length of the continuation cycle in weeks.</summary>
    public int RenewalWeeks { get; set; }
}
