using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services;

// wrapper for the deserialized cache
public class DeserializedCache
{
    public Dictionary<string, Crafts.Client.Model.ProfitableCraft> Crafts = new();
    public Dictionary<(string, Tier), Crafts.Client.Model.KatUpgradeCost> Kat = new();
    public Dictionary<string, ItemPrice> BazaarItems = new();
    public Dictionary<string, long> ItemPrices = new();
    public Dictionary<string, float> NpcSellPrices = new();
    public DateTime LastUpdate = DateTime.MinValue;
    public bool IsUpdating = false;
}
