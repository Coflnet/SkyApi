using System.Linq;
using System.Threading.Tasks;
using RestSharp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Sniper.Client.Api;
using Coflnet.Sky.Core;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;

namespace Coflnet.Sky.Api.Controller
{
    /// <summary>
    /// Endpoints for crafting related data
    /// </summary>
    [ApiController]
    [Route("api/craft")]
    [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class CraftingController : ControllerBase
    {
        private IProfileClient profileClient;
        private string apiUrl;
        PricesService pricesService;
        ICraftsApi craftsApi;
        private IAuctionApi auctionApi;
        /// <summary>
        /// Creates a new instance of <see cref="CraftingController"/>
        /// </summary>
        /// <param name="config"></param>
        /// <param name="pricesService"></param>
        /// <param name="profileClient"></param>
        /// <param name="auctionApi"></param>
        /// <param name="craftsApi"></param>
        public CraftingController(IConfiguration config, PricesService pricesService, IProfileClient profileClient, IAuctionApi auctionApi, ICraftsApi craftsApi)
        {

            apiUrl = config["API_BASE_URL"];
            this.pricesService = pricesService;
            this.profileClient = profileClient;
            this.auctionApi = auctionApi;
            this.craftsApi = craftsApi;
        }

        /// <summary>
        /// Craft flips
        /// </summary>
        /// <param name="player"></param>
        /// <param name="profile"></param>
        /// <returns></returns>
        [Route("profit")]
        [HttpGet]
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "player", "profile" })]
        public async Task<IEnumerable<ProfitableCraft>> GetProfitable(string player = null, string profile = null)
        {
            var crafts = await craftsApi.GetProfitableAsync();
            if (profile == null)
                return crafts;
            try
            {
                return await profileClient.FilterProfitableCrafts(Task.FromResult(crafts), player, profile);
            }
            catch (System.Exception e)
            {
                dev.Logger.Instance.Error(e, "getting profile data for crafts");
                return crafts;
            }
        }

        /// <summary>
        /// Returns the crafting recipe for some item
        /// </summary>
        /// <param name="itemTag"></param>
        /// <returns></returns>
        [Route("recipe/{itemTag}")]
        [HttpGet]
        [ResponseCache(Duration = 3600 * 12, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<Recipe> GetRecipe(string itemTag)
        {
            return await craftsApi.GetRecipeAsync(itemTag);
        }

        /// <summary>
        /// Returns craft instructions and if lbin
        /// </summary>
        /// <param name="itemTag"></param>
        /// <returns></returns>
        [Route("{itemTag}/instructions")]
        [HttpGet]
        [ResponseCache(Duration = 3600 * 12, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<CraftInstruction> GetInstructions(string itemTag, [FromServices] Items.Client.Api.IItemsApi itemApi)
        {
            var itemNamesTask = itemApi.ItemNamesGetAsync();
            var response = await GetRecipe(itemTag);
            var recipe = JsonConvert.DeserializeObject<Dictionary<string, string>>(JsonConvert.SerializeObject(response) ?? "{}");
            var ids = recipe.Where(x=>x.Value != null).Select(x => x.Value.Split(':').First()).Where(x => x.Length >= 3).Distinct().ToList();
            var itemsOnBazaar = await pricesService.GetBazaarItems();
            var lbins = await auctionApi.ApiAuctionLbinsGetAsync();
            var nameLookup = (await itemNamesTask).ToDictionary(x => x.Tag, x => x.Name);
            var elements = await Task.WhenAll(ids.Select(async x =>
            {
                if (itemsOnBazaar.Contains(x))
                    return (x, $"/item/{x}", $"/bz {BazaarUtils.GetSearchValue(x,nameLookup.GetValueOrDefault(x) ?? x)}");
                var lbin = lbins.GetValueOrDefault(x);
                if (lbin == null)
                    return (x, $"/item/{x}?range=active", $"/ah");
                var auction = await AuctionService.Instance.GetAuctionAsync(AuctionService.Instance.GetUuid(lbin.AuctionId));
                return (x, $"/auction/{auction.Uuid}", $"/viewauction {auction.Uuid}");
            }));
            var commands = elements.ToDictionary(x => x.Item1, x => x.Item3);
            var path = elements.ToDictionary(x => x.Item1, x => x.Item2);
            return new CraftInstruction(itemTag, recipe, commands, path);
        }
    }
}