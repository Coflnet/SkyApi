using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Api.Models;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Filter;
using Coflnet.Sky.PlayerName;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.PlayerState.Client.Api;

namespace Coflnet.Sky.Api.Controller
{
    /// <summary>
    /// The playerController handles all player related endpoints.
    /// </summary>
    [ApiController]
    [Route("api/player")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "apiKey" })]
    public class PlayerController : ControllerBase
    {
        const int pageSize = 10;
        HypixelContext context;
        private FilterEngine filterEngine;
        private PlayerNameService playerNameApi;
        private IPlayerStateApi playerStateApi;
        private ItemDetails itemDetails;

        /// <summary>
        /// Creates a new instance of <see cref="PlayerController"/>
        /// </summary>
        /// <param name="context"></param>
        /// <param name="filterEngine"></param>
        /// <param name="playerNameApi"></param>
        /// <param name="itemDetails"></param>
        /// <param name="playerStateApi"></param>
        public PlayerController(HypixelContext context, FilterEngine filterEngine, PlayerNameService playerNameApi, ItemDetails itemDetails, IPlayerStateApi playerStateApi)
        {
            this.context = context;
            this.filterEngine = filterEngine;
            this.playerNameApi = playerNameApi;
            this.itemDetails = itemDetails;
            this.playerStateApi = playerStateApi;
        }


        /// <summary>
        /// The last 10 bids (with auction) a player did
        /// </summary>
        /// <param name="playerUuid">The uuid of the player</param>
        /// <param name="page">Page of auctions (another 10)</param>
        /// <param name="filters"></param>
        /// <returns></returns>
        [Route("{playerUuid}/bids")]
        [HttpGet]
        [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
        public async Task<List<BidResult>> GetPlayerBids(string playerUuid, int page = 0, [FromQuery] Dictionary<string, string> filters = null)
        {
            AssertUuid(playerUuid);
            var offset = pageSize * page;
            var baseSelect = context.Bids.Where(b => b.BidderId == context.Players.Where(p => p.UuId == playerUuid).Select(p => p.Id).FirstOrDefault() && b.BidderId != 0);
            filters.Remove("page");
            if (filters.TryGetValue("tag", out string itemTag))
            {
                int itemId = ExtractItemIdFromFilter(filters, itemTag);
                baseSelect = baseSelect.Where(b => b.Auction.ItemId == itemId);
            }
            if (filters != null && filters.Count > 0)
            {
                var expression = filterEngine.GetMatchExpression(filters, true);
                baseSelect = baseSelect.Select(b => b.Auction).Where(expression).Select(a => a.Bids.FirstOrDefault());
            }
            //.Include (p => p.Auction)
            var playerBids = await baseSelect.OrderByDescending(auction => auction.Id).Skip(offset)
                        .Take(pageSize).Select(b => new
                        {
                            b.Auction.Uuid,
                            b.Auction.ItemName,
                            b.Auction.Tag,
                            b.Auction.HighestBidAmount,
                            b.Auction.End,
                            b.Amount,
                            b.Auction.StartingBid,
                            b.Auction.Bin

                        }).GroupBy(b => b.Uuid)
                    .Select(bid => new
                    {
                        bid.Key,
                        Amount = bid.Max(b => b.Amount),
                        HighestBid = bid.Max(b => b.HighestBidAmount),
                        ItemName = bid.Max(b => b.ItemName),
                        Tag = bid.Max(b => b.Tag),
                        HighestOwnBid = bid.Max(b => b.Amount),
                        End = bid.Max(b => b.End),
                        StartBid = bid.Max(b => b.StartingBid),
                        Bin = bid.Max(b => b.Bin)
                    })
                    .ToListAsync();

            var aggregatedBids = playerBids
                        .Select(b => new BidResult()
                        {
                            HighestBid = b.HighestBid == 0 && b.Bin ? b.HighestOwnBid : b.HighestBid,
                            AuctionId = b.Key,
                            End = b.End,
                            HighestOwnBid = b.HighestOwnBid,
                            ItemName = b.ItemName,
                            Tag = b.Tag,
                            StartingBid = b.StartBid,
                            Bin = b.Bin
                        })
                        .OrderByDescending(b => b.End)
                        .ToList();
            return aggregatedBids;
        }

        /// <summary>
        /// The last 10 auctions a player created
        /// </summary>
        /// <param name="playerUuid">The uuid of the player</param>
        /// <param name="page">Page of auctions (another 10)</param>
        /// <param name="filters"></param>
        /// <returns></returns>
        [Route("{playerUuid}/auctions")]
        [HttpGet]
        [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
        public async Task<List<AuctionResult>> GetPlayerAuctions(string playerUuid, int page = 0, [FromQuery] Dictionary<string, string> filters = null)
        {
            AssertUuid(playerUuid);
            var offset = pageSize * page;
            filters?.Remove("page");
            var baseSelect = context.Auctions
                        .Where(a => a.SellerId == context.Players.Where(p => p.UuId == playerUuid).Select(p => p.Id).FirstOrDefault() && a.SellerId != 0);
            if (filters?.TryGetValue("tag", out string itemTag) ?? false)
            {
                int itemId = ExtractItemIdFromFilter(filters, itemTag);
                baseSelect = baseSelect.Where(a => a.ItemId == itemId);
            }
            if (filters != null && filters.Count > 0)
            {
                var expression = filterEngine.GetMatchExpression(filters, true);
                baseSelect = baseSelect.Where(expression);
            }
            if (page < 30)
            {
                var auctions = await baseSelect.OrderByDescending(a => a.Id).Take(300).ToListAsync();
                return auctions.OrderByDescending(a => a.End)
                    .Skip(offset)
                    .Take(pageSize)
                    .Select(a => new AuctionResult(a))
                    .ToList();
            }
            var batch = await baseSelect
                        .OrderByDescending(a => a.Id)
                        .Skip(offset)
                        .Take(pageSize)
                        .ToListAsync();

            return batch.OrderByDescending(a => a.End)
                    .Select(a => new AuctionResult(a))
                    .ToList();
        }

        private int ExtractItemIdFromFilter(Dictionary<string, string> filters, string itemTag)
        {
            var itemId = itemDetails.GetItemIdForTag(itemTag);
            filters["ItemId"] = itemId.ToString();
            filters.Remove("tag");
            return itemId;
        }

        /// <summary>
        /// The name for a given uuid
        /// </summary>
        /// <param name="playerUuid">The uuid of the player</param>
        /// <returns></returns>
        [Route("{playerUuid}/name")]
        [HttpGet]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<string> GetPlayerName(string playerUuid)
        {
            AssertUuid(playerUuid);
            try
            {
                return await playerNameApi.GetName(playerUuid) ?? "unknown!";
            }
            catch (Exception e)
            {
                Console.WriteLine("Error fetching player name for uuid " + playerUuid + ": " + e);
                
                return "unknown!";
            }
        }

        /// <summary>
        /// Batch name retrieval
        /// </summary>
        /// <param name="playerUuids"></param>
        /// <returns></returns>
        /// <exception cref="CoflnetException"></exception>
        [Route("names")]
        [HttpPost]
        public async Task<Dictionary<string, string>> GetPlayerNames([FromBody] List<string> playerUuids)
        {
            if (playerUuids.Count > 100)
                throw new CoflnetException("too_many_uuids", "You can only request the names for 100 uuids at once. Please make two requests.");
            return await playerNameApi.GetNames(playerUuids);
        }


        /// <summary>
        /// The name for a given uuid
        /// </summary>
        /// <param name="playerUuid">The uuid of the player</param>
        /// <returns></returns>
        [Route("{playerUuid}/name")]
        [HttpPost]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<string> UpdateName(string playerUuid)
        {
            AssertUuid(playerUuid);
            await IndexerClient.TriggerNameUpdate(playerUuid);
            return "ok";
        }

        private static void AssertUuid(string playerUuid)
        {
            if (playerUuid.Length != 32)
                throw new CoflnetException("invalid_uuid", "The provided string does not seem to be a valid minecraft account uuid.");
        }


        /// <summary>
        /// Returns the last know bazaar orders of a player
        /// </summary>
        /// <returns></returns>
        [Route("bazaar/orders")]
        [HttpGet]
        public async Task<List<PlayerState.Client.Model.Offer>> GetPlayerOrders([FromServices] ApiKeyService keyService, string apiKey = null)
        {
            var keyInfo = await keyService.GetKeyInfo(this);
            return await playerStateApi.PlayerStatePlayerIdBazaarGetAsync(keyInfo.MinecraftName);
        }

        /// <summary>
        /// Bazaar flips a player made while using our mod
        /// </summary>
        /// <param name="keyService"></param>
        /// <param name="profitApi"></param>
        /// <param name="apiKey">Key generated from /cofl api in game</param>
        /// <returns></returns>
        [Route("bazaar/flips")]
        [HttpGet]
        public async Task<List<PlayerState.Client.Model.CompletedBazaarFlip>> GetPlayerBazaarFlips([FromServices] ApiKeyService keyService,[FromServices] IBazaarProfitApi profitApi, string apiKey = null)
        {
            var keyInfo = await keyService.GetKeyInfo(this);
            return await profitApi.BazaarProfitFlipsPlayerUuidGetAsync(Guid.Parse(keyInfo.MinecraftUuid));
        }
        /// <summary>
        /// Gives access to extracted player information like hotf, booster cookie expiry time, attribute levels etc.
        /// </summary>
        /// <returns></returns>
        [Route("extracted")]
        [HttpGet]
        public async Task<PlayerState.Client.Model.ExtractedInfo> GetPlayerSate([FromServices] ApiKeyService keyService, string apiKey = null)
        {
            var keyInfo = await keyService.GetKeyInfo(this);
            return (await playerStateApi.PlayerStatePlayerIdGetAsync(keyInfo.MinecraftUuid))?.ExtractedInfo;
        }
    }
}

