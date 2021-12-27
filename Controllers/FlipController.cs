using System;
using System.Threading.Tasks;
using Coflnet.Sky;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Filter;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using RestSharp;

namespace Coflnet.Hypixel.Controller
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

        /// <summary>
        /// Creates a new instance of <see cref="FlipController"/>
        /// </summary>
        /// <param name="config"></param>
        public FlipController(IConfiguration config)
        {
            this.config = config;
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
        /// <returns></returns>
        [Route("track/purchase/{auctionId}")]
        [HttpPost]
        public async Task TrackExternalFlip(string auctionId, string finder, string playerId, int price = -1)
        {
            await Sky.Commands.FlipTrackingService.Instance.NewFlip(new LowPricedAuction()
            {
                Auction = new hypixel.SaveAuction() { Uuid = auctionId },
                Finder = finder.ToLower() == "tfm" ? LowPricedAuction.FinderType.TFM : LowPricedAuction.FinderType.EXTERNAL,
                TargetPrice = price
            });
            await Sky.Commands.FlipTrackingService.Instance.ReceiveFlip(auctionId, playerId);
        }

        /// <summary>
        /// Callback for external flip finders to be included in tracking. 
        /// </summary>
        /// <param name="finder">Identifier of finder</param>
        /// <param name="auctionId">The id of the found auction</param>
        /// <param name="price">Suggested target price</param>
        /// <returns></returns>
        [Route("track/found/{auctionId}")]
        [HttpPost]
        public async Task TrackExternalFlip(string auctionId, string finder, int price = -1)
        {
            await Sky.Commands.FlipTrackingService.Instance.NewFlip(new LowPricedAuction()
            {
                Auction = new hypixel.SaveAuction() { Uuid = auctionId },
                Finder = finder.ToLower() == "tfm" ? LowPricedAuction.FinderType.TFM : LowPricedAuction.FinderType.EXTERNAL,
                TargetPrice = price
            });
        }
    }
}

