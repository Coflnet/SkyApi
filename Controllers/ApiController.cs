using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using hypixel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Coflnet.Hypixel.Controller
{
    /// <summary>
    /// Main API endpoints
    /// </summary>
    [ApiController]
    [Route("api")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class ApiController : ControllerBase
    {
        /// <summary>
        /// Searches through all items
        /// </summary>
        /// <param name="searchVal">The search term to search for</param>
        /// <returns>An array of search results matching the searchValue</returns>
        [Route("item/search/{searchVal}")]
        [HttpGet]
        [ResponseCache(Duration = 3600 * 12, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<ActionResult<List<SearchResultItem>>> SearchItem(string searchVal)
        {
            var result = await CoreServer.ExecuteCommandWithCache<string, List<SearchResultItem>>("itemSearch", searchVal);
            return Ok(result);
        }

        /// <summary>
        /// Full search, includes items, players and enchantments
        /// </summary>
        /// <param name="searchVal">The search term to search for</param>
        /// <returns></returns>
        [Route("search/{searchVal}")]
        [HttpGet]
        [ResponseCache(Duration = 3600 * 6, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<ActionResult<List<SearchResultItem>>> FullSearch(string searchVal)
        {
            searchVal = searchVal.ToLower();
            var collection = await NewMethod(searchVal, 1000);
            if (collection.Count == 0) // search again
                collection = await NewMethod(searchVal, 3000);
            if (collection.Count == 0) // search once more
                collection = await NewMethod(searchVal);
            var result = SearchService.Instance.RankSearchResults(searchVal, collection);
            if (result.Count == 0)
            {
                HttpContext.Response.GetTypedHeaders().CacheControl =
                    new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
                    {
                        NoCache = true,
                        NoStore = true,
                        MaxAge = TimeSpan.Zero
                    };
            }
            return Ok(result);
        }

        private static async Task<ConcurrentQueue<SearchResultItem>> NewMethod(string searchVal, int timeout = 4000)
        {
            var cancelationSource = new CancellationTokenSource();
            cancelationSource.CancelAfter(timeout);
            var channel = await SearchService.Instance.Search(searchVal, cancelationSource.Token);

            var collection = new  ConcurrentQueue<SearchResultItem>();

            // either get a decent amount of results or timeout
            while (!EnoughResults(searchVal, collection.Count) && !cancelationSource.Token.IsCancellationRequested)
            {
                var element = await channel.Reader.ReadAsync(cancelationSource.Token);
                collection.Enqueue(element);
            }
            // give an extra buffer for more results to arrive
            await Task.Delay(10);
            while(channel.Reader.TryRead(out SearchResultItem item))
            {
                collection.Enqueue(item);
            }

            return collection;
        }

        /// <summary>
        /// Determines if the result count is enough for a search
        /// 1 char => 10+ results
        /// 2 char => 5+
        /// 3 char => 3
        /// 4-5 char => 2
        /// 6 + one is enough
        /// </summary>
        /// <param name="searchVal"></param>
        /// <param name="resultCount"></param>
        /// <returns></returns>
        public static bool EnoughResults(string searchVal, int resultCount)
        {
            if(resultCount == 0)
                return false;
            var charCount = searchVal.Length;
            return (10 / charCount) <= resultCount ;
        }


        /// <summary>
        /// Search player 
        /// </summary>
        /// <param name="playerName">The player name to search for</param>
        /// <returns></returns>
        [Route("search/player/{playerName}")]
        [HttpGet]
        [ResponseCache(Duration = 3600 * 2, Location = ResponseCacheLocation.Any, NoStore = false)]
        public Task<IEnumerable<PlayerResult>> PlayerSearch(string playerName)
        {
            return hypixel.PlayerSearch.Instance.Search(playerName, 5);
        }
    }
}

