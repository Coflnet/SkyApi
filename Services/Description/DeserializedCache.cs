using System.Collections.Immutable;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services;

// wrapper for the deserialized cache
public class DeserializedCache
{
    public Dictionary<string, Crafts.Client.Model.ProfitableCraft> Crafts = new();
    public Dictionary<(string, Tier), Crafts.Client.Model.KatUpgradeCost> Kat = new();
    public ImmutableDictionary<string, ItemPrice> BazaarItems = ImmutableDictionary<string, ItemPrice>.Empty;
    public Dictionary<string, long> ItemPrices = new();
    public Dictionary<string, float> NpcSellPrices = new();
    public DateTime LastUpdate = DateTime.MinValue;
    public Dictionary<string,string> ItemTagToName = new();
    public bool IsUpdating = false;
}
