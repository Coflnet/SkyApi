using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.FlipTracker.Client.Model;

namespace Coflnet.Sky.Api.Services.Description;
/// <summary>Represents a data container.</summary>
public class DataContainer
{
    /// <summary>Stores the inventory.</summary>
    public InventoryDataWithSettings inventory;
    /// <summary>Stores the auction represent.</summary>
    public List<(Core.SaveAuction auction, string[] desc)> auctionRepresent;
    /// <summary>Stores the price est.</summary>
    public List<Sniper.Client.Model.PriceEstimate> PriceEst;
    /// <summary>Stores the bazaar prices.</summary>
    public ImmutableDictionary<string, ItemPrice> bazaarPrices;
    /// <summary>Stores the npc sell prices.</summary>
    public Dictionary<string, float> NpcSellPrices;
    /// <summary>Stores the mods.</summary>
    public List<List<DescModification>> mods;
    /// <summary>Stores the prices paid.</summary>
    public Dictionary<string, (long, DateTime, string)> pricesPaid;
    internal ModDescriptionService modService;
    /// <summary>Stores the item listings.</summary>
    public ILookup<string, ListingSum> itemListings;
    internal Dictionary<(string, Core.Tier), KatUpgradeCost> katUpgradeCost;
    internal Dictionary<string, long> itemPrices = new();
    internal Dictionary<string, ProfitableCraft> allCrafts;
    /// <summary>Stores the item tag to name.</summary>
    public Dictionary<string,string> itemTagToName;
    internal AccountInfo accountInfo;
    internal ILookup<long, Flip> flips;
    /// <summary>
    /// Minecraft name of the player the description is computed for, used to look up player state.
    /// </summary>
    internal string mcName;

    /// <summary>Gets or sets the items.</summary>
    public List<Item> Items { get; internal set; }
    /// <summary>Gets or sets the loaded.</summary>
    public Dictionary<string, Task<string>> Loaded { get; set; }

    /// <summary>Gets itemprice.</summary>
    public long GetItemprice(string itemKey)
    {
        return GetItemprice(itemKey, useBuyOrderPrices: false);
    }

    /// <summary>
    /// Gets the price of an item, optionally using buy order prices instead of insta-sell prices
    /// </summary>
    /// <param name="itemKey">The item key/tag</param>
    /// <param name="useBuyOrderPrices">If true, uses bazaar buy price (top buy order); if false, uses sell price (lowest sell offer)</param>
    /// <returns>The item price in coins</returns>
    public long GetItemprice(string itemKey, bool useBuyOrderPrices)
    {
        if (itemKey == null)
            return 0;
        if (itemPrices.TryGetValue(itemKey, out var price))
            return price;
        if (bazaarPrices.TryGetValue(itemKey, out var bazaarPrice))
        {
            // Use buy price (top buy order) if buy order mode is enabled, otherwise use sell price (lowest sell offer)
            return useBuyOrderPrices ? (long)bazaarPrice.BuyPrice : (long)bazaarPrice.SellPrice;
        }
        return 0;
    }
}
