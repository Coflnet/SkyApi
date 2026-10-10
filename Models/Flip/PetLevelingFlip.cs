using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Models
{
    /// <summary>
    /// Profit opportunity from buying a low level pet and selling it once leveled
    /// </summary>
    public class PetLevelingFlip
    {
        /// <summary>
        /// The item tag of the pet, eg. PET_SHEEP
        /// </summary>
        public string Tag { get; set; }
        /// <summary>
        /// The rarity the pet is bought and sold at
        /// </summary>
        public Tier Tier { get; set; }
        /// <summary>
        /// The skill the pet gains full exp from, eg. FORAGING, null if unknown
        /// </summary>
        public string ExpType { get; set; }
        /// <summary>
        /// The level the pet is sold at
        /// </summary>
        public int TargetLevel { get; set; }
        /// <summary>
        /// Clean price of the low level pet
        /// </summary>
        public long BuyPrice { get; set; }
        /// <summary>
        /// Clean price of the pet at <see cref="TargetLevel"/>
        /// </summary>
        public long SellPrice { get; set; }
        /// <summary>
        /// Difference between <see cref="SellPrice"/> and <see cref="BuyPrice"/>, before auction house fees
        /// </summary>
        public long Profit { get; set; }
        /// <summary>
        /// Pet exp needed to get from level 1 to <see cref="TargetLevel"/>, 0 if unknown
        /// </summary>
        public long ExpRequired { get; set; }
        /// <summary>
        /// <see cref="Profit"/> per pet exp, 0 if <see cref="ExpRequired"/> is unknown
        /// </summary>
        public double CoinsPerExp { get; set; }
    }
}
