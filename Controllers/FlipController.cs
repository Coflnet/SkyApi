using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using RestSharp;
using Coflnet.Leaderboard.Client.Api;
using Coflnet.Leaderboard.Client.Model;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Controller
{
    /// <summary>
    /// Endpoints for flips
    /// </summary>
    [ApiController]
    [Route("api/flip")]
    [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class FlipController : ControllerBase
    {
        private IConfiguration config;
        private TfmService tfm;
        private FlipTrackingService flipService;
        private IScoresApi scoresApi;
        private ILogger<FlipController> logger;

        /// <summary>
        /// Creates a new instance of <see cref="FlipController"/>
        /// </summary>
        /// <param name="config"></param>
        /// <param name="tfm"></param>
        /// <param name="flipService"></param>
        /// <param name="scoresApi"></param>
        /// <param name="logger"></param>
        public FlipController(IConfiguration config, TfmService tfm, FlipTrackingService flipService, IScoresApi scoresApi, ILogger<FlipController> logger)
        {
            this.config = config;
            this.tfm = tfm;
            this.flipService = flipService;
            this.scoresApi = scoresApi;
            this.logger = logger;
        }

        /// <summary>
        /// The last time an update was loaded (cached for 30min)
        /// You should only look at the second part
        /// </summary>
        /// <returns></returns>
        [Route("update/when")]
        [HttpGet]
        public async Task<DateTime> GetFlipTime()
        {
            return await new NextUpdateRetriever().Get();
        }

        /// <summary>
        /// Shows you the available settings options for the socket comand subFlip,
        /// Doesn't currently actually do anything.
        /// </summary>
        /// <returns>The default settings for modsocket v1</returns>
        [Route("settings/options")]
        [HttpGet]
        public FlipSettings SeeOptions()
        {
            return null;//Sky.Commands.MC.MinecraftSocket.DEFAULT_SETTINGS;
        }


        /// <summary>
        /// Callback for external flip finders to be included in tracking
        /// </summary>
        /// <param name="auctionId">Id of found and purchased auction</param>
        /// <param name="finder">Identifier of finder</param>
        /// <param name="playerId">The uuid of the player</param>
        /// <param name="price">Sugested target price</param>
        /// <param name="timeStamp">Unix millisecond timestamp when the flip was found</param>
        /// <returns></returns>
        [Route("track/purchase/{auctionId}")]
        [HttpPost]
        public async Task TrackExternalFlip(string auctionId, string finder, string playerId, int price = -1, long timeStamp = 0)
        {
            var received = GetTime(timeStamp);

            var finderType = finder.ToLower() switch
            {
                "tfm" => LowPricedAuction.FinderType.TFM,
                "stonks" => LowPricedAuction.FinderType.STONKS,
                "binmaster" => LowPricedAuction.FinderType.BINMASTER,
                _ => LowPricedAuction.FinderType.EXTERNAL,
            };

            if (finderType == LowPricedAuction.FinderType.TFM && !await tfm.IsUserOnAsync(playerId))
            {
                await Task.Delay(new Random().Next(100, 500)); // avoid timing attacks and silently complete
                return;
            }

            await flipService.NewFlip(new LowPricedAuction()
            {
                Auction = new SaveAuction() { Uuid = auctionId },
                Finder = finderType,
                TargetPrice = price
            }, received);
            await flipService.ReceiveFlip(auctionId, playerId, received);
            await flipService.ClickFlip(auctionId, playerId);
        }

        /// <summary>
        /// Callback for external flip finders to be included in tracking. 
        /// </summary>
        /// <param name="finder">Identifier of finder</param>
        /// <param name="auctionId">The id of the found auction</param>
        /// <param name="price">Suggested target price</param>
        /// <param name="timeStamp">Unix millisecond timestamp when the flip was found</param>
        /// <returns></returns>
        [Route("track/found/{auctionId}")]
        [HttpPost]
        public async Task TrackExternalFlip(string auctionId, string finder, int price = -1, long timeStamp = 0)
        {
            DateTime time = GetTime(timeStamp);
            await flipService.NewFlip(new LowPricedAuction()
            {
                Auction = new SaveAuction() { Uuid = auctionId },
                Finder = finder.ToLower() == "tfm" ? LowPricedAuction.FinderType.TFM : LowPricedAuction.FinderType.EXTERNAL,
                TargetPrice = price
            }, time);
            if (finder.ToLower() == "tfm")
                Console.WriteLine($"TFM found {auctionId} at {timeStamp}");
        }

        private static DateTime GetTime(long timeStamp)
        {
            DateTime time = DateTime.UtcNow;
            if (timeStamp != 0)
            {
                time = (new DateTime(1970, 1, 1)).AddMilliseconds(timeStamp);
                if (time > DateTime.UtcNow)
                    throw new CoflnetException("invalid_time", "Flips can't be found in the future");
                if (time < DateTime.UtcNow - TimeSpan.FromSeconds(10))
                    throw new CoflnetException("invalid_time", "Provided timestamp is more than 10 seconds in the past. Make sure you are providing timestamp as miliseconds");
            }
            return time;
        }


        /// <summary>
        /// Get flips stats for player
        /// </summary>
        /// <param name="playerUuid">Uuid of player to get stats for</param>
        /// <param name="days"></param>
        /// <returns></returns>
        [Route("stats/player/{playerUuid}")]
        [HttpGet]
        [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "days" })]
        public async Task<FlipSumary> GetStats(string playerUuid, float days = 7)
        {
            if (days > 7)
                throw new CoflnetException("invalid_time", "Sorry but this is currently limited to one week");
            if (days < 0)
                throw new CoflnetException("invalid_time", "You can't request flips in the future");
            var result = await flipService.GetPlayerFlips(playerUuid, TimeSpan.FromDays(days));
            _ = Task.Run(async () =>
            {
                try
                {
                    // postfix week
                    var boardSlug = $"sky-flippers-{DateTime.UtcNow.RoundDown(TimeSpan.FromDays(7)).ToString("yyyy-MM-dd")}";
                    await scoresApi.ScoresLeaderboardSlugPostAsync(boardSlug, new ScoreCreate(playerUuid, result.TotalProfit, 100));
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to post flip score");
                }
            });
            return result;
        }

        /// <summary>
        /// Get flips stats for player for the last hour (faster)
        /// </summary>
        /// <param name="playerUuid">Uuid of player</param>
        /// <returns>List of lips</returns>
        [Route("stats/player/{playerUuid}/hour")]
        [HttpGet]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<FlipSumary> GetHourStats(string playerUuid)
        {
            Console.WriteLine("called hourly flip profit");
            return await flipService.GetPlayerFlips(playerUuid, TimeSpan.FromHours(1));
        }


        /// <summary>
        /// Get flips stats for one type of flip finder
        /// </summary>
        /// <param name="finderName">Name of finder to get stats for, eg SNIPER,FLIPPER or STONKS</param>
        /// <param name="start">The start time of flips to get (inclusive)</param>
        /// <param name="end">The end time of flips to get (exclusive)</param>
        /// <returns></returns>
        [Route("stats/finder/{finderName}")]
        [HttpGet]
        [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "start", "end" })]
        public async Task<List<FlipDetails>> GetFlipsForFinder(string finderName, DateTime start = default, DateTime end = default)
        {
            if (end == default)
                end = DateTime.Now;
            if (start == default)
                start = end - TimeSpan.FromHours(1);
            return await flipService.GetFlipsForFinder(Enum.Parse<LowPricedAuction.FinderType>(finderName, true), start, end);
        }
    }
}

