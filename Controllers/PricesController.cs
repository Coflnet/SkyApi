using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.Api.Models;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Api.Models.Mod;
using Prometheus;

namespace Coflnet.Sky.Api.Controller;
/// <summary>
/// Endpoints for retrieving prices
/// </summary>
[ApiController]
[Route("api")]
[ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
public class PricesController : ControllerBase
{
    private PricesService priceService;
    HypixelContext context;
    private IItemsApi itemsApi;
    private ILogger<PricesController> logger;
    private ModDescriptionService modDescriptionSerice;
    private AhListChecker ahListChecker;
    Counter counter = Metrics.CreateCounter("sky_api_nbt", "Counts requests to /api/item/price/nbt");

    /// <summary>
    /// Creates a new intance of <see cref="PricesController"/>
    /// </summary>
    /// <param name="pricesService"></param>
    /// <param name="context"></param>
    /// <param name="itemsApi"></param>
    /// <param name="logger"></param>
    /// <param name="modDescriptionSerice"></param>
    public PricesController(
        PricesService pricesService,
        HypixelContext context,
        IItemsApi itemsApi,
        ILogger<PricesController> logger,
        ModDescriptionService modDescriptionSerice,
        AhListChecker ahListChecker)
    {
        priceService = pricesService;
        this.context = context;
        this.itemsApi = itemsApi;
        this.logger = logger;
        this.modDescriptionSerice = modDescriptionSerice;
        this.ahListChecker = ahListChecker;
    }
    /// <summary>
    /// Aggregated sumary of item prices for the 2 last days
    /// stackable items are reduced to a single item
    /// </summary>
    /// <param name="itemTag">The item tag you want prices for</param>
    /// <param name="query">Filter query</param>
    /// <returns></returns>
    [Route("item/price/{itemTag}")]
    [HttpGet]
    [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
    public Task<PriceSumary> GetSumary(string itemTag, [FromQuery] IDictionary<string, string> query)
    {
        return priceService.GetSumary(itemTag, new Dictionary<string, string>(query));
    }

    /// <summary>
    /// Gets the lowest bin by item type
    /// </summary>
    /// <param name="itemTag">The tag of the item to search for bin</param>
    /// <param name="query"></param>
    /// <returns></returns>
    [Route("item/price/{itemTag}/bin")]
    [HttpGet]
    [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
    public async Task<ActionResult<BinResponse>> GetLowestBin(string itemTag, [FromQuery] IDictionary<string, string> query)
    {
        var result = await priceService.GetLowestBinData(itemTag, new Dictionary<string, string>(query));
        return Ok(new BinResponse(result.cost, result.uuid, result.slbin));
    }

    /// <summary>
    /// Gets the current (latest known) price for an item and available quantity, supports items from bazaar and ah
    /// </summary>
    /// <param name="itemTag">The tag of the item</param>
    /// <param name="count">How many items to search for (and include in cost)</param>
    /// <returns>The current buy and cost (for how much given count could be sold) and how many items are available for purchase</returns>
    [Route("item/price/{itemTag}/current")]
    [HttpGet]
    [ResponseCache(Duration = 180, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "count" })]
    public async Task<CurrentPrice> GetCurrentPrice(string itemTag, int count = 1)
    {
        return await priceService.GetCurrentPrice(itemTag, count);
    }
    /// <summary>
    /// Gets the price history for an item for the last 24 hours
    /// </summary>
    /// <param name="itemTag">The tag of the item</param>
    /// <param name="query">filter query</param>
    /// <returns></returns>
    [Route("item/price/{itemTag}/history/day")]
    [HttpGet]
    [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
    public async Task<IEnumerable<AveragePrice>> GetDayHistory(string itemTag, [FromQuery] IDictionary<string, string> query)
    {
        return await priceService.GetHistory(itemTag, DateTime.UtcNow - TimeSpan.FromDays(1), DateTime.UtcNow, new Dictionary<string, string>(query));
    }
    /// <summary>
    /// Gets the price history for an item for the last 7 days
    /// </summary>
    /// <param name="itemTag">The tag of the item</param>
    /// <param name="query">filter query</param>
    /// <returns></returns>
    [Route("item/price/{itemTag}/history/week")]
    [HttpGet]
    [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
    public async Task<IEnumerable<AveragePrice>> GetWeekHistory(string itemTag, [FromQuery] IDictionary<string, string> query)
    {
        return await priceService.GetHistory(itemTag, DateTime.UtcNow - TimeSpan.FromDays(7), DateTime.UtcNow, new Dictionary<string, string>(query));
    }
    /// <summary>
    /// Gets the price history for an item for one month
    /// </summary>
    /// <param name="itemTag">The tag of the item</param>
    /// <param name="query">filter query</param>
    /// <returns></returns>
    [Route("item/price/{itemTag}/history/month")]
    [HttpGet]
    [ResponseCache(Duration = 3600 * 2, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
    public async Task<IEnumerable<AveragePrice>> GetMonthHistory(string itemTag, [FromQuery] IDictionary<string, string> query)
    {
        return await priceService.GetHistory(itemTag, DateTime.UtcNow - TimeSpan.FromDays(30), DateTime.UtcNow, itemTag == "ENCHANTED_BOOK" ? null : new Dictionary<string, string>(query));
    }
    /// <summary>
    /// Gets the price history for an item for all time
    /// </summary>
    /// <param name="itemTag">The tag of the item</param>
    /// <returns></returns>
    [Route("item/price/{itemTag}/history/full")]
    [HttpGet]
    [ResponseCache(Duration = 3600 * 2, Location = ResponseCacheLocation.Any, NoStore = false)]
    public async Task<IEnumerable<AveragePrice>> GetFullHistory(string itemTag)
    {
        var id = ItemDetails.Instance.GetItemIdForTag(itemTag, true);
        return await context.Prices.Where(p => p.ItemId == id).ToListAsync();
    }

    /// <summary>
    /// Returns all available filters with all available options
    /// </summary>
    /// <returns></returns>
    [Route("filter/options")]
    [HttpGet]
    [ResponseCache(Duration = 3600 * 6, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "itemTag" })]
    public async Task<List<FilterOptions>> GetFilterOptions(string itemTag = "*")
    {
        var fe = new Sky.Filter.FilterEngine();
        var optionsTask = itemsApi.ItemItemTagModifiersAllGetAsync(itemTag);
        if (itemTag == "*")
        {
            var all = await optionsTask;
            return fe.AvailableFilters.Where(f =>
            {
                try
                {
                    var options = f.OptionsGet(new OptionValues(all));
                    return options.Count() > 0;
                }
                catch (System.Exception e)
                {
                    dev.Logger.Instance.Error(e, "retrieving filter options");
                    return false;
                }
            }).Select(f => new FilterOptions(f, all)).ToList();
        }
        var item = await itemsApi.ItemItemTagGetAsync(itemTag);
        var allOptions = await optionsTask;
        var filters = fe.FiltersFor(item);

        return filters.Select(f => new FilterOptions(f, allOptions)).ToList();
    }

    /// <summary>
    /// Returns price estimations for nbt data (for in game mods) 
    /// NOTE: THIS WILL BE A PAID FEATURE IN THE FUTURE
    /// </summary>
    /// <returns></returns>
    [Route("price/nbt")]
    [HttpPost]
    public async Task<IEnumerable<PriceEstimate>> GetFromNbt(InventoryData inventoryData)
    {
        var auctions = modDescriptionSerice.ConvertToAuctions(inventoryData).Take(90).ToList();
        var priceTask = modDescriptionSerice.GetPrices(auctions.Select(a => a.auction));
        try
        {
            ahListChecker.CheckItems(auctions.Where(a => a.auction?.ItemName != null)
                .Select(a => new Item() { Description = string.Join("\n", a.desc), ItemName = a.auction.ItemName }), "sender: " + inventoryData.SenderContactId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "failed to publish inventory nbt");
        }
        var data = await modDescriptionSerice.GetPrices(auctions.Select(a => a.auction)) 
            ?? throw new CoflnetException("sniper_unreachable", "The sniper service could not be reached for prices");
        counter.Inc();
        return data.Select(d =>
        {
            if (d == null || d.MedianKey == null && d.LbinKey == null)
                return null;
            try
            {
                return new PriceEstimate()
                {
                    Lbin = d.Lbin.Price,
                    LbinLink = d.Lbin.Price == 0 ? null : "https://sky.coflnet.com/a/" + AuctionService.Instance.GetUuid(d.Lbin.AuctionId),
                    Median = d.Median,
                    Volume = d.Volume,
                    FastSell = Math.Min(d.Lbin.Price, d.Median) * 85 / 100,
                    LbinKey = d.LbinKey,
                    MedianKey = d.MedianKey,
                    ItemKey = d.ItemKey
                };
            }
            catch (System.Exception e)
            {
                dev.Logger.Instance.Error(e, "converting price estimate\n" + JSON.Stringify(d));
                return null;
            }
        });
    }

    [Route("price/attributes")]
    [HttpGet]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
    public async Task<Dictionary<string, Dictionary<string, long>>> GetAttributePrices()
    {
        var tags = new HashSet<string>(){"FERVOR_HELMET",
                "FERVOR_CHESTPLATE", "FERVOR_LEGGINGS", "FERVOR_BOOTS",
                "HOLLOW_HELMET",  "HOLLOW_CHESTPLATE",  "HOLLOW_LEGGINGS",  "HOLLOW_BOOTS",
                "AURORA_HELMET",  "AURORA_CHESTPLATE",  "AURORA_LEGGINGS",  "AURORA_BOOTS",
                "TERROR_HELMET",  "TERROR_CHESTPLATE",  "TERROR_LEGGINGS",  "TERROR_BOOTS",
                "CRIMSON_HELMET",  "CRIMSON_CHESTPLATE",  "CRIMSON_LEGGINGS",  "CRIMSON_BOOTS",
                "IMPLOSION_BELT",  "GAUNTLET_OF_CONTAGION",
                "MOLTEN_BELT",  "MOLTEN_NECKLACE",  "MOLTEN_CLOAK",  "MOLTEN_BRACELET",
                "ATTRIBUTE_SHARD"};
        var reverseAttributeMap = new Dictionary<int, string>();
        var ids = tags.Select(t => ItemDetails.Instance.GetItemIdForTag(t, true)).ToList();
        var attributeIds = await Task.WhenAll(Constants.AttributeKeys.Select(a =>
        {
            var id = NBT.Instance.GetKeyId(a);
            reverseAttributeMap[id] = a;
            return Task.FromResult(id);
        }));
        var ignoreIds = await Task.WhenAll(new HashSet<string>() { "boss_tier", "id" }.Select(a => Task.FromResult(NBT.Instance.GetKeyId(a))));
        var oldestTime = DateTime.UtcNow.AddDays(-1);
        ignoreIds = ignoreIds.Concat(attributeIds).ToArray();
        var prices = await context.Auctions.Where(a => ids.Contains(a.ItemId) && !a.NBTLookup.Any(l => !ignoreIds.Contains(l.KeyId)) && a.End > oldestTime && a.HighestBidAmount > 0 && a.End < DateTime.UtcNow)
            .Select(a => new { k1 = a.NBTLookup.Where(l => attributeIds.Contains(l.KeyId)).OrderBy(l => l.KeyId).First(), k2 = a.NBTLookup.Where(l => attributeIds.Contains(l.KeyId)).OrderBy(l => l.KeyId).Last(), a.HighestBidAmount, a.Tag })
            .ToListAsync();
        logger.LogInformation("found {count} auctions", prices.Count);
        return prices.GroupBy(p => p.Tag).ToDictionary(g => g.Key, g => g.Where(a => a.k1 != null && a.k2 != null).GroupBy(a => new { a.Tag, a.k1.KeyId, k1Val = a.k1.Value, k2Id = a.k2.KeyId, k2Val = a.k2.Value })
            .Select(g => new { g.First().Tag, g.Key.KeyId, g.Key.k1Val, g.Key.k2Id, g.Key.k2Val, sum = g.Average(a => a.HighestBidAmount) })
            .ToDictionary(p => $"{reverseAttributeMap[p.KeyId]}:{p.k1Val}&{reverseAttributeMap[p.k2Id]}:{p.k2Val}", p => (long)p.sum));
    }

    /// <summary>
    /// Returns bazaar history 
    /// </summary>
    /// <returns></returns>
    [Route("bazaar/item/history/{itemTag}/status")]
    [HttpGet]
    [ResponseCache(Duration = 3600 * 6, Location = ResponseCacheLocation.Any, NoStore = false)]
    [Obsolete("Uses an endpoint starting with /api/bazaar instead", true)]
    public string GetBazaar(string itemTag)
    {
        return "endpoint deprecated, use one starting with /api/bazaar";
    }

}
