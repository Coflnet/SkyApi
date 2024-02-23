using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Coflnet.Sky.Api.Models.Notifications;

public class Listener
{
    public int Id { get; set; }
    /// <summary>
    /// Either User,auction or ItemId UserIds are +100.000
    /// </summary>
    /// <value></value>
    [System.ComponentModel.DataAnnotations.MaxLength(45)]
    public string TopicId { get; set; }
    /// <summary>
    /// Price point in case of item
    /// </summary>
    /// <value></value>
    public long Price { get; set; }

    public enum SubType
    {
        NONE = 0,
        PriceLowerThan = 1,
        PriceHigherThan = 2,
        OUTBID = 4,
        SOLD = 8,
        BIN = 16,
        UseSellNotBuy = 32,
        AUCTION = 64,
        PLAYER = 128,
        UNDERCUT = 256,
        /// <summary>
        /// Use flip filter
        /// </summary>
        FILTER = 512,
    }

    public SubType Type { get; set; }

    public int UserId { get; set; }

    [MaxLength(200)]
    public string? Filter { get; set; }
}