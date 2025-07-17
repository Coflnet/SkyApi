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
    public BlockPos Position;
    /// <summary>
    /// Nbt formatted as json like mineflayer does it
    /// </summary>
    public dynamic JsonNbt;
    /// <summary>
    /// Id of the sender to identify and or contact
    /// </summary>
    public string SenderContactId;
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
}

public class PricingBreakdwon
{
    public IEnumerable<CraftPrice> craftPrice;
}

public class CraftPrice
{
    public long Price;
    public string ItemTag;
    public string Attribute;
    public string FormattedReson;
    public long Count;
}