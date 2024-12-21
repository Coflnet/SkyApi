using System.Linq;
using System.Threading.Tasks;
using RestSharp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Crafts.Models;
using Newtonsoft.Json;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Sniper.Client.Api;
using Coflnet.Sky.Core;

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
        private static RestClient client = null;
        private IProfileClient profileClient;
        private string apiUrl;
        PricesService pricesService;
        private IAuctionApi auctionApi;
        /// <summary>
        /// Creates a new instance of <see cref="CraftingController"/>
        /// </summary>
        /// <param name="config"></param>
        /// <param name="pricesService"></param>
        /// <param name="profileClient"></param>
        /// <param name="auctionApi"></param>
        public CraftingController(IConfiguration config, PricesService pricesService, IProfileClient profileClient, IAuctionApi auctionApi)
        {
            if (client == null)
                client = new RestClient(config["CRAFTS_BASE_URL"] ?? "http://" + config["CRAFTS_HOST"]);
            apiUrl = config["API_BASE_URL"];
            this.pricesService = pricesService;
            this.profileClient = profileClient;
            this.auctionApi = auctionApi;
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
            var response = await client.ExecuteAsync(new RestRequest("Crafts/profit"));
            var crafts = JsonConvert.DeserializeObject<List<ProfitableCraft>>(response.Content);
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
        public async Task<Dictionary<string, string>> GetRecipe(string itemTag)
        {
            var response = await client.ExecuteAsync(new RestRequest($"Crafts/recipe/{itemTag}"));
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content);
        }

        /// <summary>
        /// Returns craft instructions and if lbin
        /// </summary>
        /// <param name="itemTag"></param>
        /// <returns></returns>
        [Route("{itemTag}/instructions")]
        [HttpGet]
        [ResponseCache(Duration = 3600 * 12, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<CraftInstruction> GetInstructions(string itemTag)
        {
            var response = await client.ExecuteAsync(new RestRequest($"Crafts/recipe/{itemTag}"));
            var recipe = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content ?? "{}");
            var ids = recipe.Select(x => x.Value.Split(':').First()).Distinct().ToList();
            var itemsOnBazaar = await pricesService.GetBazaarItems();
            var lbins = await auctionApi.ApiAuctionLbinsGetAsync();
            var elements = await Task.WhenAll(ids.Select(async x =>
            {
                if (itemsOnBazaar.Contains(x))
                    return (x, $"/item/{x}", $"/bz {x}");
                var auction = await AuctionService.Instance.GetAuctionAsync(AuctionService.Instance.GetUuid(lbins.GetValueOrDefault(x).AuctionId));
                return (x, $"/auction/{auction.Uuid}", $"/viewauction {auction.Uuid}");
            }));
            var commands = elements.ToDictionary(x => x.Item1, x => x.Item3);
            var path = elements.ToDictionary(x => x.Item1, x => x.Item2);
            return new CraftInstruction(itemTag, recipe, commands, path);
        }
    }
}