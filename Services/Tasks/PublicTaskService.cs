using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using ITaskApi = Coflnet.Sky.PlayerState.Client.Api.ITaskApi;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services.Tasks;

public interface IPublicTaskService
{
    Task<IReadOnlyList<PublicTaskEstimate>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PublicTaskEstimate> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
}

/// <summary>
/// Serves public task estimates from a short lived in memory cache and
/// falls back to the last good value when the upstream fails.
/// </summary>
public class PublicTaskService : IPublicTaskService
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(15);
    /// <summary>Upper bound for one upstream call, the generated client's own default is 100 seconds</summary>
    public static readonly TimeSpan UpstreamTimeout = TimeSpan.FromSeconds(10);
    private readonly ITaskApi taskApi;
    private readonly IItemsApi itemsApi;
    private readonly TimeProvider time;
    private readonly ILogger<PublicTaskService> logger;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private IReadOnlyList<PublicTaskEstimate> cached;
    private DateTimeOffset cachedAt;
    private DateTimeOffset lastFailure = DateTimeOffset.MinValue;

    public PublicTaskService(ITaskApi taskApi, IItemsApi itemsApi, TimeProvider time, ILogger<PublicTaskService> logger)
    {
        this.taskApi = taskApi;
        this.itemsApi = itemsApi;
        this.time = time;
        this.logger = logger;
    }

    /// <summary>
    /// With a stale value only one caller refreshes, everyone else gets the stale value immediately.
    /// Without any value callers wait for the shared refresh and honor <paramref name="cancellationToken"/>.
    /// </summary>
    public async Task<IReadOnlyList<PublicTaskEstimate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (IsFresh())
            return cached;
        var stale = cached;
        if (stale != null && !await refreshLock.WaitAsync(0, CancellationToken.None))
            return stale;
        if (stale == null)
            await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (IsFresh())
                return cached;
            return await RefreshOrFallback(stale == null ? cancellationToken : CancellationToken.None);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public async Task<PublicTaskEstimate> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        return all.FirstOrDefault(t => t.Slug == slug);
    }

    private bool IsFresh() => cached != null && time.GetUtcNow() - cachedAt < CacheDuration;

    private async Task<IReadOnlyList<PublicTaskEstimate>> RefreshOrFallback(CancellationToken cancellationToken)
    {
        if (cached == null && time.GetUtcNow() - lastFailure < RetryAfterFailure)
            throw Unavailable();
        try
        {
            var upstream = await FetchEstimates(cancellationToken);
            RejectEmptyOverNonEmpty(upstream);
            var names = await LoadItemNames();
            cached = PublicTaskMapper.MapAll(upstream, names);
            cachedAt = time.GetUtcNow();
            return cached;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            return HandleFailure(e);
        }
    }

    /// <summary>
    /// Calls GET /Task/estimates, cancelled by <paramref name="requestAborted"/> or after <see cref="UpstreamTimeout"/>.
    /// The generated (RestSharp) client reports an aborted request as a status 0 <c>ApiException</c>,
    /// so an abort by the caller is rethrown as a cancellation instead of counting as an upstream failure.
    /// A 200 with an unreadable body (html, truncated json) yields null without any exception, that is a failure too.
    /// </summary>
    private async Task<List<TaskEstimate>> FetchEstimates(CancellationToken requestAborted)
    {
        using var timeout = new CancellationTokenSource(UpstreamTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, timeout.Token);
        try
        {
            return await taskApi.TaskEstimatesGetAsync(cancellationToken: linked.Token)
                ?? throw new InvalidOperationException("PlayerState returned no readable task estimates");
        }
        catch (Exception e) when (requestAborted.IsCancellationRequested && e is not OperationCanceledException)
        {
            throw new OperationCanceledException("Task estimate request aborted", e, requestAborted);
        }
    }

    /// <summary>
    /// An empty list never replaces a non-empty cached value, it would blank the website and its sitemap.
    /// Without a cached value an empty list is accepted as is.
    /// </summary>
    private void RejectEmptyOverNonEmpty(List<TaskEstimate> upstream)
    {
        if (upstream.Count > 0 || cached == null || cached.Count == 0)
            return;
        throw new InvalidOperationException("PlayerState returned no task estimates while a non-empty value is cached");
    }

    private IReadOnlyList<PublicTaskEstimate> HandleFailure(Exception e)
    {
        logger.LogError(e, "Loading task estimates failed, {Fallback}", cached != null ? "serving stale value" : "no stale value");
        lastFailure = time.GetUtcNow();
        if (cached == null)
            throw Unavailable();
        // back off: the value counts as fresh until the next retry is due
        cachedAt = lastFailure - CacheDuration + RetryAfterFailure;
        return cached;
    }

    private static CoflnetException Unavailable() => new("tasks_unavailable", "Task estimates are temporarily unavailable");

    private async Task<IReadOnlyDictionary<string, string>> LoadItemNames()
    {
        try
        {
            var items = await itemsApi.ItemNamesGetAsync();
            return items.Where(i => i.Tag != null).GroupBy(i => i.Tag).ToDictionary(g => g.Key, g => g.First().Name);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load item names for task drops");
            return new Dictionary<string, string>();
        }
    }
}
