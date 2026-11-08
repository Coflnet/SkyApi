using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services.Tasks.Setups;

public interface IGearSetupService
{
    Task<IReadOnlyList<TaskGearSetups>> GetAllAsync(CancellationToken cancellationToken = default);
    /// <summary>The gear setups of a task or null when the task has no gear profile</summary>
    Task<TaskGearSetups> GetByTaskNameAsync(string taskName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Serves the upstream gear setups from a short lived in memory cache and
/// falls back to the last good value when the upstream fails.
/// </summary>
public class GearSetupService : IGearSetupService
{
    private readonly ITaskGearApi gearApi;
    private readonly TimeProvider time;
    private readonly RefreshingCache<IReadOnlyList<TaskGearSetups>> cache;

    public GearSetupService(ITaskGearApi gearApi, TimeProvider time, ILogger<GearSetupService> logger)
    {
        this.gearApi = gearApi;
        this.time = time;
        cache = new(Load, time, logger, "task gear setups", "gear_setups_unavailable", list => list.Count == 0);
    }

    public Task<IReadOnlyList<TaskGearSetups>> GetAllAsync(CancellationToken cancellationToken = default)
        => cache.GetAsync(cancellationToken);

    public async Task<TaskGearSetups> GetByTaskNameAsync(string taskName, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken);
        return all.FirstOrDefault(s => s.TaskName == taskName);
    }

    private async Task<IReadOnlyList<TaskGearSetups>> Load(CancellationToken cancellationToken)
    {
        return await RefreshingCache.CallUpstream<IReadOnlyList<TaskGearSetups>>(time, cancellationToken,
            async token => await gearApi.TaskGearSetupsGetAsync(cancellationToken: token), "task gear setups");
    }
}
