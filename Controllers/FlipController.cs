using System.Threading.Tasks;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Coflnet.Leaderboard.Client.Api;
using Coflnet.Leaderboard.Client.Model;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using System.Linq;
using Coflnet.Sky.Api.Models.Bazaar;
using Coflnet.Sky.Items.Client.Api;

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
        private ILogger<FlipController> logger;
        private PremiumTierService premiumTierService;
        private IBazaarFlipperApi bazaarFlipperApi;
        private IItemsApi itemsApi;

        /// <summary>
        /// Creates a new instance of <see cref="FlipController"/>
        /// </summary>
        /// <param name="config"></param>
        /// <param name="tfm"></param>
        /// <param name="flipService"></param>
        /// <param name="logger"></param>
        /// <param name="premiumTierService"></param>
        /// <param name="bazaarFlipperApi"></param>
        public FlipController(IConfiguration config,
                              TfmService tfm,
                              FlipTrackingService flipService,
                              ILogger<FlipController> logger,
                              PremiumTierService premiumTierService,
                              IBazaarFlipperApi bazaarFlipperApi,
                              IItemsApi itemsApi)
        {
            this.config = config;
            this.tfm = tfm;
            this.flipService = flipService;
            this.logger = logger;
            this.premiumTierService = premiumTierService;
            this.bazaarFlipperApi = bazaarFlipperApi;
            this.itemsApi = itemsApi;
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
        /// Spread based bazaar flips
        /// </summary>
        /// <returns></returns>
        [Route("bazaar/spread")]
        [HttpGet]
        [ResponseCache(Duration = 20, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<SpreadFlip>> GetBazaarFlipper()
        {
            var flips = await bazaarFlipperApi.FlipsGetAsync();
            var names = (await itemsApi.ItemNamesGetAsync()).ToDictionary(i => i.Tag, i => i.Name);
            return flips.Select(f =>
            {
                var profitmargin = f.BuyPrice / f.MedianBuyPrice;
                var isManipulated = profitmargin > 2 || profitmargin > 1.5 && f.BuyPrice > 7_500_000;
                return new SpreadFlip
                {
                    Flip = f,
                    ItemName = names[f.ItemTag],
                    IsManipulated = isManipulated
                };
            });
        }

        /// <summary>
        /// Get the current book flips on bazaar
        /// </summary>
        [Route("bazaar/books")]
        [HttpGet]
        [ResponseCache(Duration = 20, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<Bazaar.Flipper.Client.Model.BookFlip>> GetBazaarBookFlipper()
        {
            return await bazaarFlipperApi.BooksGetAsync();
        }

        /// <summary>
        /// Shows you the available settings options for the socket comand subFlip,
        /// Doesn't currently actually do anything.
        /// </summary>
        /// <returns>The default settings for modsocket v1</returns>
        [Route("settings/options")]
        [HttpGet]
        [Obsolete("This endpoint is deprecated. It doesn't do anything and is not used anywhere.")]
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
                "stonks" => LowPricedAuction.FinderType.LEIKO,
                "binmaster" => LowPricedAuction.FinderType.BINMASTER,
                _ => LowPricedAuction.FinderType.EXTERNAL,
            };

            if (finderType == LowPricedAuction.FinderType.TFM)
            {
                // from cloudflare header
                var clientIp = Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? Request.HttpContext.Connection.RemoteIpAddress.ToString();
                logger.LogInformation($"TFM found {auctionId} at {received} from {clientIp}");
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
            var finderParsed = finder.ToLower() == "tfm" ? LowPricedAuction.FinderType.TFM :
                finder.ToLower() == "leiko" ? LowPricedAuction.FinderType.LEIKO : LowPricedAuction.FinderType.EXTERNAL;
            await flipService.NewFlip(new LowPricedAuction()
            {
                Auction = new SaveAuction() { Uuid = auctionId },
                Finder = finderParsed,
                TargetPrice = price
            }, time);
            if (finder.Equals("leiko", StringComparison.CurrentCultureIgnoreCase))
                Console.WriteLine($"LeikOwO found {auctionId} at {timeStamp}");
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
        /// <param name="start"></param>
        /// <param name="end">The end time to retrieve</param>
        /// <returns></returns>
        [Route("stats/player/{playerUuid}")]
        [HttpGet]
        [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "start", "end" })]
        public async Task<FlipSumary> GetStats(string playerUuid, DateTime start, DateTime end)
        {
            if (end == default)
                end = DateTime.UtcNow;
            if (start == default)
                start = end.AddDays(-7);
            Console.WriteLine($"Getting stats for {playerUuid} from {start} to {end}");
            var length = (end - start);
            if (length.TotalDays > 7 || start < DateTime.UtcNow.AddDays(-7.1))
                if (!await premiumTierService.HasPremium(this))
                    throw new CoflnetException("invalid_time",
                        "Sorry but this is currently limited to one week for non premium users. "
                        + $"Please provide a google token as `{premiumTierService.HeaderName}` header to get further history");
            if (length.TotalDays <= 0)
                throw new CoflnetException("invalid_time", "End has to be after start");
            var result = await flipService.GetPlayerFlips(playerUuid, length, end);
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
        [Obsolete("This endpoint got deprecated. This was made for tfm which doesn't use it anymore. If you do, please open a suggestion thread on our discord.")]
        public async Task<List<FlipDetails>> GetFlipsForFinder(string finderName, DateTime start = default, DateTime end = default)
        {
            throw new CoflnetException("deprecated", "This endpoint got deprecated. This was made for tfm which doesn't use it anymore. If you do, please open a suggestion thread on our discord.");
        }
    }
}

