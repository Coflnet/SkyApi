using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerName.Client.Api;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.Trade.Client.Api;
using Coflnet.Sky.Trade.Client.Model;
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
    private readonly ITradeApi tradeApi;
    private readonly IMapper mapper;

    public TradeController(ILogger<TradeController> logger,
                           IPlayerStateApi playerStateApi,
                           GoogletokenService googletokenService,
                           McConnect.Api.IConnectApi connectApi,
                           IPlayerNameApi playerNameApi,
                           ITradeApi tradeApi,
                           IMapper mapper)
    {
        this.logger = logger;
        this.playerStateApi = playerStateApi;
        this.googletokenService = googletokenService;
        this.connectApi = connectApi;
        this.playerNameApi = playerNameApi;
        this.tradeApi = tradeApi;
        this.mapper = mapper;
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
        var uuid = mcUser.Accounts.Where(a => a.Verified).OrderByDescending(a => a.LastRequestedAt).FirstOrDefault()?.AccountUuid;
        if (uuid == null)
        {
            throw new CoflnetException("no_verified_account", "You need to verify on a minecraft account with our mod first");
        }
        var name = await playerNameApi.PlayerNameNameUuidGetAsync(uuid);
        return await playerStateApi.PlayerStatePlayerIdLastChestGetAsync(name.Trim('"'));
    }

    /// <summary>
    /// Creates a new trade request
    /// </summary>
    [Route("trades")]
    [HttpPost]
    public async Task CreateTrade([FromBody] List<TradeRequest> trades)
    {
        var mapped = mapper.Map<List<TradeRequest>,List<TradeRequestDTO>>(trades);
        string uuid = await GetPlayerUuid();
        foreach (var trade in mapped)
        {
            trade.PlayerUuid = uuid;
        }
        await tradeApi.ApiTradesInsertTradesPostAsync(mapped);
    }

    /// <summary>
    /// Returns trades based on the given filters
    /// </summary>
    [Route("trades")]
    [HttpGet]
    public async Task<List<TradeRequest>> GetTrades(Dictionary<string, string>? filters = null)
    {
        var requests = await tradeApi.ApiTradesGetTradesByFiltersGetAsync(filters);
        var mapped = mapper.Map<List<TradeRequest>>(requests);
        var names = playerNameApi.PlayerNameNamesBatchPostAsync(mapped.Select(t => t.PlayerUuid).ToList());
        foreach (var trade in mapped)
        {
            trade.PlayerName = names.Result.FirstOrDefault(n => n.Key == trade.PlayerUuid).Value;
        }
        return mapped;
    }

    private async Task<string> GetPlayerUuid()
    {
        GoogleUser user = await googletokenService.GetUserWithToken(this);
        var mcUser = await connectApi.ConnectUserUserIdGetAsync(user.Id.ToString());
        var uuid = mcUser.Accounts.Where(a => a.Verified).OrderByDescending(a => a.LastRequestedAt).FirstOrDefault()?.AccountUuid;
        if (uuid == null)
        {
            throw new CoflnetException("no_verified_account", "You need to verify on a minecraft account with our mod first");
        }

        return uuid;
    }
}
