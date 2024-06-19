using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using static Coflnet.Sky.Core.ItemPrices;

namespace Coflnet.Sky.Api.Models;

public class ArchiveResponse
{
    /// <summary>
    /// The auctions that were found
    /// </summary>
    public List<AuctionPreview> Auctions { get; set; }
    /// <summary>
    /// Status of the query
    /// </summary>
    public QueryStatus queryStatus { get; set; }

    /// <summary>
    /// What status the query has
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum QueryStatus
    {
        /// <summary>
        /// The query was successful
        /// </summary>
        Success,
        /// <summary>
        /// The query was successful but no results were found
        /// </summary>
        NoResults,
        /// <summary>
        /// The query is still being processed
        /// </summary>
        Pending,
        /// <summary>
        /// The query was only partially done and result attached
        /// </summary>
        Partial
    }
}