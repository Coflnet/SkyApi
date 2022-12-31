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
    ModDescriptionService modDescriptionService;
    static Counter profitFound = Metrics.CreateCounter("sky_api_pf_profit_found", "How much coins of profit were found");
    static Counter flipsFound = Metrics.CreateCounter("sky_api_pf_flips_found", "How many flips were found");
    static Counter namesChecked = Metrics.CreateCounter("sky_api_pf_names_checked", "How many names were checked");
    static Counter namesCheckAttempts = Metrics.CreateCounter("sky_api_pf_names_check_attempts", "How many names were checked");
    static Counter newAuctionsFound = Metrics.CreateCounter("sky_api_pf_new_auctions_found", "How many new auctions were found");

    /// <summary>
    /// Creates a new instance of <see cref="DataController"/>
    /// </summary>
    public DataController(IConfiguration config, ISniperApi sniperApi, PlayerNameService playerNameService, ModDescriptionService modDescriptionService, FlipTrackingService ft)
    {
        this.config = config;
        this.sniperApi = sniperApi;
        this.proxyClient = new RestClient(config["Proxy_Base_Url"]);
        this.playerNameService = playerNameService;
        this.modDescriptionService = modDescriptionService;
        this.ft = ft;
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
    public async Task<(int, long)> LoadPlayerAuctions(string name)
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
        var auctionsRequest = new RestRequest($"Proxy/hypixel/ah/player/{uuid}?maxAgeSeconds=1209600", Method.Get);
        var response = await proxyClient.ExecuteAsync(auctionsRequest);
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            throw new Exception($"Failed to get auctions for {uuid}({name}) got {response.StatusCode} {response.Content}");
        }
        var allAuctions = JsonConvert.DeserializeObject<SaveAuction[]>(response.Content);
        var auctions = allAuctions.Where(a => a.Start > DateTime.UtcNow.AddSeconds(-19)).ToList();
        var prices = await modDescriptionService.GetPrices(auctions);
        var profitSum = 0L;
        for (int i = 0; i < auctions.Count; i++)
        {
            var target = Math.Min(prices[i].Lbin.Price, prices[i].Median);
            var profit = target - auctions[i].StartingBid;
            Console.WriteLine($"Auction {auctions[i].Uuid} has a median price of {prices[i].Median} lbin {prices[i].Lbin.Price}, cost {auctions[i].StartingBid} profit {profit}");
            if (profit > 200_000)
            {
                profitFound.Inc(profit);
                flipsFound.Inc();
                profitSum += profit;
                await ft.NewFlip(new LowPricedAuction()
                {
                    Auction = auctions[i],
                    Finder = LowPricedAuction.FinderType.STONKS,
                    TargetPrice = target
                }, DateTime.UtcNow);
            }
            newAuctionsFound.Inc();

        }
        namesChecked.Inc();
        Console.WriteLine($"Found {auctions.Count} new auctions for {name}({uuid}) with a total profit of {profitSum} {allAuctions.Length} auctions in total");
        return (auctions.Count, profitSum);
    }
    /// <summary>
    /// Accepts player name based auction hints
    /// </summary>
    /// <returns></returns>
    [Route("playerNames")]
    [HttpPost]
    public async Task<IEnumerable<(int auctions, long profit)>> LoadPlayerAuctions([FromBody] IEnumerable<string> name)
    {
        return await Task.WhenAll(name.Select(LoadPlayerAuctions).Distinct().Take(24));
    }
}
