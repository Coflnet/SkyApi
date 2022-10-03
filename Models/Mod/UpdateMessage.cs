using System;
using System.Collections.Generic;
using MessagePack;

namespace Coflnet.Sky.PlayerState.Models;

[MessagePackObject]
public class UpdateMessage
{
    [Key(0)]
    public UpdateKind Kind;

    [Key(1)]
    public DateTime ReceivedAt;
    [Key(2)]
    public ChestView Chest;
    [Key(3)]
    public List<string> ChatBatch;
    [Key(4)]
    public string PlayerId;
    [Key(5)]
    public string SessionId { get; set; }

    public enum UpdateKind 
    {
        UNKOWN,
        CHAT,
        INVENTORY,
        API = 4,

    }
}

[MessagePackObject]
public class ChestView
{
    /// <summary>
    /// All items in the ui view
    /// </summary>
    [Key(0)]
    public List<Item> Items = new ();
    [Key(1)]
    public string Name;
}
#nullable enable
[MessagePackObject]
public class Item
{
    /// <summary>
    /// 
    /// </summary>
    [Key(0)]
    public long? Id { get; set; }
    /// <summary>
    /// The item name for display
    /// </summary>
    [Key(1)]
    public string ItemName { get; set; } = null!;
    /// <summary>
    /// Hypixel item tag for this item
    /// </summary>
    [Key(2)]
    public string Tag { get; set; } = null!;
    /// <summary>
    /// Other aditional attributes
    /// </summary>
    [Key(3)]
    public Dictionary<string, object>? ExtraAttributes { get; set; }

    /// <summary>
    /// Enchantments if any
    /// </summary>
    [Key(4)]
    public Dictionary<string, byte>? Enchantments { get; set; }  = new();
    /// <summary>
    /// Color element
    /// </summary>
    [Key(5)]
    public int? Color { get; set; } 
    /// <summary>
    /// Item Description aka Lore displayed in game, is a written form of <see cref="ExtraAttributes"/>
    /// </summary>
    [Key(6)]
    public string? Description { get; set; }

}

#nullable restore