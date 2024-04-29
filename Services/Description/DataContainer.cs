using System.Linq;
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
    public List<(SaveAuction auction, string[] desc)> auctionRepresent;
    public List<Sniper.Client.Model.PriceEstimate> PriceEst;
    public Dictionary<string, ItemPrice> bazaarPrices;
    public List<List<DescModification>> mods;
    public Dictionary<string, (long, DateTime, string)> pricesPaid;
    internal ModDescriptionService modService;
    public ILookup<string, ListingSum> itemListings;
    internal Dictionary<(string, Core.Tier), KatUpgradeCost> katUpgradeCost;
    internal Dictionary<string, long> itemPrices = new();
    internal Dictionary<string, ProfitableCraft> allCrafts;
    internal AccountInfo accountInfo;
    internal ILookup<long, Flip> flips;

    public List<Item> Items { get; internal set; }

    public long GetItemprice(string itemKey)
    {
        if (itemKey == null)
            return 0;
        if (itemPrices.TryGetValue(itemKey, out var price))
            return price;
        if (bazaarPrices.TryGetValue(itemKey, out var bazaarPrice))
            return (long)bazaarPrice.SellPrice;
        return 0;
    }
}