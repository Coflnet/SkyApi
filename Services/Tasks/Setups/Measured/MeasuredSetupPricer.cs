using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Services.Tasks.Setups.Acquisition;
using Coflnet.Sky.PlayerState.Client.Model;

namespace Coflnet.Sky.Api.Services.Tasks.Setups.Measured;

/// <summary>Values of <see cref="PricedPiece.CostSource"/></summary>
public static class CostSources
{
    public const string Direct = "direct";
    public const string Base = "base";
    /// <summary>Recent sales of the item, used when nothing that matches is listed</summary>
    public const string Market = "market";
}

/// <summary>
/// A measured item with its acquisition and resale quote. <see cref="Cost"/> is null when no price is known.
/// <see cref="NoMarket"/> items are untradeable by positive evidence (the curated profile says so) and left out of every cost.
/// </summary>
/// <param name="LowerBound">
/// The cost is a lower bound: a market price of the plain item (no filters for measured modifiers, or the variant had
/// no sales) or of a base that still needs grinding
/// </param>
/// <param name="Transient">A lookup failed or timed out, so the price is missing or unreliable; the next build may have it</param>
public record PricedPiece(MeasuredPiece Piece, AcquisitionResult Acquisition, ItemResaleQuote Quote,
    double? Cost, string CostSource, bool NoMarket, bool LowerBound = false, bool Transient = false)
{
    public bool Priced => Cost != null || NoMarket;
}

/// <summary>A measured cluster with its priced items</summary>
public record PricedCluster(GearCluster Cluster, IReadOnlyList<PricedPiece> Pieces)
{
    /// <summary>The key's parts (tool=..., pet=...) to tell parent and child clusters apart</summary>
    public IReadOnlyDictionary<string, string> KeyParts { get; } = ClusterKeys.Parts(Cluster.Key);

    public double Rate => Cluster.CoinsPerHour;
    public bool Priced => Pieces.All(p => p.Priced);
    /// <summary>What every item costs together, null when an item has no price</summary>
    public double? FullCost => Priced ? Pieces.Sum(p => p.Cost ?? 0) : null;
    /// <summary>True when <see cref="FullCost"/> is only a lower bound</summary>
    public bool LowerBound => Pieces.Any(p => p.LowerBound);
    /// <summary>An item's price lookup failed: the cluster sits this build out instead of being ranked on a wrong price</summary>
    public bool TransientlyUnpriced => Pieces.Any(p => p.Transient);

    public HashSet<string> SpecKeys() => Pieces.Select(p => p.Piece.SpecKey).ToHashSet();

    /// <summary>What the items not in <paramref name="owned"/> cost, null when one of them has no price</summary>
    public double? CostWithout(IReadOnlySet<string> owned)
    {
        var bought = Pieces.Where(p => !owned.Contains(p.Piece.SpecKey)).ToList();
        return bought.All(p => p.Priced) ? bought.Sum(p => p.Cost ?? 0) : null;
    }
}

/// <param name="Degraded">True when a lookup failed or timed out, the result should only be kept briefly</param>
public record PricedClusters(IReadOnlyList<PricedCluster> Clusters, bool Degraded);

