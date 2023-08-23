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
        private static RestClient profileClient = null;
        private string apiUrl;
        PricesService pricesService;
        /// <summary>
        /// Creates a new instance of <see cref="CraftingController"/>
        /// </summary>
        /// <param name="config"></param>
        /// <param name="pricesService"></param>
        public CraftingController(IConfiguration config, PricesService pricesService)
        {
            if (client == null)
                client = new RestClient(config["CRAFTS_BASE_URL"] ?? "http://" + config["CRAFTS_HOST"]);
            if (profileClient == null)
                profileClient = new RestClient(config["PROFILE_BASE_URL"] ?? "http://" + config["PROFILE_HOST"]);
            apiUrl = config["API_BASE_URL"];
            this.pricesService = pricesService;
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
            var slayersJsonTask = profileClient.ExecuteAsync(new RestRequest($"/api/profile/{player}/{profile}/data/slayers"));
            var collectionJson = await profileClient.ExecuteAsync(new RestRequest($"/api/profile/{player}/{profile}/data/collections"));
            var slayersJson = await slayersJsonTask;
            var collection = JsonConvert.DeserializeObject<Dictionary<string, CollectionElem>>(collectionJson.Content);
            var slayers = JsonConvert.DeserializeObject<Dictionary<string, SlayerElem>>(slayersJson.Content);
            var list = new List<ProfitableCraft>();
            foreach (var item in crafts)
            {
                if (item == null)
                    continue;

                if (item.ReqCollection == null
                || collection.TryGetValue(item.ReqCollection.Name, out CollectionElem elem)
                        && elem.tier >= item.ReqCollection.Level)
                {
                    list.Add(item);
                }
                else if (item.ReqSlayer == null
                    || slayers.TryGetValue(item.ReqSlayer.Name.ToLower(), out SlayerElem slayerElem)
                      && slayerElem.Level.currentLevel >= item.ReqSlayer.Level)
                    list.Add(item);
                else
                    Console.WriteLine("Blocked " + item.ItemId + " " + item.ReqCollection.Name);
            }
            return list;
        }

        private async Task<IEnumerable<ProfitableCraft>> AddSaleData(List<ProfitableCraft> list)
        {
            var result = new List<ProfitableCraft>();
            await Parallel.ForEachAsync(list,async (i,t) =>
            {
                try
                {
                    i.Median = -1;
                    var sumary = await pricesService.GetSumaryCache(i.ItemId).ConfigureAwait(false);
                    i.Volume = sumary.Volume;
                    i.Median = sumary.Med;
                }
                catch (Exception e)
                {
                    dev.Logger.Instance.Error(e, "getting price summary for crafts");
                }
                
                if (i.Volume > 2)
                    result.Add(i);
            });
            return result.OrderByDescending(r => r.SellPrice - r.CraftCost);
        }

        /// <summary>
        /// Returns the crafting recipe for some item
        /// </summary>
        /// <param name="itemTag"></param>
        /// <returns></returns>
        [Route("recipe/{itemTag}")]
        [HttpGet]
        [Route("api/craft")]
        [ResponseCache(Duration = 3600 * 12, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<Dictionary<string, string>> GetRecipe(string itemTag)
        {
            var response = await client.ExecuteAsync(new RestRequest($"Crafts/recipe/{itemTag}"));
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(response.Content);
        }
    }
}