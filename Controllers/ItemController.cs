using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Api.Helper;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        private ILogger<ItemController> logger;

        /// <summary>
        /// Creates a new instance of <see cref="FlipController"/>
        /// </summary>
        /// <param name="config"></param>
        /// <param name="context"></param>
        /// <param name="itemsApi"></param>
        public ItemController(IConfiguration config, HypixelContext context, Sky.Items.Client.Api.IItemsApi itemsApi, Crafts.Client.Api.ICraftsApi craftsApi, ILogger<ItemController> logger)
        {
            this.config = config;
            this.db = context;
            this.itemsApi = itemsApi;
            this.craftsApi = craftsApi;
            this.logger = logger;
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
            var response = await itemsApi.ItemsGetWithHttpInfoAsync();
            var settings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { new FlagsEnumConverter() }
            };
            var items = JsonConvert.DeserializeObject<List<Sky.Items.Client.Model.Item>>(response.RawContent.Replace("SUPREME", "DIVINE"), settings);
            return items.Select(i =>
            {
                if (i.Tag == "PET_SKIN_CROW_SNOW")
                {
                    Console.WriteLine(i.Flags?.ToString());
                    Console.WriteLine(JsonConvert.SerializeObject(i));
                }
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
                Category = source?.Category,
                Flags = source?.Flags,
                IconUrl = source?.IconUrl,
                Name = source?.Name,
                Tag = source?.Tag ?? itemTag,
                Tier = (Tier?)source?.Tier,
                NpcSellPrice = source?.NpcSellPrice ?? -1,
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
            var allItems = await itemsApi.ItemsGetWithHttpInfoAsync();
            var settings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { new FlagsEnumConverter() }
            };
            var source = JsonConvert.DeserializeObject<List<Items.Client.Model.Item>>(allItems.RawContent.Replace("SUPREME", "DIVINE"), settings);
            Console.WriteLine($"Found {source?.Count} items in source");
            var recipe = await craftsApi.GetRecipeAsync(itemTag);
            var search = itemTag.Truncate(10);
            if (itemTag.Contains("_"))
                search = itemTag.Substring(0, itemTag.LastIndexOf("_"));
            var similarName = source.Where(i => i.Tag != itemTag && i.Tag.StartsWith(search) && (i.Flags.Value.HasFlag(Items.Client.Model.ItemFlags.AUCTION) || i.Flags.Value.HasFlag(Items.Client.Model.ItemFlags.BAZAAR))).OrderBy(x => Random.Shared.Next()).Take(5)
                .Select(i => new Items.Client.Model.ItemPreview
                {
                    Name = i.Name,
                    Tag = i.Tag
                });
            IEnumerable<Items.Client.Model.ItemPreview> recipeBased = NewMethod(source, recipe);
            return recipeBased.Concat(similarName).Take(5);
        }
        private static IEnumerable<Items.Client.Model.ItemPreview> NewMethod(List<Items.Client.Model.Item> source, Crafts.Client.Model.Recipe recipe)
        {
            if (recipe == null)
                return new Items.Client.Model.ItemPreview[0];
            var allItems = new string[] { recipe.A1, recipe.A2, recipe.A3, recipe.B1, recipe.B2, recipe.B3, recipe.C1, recipe.C2, recipe.C3 };
            var recipeBased = allItems?.Select(t => t?.Split(':').First())
                .Distinct()
                .Where(t => t != null && t.Length > 2)
                .Select(tag => new Items.Client.Model.ItemPreview
                {
                    Name = source.Where(i => i.Tag == tag).FirstOrDefault()?.Name,
                    Tag = tag
                }) ?? new Items.Client.Model.ItemPreview[0];
            return recipeBased;
        }
    }
}

