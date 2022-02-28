using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using RestSharp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Crafts.Models;
using Newtonsoft.Json;
using Coflnet.Sky.Api.Models;

namespace Coflnet.Hypixel.Controller
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
        /// <summary>
        /// Creates a new instance of <see cref="CraftingController"/>
        /// </summary>
        /// <param name="config"></param>
        public CraftingController(IConfiguration config)
        {
            if (client == null)
                client = new RestClient("http://" + config["CRAFTS_HOST"]);
            if (profileClient == null)
                profileClient = new RestClient("http://" + config["PROFILE_HOST"]);
            apiUrl = config["API_BASE_URL"];
        }

        /// <summary>
        /// Craft flips
        /// </summary>
        /// <param name="player"></param>
        /// <param name="profile"></param>
        /// <returns></returns>
        [Route("profit")]
        [HttpGet]
        [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "player", "profile" })]
        public async Task<IEnumerable<ProfitableCraft>> GetProfitable(string player = null, string profile = null)
        {
            var response = await client.ExecuteAsync(new RestRequest("Crafts/profit"));
            var crafts = JsonConvert.DeserializeObject<List<ProfitableCraft>>(response.Content);
            if (profile == null)
                return await AddSaleData(crafts);
            var collectionJson = await profileClient.ExecuteAsync(new RestRequest($"/api/profile/{player}/{profile}/data/collections"));
            var slayersJson = await profileClient.ExecuteAsync(new RestRequest($"/api/profile/{player}/{profile}/data/collections"));
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
            return await AddSaleData(list);
        }

        private async Task<IEnumerable<ProfitableCraft>> AddSaleData(List<ProfitableCraft> list)
        {
            return await Task.WhenAll(list.Select(async i =>
            {
                var apiClient = new RestClient(apiUrl);
                apiClient.Timeout = 3000;
                try
                {
                    i.Median = -1;
                    var salesJson = await apiClient.ExecuteAsync(new RestRequest("/api/item/price/" + i.ItemId));
                    var sumary = JsonConvert.DeserializeObject<hypixel.PriceSumary>(salesJson.Content);
                    i.Volume = sumary.Volume;
                    i.Median = sumary.Med;
                }
                catch (Exception e)
                {
                    dev.Logger.Instance.Error(e, "getting price summary for crafts");
                }
                return i;
            }));
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