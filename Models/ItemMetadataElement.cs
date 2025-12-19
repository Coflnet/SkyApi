using Coflnet.Sky.Items.Client.Model;
using Newtonsoft.Json;
using Coflnet.Sky.Api.Helper;

namespace Coflnet.Sky.Api.Models
{
    /// <summary>
    /// Item Metadata
    /// </summary>
    public class ItemMetadataElement
    {
        /// <summary>
        /// The name of the element
        /// </summary>
        /// <value></value>
        public string Name { get; set; }
        /// <summary>
        /// The hypixel tag of the item
        /// </summary>
        /// <value></value>
        public string Tag { get; set; }
        /// <summary>
        /// Can item be auctioned, sold on bazaar or traded
        /// </summary>
        [JsonConverter(typeof(FlagsEnumConverter))]
        public ItemFlags Flags { get; set; }
    }
}