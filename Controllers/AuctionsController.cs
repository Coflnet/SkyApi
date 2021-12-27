

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

namespace Coflnet.Hypixel.Controller
{
    [ApiController]
    [Route("api")]
    public class AuctionsController : ControllerBase
    {
        AuctionService auctionService;
        HypixelContext context;
        ILogger<AuctionsController> logger;

        public AuctionsController(AuctionService auctionService, HypixelContext context, ILogger<AuctionsController> logger)
        {
            this.auctionService = auctionService;
            this.context = context;
            this.logger = logger;
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
        //[ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
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
        /// Get items that are in low supply
        /// </summary>
        /// <returns></returns>
        [Route("auctions/supply/low")]
        [HttpGet]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<SupplyElement>> GetLowestBins()
        {
            var client = new RestClient("http://localhost:8000");
            var lowSupply = await IndexerClient.LowSupply();
            var result = new List<SupplyElement>();
            await Parallel.ForEachAsync(lowSupply, 
            new ParallelOptions() { MaxDegreeOfParallelism = 2},
            async (item, cancelToken) =>
            {
                try
                {
                    var lowestBinTask = client.ExecuteAsync(new RestRequest($"/api/item/price/{item.Key}/bin"));
                    var response = await client.ExecuteAsync(new RestRequest("/api/item/price/" + item.Key));
                    var lowestBin = await lowestBinTask;
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        logger.LogInformation("been rate limited");
                        return;
                    }
                    var data = JsonConvert.DeserializeObject<PriceSumary>(response.Content);
                    var lbinData = JsonConvert.DeserializeObject<PricesController.BinResponse>(lowestBin.Content);
                    if (data.Med < 1_000_000 && data.Volume > 0)
                        return;
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
            return result;
        }

        public class SupplyElement
        {
            public string Tag;
            public long Supply;
            public long Median;
            public PricesController.BinResponse LbinData;
            public long Volume;
        }
    }
}

