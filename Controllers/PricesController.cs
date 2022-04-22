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

        /// <summary>
        /// Creates a new intance of <see cref="PricesController"/>
        /// </summary>
        /// <param name="pricesService"></param>
        /// <param name="context"></param>
        public PricesController(PricesService pricesService, HypixelContext context)
        {
            priceService = pricesService;
            this.context = context;
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
        /// Gets the current (latest known) price for an item
        /// </summary>
        /// <param name="itemTag">The tag of the item/param>
        /// <param name="count">How many items to search for</param>
        /// <returns></returns>
        [Route("item/price/{itemTag}/current")]
        [HttpGet]
        [ResponseCache(Duration = 180, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "count" })]
        public async Task<CurrentPrice> GetCurrentPrice(string itemTag, int count = 1)
        {
            return await priceService.GetCurrentPrice(itemTag, count);
        }
        /// <summary>
        /// Gets the price history for an item
        /// </summary>
        /// <param name="itemTag">The tag of the item/param>
        /// <param name="count">How many items to search for</param>
        /// <returns></returns>
        [Route("item/price/{itemTag}/history/day")]
        [HttpGet]
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
        public async Task<IEnumerable<AveragePrice>> GetDayHistory(string itemTag, [FromQuery] IDictionary<string, string> query)
        {
            return await priceService.GetHistory(itemTag, DateTime.UtcNow - TimeSpan.FromDays(1), DateTime.UtcNow, new Dictionary<string, string>(query));
        }
        /// <summary>
        /// Gets the price history for an item
        /// </summary>
        /// <param name="itemTag">The tag of the item/param>
        /// <param name="count">How many items to search for</param>
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
        /// <param name="itemTag">The tag of the item/param>
        /// <param name="count">How many items to search for</param>
        /// <returns></returns>
        [Route("item/price/{itemTag}/history/month")]
        [HttpGet]
        [ResponseCache(Duration = 3600*2, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "count" })]
        public async Task<IEnumerable<AveragePrice>> GetMonthHistory(string itemTag)
        {
            return await priceService.GetHistory(itemTag, DateTime.UtcNow - TimeSpan.FromDays(30), DateTime.UtcNow, null);
        }

        /// <summary>
        /// Returns all available filters with all available options
        /// </summary>
        /// <returns></returns>
        [Route("filter/options")]
        [HttpGet]
        [ResponseCache(Duration = 3600 * 6, Location = ResponseCacheLocation.Any, NoStore = false)]
        public List<FilterOptions> GetFilterOptions()
        {
            var fe = new Sky.Filter.FilterEngine();
            return fe.AvailableFilters.Select(f => new FilterOptions(f)).ToList();
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