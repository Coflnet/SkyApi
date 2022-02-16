using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky;
using Coflnet.Sky.Filter;
using hypixel;
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
        /// Aggregated sumary of item prices for the last day
        /// </summary>
        /// <param name="itemTag">The item tag you want prices for</param>
        /// <param name="query">Filter query</param>
        /// <returns></returns>
        [Route("item/price/{itemTag}")]
        [HttpGet]
        [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "query" })]
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
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "query" })]
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
        public async Task<List<TimedQuickStatus>> GetBazaar(string itemTag)
        {
            var itemId = ItemDetails.Instance.GetItemIdForName(itemTag);
            var maxTime = DateTime.Now - TimeSpan.FromDays(5);
            var fe = await context.BazaarPull.Where(b => b.Timestamp.Minute == 0 && b.Timestamp.Hour == 0 && b.Timestamp > maxTime)
                    //.GroupBy(b=> new {/*b.PullInstance.Timestamp.Hour,*/ b.PullInstance.Timestamp.Date})
                    .SelectMany(p => p.Products.Where(b => b.ProductId == itemTag).Select(b => new { b.QuickStatus, b.PullInstance.Timestamp })).ToListAsync();

            return fe.GroupBy(b => b.Timestamp.Date).Select(b => b.First()).Select(f => new TimedQuickStatus()
            {
                BuyMovingWeek = f.QuickStatus.BuyMovingWeek,
                BuyOrders = f.QuickStatus.BuyOrders,
                BuyPrice = f.QuickStatus.BuyPrice,
                BuyVolume = f.QuickStatus.BuyVolume,
                SellMovingWeek = f.QuickStatus.SellMovingWeek,
                SellOrders = f.QuickStatus.SellOrders,
                SellPrice = f.QuickStatus.SellPrice,
                SellVolume = f.QuickStatus.SellVolume,
                Time = f.Timestamp
            }).ToList();
        }

        public class TimedQuickStatus : dev.QuickStatus
        {
            public DateTime Time;
        }
    }
}