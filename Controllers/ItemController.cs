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

namespace Coflnet.Hypixel.Controller
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

        /// <summary>
        /// Creates a new instance of <see cref="FlipController"/>
        /// </summary>
        /// <param name="config"></param>
        /// <param name="context"></param>
        /// <param name="itemsApi"></param>
        public ItemController(IConfiguration config, HypixelContext context, Sky.Items.Client.Api.IItemsApi itemsApi)
        {
            this.config = config;
            this.db = context;
            this.itemsApi = itemsApi;
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
            return (await itemsApi.ItemsGetAsync()).Select(i=>{
                return new ItemMetadataElement{
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
        public async Task<Dictionary<string,string>> ItemTags([FromBody] HashSet<string> tags)
        {
            var items = await itemsApi.ItemsGetAsync();
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
            var source =  await itemsApi.ItemItemTagGetAsync(itemTag);
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
    }
}

