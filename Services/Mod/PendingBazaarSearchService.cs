using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Api.Services.Mod;

/// <summary>Stores bazaar searches queued for users without a booster cookie.</summary>
public interface IPendingBazaarSearchService
{
    /// <summary>Queues a search (deduped by name, capped at 10).</summary>
    Task Add(string userId, string name, string tag = null, long? amount = null);
    /// <summary>Gets the non expired pending searches.</summary>
    Task<List<PendingBazaarSearch>> Get(string userId);
    /// <summary>Removes searches whose name is contained in the chest name.</summary>
    Task RemoveMatching(string userId, string chestName);
}

/// <inheritdoc/>
public class PendingBazaarSearchService : IPendingBazaarSearchService
{
    /// <summary>Settings key the list is stored under.</summary>
    public const string SettingsKey = "pendingBzSearch";
    private const int MaxEntries = 10;
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private readonly SettingsService settings;

    /// <summary>Creates a new instance.</summary>
    public PendingBazaarSearchService(SettingsService settings)
    {
        this.settings = settings;
    }

    private async Task<List<PendingBazaarSearch>> Load(string userId)
    {
        var list = await settings.GetCurrentValue<List<PendingBazaarSearch>>(userId, SettingsKey, () => new());
        var cutoff = DateTime.UtcNow - MaxAge;
        return (list ?? new()).Where(p => p?.Name != null && p.CreatedAt > cutoff).ToList();
    }

    /// <inheritdoc/>
    public async Task Add(string userId, string name, string tag = null, long? amount = null)
    {
        var list = await Load(userId);
        list.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        list.Add(new PendingBazaarSearch { Name = name, Tag = tag, Amount = amount, CreatedAt = DateTime.UtcNow });
        if (list.Count > MaxEntries)
            list = list.Skip(list.Count - MaxEntries).ToList();
        await settings.UpdateSetting(userId, SettingsKey, list);
    }

    /// <inheritdoc/>
    public Task<List<PendingBazaarSearch>> Get(string userId) => Load(userId);

    /// <inheritdoc/>
    public async Task RemoveMatching(string userId, string chestName)
    {
        if (string.IsNullOrEmpty(chestName))
            return;
        var list = await Load(userId);
        var removed = list.RemoveAll(p => chestName.Contains(p.Name, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
            await settings.UpdateSetting(userId, SettingsKey, list);
    }
}
