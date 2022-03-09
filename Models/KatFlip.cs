using System.Runtime.Serialization;

namespace Coflnet.Sky.Api.Models
{
    public class KatFlip
    {
        public KatFlip(Crafts.Client.Model.KatUpgradeResult up)
        {
            this.CoreData = new(up.CoreData);
            this.OriginAuction = up.OriginAuction;
            this.TargetRarity = (hypixel.Tier?)up.TargetRarity;
            this.MaterialCost = up.MaterialCost;
            this.Profit = up.Profit;
            this.ReferenceAuction = up.ReferenceAuction;
        }
        public double UpgradeCost { get; }
        //
        // Summary:
        //     Gets or Sets MaterialCost
        [DataMember(Name = "materialCost", EmitDefaultValue = false)]
        public double MaterialCost { get; }
        //
        // Summary:
        //     Gets or Sets OriginAuction
        [DataMember(Name = "originAuction", EmitDefaultValue = true)]
        public string OriginAuction { get; set; }
        //
        // Summary:
        //     Gets or Sets CoreData
        [DataMember(Name = "coreData", EmitDefaultValue = false)]
        public KatUpgradeCost CoreData { get; set; }
        //
        // Summary:
        //     Gets or Sets TargetRarity
        [DataMember(Name = "targetRarity", EmitDefaultValue = false)]
        public hypixel.Tier? TargetRarity { get; set; }
        //
        // Summary:
        //     Gets or Sets Profit
        [DataMember(Name = "profit", EmitDefaultValue = false)]
        public double Profit { get; }
        //
        // Summary:
        //     Gets or Sets ReferenceAuction
        [DataMember(Name = "referenceAuction", EmitDefaultValue = true)]
        public string ReferenceAuction { get; set; }

        public float Volume;
        public long Median;
    }
}