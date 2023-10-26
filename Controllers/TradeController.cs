using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerName.Client.Api;
using Coflnet.Sky.PlayerState.Client.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Controller;

/// <summary>
/// Things related to item trading
/// </summary>
[ApiController]
[Route("api")]
[ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
public class TradeController : ControllerBase
{
    private readonly ILogger<TradeController> logger;
    private readonly IPlayerStateApi playerStateApi;
    private readonly GoogletokenService googletokenService;
    private readonly McConnect.Api.IConnectApi connectApi;
    private readonly IPlayerNameApi playerNameApi;

    public TradeController(ILogger<TradeController> logger,
                           IPlayerStateApi playerStateApi,
                           GoogletokenService googletokenService,
                           McConnect.Api.IConnectApi connectApi,
                           IPlayerNameApi playerNameApi)
    {
        this.logger = logger;
        this.playerStateApi = playerStateApi;
        this.googletokenService = googletokenService;
        this.connectApi = connectApi;
        this.playerNameApi = playerNameApi;
    }

    /// <summary>
    /// Returns the last known inventory of the player based on his account token
    /// </summary>
    [Route("inventory")]
    [HttpGet]
    public async Task<List<PlayerState.Client.Model.Item>> GetInventory()
    {
        GoogleUser user = await googletokenService.GetUserWithToken(this);
        var mcUser = await connectApi.ConnectUserUserIdGetAsync(user.Id.ToString());
        var uuid = mcUser.Accounts.Where(a=>a.Verified).OrderByDescending(a => a.LastRequestedAt).FirstOrDefault()?.AccountUuid;
        if (uuid == null)
        {
            throw new CoflnetException("no_verified_account", "You need to verify on a minecraft account with our mod first");
        }
        var name = await playerNameApi.PlayerNameNameUuidGetAsync(uuid);
        return await playerStateApi.PlayerStatePlayerIdLastChestGetAsync(name);
    }

}
