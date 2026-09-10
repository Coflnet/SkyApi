namespace Coflnet.Sky.Api.Models;
/// <summary>Represents item ownership information.</summary>
public class OwnerShip
{
    /// <summary>Gets or sets the expires at.</summary>
    public DateTime ExpiresAt { get; set; }
    public string OwnerId { get; set; }
    public long? SlotId { get; set; }
    public bool CanManage { get; set; } = true;
}
