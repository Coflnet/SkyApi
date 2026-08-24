using System.ComponentModel.DataAnnotations;

#nullable enable annotations

namespace Coflnet.Sky.Api.Models.Notifications;

/// <summary>Represents a listener.</summary>
public class Listener
{
    /// <summary>Gets or sets the id.</summary>
    public int Id { get; set; }
    /// <summary>
    /// Either User,auction or ItemId UserIds are +100.000
    /// </summary>
    /// <value></value>
    [MaxLength(45)]
    public string TopicId { get; set; }
    /// <summary>
    /// Price point in case of item
    /// </summary>
    /// <value></value>
    public long Price { get; set; }

    /// <summary>Defines the available sub type values.</summary>
    public enum SubType
    {
        /// <summary>Gets or sets the none.</summary>
        NONE = 0,
        /// <summary>Gets or sets the price lower than.</summary>
        PriceLowerThan = 1,
        /// <summary>Gets or sets the price higher than.</summary>
        PriceHigherThan = 2,
        /// <summary>Gets or sets the outbid.</summary>
        OUTBID = 4,
        /// <summary>Gets or sets the sold.</summary>
        SOLD = 8,
        /// <summary>Gets or sets the bin.</summary>
        BIN = 16,
        /// <summary>Gets or sets the use sell not buy.</summary>
        UseSellNotBuy = 32,
        /// <summary>Gets or sets the auction.</summary>
        AUCTION = 64,
        /// <summary>Gets or sets the player.</summary>
        PLAYER = 128,
        /// <summary>Gets or sets the undercut.</summary>
        UNDERCUT = 256,
        /// <summary>
        /// Use flip filter
        /// </summary>
        FILTER = 512,
    }

    /// <summary>Gets or sets the type.</summary>
    public SubType Type { get; set; }

    /// <summary>Gets or sets the user id.</summary>
    public int UserId { get; set; }

    /// <summary>
    /// Additional filter to apply before sending the notification
    /// </summary>
    [MaxLength(5000)]
    public string? Filter { get; set; }
}
