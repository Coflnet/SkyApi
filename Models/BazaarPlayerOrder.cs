using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Models;

/// <summary>A tracked Bazaar order with the matching engine's current fill state.</summary>
public class BazaarPlayerOrder : PlayerState.Client.Model.Offer
{
    [JsonProperty("isExpired")]
    public bool IsExpired { get; set; }
    [JsonProperty("claimedAmount")]
    public long? ClaimedAmount { get; set; }
    /// <summary>Filled quantity, including the current matching estimate.</summary>
    [JsonProperty("filledAmount")]
    public long FilledAmount { get; set; }
    /// <summary>True until a personal observation or the market moving past this price confirms the fill.</summary>
    [JsonProperty("isEstimate")]
    public bool IsEstimate { get; set; }
}
