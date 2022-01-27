

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Filter;
using hypixel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestSharp;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Api.Models;

namespace Coflnet.Hypixel.Controller
{
    [ApiController]
    [Route("api")]
    public class AuctionsController : ControllerBase
    {
        AuctionService auctionService;
        HypixelContext context;
        ILogger<AuctionsController> logger;
        IConfiguration config;

        public AuctionsController(AuctionService auctionService, HypixelContext context, ILogger<AuctionsController> logger, IConfiguration config)
        {
            this.auctionService = auctionService;
            this.context = context;
            this.logger = logger;
            this.config = config;
        }

        /// <summary>
        /// Retrieve details of a specific auction
        /// </summary>
        /// <param name="auctionUuid">The uuid of the auction you want the details for</param>
        /// <returns></returns>
        [Route("auction/{auctionUuid}")]
        [HttpGet]
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<EnchantColorMapper.ColorSaveAuction> getAuctionDetails(string auctionUuid)
        {
            var result = await auctionService.GetAuctionAsync(auctionUuid, auction => auction
                        .Include(a => a.Enchantments)
                        .Include(a => a.NbtData)
                        .Include(a => a.Bids));

            return EnchantColorMapper.Instance.AddColors(result);
        }

        /// <summary>
        /// Get the 10 (or how many are available) lowest bins
        /// </summary>
        /// <param name="itemTag">The itemTag to get bins for</param>
        /// <param name="query">Filters for the auctions</param>
        /// <returns></returns>
        [Route("auctions/tag/{itemTag}/active/bin")]
        [HttpGet]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<List<SaveAuction>> GetLowestBins(string itemTag, [FromQuery] IDictionary<string, string> query)
        {
            var itemId = ItemDetails.Instance.GetItemIdForName(itemTag);
            var filter = new Dictionary<string, string>(query);
            int page = 0;
            if (filter.ContainsKey("page"))
            {
                int.TryParse(filter["page"], out page);
                filter.Remove("page");
            }
            filter["ItemId"] = itemId.ToString();
            var pageSize = 10;
            var result = await new FilterEngine().AddFilters(context.Auctions
                        .Where(a => a.ItemId == itemId && a.End > DateTime.Now && a.HighestBidAmount == 0 && a.Bin), filter)
                        .Include(a => a.Enchantments)
                        .Include(a => a.NbtData)
                        .OrderBy(a => a.StartingBid)
                        .Skip(page * pageSize)
                        .Take(pageSize).ToListAsync();

            return result;
        }
        /// <summary>
        /// Get a batch of 1000 auctions that sold in the last week for any kind of processing.
        /// Please credit us with providing data for whatever you are doing.
        /// You can also manually request a review to get older data on the discord.
        /// </summary>
        /// <param name="itemTag">The itemTag to get auctions for</param>
        /// <param name="page">Page of auctions to get</param>
        /// <returns></returns>
        [Route("auctions/tag/{itemTag}/sold")]
        [HttpGet]
        [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "page" })]
        public async Task<List<SaveAuction>> GetHistory(string itemTag, int page = 0)
        {
            var itemId = ItemDetails.Instance.GetItemIdForName(itemTag);
            var pageSize = 1000;
            var startTime = DateTime.Now.RoundDown(TimeSpan.FromHours(1)) - TimeSpan.FromDays(7);
            var result = await context.Auctions
                        .Where(a => a.ItemId == itemId && a.End > startTime && a.End < DateTime.Now && a.HighestBidAmount > 0)
                        .Include(a => a.Enchantments)
                        .Include(a => a.NbtData)
                        .Skip(page * pageSize)
                        .Take(pageSize).ToListAsync();

            return result;
        }

        /// <summary>
        /// Get items that are in low supply
        /// </summary>
        /// <returns></returns>
        [Route("auctions/supply/low")]
        [HttpGet]
        [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<SupplyElement>> GetLowestBins()
        {
            var client = new RestClient(config["API_BASE_URL"]);
            var lowSupply = await IndexerClient.LowSupply();
            var result = new List<SupplyElement>();
            var tasks = lowSupply.Where(s => s.Value > 2).Select(
            async item =>
            {
                try
                {
                    var response = await client.ExecuteAsync(CreateRequestTo("/api/item/price/" + item.Key));
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        logger.LogInformation("been rate limited");
                        return;
                    }
                    var data = JsonConvert.DeserializeObject<PriceSumary>(response.Content);
                    if (data.Med < 1_000_000 && data.Volume > 0)
                        return;

                    var lowestBinTask = client.ExecuteAsync(CreateRequestTo($"/api/item/price/{item.Key}/bin"));
                    var lbinData = JsonConvert.DeserializeObject<BinResponse>((await lowestBinTask).Content);
                    result.Add(new SupplyElement()
                    {
                        Supply = item.Value,
                        Tag = item.Key,
                        Median = data.Med,
                        LbinData = lbinData,
                        Volume = data.Volume
                    });
                }
                catch (Exception e)
                {
                    logger.LogError(e, "getting average price data for low supply page");
                }
            });
            await Task.WhenAll(tasks);
            return result;
        }

        private static RestRequest CreateRequestTo(string path)
        {
            var lbinReq = new RestRequest(path);
            lbinReq.Timeout = 3000;
            return lbinReq;
        }

        public class SupplyElement
        {
            public string Tag;
            public long Supply;
            public long Median;
            public BinResponse LbinData;
            public long Volume;
        }
    }
}

