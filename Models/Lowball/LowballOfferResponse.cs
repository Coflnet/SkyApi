using System;
using System.Runtime.Serialization;

namespace Coflnet.Sky.Api.Models.Lowball;

/// <summary>Represents a lowball offer response.</summary>
[DataContract]
public class LowballOfferResponse
{
    /// <summary>Gets or sets the user id.</summary>
    [DataMember(Name = "userId", EmitDefaultValue = true)]
    public string UserId { get; set; }

    /// <summary>Gets or sets the created at.</summary>
    [DataMember(Name = "createdAt", EmitDefaultValue = true)]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the offer id.</summary>
    [DataMember(Name = "offerId", EmitDefaultValue = true)]
    public Guid OfferId { get; set; }

    /// <summary>Gets or sets the item tag.</summary>
    [DataMember(Name = "itemTag", EmitDefaultValue = true)]
    public string ItemTag { get; set; }

    /// <summary>Gets or sets the minecraft account.</summary>
    [DataMember(Name = "minecraftAccount", EmitDefaultValue = true)]
    public Guid MinecraftAccount { get; set; }

    /// <summary>Gets or sets the item name.</summary>
    [DataMember(Name = "itemName", EmitDefaultValue = true)]
    public string ItemName { get; set; }

    /// <summary>Gets or sets the api auction json.</summary>
    [DataMember(Name = "apiAuctionJson", EmitDefaultValue = false)]
    public string ApiAuctionJson { get; set; }

    /// <summary>Gets or sets the filters.</summary>
    [DataMember(Name = "filters", EmitDefaultValue = false)]
    public string Filters { get; set; }

    /// <summary>Gets or sets the asking price.</summary>
    [DataMember(Name = "askingPrice", EmitDefaultValue = true)]
    public long AskingPrice { get; set; }

    /// <summary>Gets or sets the lore.</summary>
    [DataMember(Name = "lore", EmitDefaultValue = false)]
    public string Lore { get; set; }

    /// <summary>Gets or sets the item count.</summary>
    [DataMember(Name = "itemCount", EmitDefaultValue = true)]
    public int ItemCount { get; set; }
}
