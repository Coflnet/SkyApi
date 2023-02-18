
namespace Coflnet.Sky.Api.Models;

/// <summary>
/// Lookup element for sold items
/// </summary>
public class ItemSell
{
    /// <summary>
    /// The minecraft account uuid that sold the item
    /// </summary>
    public string Seller;
    /// <summary>
    /// The hypixel auction uuid for the item
    /// </summary>
    public string Uuid;
    /// <summary>
    /// The minecraft account uuid that bought the item
    /// </summary>
    public string Buyer;
    /// <summary>
    /// When was the item sold
    /// </summary>
    public DateTime Timestamp;
}