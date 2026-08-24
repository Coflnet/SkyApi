using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using RestSharp;

namespace Coflnet.Sky.Api.Services
{
    /// <summary>Provides TFM status operations.</summary>
    public class TfmService
    {
        private RestClient client = new RestClient("https://api.thom.club");

        /// <summary>Determines whether a user is online in TFM.</summary>
        public async Task<bool> IsUserOnAsync(string uuid)
        {
            var tfmTask = client.ExecuteAsync(new RestRequest("online_tfm_users"));
            var name = await PlayerSearch.Instance.GetName(uuid);
            var onlinePlayersJson = (await tfmTask).Content;
            var onlinePlayers = JsonConvert.DeserializeObject<OnlineResponse>(onlinePlayersJson).user_list.Select(a => a.First());

            return onlinePlayers.Contains(name);

        }

        /// <summary>Represents an online response.</summary>
        public class OnlineResponse
        {
            /// <summary>Stores the user list.</summary>
            public dynamic[][] user_list;
        }
    }
}
