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
    /// Settings of what modifications to include
    /// </summary>
    public DescriptionSetting Settings { get; set; }
}
