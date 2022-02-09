namespace Coflnet.Sky.Api.Models
{
    /// <summary>
    /// Element for the /lowSupply page
    /// </summary>
    public class SupplyElement
    {
        /// <summary>
        /// Item Tag of the time
        /// </summary>
        public string Tag;
        /// <summary>
        /// How much supply there is on the ah
        /// </summary>
        public long Supply;
        /// <summary>
        /// Median sell price
        /// </summary>
        public long Median;
        /// <summary>
        /// Data about lbin
        /// </summary>
        public BinResponse LbinData;
        /// <summary>
        /// The amount of sells in 24 hours
        /// </summary>
        public long Volume;
    }
}