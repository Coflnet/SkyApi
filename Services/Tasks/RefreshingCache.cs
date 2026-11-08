using System;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services.Tasks;

/// <summary>
/// Holds one upstream value in memory for a short time and falls back to the last good value when the upstream fails.
/// With a stale value only one caller refreshes, everyone else gets the stale value immediately.
/// Without any value callers wait for the shared refresh and honor their cancellation token.
/// </summary>
public class RefreshingCache<T> where T : class
{
    private readonly Func<CancellationToken, Task<T>> load;
    private readonly Func<T, bool> isEmpty;
    private readonly TimeProvider time;
    private readonly ILogger logger;
    private readonly string name;
    private readonly string unavailableSlug;
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private T cached;
    private DateTimeOffset cachedAt;
    private DateTimeOffset lastFailure = DateTimeOffset.MinValue;

    /// <param name="load">Loads a fresh value, see <see cref="RefreshingCache.CallUpstream{TResult}"/> for the upstream call itself</param>
    /// <param name="isEmpty">An empty value never replaces a non-empty cached one</param>
    public RefreshingCache(Func<CancellationToken, Task<T>> load, TimeProvider time, ILogger logger,
        string name, string unavailableSlug, Func<T, bool> isEmpty = null)
    {
        this.load = load;
        this.time = time;
        this.logger = logger;
        this.name = name;
        this.unavailableSlug = unavailableSlug;
        this.isEmpty = isEmpty ?? (_ => false);
    }

    public async Task<T> GetAsync(CancellationToken cancellationToken = default)
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

    private bool IsFresh() => cached != null && time.GetUtcNow() - cachedAt < RefreshingCache.CacheDuration;

    private async Task<T> RefreshOrFallback(CancellationToken cancellationToken)
    {
        if (cached == null && time.GetUtcNow() - lastFailure < RefreshingCache.RetryAfterFailure)
            throw Unavailable();
        try
        {
            var value = await load(cancellationToken);
            RejectEmptyOverNonEmpty(value);
            cached = value;
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
    /// An empty value never replaces a non-empty cached value, it would blank the website and its sitemap.
    /// Without a cached value an empty value is accepted as is.
    /// </summary>
    private void RejectEmptyOverNonEmpty(T value)
    {
        if (!isEmpty(value) || cached == null || isEmpty(cached))
            return;
        throw new InvalidOperationException($"PlayerState returned no {name} while a non-empty value is cached");
    }

    private T HandleFailure(Exception e)
    {
        logger.LogError(e, "Loading {Name} failed, {Fallback}", name, cached != null ? "serving stale value" : "no stale value");
        lastFailure = time.GetUtcNow();
        if (cached == null)
            throw Unavailable();
        // back off: the value counts as fresh until the next retry is due
        cachedAt = lastFailure - RefreshingCache.CacheDuration + RefreshingCache.RetryAfterFailure;
        return cached;
    }

    private CoflnetException Unavailable() => new(unavailableSlug, $"{char.ToUpper(name[0])}{name[1..]} are temporarily unavailable");
}

/// <summary>Timings and the upstream call helper shared by the refreshing caches</summary>
public static class RefreshingCache
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(15);
    /// <summary>Upper bound for one upstream call, the generated client's own default is 100 seconds</summary>
    public static readonly TimeSpan UpstreamTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Runs an upstream call, cancelled by <paramref name="requestAborted"/> or after <see cref="UpstreamTimeout"/>.
    /// The generated (RestSharp) client reports an aborted request as a status 0 <c>ApiException</c>,
    /// so an abort by the caller is rethrown as a cancellation instead of counting as an upstream failure.
    /// A 200 with an unreadable body (html, truncated json) yields null without any exception, that is a failure too.
    /// </summary>
    public static async Task<TResult> CallUpstream<TResult>(TimeProvider time, CancellationToken requestAborted,
        Func<CancellationToken, Task<TResult>> call, string what) where TResult : class
    {
        using var timeout = new CancellationTokenSource(UpstreamTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, timeout.Token);
        try
        {
            return await call(linked.Token)
                ?? throw new InvalidOperationException($"PlayerState returned no readable {what}");
        }
        catch (Exception e) when (requestAborted.IsCancellationRequested && e is not OperationCanceledException)
        {
            throw new OperationCanceledException($"{what} request aborted", e, requestAborted);
        }
    }
}
