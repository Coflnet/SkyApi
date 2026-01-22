

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
using Coflnet.Sky.Auctions.Client.Api;
using AutoMapper;
using System.Diagnostics;
using static Coflnet.Sky.Filter.Controllers.FilterController;

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
        IAuctionApi auctionApi;
        Auctions.Client.Api.IExportApi exportApi;
        ItemDetails itemDetails;
        IMapper mapper;
        private PremiumTierService premiumTierService;

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
        /// <param name="auctionApi"></param>
        /// <param name="premiumTierService"></param>
        /// <param name="exportApi"></param>
        /// <param name="mapper"></param>
        /// <param name="itemDetails"></param>
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
                                  ModDescriptionService modDescriptionService,
                                  IAuctionApi auctionApi,
                                  PremiumTierService premiumTierService,
                                  Auctions.Client.Api.IExportApi exportApi,
                                  IMapper mapper,
                                  ItemDetails itemDetails)
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
            this.auctionApi = auctionApi;
            this.premiumTierService = premiumTierService;
            this.exportApi = exportApi;
            this.mapper = mapper;
            this.itemDetails = itemDetails;
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
            if (result == null)
            {
                // check archive
                var archived = await auctionApi.ApiAuctionUuidGetWithHttpInfoAsync(auctionUuid);
                if (archived.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    result = JsonConvert.DeserializeObject<ApiSaveAuction>(archived.RawContent);
                    logger.LogInformation($"Got auction {auctionUuid} from archive");
                }
            }
            if (result != null && string.IsNullOrEmpty(result.ItemName))
                result.ItemName = ItemDetails.TagToName(result.Tag);
            // order enchants
            var prices = modDescriptionService.GetEnchantBreakdown(result, modDescriptionService.DeserializedCache.BazaarItems)
                .GroupBy(e => e.e.Type).Select(g => g.First()).ToDictionary(e => e.e.Type, e => e.Item2);
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
        /// Get a random auction from the database, built for Thomases guessing game
        /// </summary>
        [Route("auction/random")]
        [HttpGet]
        [ResponseCache(Duration = 3, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "token" })]
        public async Task<SaveAuction> GetRandomAuction(string token)
        {
            if (GetTokenHash(token) != "A1D897D3B8DDE4E7119F9624C6A3150E198F0A27279A505B17A1A2DF0C2D7403")
                throw new CoflnetException("not_allowed", "You are not allowed to access this endpoint, please contact us on discord if you want to use it");

            // Get a random uid between -minValue and 80% of the +maxValue
            var randomId = new Random().NextInt64(long.MinValue, (long.MaxValue / 5) * 4);
            return await context.Auctions.Where(a => a.UId >= randomId && a.HighestBidAmount > 0)
                .Take(1)
                .Include(a => a.Enchantments)
                .Include(a => a.NbtData)
                .Include(a => a.Bids)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Get the 10 (or how many are available) lowest bins
        /// </summary>
        /// <param name="itemTag">The itemTag to get bins for</param>
        /// <param name="query">Filters for the auctions</param>
        /// <returns></returns>
        [Route("auctions/tag/{itemTag}/active/bin")]
        [HttpGet]
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = ["*"])]
        public async Task<List<SaveAuction>> GetLowestBins(string itemTag, [FromQuery] IDictionary<string, string> query)
        {
            var itemId = itemDetails.GetItemIdForTag(itemTag);
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
        [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = ["page", "pageSize", "token"])]
        public async Task<List<SoldAuction>> GetHistory(string itemTag, int page = 0, int pageSize = 1000, string token = null)
        {
            var itemId = itemDetails.GetItemIdForTag(itemTag);
            var max = 1000;
            var isPartner = IsValidPartner(token);
            if (isPartner)
                max = 10000;
            if (pageSize < 0 || pageSize > max)
                pageSize = max;
            var daysToReturn = config["MAX_SELL_LOOKBACK_ENDPOINT_DAYS"] ?? "7";
            var isSherly = GetTokenHash(token) == "9364BF7E16C578C95E0991A2618225D5B270943684EE0337B2BDEF2EC7A201E5";
            if (isSherly)
            {
                var start = DateTime.Now.RoundDown(TimeSpan.FromDays(1)) - TimeSpan.FromDays(29);
                var idOf30DaysAgo = await context.Auctions
                        .Where(a => a.ItemId == itemId && a.End > start && a.End < DateTime.Now)
                        .OrderBy(a => a.End)
                        .Select(a => a.Id)
                        .FirstOrDefaultAsync();
                var auctions = await context.Auctions
                        .Where(a => a.Id >= idOf30DaysAgo)
                        .Include(a => a.Enchantments)
                        .Include(a => a.NbtData)
                        .OrderBy(a => a.Id)
                        .Skip(page * pageSize)
                        .Take(pageSize)
                        .Where(a => a.End > start && a.End < DateTime.Now && a.HighestBidAmount != 0).ToListAsync();
                return auctions.Select(a => new SoldAuction
                {
                    Id = a.Id,
                    Uuid = a.Uuid,
                    Tag = a.Tag,
                    ItemName = a.ItemName,
                    AuctioneerId = a.AuctioneerId,
                    StartingBid = a.StartingBid,
                    HighestBidAmount = a.HighestBidAmount,
                    Start = a.Start,
                    End = a.End,
                    Bin = a.Bin,
                    Count = a.Count,
                    Tier = a.Tier,
                    Enchantments = a.Enchantments,
                    ShortItemBytes = a.NbtData?.data != null ? Convert.ToBase64String(a.NbtData.data) : null
                }).ToList();
            }
            if (!string.IsNullOrEmpty(token) && IsValidPartner(token))
                daysToReturn = "30";
            var startTime = DateTime.Now.RoundDown(TimeSpan.FromHours(1)) - TimeSpan.FromDays(int.Parse(daysToReturn));
            Console.WriteLine($"Getting history for {itemTag} from {startTime} to now, page {page}, pageSize {pageSize}, isPartner: {isPartner}");
            var bidNotEqualTo = 0;
            var result = await context.Auctions
                        .Where(a => a.ItemId == itemId && a.End > startTime && a.End < DateTime.Now && a.HighestBidAmount != bidNotEqualTo)
                        .Include(a => a.Enchantments)
                        .Include(a => a.NbtData)
                        .OrderByDescending(a => a.End)
                        .Skip(page * pageSize)
                        .Take(pageSize).ToListAsync();

            return result.Select(a => new SoldAuction
            {
                Id = a.Id,
                Uuid = a.Uuid,
                Tag = a.Tag,
                ItemName = a.ItemName,
                AuctioneerId = a.AuctioneerId,
                StartingBid = a.StartingBid,
                HighestBidAmount = a.HighestBidAmount,
                Start = a.Start,
                End = a.End,
                Bin = a.Bin,
                Count = a.Count,
                Tier = a.Tier,
                Enchantments = a.Enchantments,
                FlattenedNbt = a.FlatenedNBT
            }).ToList();
        }

        /// <summary>
        /// Batch raw item value export, requires token
        /// </summary>
        /// <param name="page">Page of auctions to get</param>
        /// <param name="tag"></param>
        /// <param name="token">Secret token to access data</param>
        /// <param name="count"></param>
        /// <returns></returns>
        [Route("auctions/batch")]
        [HttpGet]
        [ResponseCache(Duration = 1800, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = ["page", "tag", "count"])]
        public async Task GetHistory(string page = "last", string tag = "*", string token = "", int count = 50)
        {
            // unused by anyone
        }

        private bool IsValidPartner(string token)
        {
            var tokens = config.GetSection("PartnerTokenHashes").Get<string[]>();
            return tokens.Contains(GetTokenHash(token));
        }

        private static string GetTokenHash(string token)
        {
            if (token == null)
                return null;
            using var mySHA256 = SHA256.Create();
            var hash = mySHA256.ComputeHash(Encoding.UTF8.GetBytes(token));
            return BitConverter.ToString(hash).Replace("-", "").ToUpper();
        }

        /// <summary>
        /// Gets a preview of recent auctions useful in overviews
        /// </summary>
        /// <param name="itemTag">The itemTag to get auctions for</param>
        /// <param name="query"></param>
        /// <param name="popularityService"></param>
        /// <returns></returns>
        [Route("auctions/tag/{itemTag}/recent/overview")]
        [HttpGet]
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = ["*"])]
        public async Task<List<AuctionPreview>> GetRecent(string itemTag, [FromQuery] IDictionary<string, string> query, [FromServices] FilterPobularityService popularityService)
        {
            List<AuctionPreview> preview = await GetRecentFor(itemTag, query, 1);
            var referrer = Request.Headers["Referer"].ToString();
            if (referrer?.Contains("sky.coflnet.com") ?? false)
                foreach (var item in query)
                {
                    popularityService.AddFilterUse(itemTag, item.Key);
                }
            if (preview.Count >= 12)
                return preview;
            preview = await GetRecentFor(itemTag, query, 14);
            if (preview.Count >= 12 || query.Count > 2)
                return preview;
            preview = await GetRecentFor(itemTag, query, 90);
            return preview;
        }

        private async Task<List<AuctionPreview>> GetRecentFor(string itemTag, IDictionary<string, string> query, int days)
        {
            try
            {
                var minTime = DateTime.Now.Subtract(TimeSpan.FromDays(days));
                var itemId = itemDetails.GetItemIdForTag(itemTag);
                var baseSelect = context.Auctions
                            .Where(a => a.ItemId == itemId && a.End < DateTime.Now && a.End > minTime)
                            .OrderByDescending(a => a.End);
                var preview = await ToPreview(query, itemId, baseSelect);
                return preview;
            }
            catch (Exception e)
            {
                if (e.Message.Contains("max_statement_time exceeded"))
                    throw new CoflnetException("timeout", "The query took to long to execute, please try again with a smaller time frame");
                throw;
            }
        }

        /// <summary>
        /// Longer time overview of auctions
        /// </summary>
        /// <param name="itemTag"></param>
        /// <param name="query"></param>
        /// <returns></returns>
        [Route("auctions/tag/{itemTag}/archive/overview")]
        [HttpGet]
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = ["*"])]
        public async Task<ArchiveResponse> GetArchived(string itemTag, [FromQuery] IDictionary<string, string> query)
        {
            if (!await premiumTierService.HasPremiumPlus(this))
                throw new CoflnetException("premplus_required",
                           "Sorry but you need to be a premium plus member to access this data, Authorization header with google/account token");
            AssertArchiveQuery(query);
            await Task.Delay(2000); // soft ratelimit for db
            List<AuctionPreview> preview = await GetRecentFor(itemTag, query, 1500);
            return new ArchiveResponse()
            {
                Auctions = preview,
                queryStatus = ArchiveResponse.QueryStatus.Success // from the main db there are only full answers
            };
        }

        private static void AssertArchiveQuery(IDictionary<string, string> query)
        {
            if (!query.ContainsKey("EndAfter") || !query.ContainsKey("EndBefore"))
                throw new CoflnetException("missing_params", "Please provide EndAfter and EndBefore filters in query");
            if (!long.TryParse(query["EndAfter"], out var after) || !long.TryParse(query["EndBefore"], out var before))
                throw new CoflnetException("invalid_params", "Please provide valid dates in EndAfter and EndBefore filters in query (unix timestamp in seconds)");
            if (after > before)
                throw new CoflnetException("invalid_params", "EndAfter must be before EndBefore (lower unix timestamp)");
        }
        /// <summary>
        /// Request an export of auction data to discord webhook
        /// Export can contain contact details and inventory checks, access will be revoked if missuse is reported.
        /// </summary>
        /// <param name="itemTag"></param>
        /// <param name="request"></param>
        /// <returns></returns>
        /// <exception cref="CoflnetException"></exception>
        [Route("auctions/tag/{itemTag}/archive/export")]
        [HttpPost]
        public async Task<Auctions.Client.Model.ExportRequest> RequestExport(string itemTag, [FromBody] ExportRequestCreate request)
        {
            if (!await premiumTierService.HasPremiumPlus(this))
                throw new CoflnetException("premplus_required",
                           "Sorry but you need to be a premium plus member to access this data, Authorization header with google/account token");
            AssertArchiveQuery(request.Filters);
            var mapped = mapper.Map<Auctions.Client.Model.ExportRequest>(request);
            mapped.ItemTag = itemTag;
            var user = await premiumTierService.GetUserOrDefault(this);
            mapped.ByEmail = user.Email;
            return await exportApi.ExportPostAsync(mapped);
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
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = ["*"])]
        public async Task<List<AuctionPreview>> GetActive(string itemTag, [FromQuery] IDictionary<string, string> query)
        {
            var filter = new Dictionary<string, string>(query, StringComparer.OrdinalIgnoreCase);
            var itemId = itemDetails.GetItemIdForTag(itemTag);
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
                if (page > 10 && !await premiumTierService.HasPremiumPlus(this))
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
            var key = fe.NbtInstance.GetKeyId("uid");
            var result = await context.Auctions
                        .Where(a => a.NBTLookup.Where(l => l.KeyId == key && l.Value == numericId).Any())
                        .Where(a => a.HighestBidAmount > 0)
                        .Select(a => new
                        {
                            a.AuctioneerId,
                            a.Tag,
                            a.HighestBidAmount,
                            a.Uuid,
                            a.End,
                            Buyer = a.Bids.OrderByDescending(b => b.Amount).Select(b => b.Bidder).FirstOrDefault()
                        })
                        .AsSplitQuery()
                        .ToListAsync();

            return result.Select(i => new ItemSell()
            {
                Buyer = i.Buyer,
                Seller = i.AuctioneerId,
                Timestamp = i.End,
                Uuid = i.Uuid,
                ItemTag = i.Tag,
                HighestBid = i.HighestBidAmount
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
            var key = fe.NbtInstance.GetKeyId("uid");
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
                            uid = a.NBTLookup.Where(l => l.KeyId == key).Select(l => l.Value).FirstOrDefault(),
                            a.HighestBidAmount
                        })
                        .AsSplitQuery()
                        .ToListAsync();

            var sells = result.GroupBy(i => i.uid).ToDictionary(i => numericIds[i.Key], items => items.Select(i => new ItemSell()
            {
                Buyer = i.Buyer,
                Seller = i.AuctioneerId,
                Timestamp = i.End,
                Uuid = i.Uuid,
                HighestBid = i.HighestBidAmount
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
            var key = fe.NbtInstance.GetKeyId("uid");
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
                    var itemId = itemDetails.GetItemIdForTag(itemTag);
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
            var lbinReq = new RestRequest(path)
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
            return lbinReq;
        }
    }
}

