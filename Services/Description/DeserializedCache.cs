using System.Collections.Immutable;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services;

// wrapper for the deserialized cache
/// <summary>Represents a deserialized cache.</summary>
public class DeserializedCache
{
    /// <summary>Stores the crafts.</summary>
    public Dictionary<string, Crafts.Client.Model.ProfitableCraft> Crafts = new();
    /// <summary>Stores the kat.</summary>
    public Dictionary<(string, Tier), Crafts.Client.Model.KatUpgradeCost> Kat = new();
    /// <summary>Stores the bazaar items.</summary>
    public ImmutableDictionary<string, ItemPrice> BazaarItems = ImmutableDictionary<string, ItemPrice>.Empty;
    /// <summary>Stores the item prices.</summary>
    public Dictionary<string, long> ItemPrices = new();
    /// <summary>Stores the NPC sell prices.</summary>
    public Dictionary<string, float> NpcSellPrices = new();
    /// <summary>Stores the last update.</summary>
    public DateTime LastUpdate = DateTime.MinValue;
    /// <summary>Stores the item tag to name.</summary>
    public Dictionary<string,string> ItemTagToName = new();
    /// <summary>Indicates whether a cache update is in progress.</summary>
    public bool IsUpdating = false;
}
