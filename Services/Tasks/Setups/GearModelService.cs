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
/// The measured gear model of a task. <paramref name="Model"/> is null when PlayerState does not know the task
/// (<paramref name="Failed"/> false) or the call failed or timed out (<paramref name="Failed"/> true).
/// </summary>
public record GearModelLookup(TaskGearModel Model, bool Failed)
{
    public static readonly GearModelLookup NotFound = new(null, false);
    public static readonly GearModelLookup Unavailable = new(null, true);
}

public interface IGearModelService
{
    /// <summary>Never throws for upstream problems, they come back as <see cref="GearModelLookup.Unavailable"/></summary>
    Task<GearModelLookup> GetAsync(string taskName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Calls PlayerState's gear model endpoint (GET /Task/gear-model/{taskName}) with a short timeout. After a failure the task is not asked again for
/// <see cref="FailureBackoff"/>, so a hanging upstream does not make every rebuild wait for the timeout.
/// The result itself is not cached here, the setups views built from it are.
/// </summary>
public class GearModelService : IGearModelService
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(1);
    private readonly ITaskGearApi gearApi;
    private readonly TimeProvider time;
    private readonly ILogger<GearModelService> logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> failedAt = new();

    public GearModelService(ITaskGearApi gearApi, TimeProvider time, ILogger<GearModelService> logger)
    {
        this.gearApi = gearApi;
        this.time = time;
        this.logger = logger;
    }

    public async Task<GearModelLookup> GetAsync(string taskName, CancellationToken cancellationToken = default)
    {
        if (IsBackingOff(taskName))
            return GearModelLookup.Unavailable;
        using var timeout = new CancellationTokenSource(Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            // a timeout looks like an abort to CallUpstream, it is caught below as a failure
            var model = await RefreshingCache.CallUpstream(time, linked.Token,
                token => gearApi.TaskGearModelTaskNameGetAsync(taskName, 0, token), "task gear model");
            failedAt.TryRemove(taskName, out _);
            return new GearModelLookup(model, false);
        }
        catch (ApiException e) when (e.ErrorCode == 404)
        {
            return GearModelLookup.NotFound;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Loading the gear model of {Task} failed, serving estimates only", taskName);
            failedAt[taskName] = time.GetUtcNow();
            return GearModelLookup.Unavailable;
        }
    }

    private bool IsBackingOff(string taskName)
        => failedAt.TryGetValue(taskName, out var at) && time.GetUtcNow() - at < FailureBackoff;
}
