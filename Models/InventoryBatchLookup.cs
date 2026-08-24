namespace Coflnet.Sky.Api.Models
{
    /// <summary>Represents an inventory batch lookup.</summary>
    public class InventoryBatchLookup
    {
        /// <summary>Stores the profile id.</summary>
        public string ProfileId;
        /// <summary>Stores the player id.</summary>
        public string PlayerId;
        /// <summary>Stores the uuids.</summary>
        public string[] Uuids;
    }
}
