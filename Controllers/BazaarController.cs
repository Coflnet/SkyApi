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

        /// <summary>
        /// Exports detailed item data for a specific item, if there is no start/end specified it will return a compressed file with 20sec increments of the last 2 weeks
        /// For longer timeframes we only keep 5min increments and return those, optionally with full orderbook for each point.
        /// Note that this endpoint requires a google id token of an account with prem+ and is subject to strict non distribute and non profit license terms
        /// </summary>
        /// <param name="itemTag"></param>
        /// <param name="premiumTierService"></param>
        /// <param name="redis"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <param name="fullOrderBook"></param>
        /// <returns></returns>
        [Route("{itemTag}/export")]
        [HttpGet]
        [Microsoft.AspNetCore.Authorization.Authorize]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Export(string itemTag,
            [FromServices] Coflnet.Sky.Api.Services.PremiumTierService premiumTierService,
            [FromServices] StackExchange.Redis.IConnectionMultiplexer redis,
            DateTime? start = null, DateTime? end = null, bool fullOrderBook = false)
        {
            var user = await premiumTierService.GetUserOrDefault(this);
            if (user == null)
            {
                return Unauthorized();
            }

            if (!await premiumTierService.HasPremiumPlus(this))
            {
                throw new CoflnetException("no_premium_plus", "Sorry this feature is only available for premium+ users.");
            }

            var db = redis.GetDatabase();
            var now = DateTimeOffset.UtcNow;
            var windowStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, (now.Minute / 5) * 5, 0, now.Offset);
            var key = $"ratelimit:export:user:{user.Id}:{windowStart.ToUnixTimeSeconds()}";

            var count = await db.StringIncrementAsync(key);
            if (count == 1)
            {
                await db.KeyExpireAsync(key, TimeSpan.FromMinutes(6));
            }

            if (count > 5)
            {
                return StatusCode(429, "Rate limit exceeded. You can make up to 5 requests per 5 minutes.");
            }

            var data = await bazaarClient.GetDataAsync(itemTag,
                start.HasValue ? start!.Value.RoundDown(TimeSpan.FromMinutes(1)) : null,
                end.HasValue ? end!.Value.RoundDown(TimeSpan.FromMinutes(1)) : null, true, fullOrderBook);

            Response.ContentType = "application/gzip";
            Response.Headers["Content-Disposition"] = $"attachment; filename={itemTag}.json.gz";

            await using (var gzipStream = new System.IO.Compression.GZipStream(Response.Body, System.IO.Compression.CompressionLevel.Optimal))
            await using (var streamWriter = new System.IO.StreamWriter(gzipStream))
            {
                await streamWriter.WriteAsync("[");
                bool first = true;
                foreach (var item in data)
                {
                    if (!first)
                        await streamWriter.WriteAsync(",");
                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(item);
                    await streamWriter.WriteAsync(json);
                    first = false;
                }
                await streamWriter.WriteAsync("]");
            }

            return new EmptyResult();
        }
    }
}

