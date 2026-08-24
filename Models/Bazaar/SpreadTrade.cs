
using Coflnet.Sky.Bazaar.Flipper.Client.Model;

namespace Coflnet.Sky.Api.Models.Bazaar;
/// <summary>
/// Item Metadata
/// </summary>
public class SpreadFlip
{
    /// <summary>Gets or sets the flip.</summary>
    public BazaarFlip Flip { get; set; }
    /// <summary>Gets or sets the item name.</summary>
    public string ItemName { get; set; }
    /// <summary>Gets or sets the is manipulated.</summary>
    public bool IsManipulated { get; set; }
}

/// <summary>Represents a demand spread flip.</summary>
public class DemandSpreadFlip
{
    /// <summary>Gets or sets the flip.</summary>
    public DemandFlip Flip { get; set; }
    /// <summary>Gets or sets the item name.</summary>
    public string ItemName { get; set; }
}
