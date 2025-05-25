using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.PlayerName.Client.Api;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.Trade.Client.Api;
using Coflnet.Sky.Trade.Client.Model;
using HashidsNet;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Controller;

/// <summary>
/// Things related to item trading
/// </summary>
[ApiController]
[Route("api")]
public class TradeController : ControllerBase
{
    private readonly ILogger<TradeController> logger;
    private readonly IPlayerStateApi playerStateApi;
    private readonly GoogletokenService googletokenService;
    private readonly McConnect.Api.IConnectApi connectApi;
    private readonly IPlayerNameApi playerNameApi;
    private readonly ITradeApi tradeApi;
    private readonly Coflnet.Sky.Items.Client.Api.IItemsApi itemsApi;
    private readonly IMapper mapper;
    Hashids hashids = new Hashids("CoflnetSkyTrades", 8);

    public TradeController(ILogger<TradeController> logger,
                           IPlayerStateApi playerStateApi,
                           GoogletokenService googletokenService,
                           McConnect.Api.IConnectApi connectApi,
                           IPlayerNameApi playerNameApi,
                           ITradeApi tradeApi,
                           IMapper mapper,
                           Coflnet.Sky.Items.Client.Api.IItemsApi itemsApi)
    {
        this.logger = logger;
        this.playerStateApi = playerStateApi;
        this.googletokenService = googletokenService;
        this.connectApi = connectApi;
        this.playerNameApi = playerNameApi;
        this.tradeApi = tradeApi;
        this.mapper = mapper;
        this.itemsApi = itemsApi;
    }

    /// <summary>
    /// Returns the last known inventory of the player based on his account token
    /// </summary>
    [Route("inventory")]
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
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
        var mapped = mapper.Map<List<TradeRequest>, List<TradeRequestDTO>>(trades);
        GoogleUser user = await googletokenService.GetUserWithToken(this);
        string uuid = await GetPlayerUuid(user);
        foreach (var trade in mapped)
        {
            trade.PlayerUuid = uuid;
            trade.UserId = user.Id.ToString();
            foreach (var item in trade.WantedItems)
            {
                if (item.Tag == null || item.Tag == "SKYBLOCK_COIN")
                    continue;
                if (DiHandler.GetService<ItemDetails>().GetItemIdForTag(item.Tag) == 0)
                    throw new CoflnetException("invalid_item", $"The item tag `{item.Tag}` is invalid");
            }
        }
        await tradeApi.ApiTradesInsertTradesPostAsync(mapped);
    }

    /// <summary>
    /// Returns trades based on the given filters
    /// </summary>
    [Route("trades")]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any, NoStore = false, VaryByQueryKeys = new string[] { "*" })]
    [HttpGet]
    public async Task<List<TradeRequest>> GetTrades(Dictionary<string, string>? filters = null, int page = 0, int pageSize = 10)
    {
        var requests = await tradeApi.ApiTradesGetTradesByFiltersGetAsync(filters, pageSize, page);
        var mapped = mapper.Map<List<TradeRequest>>(requests);
        await FillDataForDisplay(mapped);
        return mapped;
    }

    private async Task FillDataForDisplay(List<TradeRequest> mapped)
    {
        var nameTask = playerNameApi.PlayerNameNamesBatchPostAsync(mapped.Select(t => t.PlayerUuid).ToList());
        var itemNames = await itemsApi.ItemNamesGetAsync();
        var names = await nameTask;
        foreach (var trade in mapped)
        {
            trade.PlayerName = names.FirstOrDefault(n => n.Key == trade.PlayerUuid).Value;
            foreach (var wanted in trade.WantedItems)
            {
                wanted.ItemName = itemNames.FirstOrDefault(i => i.Tag == wanted.Tag)?.Name;
            }
        }
    }

    /// <summary>
    /// Deletes the trade with the given id
    /// </summary>
    [Route("trades/{id}")]
    [HttpDelete]
    public async Task DeleteTrade(string id)
    {
        var numericId = hashids.DecodeLong(id).FirstOrDefault();
        Console.WriteLine($"Deleting trade {numericId}");
        GoogleUser user = await googletokenService.GetUserWithToken(this);
        await tradeApi.ApiTradesTradeUserIdIdDeleteAsync(user.Id.ToString(), numericId);
    }

    /// <summary>
    /// Trades of current user
    /// </summary>
    [Route("trades/own")]
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<List<TradeRequest>> GetMyTrades()
    {
        GoogleUser user = await googletokenService.GetUserWithToken(this);
        var requests = await tradeApi.ApiTradesTradesByUserGetAsync(user.Id.ToString());
        var mapped = mapper.Map<List<TradeRequest>>(requests);
        await FillDataForDisplay(mapped);
        return mapped;
    }

    private async Task<string> GetPlayerUuid(GoogleUser user)
    {
        var mcUser = await connectApi.ConnectUserUserIdGetAsync(user.Id.ToString());
        var uuid = mcUser.Accounts.Where(a => a.Verified).OrderByDescending(a => a.LastRequestedAt).FirstOrDefault()?.AccountUuid;
        if (uuid == null)
        {
            throw new CoflnetException("no_verified_account", "You need to verify on a minecraft account with our mod first");
        }

        return uuid;
    }
}
