using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using hypixel;
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
        public async Task<ActionResult<List<SearchService.SearchResultItem>>> SearchItem(string searchVal)
        {
            var result = await CoreServer.ExecuteCommandWithCache<string, List<SearchService.SearchResultItem>>("itemSearch", searchVal);
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
        public async Task<ActionResult<List<SearchService.SearchResultItem>>> FullSearch(string searchVal)
        {
            var result = await CoreServer.ExecuteCommandWithCache<string, List<SearchService.SearchResultItem>>("fullSearch", searchVal);
            return Ok(result);
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

