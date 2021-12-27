using System.Collections.Generic;
using System.Threading.Tasks;
using hypixel;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Api.Models;

namespace Coflnet.Hypixel.Controller
{
    [ApiController]
    [Route("api/player")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class PlayerController : ControllerBase
    {
        const int pageSize = 10;
        /// <summary>
        /// The last 10 auctions a player bid on
        /// </summary>
        /// <param name="playerUuid">The uuid of the player</param>
        /// <param name="page">Page of auctions (another 10)</param>
        /// <returns></returns>
        [Route("{playerUuid}/bids")]
        [HttpGet]
        public async Task<List<BidResult>> GetPlayerBids(string playerUuid, int page = 0)
        {
            AssertUuid(playerUuid);
            var result = await CoreServer.ExecuteCommandWithCache<PaginatedRequest, List<BidResult>>(
                "playerBids", new PaginatedRequest()
                {
                    Amount = pageSize,
                    Offset = pageSize * page,
                    Uuid = playerUuid
                });
            return result;
        }

        /// <summary>
        /// The last 10 auctions a player created
        /// </summary>
        /// <param name="playerUuid">The uuid of the player</param>
        /// <param name="page">Page of auctions (another 10)</param>
        /// <returns></returns>
        [Route("{playerUuid}/auctions")]
        [HttpGet]
        public async Task<List<AuctionResult>> GetPlayerAuctions(string playerUuid, int page = 0)
        {
            AssertUuid(playerUuid);
            var result = await CoreServer.ExecuteCommandWithCache<PaginatedRequest, List<AuctionResult>>(
                "playerAuctions", new PaginatedRequest()
                {
                    Amount = pageSize,
                    Offset = pageSize * page,
                    Uuid = playerUuid
                });
            return result;
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
            return (await PlayerService.Instance.GetPlayer(playerUuid)).Name;
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
    }
}

