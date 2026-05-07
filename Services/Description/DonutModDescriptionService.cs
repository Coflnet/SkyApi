using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core;
using fNbt.Tags;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

#nullable enable

namespace Coflnet.Sky.Api.Services;

public class DonutModDescriptionService
{
    private static readonly DescModification[] EmptyMods = Array.Empty<DescModification>();

    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<DonutModDescriptionService> logger;
    private readonly DonutInventoryItemParser itemParser = new();

    public DonutModDescriptionService(IHttpClientFactory httpClientFactory, ILogger<DonutModDescriptionService> logger)
    {
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
    }

    public async Task<IEnumerable<string[]>> GetDescriptions(InventoryDataWithSettings inventory)
    {
        var modifications = await GetModifications(inventory);
        return modifications.Select(mods => mods.Select(m => m.Value).ToArray());
    }

    public async Task<IEnumerable<IEnumerable<DescModification>>> GetModifications(InventoryDataWithSettings inventory)
    {
        var slots = ParseInventory(inventory).ToList();
        if (slots.Count == 0)
            return Array.Empty<IEnumerable<DescModification>>();

        var result = CreateEmptyResult(slots.Count);
        var items = slots.Where(slot => slot.Item != null).Select(slot => slot.Item!).ToList();
        if (items.Count == 0)
            return result;

        DonutItemPriceResponse? response;
        try
        {
            response = await RequestPrices(items);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to get Donut item prices for mod description request");
            return result;
        }

        if (response?.Items == null || response.Items.Count == 0)
            return result;

        var priceIndex = 0;
        for (var slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            if (slots[slotIndex].Item == null)
                continue;
            var priceInfo = priceIndex < response.Items.Count ? response.Items[priceIndex++] : null;
            result[slotIndex] = BuildModifications(slots[slotIndex].Item!, priceInfo);
        }

        return result;
    }

    private async Task<DonutItemPriceResponse?> RequestPrices(List<DonutItemPriceRequestItem> items)
    {
        using var client = httpClientFactory.CreateClient("DonutApi");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/donut/items/prices")
        {
            Content = new StringContent(JsonConvert.SerializeObject(new DonutItemPriceRequest { Items = items }), Encoding.UTF8, "application/json")
        };
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("DonutApi returned {StatusCode} for mod price request", response.StatusCode);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<DonutItemPriceResponse>(body);
    }

    private static List<IEnumerable<DescModification>> CreateEmptyResult(int slotCount)
    {
        var result = new List<IEnumerable<DescModification>>(slotCount);
        for (var i = 0; i < slotCount; i++)
        {
            result.Add(EmptyMods);
        }
        return result;
    }

    private IEnumerable<ParsedInventorySlot> ParseInventory(InventoryData inventory)
    {
        try
        {
            return itemParser.Parse(inventory?.FullInventoryNbt);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to parse Donut inventory NBT for chest {ChestName}", inventory?.ChestName);
            return Array.Empty<ParsedInventorySlot>();
        }
    }

    private static IEnumerable<DescModification> BuildModifications(DonutItemPriceRequestItem item, DonutItemPriceInfo? priceInfo)
    {
        var hasMapInfo = item.MapId.HasValue;
        var hasMatchedAuction = priceInfo?.MatchedAuction != null;
        var hasPriceInfo = priceInfo != null
            && string.IsNullOrWhiteSpace(priceInfo.Error)
            && priceInfo.MedianPrice > 0;

        if (!hasMapInfo && !hasPriceInfo && !hasMatchedAuction)
            return EmptyMods;

        var modifications = new List<DescModification>
        {
            new(string.Empty)
        };

        if (item.MapId.HasValue)
        {
            modifications.Add(new($"{McColorCodes.GRAY}Map ID: {McColorCodes.YELLOW}{item.MapId.Value}"));
            if (priceInfo?.HasMapContent == true)
                modifications.Add(new($"{McColorCodes.GRAY}Map art: {McColorCodes.GREEN}saved"));
        }

        if (priceInfo?.MatchedAuction != null)
        {
            modifications.Add(new($"{McColorCodes.GRAY}Listing: {McColorCodes.GOLD}{ModDescriptionService.FormatPriceShort((double)priceInfo.MatchedAuction.Price)}"));
            if (!string.IsNullOrWhiteSpace(priceInfo.MatchedAuction.SellerName))
                modifications.Add(new($"{McColorCodes.GRAY}Seller: {McColorCodes.YELLOW}{priceInfo.MatchedAuction.SellerName}"));
        }

        if (!hasPriceInfo)
            return modifications;

        modifications.Add(new($"{McColorCodes.GRAY}Donut median: {McColorCodes.GOLD}{ModDescriptionService.FormatPriceShort((double)priceInfo!.MedianPrice)}"));

        if (item.Count > 1)
        {
            modifications.Add(new($"{McColorCodes.GRAY}Per item: {McColorCodes.YELLOW}{ModDescriptionService.FormatPriceShort((double)priceInfo.MedianPrice / item.Count)}"));
        }
        if (priceInfo.MinPrice > 0 || priceInfo.MaxPrice > 0)
        {
            modifications.Add(new($"{McColorCodes.GRAY}Range: {McColorCodes.YELLOW}{ModDescriptionService.FormatPriceShort((double)priceInfo.MinPrice)}{McColorCodes.GRAY}-{McColorCodes.YELLOW}{ModDescriptionService.FormatPriceShort((double)priceInfo.MaxPrice)}"));
        }
        if (priceInfo.Volume > 0)
        {
            modifications.Add(new($"{McColorCodes.GRAY}Vol: {McColorCodes.YELLOW}{ModDescriptionService.FormatPriceShort(priceInfo.Volume)}"));
        }
        if (priceInfo.TransactionCount > 0)
        {
            modifications.Add(new($"{McColorCodes.GRAY}Sales: {McColorCodes.YELLOW}{ModDescriptionService.FormatPriceShort(priceInfo.TransactionCount)}"));
        }

        return modifications;
    }

    private sealed class DonutItemPriceRequest
    {
        [JsonProperty("items")]
        public List<DonutItemPriceRequestItem> Items { get; init; } = new();
    }

    private sealed class DonutItemPriceResponse
    {
        [JsonProperty("items")]
        public List<DonutItemPriceInfo> Items { get; init; } = new();
    }

    private sealed class DonutItemPriceInfo
    {
        [JsonProperty("medianPrice")]
        public decimal MedianPrice { get; init; }

        [JsonProperty("minPrice")]
        public decimal MinPrice { get; init; }

        [JsonProperty("maxPrice")]
        public decimal MaxPrice { get; init; }

        [JsonProperty("transactionCount")]
        public int TransactionCount { get; init; }

        [JsonProperty("volume")]
        public long Volume { get; init; }

        [JsonProperty("hasMapContent")]
        public bool HasMapContent { get; init; }

        [JsonProperty("matchedAuction")]
        public MatchedAuctionInfo? MatchedAuction { get; init; }

        [JsonProperty("error")]
        public string? Error { get; init; }
    }

    private sealed class MatchedAuctionInfo
    {
        [JsonProperty("auctionId")]
        public string AuctionId { get; init; } = string.Empty;

        [JsonProperty("sellerName")]
        public string? SellerName { get; init; }

        [JsonProperty("price")]
        public decimal Price { get; init; }
    }
}