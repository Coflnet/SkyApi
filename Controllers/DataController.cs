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
    static Counter profitFound = Metrics.CreateCounter("sky_api_pf_profit_found", "How much coins of profit were found");
    static Counter flipsFound = Metrics.CreateCounter("sky_api_pf_flips_found", "How many flips were found");
    static Counter namesChecked = Metrics.CreateCounter("sky_api_pf_names_checked", "How many names were checked");
    static Counter namesCheckAttempts = Metrics.CreateCounter("sky_api_pf_names_check_attempts", "How many names were checked");
    static Counter newAuctionsFound = Metrics.CreateCounter("sky_api_pf_new_auctions_found", "How many new auctions were found");

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
        namesCheckAttempts.Inc();
        var uuid = await playerNameService.GetUuid(name);
        var auctionsRequest = new RestRequest($"Proxy/hypixel/ah/player/{uuid}?maxAgeSeconds=1209600", Method.Get);
        var response = await proxyClient.ExecuteAsync(auctionsRequest);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            throw new Exception($"Failed to get auctions for {uuid}({name}) got {response.StatusCode} {response.Content}");
        }
        var allAuctions = JsonConvert.DeserializeObject<SaveAuction[]>(response.Content);
        var auctions = allAuctions.Where(a => a.Start > DateTime.UtcNow.AddSeconds(-40)).ToList();
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
            newAuctionsFound.Inc();
        }
        namesChecked.Inc();
        Console.WriteLine($"Found {auctions.Count} new auctions for {name}({uuid}) with a total profit of {profitSum}");
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
        return await Task.WhenAll(name.Select(UploadProxied).Distinct().Take(24));
    }
}
