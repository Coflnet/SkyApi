using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Lowball;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Controller;

/// <summary>
/// Lowball trade offers
/// </summary>
[ApiController]
[Route("api/lowball")]
public class LowballController : ControllerBase
{
    private readonly ILogger<LowballController> logger;
    private readonly GoogletokenService googletokenService;
    private readonly McConnect.Api.IConnectApi connectApi;
    private readonly HttpClient httpClient;

    public LowballController(
        ILogger<LowballController> logger,
        GoogletokenService googletokenService,
        McConnect.Api.IConnectApi connectApi,
        IHttpClientFactory httpClientFactory)
    {
        this.logger = logger;
        this.googletokenService = googletokenService;
        this.connectApi = connectApi;
        this.httpClient = httpClientFactory.CreateClient("ModCommands");
    }

    /// <summary>
    /// Get lowball offers for the authenticated user
    /// </summary>
    [HttpGet("own")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<ActionResult<List<LowballOfferResponse>>> GetOwnOffers([FromQuery] DateTimeOffset? before = null, [FromQuery] int limit = 20)
    {
        var user = await googletokenService.GetUserWithToken(this);
        var uuid = await GetPlayerUuid(user);
        var url = $"api/lowball/user/{Uri.EscapeDataString(uuid)}?limit={limit}";
        if (before.HasValue)
            url += $"&before={Uri.EscapeDataString(before.Value.ToString("o"))}";
        var response = await httpClient.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, payload);

        return Ok(DeserializeOffers(payload));
    }

    /// <summary>
    /// Get lowball offers by item tag
    /// </summary>
    [HttpGet("item/{itemTag}")]
    [ResponseCache(Duration = 30, Location = ResponseCacheLocation.Any, VaryByQueryKeys = new[] { "*" })]
    public async Task<ActionResult<List<LowballOfferResponse>>> GetItemOffers(string itemTag, [FromQuery] DateTimeOffset? before = null, [FromQuery] int limit = 20)
    {
        var url = $"api/lowball/item/{Uri.EscapeDataString(itemTag)}?limit={limit}";
        if (before.HasValue)
            url += $"&before={Uri.EscapeDataString(before.Value.ToString("o"))}";
        var response = await httpClient.GetAsync(url);
        var payload = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            return StatusCode((int)response.StatusCode, payload);

        return Ok(DeserializeOffers(payload));
    }

    /// <summary>
    /// Delete the authenticated user's lowball offer
    /// </summary>
    [HttpDelete("offer/{offerId}")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<ActionResult> DeleteOffer(Guid offerId)
    {
        var user = await googletokenService.GetUserWithToken(this);
        var uuid = await GetPlayerUuid(user);
        var response = await httpClient.DeleteAsync($"api/lowball/user/{Uri.EscapeDataString(uuid)}/offer/{offerId}");
        return StatusCode((int)response.StatusCode);
    }

    private async Task<string> GetPlayerUuid(GoogleUser user)
    {
        var mcUser = await connectApi.ConnectUserUserIdGetAsync(user.Id.ToString());
        var uuid = mcUser.Accounts.Where(a => a.Verified).OrderByDescending(a => a.LastRequestedAt).FirstOrDefault()?.AccountUuid;
        if (uuid == null)
            throw new CoflnetException("no_verified_account", "You need to verify on a minecraft account with our mod first");
        return uuid;
    }

    private static List<LowballOfferResponse> DeserializeOffers(string payload)
    {
        return JsonConvert.DeserializeObject<List<LowballOfferResponse>>(payload) ?? new List<LowballOfferResponse>();
    }
}
