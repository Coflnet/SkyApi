using System.Runtime.Serialization;

namespace Coflnet.Sky.Api.Models;

public class CoinTransaction
{
    //
    // Summary:
    //     Gets or Sets ProductId
    [DataMember(Name = "productId", EmitDefaultValue = true)]
    public string ProductId { get; set; }

    //
    // Summary:
    //     Gets or Sets Reference
    [DataMember(Name = "reference", EmitDefaultValue = true)]
    public string Reference { get; set; }

    //
    // Summary:
    //     Gets or Sets Amount
    [DataMember(Name = "amount", EmitDefaultValue = false)]
    public double Amount { get; set; }

    //
    // Summary:
    //     Gets or Sets TimeStamp
    [DataMember(Name = "timeStamp", EmitDefaultValue = false)]
    public DateTime TimeStamp { get; set; }
}