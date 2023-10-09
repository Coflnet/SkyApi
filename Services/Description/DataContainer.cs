using System.Linq;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Crafts.Client.Model;

namespace Coflnet.Sky.Api.Services.Description;
public class DataContainer
{
    InventoryDataWithSettings inventory;
    public List<(SaveAuction auction, IEnumerable<string> desc)> auctionRepresent;
    public List<Sniper.Client.Model.PriceEstimate> res;
    public Dictionary<string, ItemPrice> bazaarPrices;
    public List<List<DescModification>> mods;
    public Dictionary<string, (long, DateTime)> pricesPaid;
    internal ModDescriptionService modService;
    public ILookup<string, ListingSum> itemListings;
    internal Dictionary<(string, Core.Tier), KatUpgradeCost> katUpgradeCost;

    public List<Item> Items { get; internal set; }
}