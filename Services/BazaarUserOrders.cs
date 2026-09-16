using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models;
using Coflnet.Sky.PlayerState.Client.Api;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;

namespace Coflnet.Sky.Api.Services;

/// <summary>Reads live fill state published by SkyBazaar; player state supplies customer history only.</summary>
public class BazaarUserOrders([FromKeyedServices("bazaar")] IConnectionMultiplexer redis, IPlayerStateApi playerState, IHttpClientFactory clients = null, ILogger<BazaarUserOrders> logger = null)
{
    private readonly ILogger<BazaarUserOrders> log = logger ?? NullLogger<BazaarUserOrders>.Instance;

    /// <summary>Returns only orders belonging to the authenticated user and Minecraft player.</summary>
    public async Task<List<BazaarPlayerOrder>> Get(string userId, string playerName)
    {
        using var span = BazaarTelemetry.Source.StartActivity("bazaar.orders.read");
        span?.SetTag("bazaar.user_id", userId);
        var snapshotTask = ReadSnapshot(userId);
        var historyTask = playerState.PlayerStatePlayerIdBazaarGetAsync(playerName);
        var snapshot = await snapshotTask;
        // Preserve the previous endpoint behavior when an older SkyBazaar has no snapshots yet.
        List<PlayerState.Client.Model.Offer> history;
        try { history = await historyTask; }
        catch (Exception e) when (snapshot != null && (e is HttpRequestException or TaskCanceledException
            or PlayerState.Client.Client.ApiException))
        {
            BazaarTelemetry.Reads.WithLabels("history_unavailable").Inc();
            log.LogWarning(e, "Customer history unavailable for Bazaar user {UserId}; using authoritative fills; TraceId {TraceId}", userId, Activity.Current?.TraceId.ToString());
            history = new();
        }
        if (snapshot == null)
        {
            BazaarTelemetry.Reads.WithLabels("history_fallback").Inc();
            log.LogDebug("Using estimated history for Bazaar user {UserId}; TraceId {TraceId}", userId, Activity.Current?.TraceId.ToString());
            return history.Select(o => new BazaarPlayerOrder {
                IsSell = o.IsSell, ItemTag = o.ItemTag, ItemName = o.ItemName,
                Amount = o.Amount, PricePerUnit = o.PricePerUnit, Created = o.Created, Customers = o.Customers,
                FilledAmount = Math.Min(o.Amount, o.Customers?.Sum(c => c.Amount) ?? 0), IsEstimate = true
            }).ToList();
        }
        span?.SetTag("bazaar.revision", (long?)snapshot["Revision"]);
        if (ActivityContext.TryParse((string)snapshot["TraceParent"], (string)snapshot["TraceState"], true, out var origin))
            span?.AddLink(new ActivityLink(origin));
        log.LogDebug("Returning Bazaar snapshot for {UserId}, revision {Revision}, origin {SnapshotTraceParent}; TraceId {TraceId}",
            userId, (long?)snapshot["Revision"], (string)snapshot["TraceParent"], Activity.Current?.TraceId.ToString());
        if ((string)snapshot["UserId"] != userId)
            throw new InvalidOperationException("Bazaar snapshot owner mismatch");
        var names = snapshot["ItemNames"]?.ToObject<Dictionary<string, string>>() ?? new();
        return snapshot["Orders"].ToObject<List<FillState>>()
            .Where(o => string.Equals(o.PlayerName, playerName, StringComparison.OrdinalIgnoreCase))
            .Select(order => new BazaarPlayerOrder {
                IsSell = order.IsSell, ItemTag = order.ItemId,
                ItemName = names.GetValueOrDefault(order.ItemId, order.ItemId),
                Amount = order.Amount, PricePerUnit = order.PricePerUnit, Created = order.Timestamp,
                FilledAmount = order.Filled, IsEstimate = order.IsEstimate != false,
                IsExpired = order.IsExpired, ClaimedAmount = order.Claimed,
                Customers = history?.FirstOrDefault(o => o.IsSell == order.IsSell
                    && o.ItemTag == order.ItemId && o.Created.Ticks / TimeSpan.TicksPerMillisecond
                        == order.Timestamp.Ticks / TimeSpan.TicksPerMillisecond)?.Customers ?? new()
            }).ToList();
    }

    private async Task<JObject> ReadSnapshot(string userId)
    {
        try
        {
            var cached = await redis.GetDatabase().StringGetAsync($"bazaar:orders:v1:{userId}");
            if (!cached.IsNullOrEmpty)
            {
                BazaarTelemetry.Reads.WithLabels("cache_hit").Inc();
                return JObject.Parse(cached!);
            }
            BazaarTelemetry.Reads.WithLabels("cache_miss").Inc();
            log.LogDebug("Bazaar cache miss for {UserId}; requesting authority; TraceId {TraceId}", userId, Activity.Current?.TraceId.ToString());
        }
        catch (RedisException e)
        {
            BazaarTelemetry.Reads.WithLabels("cache_error").Inc();
            log.LogWarning(e, "Bazaar cache unavailable for {UserId}; requesting authority; TraceId {TraceId}", userId, Activity.Current?.TraceId.ToString());
        }
        if (clients == null)
            return null;
        try
        {
            using var response = await clients.CreateClient("BazaarOrders")
                .GetAsync($"OrderBook/user/{Uri.EscapeDataString(userId)}");
            if (response.IsSuccessStatusCode)
            {
                BazaarTelemetry.Reads.WithLabels("authority_success").Inc();
                log.LogDebug("Restored Bazaar snapshot from authority for {UserId}; TraceId {TraceId}", userId, Activity.Current?.TraceId.ToString());
                return JObject.Parse(await response.Content.ReadAsStringAsync());
            }
            log.LogDebug("Bazaar authority returned {StatusCode} for {UserId}; using history; TraceId {TraceId}",
                (int)response.StatusCode, userId, Activity.Current?.TraceId.ToString());
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(e, "Bazaar authority unavailable for {UserId}; using history; TraceId {TraceId}", userId, Activity.Current?.TraceId.ToString());
        }
        BazaarTelemetry.Reads.WithLabels("authority_unavailable").Inc();
        return null; // Older server or temporarily loading: return observed history as estimates.
    }

    private class FillState : Bazaar.Client.Model.OrderEntry
    {
        [JsonProperty("isExpired")]
        public bool IsExpired { get; set; }
        [JsonProperty("claimed")]
        public int? Claimed { get; set; }
        [JsonProperty("isEstimate")]
        public bool? IsEstimate { get; set; }
    }
}
