using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Microsoft.Extensions.DependencyInjection;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;

/// <summary>Active BIN listings with their NBT</summary>
public interface IGearListingSource
{
    /// <summary>False until the in-memory replica of the active auctions has loaded</summary>
    bool IsReady { get; }
    /// <summary>Active BINs without bids of the tag, cheapest first. Treat as read only, the objects are shared.</summary>
    IReadOnlyList<SaveAuction> ActiveBins(string tag);
    /// <summary>Compiled FilterEngine matcher, throws <see cref="UnknownFilterException"/> for unknown filters</summary>
    Func<SaveAuction, bool> Matcher(Dictionary<string, string> filters);
    /// <summary>Cheapest active BIN that passes the filters, from the database. Null when there is none.</summary>
    Task<(long Price, string Uuid)?> LowestFromDatabaseAsync(string tag, Dictionary<string, string> filters, CancellationToken cancellationToken);
}

/// <summary>Reads the in-memory <see cref="ActiveAuctionSet"/>, falls back to <see cref="PricesService"/> for the database</summary>
public class ActiveAuctionGearListingSource(ActiveAuctionSet set, FilterEngine filterEngine, IServiceScopeFactory scopeFactory) : IGearListingSource
{
    public bool IsReady => set.IsReady;

    public IReadOnlyList<SaveAuction> ActiveBins(string tag)
    {
        var snapshot = set.RebuildSnapshotIfDirty(); // throttled, never forces a rebuild
        if (tag == null || !snapshot.ByTag.TryGetValue(tag, out var indices))
            return Array.Empty<SaveAuction>();
        var now = DateTime.UtcNow;
        return indices.Select(i => snapshot.All[i])
            .Where(a => a.Bin && a.HighestBidAmount == 0 && a.End > now)
            .OrderBy(a => a.StartingBid)
            .ToList();
    }

    public Func<SaveAuction, bool> Matcher(Dictionary<string, string> filters)
        => filters.Count == 0 ? _ => true : filterEngine.GetMatcher(new Dictionary<string, string>(filters));

    public async Task<(long Price, string Uuid)?> LowestFromDatabaseAsync(string tag, Dictionary<string, string> filters, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var prices = scope.ServiceProvider.GetRequiredService<PricesService>();
        // awaited directly: the scope (and its db context) must outlive the query, the token cancels it
        var (cost, uuid, _) = await prices.GetLowestBinData(tag, new Dictionary<string, string>(filters), cancellationToken);
        return uuid == null ? null : (cost, uuid);
    }
}
