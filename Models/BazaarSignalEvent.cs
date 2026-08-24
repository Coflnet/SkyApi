using System;

namespace Coflnet.Sky.Api.Models;
#nullable enable

/// <summary>Defines bazaar signal channels.</summary>
public static class BazaarSignalChannels
{
    /// <summary>Stores the live signals.</summary>
    public const string LiveSignals = "bazaar:signals:v1";
}

/// <summary>Defines bazaar signal types.</summary>
public static class BazaarSignalTypes
{
    /// <summary>Stores the order filled.</summary>
    public const string OrderFilled = "order_filled";
    /// <summary>Stores the insta sell intent.</summary>
    public const string InstaSellIntent = "insta_sell_intent";
}

/// <summary>Defines bazaar order sides.</summary>
public static class BazaarSignalSides
{
    /// <summary>Stores the buy order.</summary>
    public const string BuyOrder = "buy_order";
    /// <summary>Stores the sell offer.</summary>
    public const string SellOffer = "sell_offer";
}

/// <summary>Represents a bazaar signal event.</summary>
public class BazaarSignalEvent
{
    /// <summary>Gets or sets the type.</summary>
    public string Type { get; set; } = string.Empty;
    /// <summary>Gets or sets the item tag.</summary>
    public string ItemTag { get; set; } = string.Empty;
    /// <summary>Gets or sets the item name.</summary>
    public string? ItemName { get; set; }
    /// <summary>Gets or sets the user ID.</summary>
    public string? UserId { get; set; }
    /// <summary>Gets or sets the Minecraft UUID.</summary>
    public string? MinecraftUuid { get; set; }
    /// <summary>Gets or sets the Minecraft name.</summary>
    public string? MinecraftName { get; set; }
    /// <summary>Gets or sets the order side.</summary>
    public string? OrderSide { get; set; }
    /// <summary>Gets or sets the amount.</summary>
    public int Amount { get; set; }
    /// <summary>Gets or sets the inventory amount.</summary>
    public int InventoryAmount { get; set; }
    /// <summary>Gets or sets the price per unit.</summary>
    public double? PricePerUnit { get; set; }
    /// <summary>Gets or sets the timestamp.</summary>
    public DateTime Timestamp { get; set; }
    /// <summary>Gets or sets the source.</summary>
    public string? Source { get; set; }
}
