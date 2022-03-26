using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using RestSharp;

namespace Coflnet.Sky.Api.Services
{
    public class TfmService 
    {
        private RestClient client = new RestClient("https://api.thom.club");

        public async Task<bool> IsUserOnAsync(string uuid)
        {
            var tfmTask = client.ExecuteAsync(new RestRequest("online_tfm_users"));
            var name = (await PlayerService.Instance.GetPlayer(uuid)).Name;
            var onlinePlayersJson = (await tfmTask).Content;
            var onlinePlayers = JsonConvert.DeserializeObject<OnlineResponse>(onlinePlayersJson).user_list.Select(a=>a.First());
            
            return onlinePlayers.Contains(name);

        }

        public class OnlineResponse
        {
            public dynamic[][] user_list;
        }
    }
}