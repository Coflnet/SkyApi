using System.ComponentModel;

namespace Coflnet.Sky.Api.Models;

/// <summary>Represents a wanted item.</summary>
public class WantedItem
{
    /// <summary>Gets or sets the tag.</summary>
    public string Tag { get; set; }
    /// <summary>Gets or sets the item name.</summary>
    public string ItemName { get; set; }
    /// <summary>Gets or sets the filters.</summary>
    public Dictionary<string, string> Filters { get; set; }
}
