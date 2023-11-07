using System.Runtime.Serialization;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Models
{
    /// <summary>
    /// Kat pet rarity upgrade flip
    /// </summary>
    public class KatFlip
    {
        /// <summary>
        /// Creates a new instance of <see cref="KatFlip"/>
        /// </summary>
        /// <param name="up"></param>
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
        /// <summary>
        /// The amount of coins the upgrade costs (excluding materials)
        /// </summary>
        public double UpgradeCost { get; }
        /// <summary>
        /// The cost for materials at current bazaar/ah rate
        /// </summary>
        [DataMember(Name = "materialCost", EmitDefaultValue = false)]
        public double MaterialCost { get; }
        /// <summary>
        /// The auction to flip
        /// </summary>
        [DataMember(Name = "originAuction", EmitDefaultValue = true)]
        public string OriginAuction { get; set; }
        /// <summary>
        /// Static data for upgrading this pet
        /// </summary>
        [DataMember(Name = "coreData", EmitDefaultValue = false)]
        public KatUpgradeCost CoreData { get; set; }
        /// <summary>
        /// The rarity the pet will be upgraded to
        /// </summary>
        [DataMember(Name = "targetRarity", EmitDefaultValue = false)]
        public Tier? TargetRarity { get; set; }
        /// <summary>
        /// The estimated profit the flip will genreate
        /// </summary>
        [DataMember(Name = "profit", EmitDefaultValue = false)]
        public double Profit { get; }
        /// <summary>
        /// The reference auction used to estimate the profit (lowest bin of higher rarity)
        /// </summary>
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