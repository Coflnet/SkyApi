
namespace Coflnet.Sky.Api.Models;

public class PremiumSubscription
{
    public string ExternalId { get; set; }
    public DateTime? EndsAt { get; set; }
    public string ProductName { get; set; }
    public string PaymentAmount { get; set; }
    public DateTime RenewsAt { get; set; }
    public DateTime CreatedAt { get; set; }
}