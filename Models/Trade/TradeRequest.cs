using System.Runtime.Serialization;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Models;

public class TradeRequest
{
    [DataMember(Name = "id", EmitDefaultValue = true)]
    public string? Id { get; set; }
    //
    // Summary:
    //     Gets or Sets PlayerUuid
    [DataMember(Name = "playerUuid", EmitDefaultValue = true)]
    public string PlayerUuid { get; set; }
    [DataMember(Name = "playerUuid", EmitDefaultValue = false)]
    public string PlayerName { get; set; }

    //
    // Summary:
    //     Gets or Sets BuyerUuid
    [DataMember(Name = "buyerUuid", EmitDefaultValue = true)]
    public string BuyerUuid { get; set; }

    //
    // Summary:
    //     Gets or Sets Item
    [DataMember(Name = "item", EmitDefaultValue = false)]
    public Item Item { get; set; }

    //
    // Summary:
    //     Gets or Sets WantedItems
    [DataMember(Name = "wantedItems", EmitDefaultValue = true)]
    public List<WantedItem> WantedItems { get; set; }

    //
    // Summary:
    //     Gets or Sets Timestamp
    [DataMember(Name = "timestamp", EmitDefaultValue = false)]
    public DateTime Timestamp { get; set; }

}
