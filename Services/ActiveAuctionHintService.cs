using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Proxy.Client.Api;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services;

/// <summary>
/// When someone views an active auction the seller's listings are requested from the proxy
/// so the replica gets fresh bid data. Throttled per auction and per seller.
/// </summary>
public class ActiveAuctionHintService
{
    private static readonly TimeSpan Throttle = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(10);

    private readonly IBaseApi proxyApi;
    private readonly ILogger<ActiveAuctionHintService> logger;
    private readonly ConcurrentDictionary<string, DateTime> lastHint = new();
    private readonly SemaphoreSlim concurrency = new(4);
    private readonly Func<DateTime> clock;
    private DateTime lastCleanup = DateTime.UtcNow;

    public ActiveAuctionHintService(IBaseApi proxyApi, ILogger<ActiveAuctionHintService> logger, Func<DateTime> clock = null)
    {
        this.proxyApi = proxyApi;
        this.logger = logger;
        this.clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Requests the seller's auctions in the background. Returns false if throttled or not possible.
    /// </summary>
    public bool HintSeller(SaveAuction active)
    {
        if (active?.AuctioneerId == null)
            return false;
        var now = clock();
        Cleanup(now);
        if (!TryClaim("a:" + active.UId, now) || !TryClaim("s:" + active.AuctioneerId, now))
            return false;
        _ = SendAsync(active.AuctioneerId);
        return true;
    }

    private bool TryClaim(string key, DateTime now)
    {
        var claimed = false;
        lastHint.AddOrUpdate(key, _ => { claimed = true; return now; }, (_, last) =>
        {
            if (now - last < Throttle)
                return last;
            claimed = true;
            return now;
        });
        return claimed;
    }

    private void Cleanup(DateTime now)
    {
        if (now - lastCleanup < TimeSpan.FromMinutes(1))
            return;
        lastCleanup = now;
        foreach (var pair in lastHint)
            if (now - pair.Value > EntryLifetime)
                lastHint.TryRemove(pair.Key, out _);
    }

    internal int TrackedEntries => lastHint.Count;

    private async Task SendAsync(string sellerUuid)
    {
        if (!await concurrency.WaitAsync(0))
            return; // already four in flight, the next view will retry
        try
        {
            var info = await proxyApi.BaseAhPlayerIdPostWithHttpInfoAsync(sellerUuid, "sky-api-auction-view");
            if (info.StatusCode != System.Net.HttpStatusCode.OK)
                logger.LogWarning("Proxy returned {status} for auction view hint of {seller}", info.StatusCode, sellerUuid);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to hint seller {seller}", sellerUuid);
        }
        finally
        {
            concurrency.Release();
        }
    }
}
