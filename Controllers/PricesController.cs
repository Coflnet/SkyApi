using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.Api.Models;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Items.Client.Api;

namespace Coflnet.Hypixel.Controller
{
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

        /// <summary>
        /// Creates a new intance of <see cref="PricesController"/>
        /// </summary>
        /// <param name="pricesService"></param>
        /// <param name="context"></param>
        /// <param name="itemsApi"></param>
        /// <param name="logger"></param>
        public PricesController(PricesService pricesService, HypixelContext context, IItemsApi itemsApi, ILogger<PricesController> logger)
        {
            priceService = pricesService;
            this.context = context;
            this.itemsApi = itemsApi;
            this.logger = logger;
        }
        /// <summary>
        /// Aggregated sumary of item prices for the 3 last days
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
            var result = await ItemPrices.GetLowestBin(itemTag, new Dictionary<string, string>(query));
            return Ok(new BinResponse(result.FirstOrDefault()?.Price ?? 0, result.FirstOrDefault()?.Uuid, result.LastOrDefault()?.Price ?? 0));
        }

        /// <summary>
        /// Gets the current (latest known) price for an item and available quantity, supports items from bazaar and ah
        /// </summary>
        /// <param name="itemTag">The tag of the item</param>
        /// <param name="count">How many items to search for (and include in cost)</param>
        /// <returns></returns>
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
        [ResponseCache(Duration = 3600*2, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
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
        [ResponseCache(Duration = 3600*2, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<AveragePrice>> GetFullHistory(string itemTag)
        {
            var id =  ItemDetails.Instance.GetItemIdForTag(itemTag, true);
            return await context.Prices.Where(p=>p.ItemId == id).ToListAsync();
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
                        return true;
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
        /// Returns bazaar history 
        /// </summary>
        /// <returns></returns>
        [Route("bazaar/item/history/{itemTag}/status")]
        [HttpGet]
        [ResponseCache(Duration = 3600 * 6, Location = ResponseCacheLocation.Any, NoStore = false)]
        [Obsolete("Uses an endpoint starting with /api/bazaar instead",true)]
        public string GetBazaar(string itemTag)
        {
            return "endpoint deprecated, use one starting with /api/bazaar";
        }
    }
}