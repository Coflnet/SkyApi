global using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RestSharp;
using Coflnet.Sky.Sniper.Client.Api;
using Microsoft.Extensions.Configuration;
using Coflnet.Sky.PlayerName;
using Newtonsoft.Json;
using Coflnet.Sky.Core;
using Coflnet.Sky.Api.Services;
using Prometheus;
using Coflnet.Sky.Commands;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Controller;
/// <summary>
/// Endpoints for collecting data
/// </summary>
[ApiController]
[Route("api/data")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class DataController : ControllerBase
{
    IConfiguration config;
    ISniperApi sniperApi;
    RestClient proxyClient;
    PlayerNameService playerNameService;
    FlipTrackingService ft;
    ILogger<DataController> logger;
    ModDescriptionService modDescriptionService;
    static Counter profitFound = Metrics.CreateCounter("sky_api_pf_profit_found", "How much coins of profit were found");
    static Counter flipsFound = Metrics.CreateCounter("sky_api_pf_flips_found", "How many flips were found");
    static Counter namesChecked = Metrics.CreateCounter("sky_api_pf_names_checked", "How many names were checked");
    static Counter namesCheckAttempts = Metrics.CreateCounter("sky_api_pf_names_check_attempts", "How many names were checked");
    static Counter newAuctionsFound = Metrics.CreateCounter("sky_api_pf_new_auctions_found", "How many new auctions were found");

    /// <summary>
    /// Creates a new instance of <see cref="DataController"/>
    /// </summary>
    public DataController(IConfiguration config, ISniperApi sniperApi, PlayerNameService playerNameService, ModDescriptionService modDescriptionService, FlipTrackingService ft, ILogger<DataController> logger)
    {
        this.config = config;
        this.sniperApi = sniperApi;
        this.proxyClient = new RestClient(config["Proxy_Base_Url"]);
        this.playerNameService = playerNameService;
        this.modDescriptionService = modDescriptionService;
        this.ft = ft;
        this.logger = logger;
    }

    /// <summary>
    /// Endpoint to upload proxied data
    /// </summary>
    /// <returns></returns>
    [Route("proxy")]
    [HttpPost]
    public string UploadProxied()
    {
        Request.Headers.TryGetValue("X-Request-Id", out var id);
        // get body as string
        var body = new System.IO.StreamReader(Request.Body).ReadToEnd();
        logger.LogInformation($"Received proxy data {id} {body.Truncate(100)}");
        return "received " + id;
    }
    /// <summary>
    /// Accepts player name based auction hints
    /// </summary>
    /// <returns></returns>
    [Route("playerName")]
    [HttpPost]
    public async Task<(int, long)> LoadPlayerAuctions(string name, string source = "anonym")
    {
        namesCheckAttempts.Inc();
        string uuid = null;
        try
        {
            uuid = await playerNameService.GetUuid(name);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to get uuid for {name} {e}");
            return (0, 0);
        }
        var auctionsRequest = new RestRequest($"Base/ah/{uuid}?hintSource={source}", Method.Post);
        var response = await proxyClient.ExecuteAsync(auctionsRequest);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            throw new Exception($"Failed to get auctions for {uuid}({name}) got {response.StatusCode} {response.Content}");
        }
        namesChecked.Inc();
        Console.WriteLine($"Checking new auctions for {name}({uuid})");
        return (1, Random.Shared.Next(1000));
    }
    /// <summary>
    /// Accepts player name based auction hints
    /// </summary>
    /// <returns></returns>
    [Route("playerNames")]
    [HttpPost]
    public async Task<IEnumerable<(int auctions, long profit)>> LoadPlayerAuctions([FromBody] IEnumerable<string> name, string source = "anonym")
    {
        return await Task.WhenAll(name.Distinct().Select(n => LoadPlayerAuctions(n, source)).Take(24));
    }
}
