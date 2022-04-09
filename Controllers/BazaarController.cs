using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Bazaar.Client.Api;
using System;

namespace Coflnet.Hypixel.Controller
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
        /// <summary>
        /// Creates a new instance of <see cref="BazaarApi"/>
        /// </summary>
        /// <param name="bazaarClient"></param>
        public BazaarController(BazaarApi bazaarClient)
        {
            this.bazaarClient = bazaarClient;
        }

        /// <summary>
        /// Gets the history data for display in a graph for one hour ( in intervalls of 10 seconds)
        /// </summary>
        /// <param name="itemTag">What item to get data for</param>
        /// <returns>An list of graph points</returns>
        [Route("{itemTag}/history/hour")]
        [HttpGet]
        [ResponseCache(Duration = 10, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<List<Sky.Bazaar.Client.Model.GraphResult>> HistoryGraphHour(string itemTag)
        {
            var data = await bazaarClient.ApiBazaarItemIdHistoryGetAsync(itemTag, DateTime.UtcNow - TimeSpan.FromHours(1), DateTime.UtcNow - TimeSpan.FromMilliseconds(2));
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(data[0]));
            return data;
        }
        /// <summary>
        /// Gets the history data for display in a graph for one day ( in intervalls of 5 minutes)
        /// </summary>
        /// <param name="itemTag">What item to get data for</param>
        /// <returns>An list of graph points</returns>
        [Route("{itemTag}/history/day")]
        [HttpGet]
        [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<List<Sky.Bazaar.Client.Model.GraphResult>> HistoryGraphDay(string itemTag)
        {
            return await bazaarClient.ApiBazaarItemIdHistoryGetAsync(itemTag, DateTime.UtcNow - TimeSpan.FromDays(1), DateTime.UtcNow - TimeSpan.FromMilliseconds(2));
        }
        /// <summary>
        /// Gets the history data for display in a graph for one week ( in intervalls of 2 hours)
        /// </summary>
        /// <param name="itemTag">What item to get data for</param>
        /// <returns>An list of graph points</returns>
        [Route("{itemTag}/history/week")]
        [HttpGet]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<List<Sky.Bazaar.Client.Model.GraphResult>> HistoryGraphWeek(string itemTag)
        {
            return await bazaarClient.ApiBazaarItemIdHistoryGetAsync(itemTag, DateTime.UtcNow - TimeSpan.FromDays(7), DateTime.UtcNow - TimeSpan.FromMilliseconds(2));
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
            return await bazaarClient.ApiBazaarItemIdHistoryGetAsync(itemTag, start, end);
        }

        /// <summary>
        /// Gets a snapshot of a specific item at a specific time
        /// </summary>
        /// <param name="itemTag">The search term to search for</param>
        /// <param name="timestamp">Whattime to retrieve the information at (defaults to now)</param>
        /// <returns>A quickstatus object representing the order book at that time</returns>
        [Route("{itemTag}/snapshot")]
        [HttpGet]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "timestamp" })]
        public async Task<Sky.Bazaar.Client.Model.StorageQuickStatus> GetSnapshot(string itemTag, DateTime timestamp = default)
        {
            if(timestamp == default)
                timestamp = DateTime.UtcNow;
            return await bazaarClient.ApiBazaarItemIdSnapshotGetAsync(itemTag, timestamp);
        }
    }
}

