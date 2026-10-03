using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services;

/// <summary>
/// Looks up mod jar signatures against the third-party isthisarat.com service on behalf of the
/// mod page, which can no longer call it directly from the browser (isthisarat.com answers with
/// a Cloudflare challenge/403 and no CORS headers). Successful verdicts are cached per hash;
/// upstream failures are briefly negative-cached so an isthisarat.com outage doesn't make us hammer it.
/// </summary>
public class ModRatCheckService(IHttpClientFactory httpClientFactory, IDistributedCache cache, ILogger<ModRatCheckService> logger = null)
{
    /// <summary>Name of the named <see cref="HttpClient"/> registered for this service (see Startup.ConfigureServices).</summary>
    public const string HttpClientName = "RatCheck";

    private readonly ILogger<ModRatCheckService> log = logger ?? NullLogger<ModRatCheckService>.Instance;

    private const string CacheKeyPrefix = "ratcheck:v1:";
    private static readonly TimeSpan SuccessCacheDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Looks up the rat-check verdict for an already-validated, lowercase SHA-256 hash.
    /// Never throws for upstream problems - unreachable/timeout/non-success/non-JSON responses
    /// are all reported as an unavailable <see cref="RatCheckResult"/> instead.
    /// </summary>
    /// <param name="hash">A 64 character lowercase hex SHA-256 digest; callers must validate this before calling.</param>
    /// <param name="cancellationToken"></param>
    public async Task<RatCheckResult> CheckAsync(string hash, CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeyPrefix + hash;
        var cached = await TryGetCachedAsync(cacheKey, cancellationToken);
        if (cached != null)
            return cached;

        var result = await LookupUpstreamAsync(hash, cancellationToken);
        await TrySetCachedAsync(cacheKey, result, cancellationToken);
        return result;
    }

    private async Task<RatCheckResult> LookupUpstreamAsync(string hash, CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync($"api/signature/{hash}", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("isthisarat.com returned {StatusCode} for a rat check lookup", (int)response.StatusCode);
                return RatCheckResult.Unavailable();
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonConvert.DeserializeObject<RatCheckingResponse>(body);
            // a body without a verdict must never be passed on as a result, the frontend would read it as "not a rat"
            if (string.IsNullOrEmpty(parsed?.Rat))
            {
                log.LogWarning("isthisarat.com returned a body without a verdict for a rat check lookup");
                return RatCheckResult.Unavailable();
            }
            return RatCheckResult.Success(parsed);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            // covers connect failures, our ~10s client timeout, and bodies that don't parse as json
            // (eg the html Cloudflare challenge page isthisarat.com answers with)
            log.LogWarning(e, "isthisarat.com rat check lookup failed");
            return RatCheckResult.Unavailable();
        }
    }

    private async Task<RatCheckResult> TryGetCachedAsync(string cacheKey, CancellationToken cancellationToken)
    {
        try
        {
            var cached = await cache.GetStringAsync(cacheKey, cancellationToken);
            if (cached == null)
                return null;
            return JsonConvert.DeserializeObject<CacheEntry>(cached)?.ToResult();
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Could not read rat check cache entry, treating it as a cache miss");
            return null;
        }
    }

    private async Task TrySetCachedAsync(string cacheKey, RatCheckResult result, CancellationToken cancellationToken)
    {
        try
        {
            var options = new DistributedCacheEntryOptions
            {
                // verdicts for a hash can change later (eg unknown -> malicious), so successes
                // aren't cached forever; failures are negative-cached only briefly so a temporary
                // outage doesn't wedge a hash as "unavailable" for long
                AbsoluteExpirationRelativeToNow = result.IsAvailable ? SuccessCacheDuration : FailureCacheDuration
            };
            await cache.SetStringAsync(cacheKey, JsonConvert.SerializeObject(CacheEntry.From(result)), options, cancellationToken);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Could not write rat check cache entry");
        }
    }

    private class CacheEntry
    {
        public bool IsAvailable { get; set; }
        public RatCheckingResponse Response { get; set; }

        public static CacheEntry From(RatCheckResult result) => new() { IsAvailable = result.IsAvailable, Response = result.Response };
        public RatCheckResult ToResult() => IsAvailable ? RatCheckResult.Success(Response) : RatCheckResult.Unavailable();
    }
}

/// <summary>
/// Outcome of a <see cref="ModRatCheckService.CheckAsync"/> lookup.
/// </summary>
public class RatCheckResult
{
    /// <summary>Whether the upstream scanner answered successfully for this hash.</summary>
    public bool IsAvailable { get; private init; }

    /// <summary>The verdict; only set when <see cref="IsAvailable"/> is true.</summary>
    public RatCheckingResponse Response { get; private init; }

    /// <summary>Creates a successful result carrying the upstream verdict.</summary>
    public static RatCheckResult Success(RatCheckingResponse response) => new() { IsAvailable = true, Response = response };

    /// <summary>Creates an unavailable result (upstream unreachable/timeout/non-success/non-JSON).</summary>
    public static RatCheckResult Unavailable() => new() { IsAvailable = false };
}
