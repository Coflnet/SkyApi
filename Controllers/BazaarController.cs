using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Bazaar.Client.Api;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Controller
{
    /// <summary>
    /// Main API endpoints
    /// </summary>
    [ApiController]
    [Route("api/bazaar")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class BazaarController : ControllerBase
    {
        private BazaarApi bazaarClient;
        private ILogger<BazaarController> logger;
        /// <summary>
        /// Creates a new instance of <see cref="BazaarApi"/>
        /// </summary>
        /// <param name="bazaarClient"></param>
        /// <param name="logger"></param>
        public BazaarController(BazaarApi bazaarClient, ILogger<BazaarController> logger)
        {
            this.bazaarClient = bazaarClient;
            this.logger = logger;
        }

        /// <summary>
        /// Gets the history data for display in a graph for one hour (in intervals of 20 seconds)
        /// </summary>
        /// <param name="itemTag">What item to get data for</param>
        /// <returns>An list of graph points</returns>
        [Route("{itemTag}/history/hour")]
        [HttpGet]
        [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<List<Sky.Bazaar.Client.Model.GraphResult>> HistoryGraphHour(string itemTag)
        {
            var data = await bazaarClient.GetHistoryGraphAsync(itemTag, Ago(TimeSpan.FromHours(1)), Ago(TimeSpan.FromSeconds(1)));
            return data;
        }

        private static DateTime Ago(TimeSpan ago)
        {
            return (DateTime.UtcNow - ago).RoundDown(new TimeSpan(10));
        }

        /// <summary>
        /// Gets the history data for display in a graph for one day ( in intervals of 5 minutes)
        /// </summary>
        /// <param name="itemTag">What item to get data for</param>
        /// <returns>An list of graph points</returns>
        [Route("{itemTag}/history/day")]
        [HttpGet]
        [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<List<Sky.Bazaar.Client.Model.GraphResult>> HistoryGraphDay(string itemTag)
        {
            return await bazaarClient.GetHistoryGraphAsync(itemTag, Ago(TimeSpan.FromDays(1)), Ago(TimeSpan.FromMilliseconds(2)));
        }
        /// <summary>
        /// Gets the history data for display in a graph for one week ( in intervals of 2 hours)
        /// </summary>
        /// <param name="itemTag">What item to get data for</param>
        /// <returns>An list of graph points</returns>
        [Route("{itemTag}/history/week")]
        [HttpGet]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<List<Sky.Bazaar.Client.Model.GraphResult>> HistoryGraphWeek(string itemTag)
        {
            return await bazaarClient.GetHistoryGraphAsync(itemTag, Ago(TimeSpan.FromDays(7)), Ago(TimeSpan.FromMilliseconds(2)));
        }


        /// <summary>
        /// Gets the history data for display in a graph
        /// </summary>
        /// <param name="itemTag">What item to get data for</param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns>An list of graph points</returns>
        [Route("{itemTag}/history")]
        [HttpGet]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "start", "end" })]
        public async Task<List<Sky.Bazaar.Client.Model.GraphResult>> HistoryGraph(string itemTag, DateTime? start = null, DateTime? end = null)
        {
            var result = await bazaarClient.GetHistoryGraphAsync(itemTag,
                start.HasValue ? start!.Value.RoundDown(TimeSpan.FromMinutes(1)) : null,
                end.HasValue ? end!.Value.RoundDown(TimeSpan.FromMinutes(1)) : null);
            if (result.Count == 0)
            {
                logger.LogInformation("No data found for {itemTag} between {start} and {end}", itemTag, start, end);
            }
            return result;
        }

        /// <summary>
        /// Gets a snapshot of a specific item at a specific time
        /// </summary>
        /// <param name="itemTag">The search term to search for</param>
        /// <param name="timestamp">Whattime to retrieve the information at (defaults to now)</param>
        /// <returns>A quickstatus object representing the order book at that time</returns>
        [Route("{itemTag}/snapshot")]
        [HttpGet]
        [ResponseCache(Duration = 360, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "timestamp" })]
        public async Task<Sky.Bazaar.Client.Model.StorageQuickStatus> GetSnapshot(string itemTag, DateTime timestamp = default)
        {
            if (timestamp == default)
                timestamp = DateTime.UtcNow;
            return await bazaarClient.GetClosestToAsync(itemTag, timestamp.AddSeconds(10).RoundDown(TimeSpan.FromSeconds(20)));
        }

        [Obsolete("Use /api/player/bazaar/orders with api key authentication")]
        [Route("player/{playerId}/orders")]
        [HttpGet]
        public async Task<List<PlayerState.Client.Model.Offer>> GetPlayerOrders(string playerId)
        {
            throw new CoflnetException("obsulete", "This endpoint is obsolete, use /api/player/bazaar/orders with api key authentication");
        }
    }
}

