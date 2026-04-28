using System;
using System.Runtime.Serialization;

namespace Coflnet.Sky.Api.Models.Lowball;

[DataContract]
public class LowballOfferResponse
{
    [DataMember(Name = "userId", EmitDefaultValue = true)]
    public string UserId { get; set; }

    [DataMember(Name = "createdAt", EmitDefaultValue = true)]
    public DateTimeOffset CreatedAt { get; set; }

    [DataMember(Name = "offerId", EmitDefaultValue = true)]
    public Guid OfferId { get; set; }

    [DataMember(Name = "itemTag", EmitDefaultValue = true)]
    public string ItemTag { get; set; }

    [DataMember(Name = "minecraftAccount", EmitDefaultValue = true)]
    public Guid MinecraftAccount { get; set; }

    [DataMember(Name = "itemName", EmitDefaultValue = true)]
    public string ItemName { get; set; }

    [DataMember(Name = "apiAuctionJson", EmitDefaultValue = false)]
    public string ApiAuctionJson { get; set; }

    [DataMember(Name = "filters", EmitDefaultValue = false)]
    public string Filters { get; set; }

    [DataMember(Name = "askingPrice", EmitDefaultValue = true)]
    public long AskingPrice { get; set; }

    [DataMember(Name = "lore", EmitDefaultValue = false)]
    public string Lore { get; set; }

    [DataMember(Name = "itemCount", EmitDefaultValue = true)]
    public int ItemCount { get; set; }
}