public interface IMeasuredSetupPricer
{
    /// <param name="untradeableTags">Tags known to be untradeable (soulbound), they are excluded from costs</param>
    Task<PricedClusters> PriceAsync(IReadOnlyList<GearCluster> clusters, IReadOnlySet<string> untradeableTags = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Prices measured clusters: one acquisition per distinct spec (at most <see cref="MaxParallelAcquisitions"/> at once),
/// then one resale quote per distinct tag and filters.
/// An item costs the cheaper of buying a matching auction and upgrading a complete cheapest base,
/// without either it costs its market price (a lower bound when only the plain item or a base needing grind has one).
/// </summary>
public class MeasuredSetupPricer : IMeasuredSetupPricer
{
    /// <summary>The acquisition service runs this many at once; a caller waiting for a slot uses up its own deadline</summary>
    public const int MaxParallelAcquisitions = GearAcquisitionService.MaxParallel;
    public const int MaxParallelQuotes = 8;
    /// <summary>Upper bound for all acquisitions of one build, what is not done by then counts as timed out</summary>
    public static readonly TimeSpan DefaultAcquisitionBudget = TimeSpan.FromSeconds(15);
    /// <summary>Upper bound for all resale quotes of one build</summary>
    public static readonly TimeSpan DefaultQuoteBudget = TimeSpan.FromSeconds(10);
    private static readonly IReadOnlySet<string> NoTags = new HashSet<string>();
    private readonly IGearAcquisitionService acquisition;
    private readonly IItemResaleService resale;
    private readonly TimeSpan acquisitionBudget;
    private readonly TimeSpan quoteBudget;

    public MeasuredSetupPricer(IGearAcquisitionService acquisition, IItemResaleService resale)
        : this(acquisition, resale, DefaultAcquisitionBudget, DefaultQuoteBudget)
    {
    }

    internal MeasuredSetupPricer(IGearAcquisitionService acquisition, IItemResaleService resale, TimeSpan acquisitionBudget, TimeSpan quoteBudget)
    {
        this.acquisition = acquisition;
        this.resale = resale;
        this.acquisitionBudget = acquisitionBudget;
        this.quoteBudget = quoteBudget;
    }

    /// <summary>A resale quote and whether it is the plain item's because the variant had no market</summary>
    private sealed record QuoteResult(ItemResaleQuote Quote, bool PlainTag);

    public async Task<PricedClusters> PriceAsync(IReadOnlyList<GearCluster> clusters, IReadOnlySet<string> untradeableTags = null,
        CancellationToken cancellationToken = default)
    {
        untradeableTags ??= NoTags;
        var pieces = (clusters ?? []).Where(c => c != null).Select(c => (Cluster: c, Pieces: MeasuredPiece.Of(c))).ToList();
        var distinct = pieces.SelectMany(p => p.Pieces).GroupBy(p => p.SpecKey).Select(g => g.First()).ToList();
        var acquisitions = await LoadAcquisitions(distinct, cancellationToken);
        var targets = distinct.Select(p => QuoteTargetOf(p, acquisitions[p.SpecKey])).ToList();
        var quotes = await LoadQuotes(targets, cancellationToken);
        var priced = pieces.Select(p => new PricedCluster(p.Cluster,
            p.Pieces.Select(piece => Price(piece, acquisitions[piece.SpecKey], quotes, untradeableTags)).ToList())).ToList();
        var degraded = acquisitions.Values.Any(IsTransient) || quotes.Values.Any(q => q.Quote.Degraded);
        return new PricedClusters(priced, degraded);
    }

    /// <summary>The cheaper of a direct buy and a complete base, else the market price; null when nothing has a price</summary>
    public static (double? Cost, string Source) CostOf(AcquisitionResult found, ItemResaleQuote quote)
    {
        double? direct = found?.Direct?.Price is > 0 and var price ? price : null;
        double? completeBase = found?.Base is { Incomplete: false, GrindRequired.Count: 0, Total: > 0 } b ? b.Total : null;
        if (direct != null && (completeBase == null || direct <= completeBase))
            return (direct, CostSources.Direct);
        if (completeBase != null)
            return (completeBase, CostSources.Base);
        if (quote?.Buy is > 0)
            return (quote.Buy, CostSources.Market);
        return (null, null);
    }

    public static bool IsTransient(AcquisitionResult result)
        => AcquisitionReasons.Transient.Contains(result?.DirectUnavailableReason ?? "")
            || AcquisitionReasons.Transient.Contains(result?.BaseUnavailableReason ?? "");

    private static PricedPiece Price(MeasuredPiece piece, AcquisitionResult found, IReadOnlyDictionary<string, QuoteResult> quotes,
        IReadOnlySet<string> untradeableTags)
    {
        var target = QuoteTargetOf(piece, found);
        var quote = quotes.GetValueOrDefault(PublicSetupsMapper.QuoteKey(target.Tag, target.Filters)) ?? new(ItemResaleQuote.Unknown(piece.Tag), false);
        if (untradeableTags.Contains(piece.Tag))
            return new PricedPiece(piece, found, quote.Quote, null, null, true);
        var (cost, source) = CostOf(found, quote.Quote);
        var lowerBound = source == CostSources.Market && (quote.PlainTag || found?.Base is { GrindRequired.Count: > 0 }
            || (target.Filters == null && piece.ModifiersMeasured));
        return new PricedPiece(piece, found, quote.Quote, cost, source, false, lowerBound, IsTransientPrice(found, quote.Quote, source));
    }

    /// <summary>
    /// A failed or timed out acquisition has no filters, so a market price would be the plain item's without any flag:
    /// such a piece is transient even with a price. A matching auction found directly is a real price, it stays.
    /// </summary>
    private static bool IsTransientPrice(AcquisitionResult found, ItemResaleQuote quote, string source)
    {
        if (source == CostSources.Direct)
            return false;
        return IsTransient(found) || (source == null && quote.Degraded);
    }

    /// <summary>The resale is quoted with the acquisition's auction filters, so a variant is not valued like a clean item</summary>
    private static QuoteTarget QuoteTargetOf(MeasuredPiece piece, AcquisitionResult found)
    {
        var filters = found?.Filters is { Count: > 0 } f ? f : null;
        return new QuoteTarget(piece.Tag, filters);
    }

    /// <summary>
    /// A lookup still running when the budget ends counts as timed out (transient, so the result is cached briefly).
    /// The acquisition service keeps computing it and caches the result, so the next build gets it.
    /// </summary>
    private async Task<Dictionary<string, AcquisitionResult>> LoadAcquisitions(List<MeasuredPiece> pieces, CancellationToken cancellationToken)
    {
        var results = new ConcurrentDictionary<string, AcquisitionResult>();
        await ForEachWithin(pieces, MaxParallelAcquisitions, acquisitionBudget, cancellationToken,
            async (piece, token) => results[piece.SpecKey] = await Acquire(piece, token));
        var timedOut = AcquisitionResult.Failed(AcquisitionReasons.Timeout, DateTimeOffset.UtcNow);
        return pieces.ToDictionary(p => p.SpecKey, p => results.GetValueOrDefault(p.SpecKey) ?? timedOut);
    }

    /// <summary>Runs <paramref name="body"/> for every item until the budget ends, only the caller's cancellation throws</summary>
    private static async Task ForEachWithin<T>(IEnumerable<T> items, int parallel, TimeSpan budget, CancellationToken cancellationToken,
        Func<T, CancellationToken, ValueTask> body)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(budget);
        var options = new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = deadline.Token };
        try
        {
            await Parallel.ForEachAsync(items, options, body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // budget used up, the missing results are filled in by the caller
        }
    }

    private Task<AcquisitionResult> Acquire(MeasuredPiece piece, CancellationToken token)
        => piece.PetSpec != null ? acquisition.GetAsync(piece.PetSpec, token) : acquisition.GetAsync(piece.ItemSpec, token);

    private async Task<Dictionary<string, QuoteResult>> LoadQuotes(List<QuoteTarget> targets, CancellationToken cancellationToken)
    {
        var distinct = targets.GroupBy(t => PublicSetupsMapper.QuoteKey(t.Tag, t.Filters)).Select(g => (g.Key, Target: g.First())).ToList();
        var results = new ConcurrentDictionary<string, QuoteResult>();
        await ForEachWithin(distinct, MaxParallelQuotes, quoteBudget, cancellationToken,
            async (entry, token) => results[entry.Key] = await QuoteWithFallback(entry.Target, token));
        return distinct.ToDictionary(e => e.Key,
            e => results.GetValueOrDefault(e.Key) ?? new QuoteResult(ItemResaleQuote.Unknown(e.Target.Tag) with { Degraded = true }, false));
    }

    /// <summary>A variant without sales is valued like the plain item, a lower bound that is flagged as such</summary>
    private async Task<QuoteResult> QuoteWithFallback(QuoteTarget target, CancellationToken token)
    {
        var quote = await SafeQuote(target.Tag, target.Filters, token);
        if (target.Filters == null || quote.Degraded || quote.Buy != null || quote.Resale != null)
            return new QuoteResult(quote, false);
        return new QuoteResult(await SafeQuote(target.Tag, null, token), true);
    }

    private async Task<ItemResaleQuote> SafeQuote(string tag, IReadOnlyDictionary<string, string> filters, CancellationToken token)
    {
        try
        {
            return await resale.GetQuoteAsync(tag, filters, token) ?? ItemResaleQuote.Unknown(tag);
        }
        catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested)
        {
            return ItemResaleQuote.Unknown(tag) with { Degraded = true };
        }
    }
}
