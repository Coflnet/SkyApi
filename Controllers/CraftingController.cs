using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using RestSharp;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.Crafts.Models;
using Newtonsoft.Json;

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
        public CraftingController(IConfiguration config)
        {
            if (client == null)
                client = new RestClient("http://" + config["CRAFTS_HOST"]);
            if (profileClient == null)
                profileClient = new RestClient("http://" + config["PROFILE_HOST"]);
        }

        [Route("profit")]
        [HttpGet]
        public async Task<IEnumerable<ProfitableCraft>> GetProfitable(string player = null, string profile = null)
        {
            var response = await client.ExecuteAsync(new RestRequest("Crafts/profit").AddParameter("profile", profile));
            var crafts = JsonConvert.DeserializeObject<List<ProfitableCraft>>(response.Content);
            if (profile == null)
                return crafts;
            var collectionJson = await profileClient.ExecuteAsync(new RestRequest($"/api/profile/{player}/{profile}/data/collections"));
            var collection = JsonConvert.DeserializeObject<Dictionary<string, CollectionElem>>(collectionJson.Content);
            var list = new List<ProfitableCraft>();
            foreach (var item in crafts)
            {
                if(item.ReqCollection == null 
                || collection.TryGetValue(item.ReqCollection.Name, out CollectionElem elem) 
                        && elem.tier >= item.ReqCollection.Level)
                    list.Add(item);
                else 
                    Console.WriteLine("Blocked " + item.ItemId + " " + item.ReqCollection.Name);
            }
            return list;
        }

        [Route("recipe/{itemTag}")]
        [HttpGet]
        public async Task<Dictionary<string,string>> GetRecipe(string itemTag)
        {
            var response = await client.ExecuteAsync(new RestRequest($"Crafts/recipe/{itemTag}"));
            return JsonConvert.DeserializeObject<Dictionary<string,string>>(response.Content);
        }
    }

    public class CollectionElem
    {
        public int tier;
    }
}