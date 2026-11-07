using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace Coflnet.Sky.Api.Helper;

/// <summary>
/// Server side cache for tier gated results that hold only shared, non personal data.
/// Use it after the tier check, the http response cache can't be used for these (see DisableResponseCaching).
/// </summary>
public static class SharedResultCache
{
    /// <summary>Returns the cached value or runs the factory, without a cache the factory always runs.</summary>
    public static Task<T> GetOrCreateShared<T>(this IMemoryCache cache, string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        if (cache == null)
            return factory();
        return cache.GetOrCreateAsync(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            return factory();
        });
    }

    /// <summary>Builds a cache key from a prefix, parts and the filters in sorted order.</summary>
    public static string Key(string prefix, IEnumerable<KeyValuePair<string, string>> filters, params object[] parts)
    {
        var sorted = (filters ?? []).OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{f.Key}={f.Value}");
        return $"{prefix}|{string.Join("|", parts)}|{string.Join("&", sorted)}";
    }
}
