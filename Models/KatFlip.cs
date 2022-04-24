using System.Runtime.Serialization;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Models
{
    public class KatFlip
    {
        public KatFlip(Crafts.Client.Model.KatUpgradeResult up)
        {
            this.CoreData = new(up.CoreData);
            this.OriginAuction = up.OriginAuction;
            this.TargetRarity = (Tier?)up.TargetRarity;
            this.MaterialCost = up.MaterialCost;
            this.UpgradeCost = up.UpgradeCost;
            this.Profit = up.Profit;
            this.ReferenceAuction = up.ReferenceAuction;
            this.PurchaseCost = up.PurchaseCost;
            this.OriginAuctionName = up.OriginAuctionName;
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
        public Tier? TargetRarity { get; set; }
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
        /// <summary>
        /// How much the starting bid of the auction is
        /// </summary>
        /// <value></value>
        [DataMember(Name = "purchaseCost", EmitDefaultValue = true)]
        public long PurchaseCost { get; }
        /// <summary>
        /// The full name of the origin auction (cotnaining pet level)
        /// </summary>
        /// <value></value>
        [DataMember(Name = "originAuctionName", EmitDefaultValue = true)]
        public string OriginAuctionName { get; }
        /// <summary>
        /// 24 hour sell volume
        /// </summary>
        public double Volume;
        /// <summary>
        /// Median prie of the item
        /// </summary>
        public long Median;
    }
}