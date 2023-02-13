using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Commands;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Coflnet.Sky.Api.Controller
{
    /// <summary>
    /// Endpoints about items
    /// </summary>
    [ApiController]
    [Route("api/items")]
    [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class ItemController : ControllerBase
    {
        private IConfiguration config;
        private HypixelContext db;
        private Sky.Items.Client.Api.IItemsApi itemsApi;
        private Sky.Crafts.Client.Api.ICraftsApi craftsApi;

        /// <summary>
        /// Creates a new instance of <see cref="FlipController"/>
        /// </summary>
        /// <param name="config"></param>
        /// <param name="context"></param>
        /// <param name="itemsApi"></param>
        public ItemController(IConfiguration config, HypixelContext context, Sky.Items.Client.Api.IItemsApi itemsApi, Crafts.Client.Api.ICraftsApi craftsApi)
        {
            this.config = config;
            this.db = context;
            this.itemsApi = itemsApi;
            this.craftsApi = craftsApi;
        }

        /// <summary>
        /// A list of item tags (hypixel ids) that are tradeable on bazaar
        /// This gets updated once every hour
        /// </summary>
        /// <returns></returns>
        [Route("bazaar/tags")]
        [HttpGet]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<string>> GetFlipTime()
        {
            return await itemsApi.ItemsBazaarTagsGetAsync();
        }

        /// <summary>
        /// Get all item tags, names and wherever they are on ah or bazaar
        /// </summary>
        /// <returns></returns>
        [Route("")]
        [HttpGet]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<ItemMetadataElement>> GetAllItems()
        {
            return (await itemsApi.ItemsGetAsync()).Select(i =>
            {
                return new ItemMetadataElement
                {
                    Name = i.Name,
                    Tag = i.Tag,
                    Flags = i.Flags ?? 0
                };
            });
        }

        /// <summary>
        /// Batch lookup names for item tags
        /// </summary>
        /// <returns></returns>
        [Route("names")]
        [HttpPost]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<Dictionary<string, string>> ItemTags([FromBody] HashSet<string> tags)
        {
            var items = await itemsApi.ItemNamesGetAsync();
            return items.Where(t => tags.Contains(t.Tag)).ToDictionary(i => i.Tag, i => i.Name);
        }

        /// <summary>
        /// Returns details about a specific item
        /// This gets updated once every hour
        /// </summary>
        /// <returns></returns>
        [Route("/api/item/{itemTag}/details")]
        [HttpGet]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<SkyblockItem> GetItemDetails(string itemTag)
        {
            var source = await itemsApi.ItemItemTagGetAsync(itemTag);
            return new SkyblockItem()
            {
                Category = source.Category,
                Flags = source.Flags,
                IconUrl = source.IconUrl,
                Name = source.Name,
                Tag = source.Tag,
                Tier = (Tier?)source.Tier,
                NpcSellPrice = source.NpcSellPrice,
            };
        }

        /// <summary>
        /// Other items related to some tag
        /// </summary>
        /// <returns></returns>
        [Route("/api/item/{itemTag}/similar")]
        [HttpGet]
        [ResponseCache(Duration = 3600 * 12, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<IEnumerable<Items.Client.Model.ItemPreview>> GetSimilar(string itemTag)
        {
            var source = await itemsApi.ItemNamesGetAsync();
            var recipe = await craftsApi.CraftsRecipeItemTagGetAsync(itemTag);
            var search = itemTag.Truncate(10);
            if (itemTag.Contains("_"))
                search = itemTag.Substring(0, itemTag.LastIndexOf("_"));
            var similarName = source.Where(i => i.Tag != itemTag && i.Tag.StartsWith(search)).OrderBy(x => Random.Shared.Next()).Take(5);
            Console.WriteLine(JSON.Stringify(recipe));
            var recipeBased = recipe?.Values?.Distinct().Where(t => t != null && t.Length > 2).Select(tag => new Items.Client.Model.ItemPreview
            {
                Name = source.Where(i => i.Tag == tag).FirstOrDefault()?.Name,
                Tag = tag
            }) ?? new Items.Client.Model.ItemPreview[0];
            return recipeBased.Concat(similarName).Take(5);
        }
    }
}

