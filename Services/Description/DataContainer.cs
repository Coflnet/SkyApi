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
public class DataContainer
{
    public InventoryDataWithSettings inventory;
    public List<(Core.SaveAuction auction, string[] desc)> auctionRepresent;
    public List<Sniper.Client.Model.PriceEstimate> PriceEst;
    public ImmutableDictionary<string, ItemPrice> bazaarPrices;
    public Dictionary<string, float> NpcSellPrices;
    public List<List<DescModification>> mods;
    public Dictionary<string, (long, DateTime, string)> pricesPaid;
    internal ModDescriptionService modService;
    public ILookup<string, ListingSum> itemListings;
    internal Dictionary<(string, Core.Tier), KatUpgradeCost> katUpgradeCost;
    internal Dictionary<string, long> itemPrices = new();
    internal Dictionary<string, ProfitableCraft> allCrafts;
    public Dictionary<string,string> itemTagToName;
    internal AccountInfo accountInfo;
    internal ILookup<long, Flip> flips;

    public List<Item> Items { get; internal set; }
    public Dictionary<string, Task<string>> Loaded { get; set; }

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