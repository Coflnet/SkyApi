using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using ITaskApi = Coflnet.Sky.PlayerState.Client.Api.ITaskApi;
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
    public static readonly TimeSpan CacheDuration = RefreshingCache.CacheDuration;
    public static readonly TimeSpan RetryAfterFailure = RefreshingCache.RetryAfterFailure;
    public static readonly TimeSpan UpstreamTimeout = RefreshingCache.UpstreamTimeout;
    private readonly ITaskApi taskApi;
    private readonly IItemsApi itemsApi;
    private readonly TimeProvider time;
    private readonly ILogger<PublicTaskService> logger;
    private readonly RefreshingCache<IReadOnlyList<PublicTaskEstimate>> cache;

    public PublicTaskService(ITaskApi taskApi, IItemsApi itemsApi, TimeProvider time, ILogger<PublicTaskService> logger)
    {
        this.taskApi = taskApi;
        this.itemsApi = itemsApi;
        this.time = time;
        this.logger = logger;
        cache = new(Load, time, logger, "task estimates", "tasks_unavailable", list => list.Count == 0);
    }

    public Task<IReadOnlyList<PublicTaskEstimate>> GetAllAsync(CancellationToken cancellationToken = default)
        => cache.GetAsync(cancellationToken);

    public async Task<PublicTaskEstimate> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        return all.FirstOrDefault(t => t.Slug == slug);
    }

    private async Task<IReadOnlyList<PublicTaskEstimate>> Load(CancellationToken cancellationToken)
    {
        var upstream = await RefreshingCache.CallUpstream(time, cancellationToken,
            token => taskApi.TaskEstimatesGetAsync(cancellationToken: token), "task estimates");
        var names = await LoadItemNames();
        return PublicTaskMapper.MapAll(upstream, names);
    }

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
