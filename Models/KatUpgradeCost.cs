using Coflnet.Sky.Core;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Models
{
    /// <summary>
    /// Information on requirements to upgrade a pet to another rarity
    /// </summary>
    public class KatUpgradeCost
    {
        /// <summary>
        /// Creates a new instance copying from the crafts service
        /// </summary>
        /// <param name="cost"></param>
        public KatUpgradeCost(Coflnet.Sky.Crafts.Client.Model.KatUpgradeCost cost)
        {
            Name = cost.Name;
            BaseRarity = (Tier)cost.BaseRarity;
            Hours = cost.Hours;
            Cost = cost.Cost;
            Material = cost.Material;
            Amount = cost.Amount;
        }

        /// <summary>
        /// The name of the pet
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// Rarity in corelation with cost
        /// </summary>
        [JsonProperty("baseRarity")]
        public Tier BaseRarity { get; set; }

        /// <summary>
        /// Time it takes to upgrade
        /// </summary>
        [JsonProperty("hours")]
        public int Hours { get; set; }

        /// <summary>
        /// Base cost of coins it takes to do the upgrade
        /// </summary>
        [JsonProperty("cost")]
        public int Cost { get; set; }

        /// <summary>
        /// Material (if any) required to upgrade
        /// </summary>
        [JsonProperty("material")]
        public string Material { get; set; }

        /// <summary>
        /// Amount of <see cref="Material"/> required to do the upgrade
        /// </summary>
        [JsonProperty("amount")]
        public int Amount { get; set; }

        /// <summary>
        /// Coflnet Item tag for the Pet
        /// </summary>
        public string ItemTag => "PET_" + Name.ToUpper();
    }
}