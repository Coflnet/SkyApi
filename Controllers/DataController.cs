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
    ModDescriptionService modDescriptionService;
    static Counter profitFound = Metrics.CreateCounter("sky_api_profit_found", "How much coins of profit were found");
    static Counter flipsFound = Metrics.CreateCounter("sky_api_flips_found", "How many flips were found");

    public DataController(IConfiguration config, ISniperApi sniperApi, PlayerNameService playerNameService, ModDescriptionService modDescriptionService)
    {
        this.config = config;
        this.sniperApi = sniperApi;
        this.proxyClient = new RestClient(config["Proxy_Base_Url"]);
        this.playerNameService = playerNameService;
        this.modDescriptionService = modDescriptionService;
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
        return "received " + id;
    }
    /// <summary>
    /// Accepts player name based auction hints
    /// </summary>
    /// <returns></returns>
    [Route("playerName")]
    [HttpPost]
    public async Task<(int, long)> UploadProxied(string name)
    {
        var uuid = await playerNameService.GetUuid(name);
        var auctionsRequest = new RestRequest($"Proxy/hypixel/ah/player/{uuid}?maxAgeSeconds=1", Method.Get);
        var response = await proxyClient.ExecuteAsync(auctionsRequest);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            throw new Exception($"Failed to get auctions for {uuid} got {response.StatusCode} {response.Content}");
        }
        var auctions = JsonConvert.DeserializeObject<SaveAuction[]>(response.Content).Where(a => a.Start > DateTime.Now.AddSeconds(-20)).ToList();
        var prices = await modDescriptionService.GetPrices(auctions);
        var profitSum = 0L;
        for (int i = 0; i < auctions.Count; i++)
        {
            var profit = prices[i].Lbin.Price - auctions[i].StartingBid;
            Console.WriteLine($"Auction {auctions[i].Uuid} has a median price of {prices[i].Median} lbin {prices[i].Lbin.Price}, cost {auctions[i].StartingBid} profit {profit}");
            if (profit > 0)
            {
                profitFound.Inc(profit);
                flipsFound.Inc();
                profitSum += profit;
            }
        }
        return (auctions.Count, profitSum);
    }
    /// <summary>
    /// Accepts player name based auction hints
    /// </summary>
    /// <returns></returns>
    [Route("playerNames")]
    [HttpPost]
    public async Task<IEnumerable<(int auctions, long profit)>> UploadProxied([FromBody] IEnumerable<string> name)
    {
        if(name.Count() > 24)
            throw new CoflnetException("to_many", "Too many names at once, max 24");
        return await Task.WhenAll(name.Select(UploadProxied));
    }
}
