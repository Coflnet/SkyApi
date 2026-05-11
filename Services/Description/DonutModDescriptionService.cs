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
    private const string StrongMatchHighlightColor = "2ea043";
    private const string WeakMatchHighlightColor = "d29922";
    private const string UnmatchedHighlightColor = "f85149";
    private const string UnavailableHighlightColor = "8b949e";

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

        var auctionPage = DonutInventoryItemParser.ParseAuctionPageNumber(inventory.ChestName);
        LogParsedItems(inventory.ChestName, auctionPage, items);

        DonutItemPriceResponse? response;
        try
        {
            response = await RequestPrices(items, auctionPage);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to get Donut item prices for mod description request");
            return result;
        }

        if (response?.Items == null || response.Items.Count == 0)
        {
            logger.LogWarning("DonutApi returned no price items for chest {ChestName} page {AuctionPage}", inventory.ChestName, auctionPage);
            return result;
        }

        if (response.Items.Count != items.Count)
        {
            logger.LogWarning(
                "DonutApi response item count mismatch for chest {ChestName} page {AuctionPage}: requested {RequestedCount}, received {ReceivedCount}",
                inventory.ChestName,
                auctionPage,
                items.Count,
                response.Items.Count);
        }

        LogPriceResponse(items, response.Items);

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

    private async Task<DonutItemPriceResponse?> RequestPrices(List<DonutItemPriceRequestItem> items, int? auctionPage)
    {
        using var client = httpClientFactory.CreateClient("DonutApi");
        logger.LogInformation(
            "Requesting Donut mod prices from {DonutApiBaseUrl} for page {AuctionPage} with {ItemCount} items",
            client.BaseAddress,
            auctionPage,
            items.Count);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/donut/items/prices")
        {
            Content = new StringContent(JsonConvert.SerializeObject(new DonutItemPriceRequest
            {
                Items = items,
                AuctionPage = auctionPage
            }), Encoding.UTF8, "application/json")
        };
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            logger.LogWarning("DonutApi returned {StatusCode} for mod price request: {Body}", response.StatusCode, errorBody);
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
            return itemParser.Parse(inventory?.FullInventoryNbt, inventory?.ChestName);
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
        var matchIndicator = GetMatchIndicator(priceInfo);

        if (!hasMapInfo && !hasPriceInfo && !hasMatchedAuction && matchIndicator == null)
            return EmptyMods;

        var modifications = new List<DescModification>();

        if (matchIndicator != null)
            modifications.Add(new(DescModification.ModType.HIGHLIGHT, -1, matchIndicator.HighlightColor));

        modifications.Add(new(string.Empty));

        if (matchIndicator != null)
            modifications.Add(new($"{McColorCodes.GRAY}Donut match: {matchIndicator.ColorCode}{matchIndicator.Label}"));

        if (item.MapId.HasValue)
        {
            modifications.Add(new($"{McColorCodes.GRAY}Map ID: {McColorCodes.YELLOW}{item.MapId.Value}"));
            if (priceInfo?.HasMapContent == true)
                modifications.Add(new($"{McColorCodes.GRAY}Map art: {McColorCodes.GREEN}saved"));
        }

        if (priceInfo?.MatchedAuction != null)
        {
            modifications.Add(new($"{McColorCodes.GRAY}Listing: {McColorCodes.GOLD}{ModDescriptionService.FormatPriceShort((double)priceInfo.MatchedAuction.Price)}"));
            var sellerDisplay = GetSellerDisplay(priceInfo.MatchedAuction);
            if (!string.IsNullOrWhiteSpace(sellerDisplay))
                modifications.Add(new($"{McColorCodes.GRAY}Seller: {McColorCodes.YELLOW}{sellerDisplay}"));
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

    private void LogParsedItems(string? chestName, int? auctionPage, IReadOnlyList<DonutItemPriceRequestItem> items)
    {
        logger.LogInformation(
            "Parsed {ItemCount} Donut auction candidates from chest {ChestName} page {AuctionPage}",
            items.Count,
            chestName,
            auctionPage);

        foreach (var item in items)
        {
            logger.LogInformation(
                "Donut candidate slot={Slot} itemId={ItemId} name={DisplayName} count={Count} visiblePrice={VisiblePrice} sellerHint={SellerUuidHint} mapId={MapId} copyId={CopyId} extraData={ExtraData}",
                item.Slot,
                item.ItemId,
                item.DisplayName,
                item.Count,
                item.VisiblePrice,
                item.SellerUuidHint,
                item.MapId,
                item.CopyId,
                item.ExtraData == null || item.ExtraData.Count == 0 ? string.Empty : JsonConvert.SerializeObject(item.ExtraData));
        }
    }

    private void LogPriceResponse(IReadOnlyList<DonutItemPriceRequestItem> items, IReadOnlyList<DonutItemPriceInfo> responseItems)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var responseItem = index < responseItems.Count ? responseItems[index] : null;
            logger.LogInformation(
                "Donut result slot={Slot} itemId={ItemId} matched={Matched} matchKind={MatchKind} listingPrice={ListingPrice} seller={SellerName} median={MedianPrice} priceSource={PriceSource} exactAttributeSales={ExactAttributeTransactionCount} hasMapContent={HasMapContent} error={Error}",
                item.Slot,
                item.ItemId,
                responseItem?.MatchedAuction != null,
                responseItem?.MatchedAuction?.MatchKind,
                responseItem?.MatchedAuction?.Price,
                responseItem?.MatchedAuction?.SellerName,
                responseItem?.MedianPrice,
                responseItem?.PriceSource,
                responseItem?.ExactAttributeTransactionCount,
                responseItem?.HasMapContent,
                responseItem?.Error);
        }
    }

    private static MatchIndicator? GetMatchIndicator(DonutItemPriceInfo? priceInfo)
    {
        if (priceInfo == null)
            return new MatchIndicator("unavailable", McColorCodes.GRAY, UnavailableHighlightColor);

        if (priceInfo.MatchedAuction != null)
        {
            var formattedKind = FormatMatchKind(priceInfo.MatchedAuction.MatchKind);
            if (IsWeakMatch(priceInfo.MatchedAuction.MatchKind))
                return new MatchIndicator($"matched ({formattedKind})", McColorCodes.GOLD, WeakMatchHighlightColor);

            return new MatchIndicator($"matched ({formattedKind})", McColorCodes.GREEN, StrongMatchHighlightColor);
        }

        return new MatchIndicator("no match", McColorCodes.RED, UnmatchedHighlightColor);
    }

    private static bool IsWeakMatch(string? matchKind)
    {
        if (string.IsNullOrWhiteSpace(matchKind))
            return false;

        return matchKind.EndsWith("-order", StringComparison.OrdinalIgnoreCase)
            || string.Equals(matchKind, "heuristic", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatMatchKind(string? matchKind)
    {
        if (string.IsNullOrWhiteSpace(matchKind))
            return "matched";

        return matchKind.Replace('-', ' ');
    }

    private static string? GetSellerDisplay(MatchedAuctionInfo matchedAuction)
    {
        if (!string.IsNullOrWhiteSpace(matchedAuction.SellerName) && !string.IsNullOrWhiteSpace(matchedAuction.SellerUuid))
            return $"{matchedAuction.SellerName} {McColorCodes.DARK_GRAY}({matchedAuction.SellerUuid}){McColorCodes.YELLOW}";

        if (!string.IsNullOrWhiteSpace(matchedAuction.SellerName))
            return matchedAuction.SellerName;

        if (!string.IsNullOrWhiteSpace(matchedAuction.SellerUuid))
            return matchedAuction.SellerUuid;

        return null;
    }

    private sealed class DonutItemPriceRequest
    {
        [JsonProperty("items")]
        public List<DonutItemPriceRequestItem> Items { get; init; } = new();

        [JsonProperty("auctionPage")]
        public int? AuctionPage { get; init; }
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

        [JsonProperty("exactAttributeTransactionCount")]
        public int ExactAttributeTransactionCount { get; init; }

        [JsonProperty("volume")]
        public long Volume { get; init; }

        [JsonProperty("priceSource")]
        public string? PriceSource { get; init; }

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

        [JsonProperty("sellerUuid")]
        public string SellerUuid { get; init; } = string.Empty;

        [JsonProperty("sellerName")]
        public string? SellerName { get; init; }

        [JsonProperty("price")]
        public decimal Price { get; init; }

        [JsonProperty("matchKind")]
        public string MatchKind { get; init; } = string.Empty;
    }

    private sealed record MatchIndicator(string Label, string ColorCode, string HighlightColor);
}