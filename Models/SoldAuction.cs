using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Models;

/// <summary>
/// Auction with NBT data encoded as base64 string for API responses
/// </summary>
public class SoldAuction
{
    /// <summary>
    /// Auction ID
    /// </summary>
    public int Id { get; set; }
    
    /// <summary>
    /// Auction UUID
    /// </summary>
    public string Uuid { get; set; }
    
    /// <summary>
    /// Item tag/ID
    /// </summary>
    public string Tag { get; set; }
    
    /// <summary>
    /// Item name
    /// </summary>
    public string ItemName { get; set; }
    
    /// <summary>
    /// Auctioneer UUID
    /// </summary>
    public string AuctioneerId { get; set; }
    
    /// <summary>
    /// Starting bid amount
    /// </summary>
    public long StartingBid { get; set; }
    
    /// <summary>
    /// Highest bid amount
    /// </summary>
    public long HighestBidAmount { get; set; }
    
    /// <summary>
    /// Auction start time
    /// </summary>
    public DateTime Start { get; set; }
    
    /// <summary>
    /// Auction end time
    /// </summary>
    public DateTime End { get; set; }
    
    /// <summary>
    /// Is BIN auction
    /// </summary>
    public bool Bin { get; set; }
    
    /// <summary>
    /// Item count
    /// </summary>
    public int Count { get; set; }
    
    /// <summary>
    /// Enchantments
    /// </summary>
    public List<Enchantment> Enchantments { get; set; }
    
    /// <summary>
    /// NBT data as base64 encoded string
    /// </summary>
    public string ShortItemBytes { get; set; }
    public Dictionary<string, string> FlattenedNbt { get; internal set; }
}
