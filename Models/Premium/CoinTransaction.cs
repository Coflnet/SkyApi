using System.Runtime.Serialization;

namespace Coflnet.Sky.Api.Models;

/// <summary>Represents a coin transaction.</summary>
public class CoinTransaction
{
    //
    // Summary:
    //     Gets or Sets ProductId
    /// <summary>Gets or sets the product id.</summary>
    [DataMember(Name = "productId", EmitDefaultValue = true)]
    public string ProductId { get; set; }

    //
    // Summary:
    //     Gets or Sets Reference
    /// <summary>Gets or sets the reference.</summary>
    [DataMember(Name = "reference", EmitDefaultValue = true)]
    public string Reference { get; set; }

    //
    // Summary:
    //     Gets or Sets Amount
    /// <summary>Gets or sets the amount.</summary>
    [DataMember(Name = "amount", EmitDefaultValue = false)]
    public double Amount { get; set; }

    //
    // Summary:
    //     Gets or Sets TimeStamp
    /// <summary>Gets or sets the time stamp.</summary>
    [DataMember(Name = "timeStamp", EmitDefaultValue = false)]
    public DateTime TimeStamp { get; set; }
}
