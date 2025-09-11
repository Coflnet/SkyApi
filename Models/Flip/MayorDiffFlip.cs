using System.Runtime.Serialization;

namespace Coflnet.Sky.Api.Models;

public class MayorDiffFlip
{
    /// <summary>
    /// Item tag of the item
    /// </summary>
    [DataMember(Name = "itemTag", EmitDefaultValue = true)]
    public string ItemTag { get; set; }
    /// <summary>
    /// Item name of the item
    /// </summary>
    [DataMember(Name = "itemName", EmitDefaultValue = true)]
    public string ItemName { get; set; }

    /// <summary>Average absolute difference between consecutive mayor window medians</summary>
    /// <value>Average absolute difference between consecutive mayor window medians</value>
    [DataMember(Name = "averageMayorMedianDiff", EmitDefaultValue = false)]
    public double AverageMayorMedianDiff { get; set; }

    /// <summary>Observed volume for the item</summary>
    /// <value>Observed volume for the item</value>
    [DataMember(Name = "volume", EmitDefaultValue = false)]
    public long Volume { get; set; }

    /// <summary>Gets or Sets ExpectedPrice</summary>
    [DataMember(Name = "expectedPrice", EmitDefaultValue = false)]
    public double ExpectedPrice { get; set; }

    /// <summary>Gets or Sets MedianPrice</summary>
    [DataMember(Name = "medianPrice", EmitDefaultValue = false)]
    public double MedianPrice { get; set; }

    /// <summary>Gets or Sets NextMayor</summary>
    [DataMember(Name = "nextMayor", EmitDefaultValue = true)]
    public string NextMayor { get; set; }

    public bool UsedPricesAfterCurrentMayor { get; set; }
    public bool UsedPricesBeforeNextMayor { get; set; }

    /// <summary>Gets or Sets CurrentMayor</summary>
    [DataMember(Name = "currentMayor", EmitDefaultValue = true)]
    public string CurrentMayor { get; set; }
}
