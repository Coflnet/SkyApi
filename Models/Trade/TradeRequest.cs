using System.Runtime.Serialization;
#nullable enable
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Models;

/// <summary>
/// Represents a trade request with item and coin details.
/// </summary>
public class TradeRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the trade request.
    /// </summary>
    [DataMember(Name = "id", EmitDefaultValue = true)]
    public string? Id { get; set; }
    /// <summary>
    /// Gets or sets the player UUID initiating the trade.
    /// </summary>
    [DataMember(Name = "playerUuid", EmitDefaultValue = true)]
    public string PlayerUuid { get; set; }
    /// <summary>
    /// Gets or sets the player name.
    /// </summary>
    [DataMember(Name = "playerUuid", EmitDefaultValue = false)]
    public string PlayerName { get; set; }

    /// <summary>
    /// Gets or sets the buyer UUID.
    /// </summary>
    [DataMember(Name = "buyerUuid", EmitDefaultValue = true)]
    public string BuyerUuid { get; set; }

    /// <summary>
    /// Gets or sets the item being traded.
    /// </summary>
    [DataMember(Name = "item", EmitDefaultValue = false)]
    public Item? Item { get; set; }
    /// <summary>
    /// The amount of coins the player wants to spend
    /// </summary>
    [DataMember(Name = "coins", EmitDefaultValue = false)]
    public long? Coins { get; set; }

    /// <summary>
    /// Gets or sets the list of wanted items in exchange.
    /// </summary>
    [DataMember(Name = "wantedItems", EmitDefaultValue = true)]
    public List<WantedItem> WantedItems { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the trade request.
    /// </summary>
    [DataMember(Name = "timestamp", EmitDefaultValue = false)]
    public DateTime Timestamp { get; set; }

}
