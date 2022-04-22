

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestSharp;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Hypixel.Controller
{
    /// <summary>
    /// Endpoints for retrieving information about auctions
    /// </summary>
    [ApiController]
    [Route("api")]
    public class AuctionsController : ControllerBase
    {
        AuctionService auctionService;
        HypixelContext context;
        ILogger<AuctionsController> logger;
        PricesService pricesService;
        IConfiguration config;
        static FilterEngine fe = new FilterEngine();

        /// <summary>
        /// Creates a new instance of <see cref="AuctionsController"/>
        /// </summary>
        /// <param name="auctionService"></param>
        /// <param name="context"></param>
        /// <param name="logger"></param>
        /// <param name="config"></param>
        /// <param name="pricesService"></param>
        public AuctionsController(AuctionService auctionService, HypixelContext context, ILogger<AuctionsController> logger, IConfiguration config, PricesService pricesService)
        {
            this.auctionService = auctionService;
            this.context = context;
            this.logger = logger;
            this.config = config;
            this.pricesService = pricesService;
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
            if (result != null && string.IsNullOrEmpty(result.ItemName))
                result.ItemName = ItemDetails.TagToName(result.Tag);
            return EnchantColorMapper.Instance.AddColors(result);
        }
        /// <summary>
        /// Retrieve the uid of an auction (mainly a helper to get the lookup id for another service)
        /// </summary>
        /// <param name="auctionUuid">The uuid of the auction you want the details for</param>
        /// <returns></returns>
        [Route("auction/{auctionUuid}/uid")]
        [HttpGet]
        [ResponseCache(Duration = 3, Location = ResponseCacheLocation.Any, NoStore = false)]
        public string getAuctionUid(string auctionUuid)
        {
            Console.WriteLine(auctionUuid);
            return auctionService.GetId(auctionUuid).ToString();
        }

        /// <summary>
        /// Get the 10 (or how many are available) lowest bins
        /// </summary>
        /// <param name="itemTag">The itemTag to get bins for</param>
        /// <param name="query">Filters for the auctions</param>
        /// <returns></returns>
        [Route("auctions/tag/{itemTag}/active/bin")]
        [HttpGet]
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
        public async Task<List<SaveAuction>> GetLowestBins(string itemTag, [FromQuery] IDictionary<string, string> query)
        {
            var itemId = ItemDetails.Instance.GetItemIdForTag(itemTag);
            var filter = new Dictionary<string, string>(query);
            int page = 0;
            if (filter.ContainsKey("page"))
            {
                int.TryParse(filter["page"], out page);
                filter.Remove("page");
            }
            filter["ItemId"] = itemId.ToString();
            var pageSize = 10;
            var select = fe.AddFilters(context.Auctions
                        .Where(a => a.ItemId == itemId && a.End > DateTime.Now && a.HighestBidAmount == 0 && a.Bin), filter)
                        .Include(a => a.Enchantments)
                        .Include(a => a.NbtData)
                        .OrderBy(a => a.StartingBid)
                        .Skip(page * pageSize)
                        .Take(pageSize);
            var result = await select.ToListAsync();
            return result;
        }
        /// <summary>
        /// Get a batch of 1000 auctions that sold in the last week for any kind of processing.
        /// Please credit us with providing data for whatever you are doing.
        /// You can also manually request a review to get older data on the discord.
        /// </summary>
        /// <param name="itemTag">The itemTag to get auctions for</param>
        /// <param name="page">Page of auctions to get</param>
        /// <param name="pageSize">how many auctions to get per page 1-1000</param>
        /// <returns></returns>
        [Route("auctions/tag/{itemTag}/sold")]
        [HttpGet]
        [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "page" })]
        public async Task<List<SaveAuction>> GetHistory(string itemTag, int page = 0, int pageSize = 1000)
        {
            var itemId = ItemDetails.Instance.GetItemIdForTag(itemTag);
            if (pageSize < 0 || pageSize > 1000)
                pageSize = 1000;
            var startTime = DateTime.Now.RoundDown(TimeSpan.FromHours(1)) - TimeSpan.FromDays(7);
            var result = await context.Auctions
                        .Where(a => a.ItemId == itemId && a.End > startTime && a.End < DateTime.Now && a.HighestBidAmount > 0)
                        .Include(a => a.Enchantments)
                        .Include(a => a.NbtData)
                        .Skip(page * pageSize)
                        .OrderByDescending(a=>a.End)
                        .Take(pageSize).ToListAsync();

            return result;
        }
        /// <summary>
        /// Gets all recorded past sells of an item with a specific uuid
        /// meant for dupe detection
        /// </summary>
        /// <param name="uid">The Item uuid or just uid</param>
        /// <returns></returns>
        [Route("auctions/uid/{uid}/sold")]
        [HttpGet]
        [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<ItemSell>> GetUidHistory(string uid)
        {
            var numericId = GetUidFromString(uid);
            var key = NBT.Instance.GetKeyId("uid");
            var result = await context.Auctions
                        .Where(a => a.NBTLookup.Where(l => l.KeyId == key && l.Value == numericId).Any())
                        .Where(a => a.HighestBidAmount > 0)
                        .Select(a => new { a.AuctioneerId, a.Uuid, a.End, Buyer = a.Bids.OrderByDescending(b => b.Amount).Select(b => b.Bidder).FirstOrDefault() })
                        .AsSplitQuery()
                        .ToListAsync();

            return result.Select(i => new ItemSell()
            {
                Buyer = i.Buyer,
                Seller = i.AuctioneerId,
                Timestamp = i.End,
                Uuid = i.Uuid
            });
        }
        /// <summary>
        /// Gets all recorded past sells of a batch of items by uuid
        /// meant for dupe detection of whole inventories
        /// </summary>
        /// <param name="request">The Item uuid or just uid</param>
        /// <returns></returns>
        [Route("auctions/uids/sold")]
        [HttpPost]
        [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<Dictionary<string, IEnumerable<ItemSell>>> GetUidsHistory([FromBody] InventoryBatchLookup request)
        {
            if (request.Uuids.Length > 35)
                throw new CoflnetException("to_many_uuid", "Please do batch lookups on no more than 35 uuids at a time");
            var numericIds = request.Uuids.GroupBy(id => id).Select(ids => ids.First()).ToDictionary(uid => GetUidFromString(uid));
            var key = NBT.Instance.GetKeyId("uid");
            var result = await context.Auctions
                        .Where(a => a.NBTLookup.Where(l => l.KeyId == key && numericIds.Keys.Contains(l.Value)).Any())
                        .Where(a => a.HighestBidAmount > 0)
                        .Select(a => new
                        {
                            a.AuctioneerId,
                            a.Uuid,
                            a.End,
                            Buyer = a.Bids.OrderByDescending(b => b.Amount).Select(b => b.Bidder).FirstOrDefault(),
                            uid = a.NBTLookup.Where(l => l.KeyId == key).Select(l => l.Value).FirstOrDefault()
                        })
                        .AsSplitQuery()
                        .ToListAsync();

            var sells = result.GroupBy(i => i.uid).ToDictionary(i => numericIds[i.Key], items => items.Select(i => new ItemSell()
            {
                Buyer = i.Buyer,
                Seller = i.AuctioneerId,
                Timestamp = i.End,
                Uuid = i.Uuid
            }));
            var defaultVal = new ItemSell[0];
            return numericIds.ToDictionary(id => id.Value, id => sells.GetValueOrDefault(id.Value, defaultVal));
        }

        /// <summary>
        /// Checks an array of item uuids if they are active on the ah
        /// </summary>
        /// <param name="uuids">The list of uuids to check</param>
        /// <returns>A list of found uuids with active auctions</returns>
        [Route("auctions/active/uuid")]
        [HttpPost]
        [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<string>> CheckIfIdsActive([FromBody] List<string> uuids)
        {
            var key = NBT.Instance.GetKeyId("uid");
            var uIds = uuids.Select(u =>
            {
                return GetUidFromString(u);
            }).ToHashSet();
            var lookups = context.NBTLookups.Where(l => l.KeyId == key && uIds.Contains(l.Value)).Select(l => l.AuctionId);

            var result = await context.Auctions
                        .Where(a => context.NBTLookups.Where(l => l.KeyId == key && uIds.Contains(l.Value)).Select(l => l.AuctionId).Contains(a.Id) && a.End > DateTime.Now)
                        .Include(a => a.NBTLookup)
                        .Select(a => a.NBTLookup.Where(l => l.KeyId == key).Select(l => l.Value).FirstOrDefault()).ToListAsync();
            var endings = result.Select(id => id.ToString("x")).ToHashSet();
            return uuids.Where(id => endings.Contains(id.Substring(id.Length - 12)));
        }

        private static long GetUidFromString(string u)
        {
            if (u.Length < 12)
                throw new CoflnetException("invalid_uuid", "One or more passed uuids are invalid (too short)");
            return NBT.UidToLong(u.Substring(u.Length - 12));
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
                    var data = await pricesService.GetSumaryCache(item.Key);
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

/*
        /// <summary>
        /// Get recently sold auctions
        /// </summary>
        /// <returns></returns>
        [Route("auctions/sold/recent")]
        [HttpGet]
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
        public async Task<List<AuctionPreview>> SoldRecent(string itemTag, [FromQuery] IDictionary<string, string> query)
        {
            var itemId = ItemDetails.Instance.GetItemIdForTag(itemTag);
            var filter = new Dictionary<string, string>(query);
            int page = 0;
            if (filter.ContainsKey("page"))
            {
                int.TryParse(filter["page"], out page);
                filter.Remove("page");
            }
            filter["ItemId"] = itemId.ToString();
            var pageSize = 20;
            var select = fe.AddFilters(context.Auctions
                        .Where(a => a.ItemId == itemId && a.End > DateTime.Now && a.HighestBidAmount == 0 && a.Bin), filter)
                        .Include(a => a.Enchantments)
                        .Include(a => a.NbtData)
                        .OrderBy(a => a.StartingBid)
                        .Skip(page * pageSize)
                        .Take(pageSize);

            var result = select
                        .OrderByDescending(a => a.End).Take(pageSize).Select(a => new
                        {
                            a.End,
                            Price = a.HighestBidAmount,
                            a.AuctioneerId,
                            a.Uuid
                        }).ToList();
            return result.Select(async a => new AuctionPreview()
            {
                End = a.End,
                Price = a.Price,
                Seller = a.AuctioneerId,
                Uuid = a.Uuid,
                PlayerName = await PlayerSearch.Instance.GetNameWithCacheAsync(a.AuctioneerId)
            }).Select(a => a.Result).ToList();
        }
*/
        private static RestRequest CreateRequestTo(string path)
        {
            var lbinReq = new RestRequest(path);
            lbinReq.Timeout = 3000;
            return lbinReq;
        }
    }
}

