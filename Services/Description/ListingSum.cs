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
    public long StartingBid { get; set; }
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
    public DateTime start { get; set; }
    public string Tag { get; set; }
}