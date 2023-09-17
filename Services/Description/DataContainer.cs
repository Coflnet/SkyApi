using System.Linq;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Services.Description;
public class DataContainer
{
    InventoryDataWithSettings inventory;
    public List<(SaveAuction auction, IEnumerable<string> desc)> auctionRepresent;
    public List<Sniper.Client.Model.PriceEstimate> res;
    public Dictionary<string, ItemPrice> bazaarPrices;
    public List<List<DescModification>> mods;
    public Dictionary<string, (long,DateTime)> pricesPaid;
    internal ModDescriptionService modService;
    public ILookup<string, (long highest, long start, DateTime end, bool requestingUserIsSeller)> itemListings;

    public List<Item> Items { get; internal set; }
}