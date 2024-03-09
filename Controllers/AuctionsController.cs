

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
using static Coflnet.Sky.Core.ItemPrices;
using Coflnet.Sky.PlayerName;
using Microsoft.AspNetCore.Http;
using Coflnet.Sky.Api.Services;
using System.Security.Cryptography;
using System.Text;
using Coflnet.Sky.Items.Client.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.Api.Controller
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
        PlayerNameService playerNameService;
        IServiceScopeFactory factory;
        IItemsApi itemsClient;
        FilterEngine fe;
        AuctionConverter transformer;
        ModDescriptionService modDescriptionService;

        /// <summary>
        /// Creates a new instance of <see cref="AuctionsController"/>
        /// </summary>
        /// <param name="auctionService"></param>
        /// <param name="context"></param>
        /// <param name="logger"></param>
        /// <param name="config"></param>
        /// <param name="pricesService"></param>
        /// <param name="playerNameService"></param>
        /// <param name="factory"></param>
        /// <param name="itemsClient"></param>
        /// <param name="fe"></param>
        /// <param name="transformer"></param>
        /// <param name="modDescriptionService"></param>
        public AuctionsController(AuctionService auctionService,
                                  HypixelContext context,
                                  ILogger<AuctionsController> logger,
                                  IConfiguration config,
                                  PricesService pricesService,
                                  PlayerNameService playerNameService,
                                  IServiceScopeFactory factory,
                                  IItemsApi itemsClient,
                                  FilterEngine fe,
                                  AuctionConverter transformer,
                                  ModDescriptionService modDescriptionService)
        {
            this.auctionService = auctionService;
            this.context = context;
            this.logger = logger;
            this.config = config;
            this.pricesService = pricesService;
            this.playerNameService = playerNameService;
            this.factory = factory;
            this.itemsClient = itemsClient;
            this.fe = fe;
            this.transformer = transformer;
            this.modDescriptionService = modDescriptionService;
        }

        /// <summary>
        /// Retrieve details of a specific auction
        /// </summary>
        /// <param name="auctionUuid">The uuid of the auction you want the details for</param>
        /// <returns></returns>
        [Route("auction/{auctionUuid}")]
        [HttpGet]
        //[ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
        [CacheControl(120)]
        public async Task<EnchantColorMapper.ColorSaveAuction> getAuctionDetails(string auctionUuid)
        {
            var uid = auctionService.GetId(auctionUuid);
            var result = await context.Auctions.Where(a => a.UId == uid)
                        .Include(a => a.Enchantments)
                        .Include(a => a.NbtData)
                        .Include(a => a.Bids).FirstOrDefaultAsync();
            if (result != null && string.IsNullOrEmpty(result.ItemName))
                result.ItemName = ItemDetails.TagToName(result.Tag);
            // order enchants
            var prices = modDescriptionService.GetEnchantBreakdown(result, modDescriptionService.DeserializedCache.BazaarItems)
                .ToDictionary(e => e.e.Type, e => e.Item2);
            var colored = EnchantColorMapper.Instance.AddColors(result);
            foreach (var item in colored.Enchantments)
            {
                item.Value = prices.GetValueOrDefault(item.Type, 0);
            }
            colored.Enchantments = colored.Enchantments.OrderByDescending(e => e.Value).ToList();
            return colored;
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
            if (auctionUuid.Length < 30)
                return auctionService.GetUuid(long.Parse(auctionUuid));
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
        /// <param name="token">Partner token to get more data</param>
        /// <returns></returns>
        [Route("auctions/tag/{itemTag}/sold")]
        [HttpGet]
        [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "page", "pageSize", "token" })]
        public async Task<List<SaveAuction>> GetHistory(string itemTag, int page = 0, int pageSize = 1000, string token = null)
        {
            var itemId = ItemDetails.Instance.GetItemIdForTag(itemTag);
            if (pageSize < 0 || pageSize > 1000)
                pageSize = 1000;
            var daysToReturn = config["MAX_SELL_LOOKBACK_ENDPOINT_DAYS"] ?? "7";
            if (!string.IsNullOrEmpty(token) && IsValidPartner(token))
                daysToReturn = itemTag == "HYPERION" ? "1200" : "360";
            var startTime = DateTime.Now.RoundDown(TimeSpan.FromHours(1)) - TimeSpan.FromDays(int.Parse(daysToReturn));
            var result = await context.Auctions
                        .Where(a => a.ItemId == itemId && a.End > startTime && a.End < DateTime.Now && a.HighestBidAmount > 0)
                        .Include(a => a.Enchantments)
                        .Include(a => a.NbtData)
                        .OrderByDescending(a => a.End)
                        .Skip(page * pageSize)
                        .Take(pageSize).ToListAsync();

            return result;
        }

        /// <summary>
        /// Batch raw item value export, requires token
        /// </summary>
        /// <param name="page">Page of auctions to get</param>
        /// <param name="token">Secret token to access data</param>
        /// <returns></returns>
        [Route("auctions/batch")]
        [HttpGet]
        [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "page" })]
        public async Task GetHistory(string page = "last", string token = "")
        {
            var pageSize = 50_000;
            var baseStart = 400_000_000;
            var itemsRequest = itemsClient.ItemItemTagModifiersAllGetAsync("*");
            AssertAccessToken(token);
            var totalAuctions = await context.Auctions.MaxAsync(a => a.Id);
            if (totalAuctions < 100_000_000)
                baseStart /= 10;
            var lastPage = (totalAuctions - baseStart) / pageSize;
            Response.Headers.Add("X-Page-Count", lastPage.ToString());
            Response.Headers.Add("X-Total-Count", totalAuctions.ToString());
            Response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await transformer.InitMayors();
            var itemModifiers = await itemsRequest;
            var columns = itemModifiers.Keys;
            var keys = transformer.ColumnKeys(columns).ToArray();
            if (!int.TryParse(page, out int pageNum))
            {
                //await HttpResponseWritingExtensions.WriteAsync(this.Response, transformer.GetHeader(columns));
                if (ItemDetails.Instance.TagLookup.Count == 0)
                    await ItemDetails.Instance.LoadLookup();
                var itemids = ItemDetails.Instance.TagLookup.Keys.ToArray();
                logger.LogInformation("Exporting " + itemids.Length + " items");
                itemModifiers["item_id"] = itemids.ToList();
                itemModifiers["headers"] = transformer.ColumnKeys(columns).ToList();
                foreach (var item in AuctionConverter.ignoreColumns.Concat(itemModifiers.Keys.Where(k => k.EndsWith(".uuid")).ToList()))
                {
                    itemModifiers.Remove(item);
                }
                await HttpResponseWritingExtensions.WriteAsync(this.Response, JsonConvert.SerializeObject(itemModifiers));
                return;
            }
            foreach (var item in context.Auctions
                        .Where(a => a.Id >= baseStart + pageSize * pageNum && a.Id < baseStart + pageSize * (pageNum + 1) && a.HighestBidAmount > 0)
                        .Include(a => a.Enchantments)
                        .Include(a => a.NbtData))
            {

                await HttpResponseWritingExtensions.WriteAsync(this.Response, transformer.Transform(item, keys));
            }
        }

        private void AssertAccessToken(string token)
        {
            bool isPartner = IsValidPartner(token);
            if (!isPartner)
                throw new CoflnetException("invalid_token", "the passed token is not whitelisted");
        }

        private bool IsValidPartner(string token)
        {
            var isPartner = false;
            var tokens = config.GetSection("PartnerTokenHashes").Get<string[]>();
            using (var mySHA256 = SHA256.Create())
            {
                var hash = mySHA256.ComputeHash(Encoding.UTF8.GetBytes(token));
                isPartner = tokens.Contains(BitConverter.ToString(hash).Replace("-", "").ToUpper());
            }

            return isPartner;
        }

        /// <summary>
        /// Gets a preview of recent auctions useful in overviews
        /// </summary>
        /// <param name="itemTag">The itemTag to get auctions for</param>
        /// <param name="query"></param>
        /// <returns></returns>
        [Route("auctions/tag/{itemTag}/recent/overview")]
        [HttpGet]
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
        public async Task<List<AuctionPreview>> GetRecent(string itemTag, [FromQuery] IDictionary<string, string> query)
        {
            List<AuctionPreview> preview = await GetRecentFor(itemTag, query, 1);
            if (preview.Count >= 12)
                return preview;
            return await GetRecentFor(itemTag, query, 14);
        }

        private async Task<List<AuctionPreview>> GetRecentFor(string itemTag, IDictionary<string, string> query, int days)
        {
            var minTime = DateTime.Now.Subtract(TimeSpan.FromDays(days));
            var itemId = ItemDetails.Instance.GetItemIdForTag(itemTag);
            var baseSelect = context.Auctions
                                        .Where(a => a.ItemId == itemId && a.End < DateTime.Now && a.End > minTime).OrderByDescending(a => a.End);
            var preview = await ToPreview(query, itemId, baseSelect);
            return preview;
        }

        /// <summary>
        /// Gets a preview of active auctions useful in overviews, available orderBy options 
        /// HIGHEST_PRICE, LOWEST_PRICE (default), ENDING_SOON
        /// </summary>
        /// <param name="itemTag">The itemTag to get auctions for</param>
        /// <param name="query"></param>
        /// <returns></returns>
        [Route("auctions/tag/{itemTag}/active/overview")]
        [HttpGet]
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
        public async Task<List<AuctionPreview>> GetActive(string itemTag, [FromQuery] IDictionary<string, string> query)
        {
            var filter = new Dictionary<string, string>(query, StringComparer.OrdinalIgnoreCase);
            var itemId = ItemDetails.Instance.GetItemIdForTag(itemTag);
            var order = ActiveItemSearchQuery.SortOrder.LOWEST_PRICE;
            if (filter.ContainsKey("orderBy"))
            {
                Enum.TryParse<ActiveItemSearchQuery.SortOrder>(filter["orderBy"], out order);
                filter.Remove("orderBy");
            }
            var baseSelect = context.Auctions
                                        .Where(a => a.ItemId == itemId && a.End > DateTime.Now);//.OrderByDescending(a => a.End);
            var orderedSelect = order switch
            {
                ActiveItemSearchQuery.SortOrder.ENDING_SOON => baseSelect.OrderBy(b => b.End),
                ActiveItemSearchQuery.SortOrder.HIGHEST_PRICE => baseSelect.OrderByDescending(a => a.HighestBidAmount == 0 ? a.StartingBid : a.HighestBidAmount),
                _ => baseSelect.OrderBy(a => a.HighestBidAmount == 0 ? a.StartingBid : a.HighestBidAmount)
            };
            return await ToPreview(filter, itemId, orderedSelect);
        }

        private async Task<List<AuctionPreview>> ToPreview(IDictionary<string, string> query, int itemId, IOrderedQueryable<SaveAuction> baseSelect)
        {
            var filter = new Dictionary<string, string>(query);
            int page = 0;
            if (filter.ContainsKey("page"))
            {
                int.TryParse(filter["page"], out page);
                filter.Remove("page");
                if (page > 10)
                    throw new CoflnetException("max_page_exceeded", "Sorry you are only allowed to query 10 pages");
            }
            filter["ItemId"] = itemId.ToString();
            var pageSize = 12;
            var select = fe.AddFilters(baseSelect, filter)
                        .Skip(page * pageSize)
                        .Take(pageSize);

            var result = await select.ToListAsync();
            var names = await playerNameService.GetNames(result.Select(a => a.AuctioneerId).ToList());
            if (names == null)
                names = new Dictionary<string, string>();
            return result.Select(a => new AuctionPreview()
            {
                End = a.End,
                Price = a.HighestBidAmount == 0 ? a.StartingBid : a.HighestBidAmount,
                Seller = a.AuctioneerId,
                Uuid = a.Uuid,
                PlayerName = names.GetValueOrDefault(a.AuctioneerId)
            }).ToList();
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
            var limit = 120;
            if (request.Uuids.Length > limit)
                throw new CoflnetException("to_many_uuid", $"Please do batch lookups on no more than {limit} uuids at a time");
            var numericIds = request.Uuids.GroupBy(id => id).Select(ids => ids.First()).ToDictionary(uid => GetUidFromString(uid));
            var key = NBT.Instance.GetKeyId("uid");
            var maxEnd = DateTime.UtcNow + TimeSpan.FromMinutes(2);
            var result = await context.Auctions
                        .Where(a => a.NBTLookup.Where(l => l.KeyId == key && numericIds.Keys.Contains(l.Value)).Any() && a.End < maxEnd)
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
                using var scope = factory.CreateScope();
                var tempService = scope.ServiceProvider.GetRequiredService<Client.Api.IPricesApi>();
                try
                {
                    var data = await tempService.ApiItemPriceItemTagGetAsync(item.Key);
                    if (data.Median < 1_000_000 && data.Volume > 0)
                        return;

                    var lowestBinTask = client.ExecuteAsync(CreateRequestTo($"/api/item/price/{item.Key}/bin"));
                    var lbinData = JsonConvert.DeserializeObject<BinResponse>((await lowestBinTask).Content);
                    result.Add(new SupplyElement()
                    {
                        Supply = item.Value,
                        Tag = item.Key,
                        Median = data.Median,
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

