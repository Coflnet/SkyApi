
using Coflnet.Sky.Bazaar.Flipper.Client.Model;

namespace Coflnet.Sky.Api.Models.Bazaar;
/// <summary>
/// Item Metadata
/// </summary>
public class SpreadFlip
{
    public BazaarFlip Flip { get; set; }
    public string ItemName { get; set; }
    public bool IsManipulated { get; set; }
}

public class DemandSpreadFlip
{
    public DemandFlip Flip { get; set; }
    public string ItemName { get; set; }
}