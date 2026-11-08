using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Client;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services.Tasks.Setups;

/// <summary>
/// Measured gear clusters of a task. <paramref name="Clusters"/> is null when PlayerState does not know the task
/// (<paramref name="Failed"/> false) or the call failed or timed out (<paramref name="Failed"/> true).
/// </summary>
public record ClusterLookup(TaskGearClusters Clusters, bool Failed)
{
    public static readonly ClusterLookup NotFound = new(null, false);
    public static readonly ClusterLookup Unavailable = new(null, true);
}

public interface IGearClusterService
{
    /// <summary>Never throws for upstream problems, they come back as <see cref="ClusterLookup.Unavailable"/></summary>
    Task<ClusterLookup> GetAsync(string taskName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Calls PlayerState's gear clusters endpoint with a short timeout. After a failure the task is not asked again for
/// <see cref="FailureBackoff"/>, so a hanging upstream does not make every rebuild wait for the timeout.
/// The result itself is not cached here, the setups views built from it are.
/// </summary>
public class GearClusterService : IGearClusterService
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(1);
    private readonly ITaskGearApi gearApi;
    private readonly TimeProvider time;
    private readonly ILogger<GearClusterService> logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> failedAt = new();

    public GearClusterService(ITaskGearApi gearApi, TimeProvider time, ILogger<GearClusterService> logger)
    {
        this.gearApi = gearApi;
        this.time = time;
        this.logger = logger;
    }

    public async Task<ClusterLookup> GetAsync(string taskName, CancellationToken cancellationToken = default)
    {
        if (IsBackingOff(taskName))
            return ClusterLookup.Unavailable;
        using var timeout = new CancellationTokenSource(Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            // a timeout looks like an abort to CallUpstream, it is caught below as a failure
            var clusters = await RefreshingCache.CallUpstream(time, linked.Token,
                token => gearApi.TaskGearClustersTaskNameGetAsync(taskName, 0, token), "task gear clusters");
            failedAt.TryRemove(taskName, out _);
            return new ClusterLookup(clusters, false);
        }
        catch (ApiException e) when (e.ErrorCode == 404)
        {
            return ClusterLookup.NotFound;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Loading the gear clusters of {Task} failed, serving estimates only", taskName);
            failedAt[taskName] = time.GetUtcNow();
            return ClusterLookup.Unavailable;
        }
    }

    private bool IsBackingOff(string taskName)
        => failedAt.TryGetValue(taskName, out var at) && time.GetUtcNow() - at < FailureBackoff;
}
