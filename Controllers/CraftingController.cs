using System.Linq;
using System.Threading.Tasks;
using RestSharp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Crafts.Models;
using Newtonsoft.Json;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Commands.Shared;

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
        private Coflnet.Sky.Commands.Shared.IProfileClient profileClient;
        private string apiUrl;
        PricesService pricesService;
        /// <summary>
        /// Creates a new instance of <see cref="CraftingController"/>
        /// </summary>
        /// <param name="config"></param>
        /// <param name="pricesService"></param>
        public CraftingController(IConfiguration config, PricesService pricesService, IProfileClient profileClient)
        {
            if (client == null)
                client = new RestClient(config["CRAFTS_BASE_URL"] ?? "http://" + config["CRAFTS_HOST"]);
            apiUrl = config["API_BASE_URL"];
            this.pricesService = pricesService;
            this.profileClient = profileClient;
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
    }
}