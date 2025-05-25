using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using System.Linq;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Items.Client.Api;
using System.Diagnostics;

namespace Coflnet.Sky.Api.Controller
{
    /// <summary>
    /// Main API endpoints
    /// </summary>
    [ApiController]
    [Route("api")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class SearchController : ControllerBase
    {
        static Regex validCharRegex = new Regex("[^-a-zA-Z0-9_\\.' ]");
        private IItemsApi itemsApi;
        private SearchService searchService;

        /// <summary>
        /// Creates a new instance of <see cref="SearchController"/>
        /// </summary>
        /// <param name="itemsApi"></param>
        /// <param name="searchService"></param>
        public SearchController(IItemsApi itemsApi, SearchService searchService)
        {
            this.itemsApi = itemsApi;
            this.searchService = searchService;
        }

        /// <summary>
        /// Searches through all items, includes the rarity of items
        /// </summary>
        /// <param name="searchVal">The search term to search for</param>
        /// <returns>An array of search results matching the searchValue</returns>
        [Route("item/search/{searchVal}")]
        [HttpGet]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<List<SearchResultItem>> SearchItem(string searchVal)
        {
            var itemsResult = await itemsApi.ItemsSearchTermGetAsync(searchVal, 10);
            var results = itemsResult?.Select(i => new SearchResultItem(new ItemDetails.ItemSearchResult()
            {
                Name = i.Text + (i.Flags.Value.HasFlag(Sky.Items.Client.Model.ItemFlags.BAZAAR) ? " - bazaar"
                        : i.Flags.Value.HasFlag(Sky.Items.Client.Model.ItemFlags.AUCTION) ? "" : " - not on ah"),
                Tag = i.Tag,
                IconUrl = "https://sky.coflnet.com/static/icon/" + i.Tag,
                HitCount = i.Flags.Value.HasFlag(Sky.Items.Client.Model.ItemFlags.AUCTION) ? 50 : 0,
                Tier = (Coflnet.Sky.Core.Tier)i.Tier - 1

            })).ToList();
            // return 5 except if they all have the same text
            return ExtendIfSame(results);
        }

        private static List<SearchResultItem> ExtendIfSame(List<SearchResultItem> results, int limit = 5)
        {
            results.Where(r=>r.Type == "item").GroupBy(r => r.Name)
                .Where(g => g.Count() > 1).SelectMany(g => g).ToList().ForEach(i =>
                {
                    var useFirst = results.Select(s => s.Id.Split('_').First()).Distinct().Count() > 1;
                    i.Name = i.Name + " - " + (useFirst ? i.Id.Split('_').First().Truncate(10) : i.Id.Split('_').Last().Truncate(10));
                });
            return results.Take(limit).ToList();
        }


        /// <summary>
        /// Full search, includes item types, items (by uuid), players, auctions and enchantments
        /// </summary>
        /// <param name="searchVal">The search term to search for</param>
        /// <param name="limit">The maximum amount of results to return</param>
        /// <returns></returns>
        [Route("search/{searchVal}")]
        [HttpGet]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<SearchResultItem>> FullSearch(string searchVal, int limit = 5)
        {
            searchVal = searchVal.ToLower().Replace('’', '\'');
            var collection = await ExecuteSearch(searchVal, 2000);
            if (collection.Count == 0) // search again
                collection = await ExecuteSearch(searchVal);
            var result = searchService.RankSearchResults(searchVal, collection.Where(r => r.Type != "internal"));
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

            if (result.Count < 5)
            {
                return result;
            }
            Activity.Current?.SetTag("search", searchVal.ToLower());
            return ExtendIfSame(result, limit);
        }

        private async Task<ConcurrentQueue<SearchResultItem>> ExecuteSearch(string searchVal, int timeout = 3000)
        {
            var cancelationSource = new CancellationTokenSource();
            cancelationSource.CancelAfter(timeout);
            var channel = await searchService.Search(searchVal, cancelationSource.Token);

            var collection = new ConcurrentQueue<SearchResultItem>();

            // either get a decent amount of results (and require the item service & player db to have responded) or timeout
            while ((!EnoughResults(searchVal, collection.Count) ||
                !collection.Any(r => r.Type == "internal" && r.Id == "items") ||
                !collection.Any(r => r.Type == "internal" && r.Id == "players")
            ) && !cancelationSource.Token.IsCancellationRequested)
            {
                try
                {
                    var element = await channel.Reader.ReadAsync(cancelationSource.Token);
                    collection.Enqueue(element);
                }
                catch (OperationCanceledException)
                {
                    // done
                }
            }
            // give an extra buffer for more results to arrive
            await Task.Delay(10);
            while (channel.Reader.TryRead(out SearchResultItem item))
            {
                collection.Enqueue(item);
            }

            return collection;
        }

        private static string RemoveInvalidChars(string search)
        {
            return validCharRegex.Replace(search, "").ToLower().TrimStart();
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
            if (resultCount == 0)
                return false;
            var charCount = searchVal.Length;
            return (10 / charCount) <= resultCount;
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
            return Coflnet.Sky.Core.PlayerSearch.Instance.Search(playerName, 5);
        }
    }
}

