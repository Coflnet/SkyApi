namespace Coflnet.Sky.Api.Services.Description;

/// <summary>
/// Container for auction metadata to generate listing cost and price paid from
/// </summary>
public class ListingSum
{
    /// <summary>
    /// highest bid
    /// </summary>
    public long highest { get; set; }
    /// <summary>
    /// The starting bid of the auction
    /// </summary>
    public long StartingBid { get; set; }
    /// <summary>
    /// When the auction will probably expire
    /// </summary>
    public DateTime end { get; set; }
    /// <summary>
    /// Whereever the mod user requesting the data is the same as the seller
    /// Price paid doesn't want to have the highest bid of the user
    /// </summary>
    public bool requestingUserIsSeller { get; set; }
    /// <summary>
    /// shortened auction uuid (first part of it)
    /// </summary>
    public long AuctionUid { get; set; }
    /// <summary>
    /// Start time of the auction
    /// </summary>
    public DateTime start { get; set; }
    /// <summary>
    /// The hypixel item id of the item listed
    /// </summary>
    public string Tag { get; set; }
}