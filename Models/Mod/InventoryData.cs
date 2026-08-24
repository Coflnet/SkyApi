using Coflnet.Sky.Api.Client.Model;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Models.Mod;

/// <summary>
/// Representation of an inventory
/// </summary>
public class InventoryData
{
    /// <summary>
    /// The name of the chest
    /// </summary>
    public string ChestName;
    /// <summary>
    /// Base64, gziped nbtdata of the inventory
    /// </summary>
    public string FullInventoryNbt;
    /// <summary>
    /// The position of the chest (if inventory is, also, a chest)
    /// </summary>
    public Coflnet.Sky.Core.BlockPos Position;
    /// <summary>
    /// Nbt formatted as json like mineflayer does it
    /// </summary>
    public dynamic JsonNbt;
    /// <summary>
    /// Id of the sender to identify and or contact
    /// </summary>
    public string SenderContactId;
    /// <summary>
    /// Optional server context for non-Hypixel integrations.
    /// </summary>
    public string Server;
}

/// <summary>
/// Representation of an inventory with settings
/// </summary>
public class InventoryDataWithSettings : InventoryData
{
    /// <summary>
    /// Settings of what modifications to include
    /// </summary>
    public DescriptionSetting Settings { get; set; }
    /// <summary>
    /// Incremental version of the client library to know supported features
    /// </summary>
    public int Version { get; set; }
}

/// <summary>Represents a pricing breakdown.</summary>
public class PricingBreakdwon
{
    /// <summary>Stores the craft price.</summary>
    public IEnumerable<CraftPrice> craftPrice;
}

/// <summary>Represents a craft price.</summary>
public class CraftPrice
{
    /// <summary>Stores the price.</summary>
    public long Price;
    /// <summary>Stores the item tag.</summary>
    public string ItemTag;
    /// <summary>Stores the attribute.</summary>
    public string Attribute;
    /// <summary>Stores the formatted reason.</summary>
    public string FormattedReson;
    /// <summary>Stores the count.</summary>
    public long Count;
}
