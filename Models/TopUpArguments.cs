namespace Coflnet.Sky.Api.Models;
/// <summary>
/// Additional arguments for a topup
/// </summary>
public class TopUpArguments
{
    /// <summary>
    /// A specific amount of coins to topup
    /// </summary>
    /// <value></value>
    public int CoinAmount { get; set; }
    /// <summary>
    /// The url to redirect to after successful payment
    /// </summary>
    public string SuccessUrl { get; set; }
    /// <summary>
    /// The url to redirect to when user aborts payment
    /// </summary>
    public string CancelUrl { get; set; }
    /// <summary>
    /// An optional creator code to apply to the purchase
    /// </summary>
    public string CreatorCode { get; set; }
    /// <summary>
    /// An optional discount code to apply to the purchase
    /// </summary>
    public string Discountcode { get; set; }
}