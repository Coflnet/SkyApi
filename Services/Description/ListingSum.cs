namespace Coflnet.Sky.Api.Services.Description;

public class ListingSum
{
    public long highest { get; set; }
    public long StartingBid { get; set; }
    public DateTime end { get; set; }
    public bool requestingUserIsSeller { get; set; }
    public DateTime start { get; set; }
}