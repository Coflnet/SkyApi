using System;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Coflnet.Sky.PlayerState.Client.Api;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services.Mod;

/// <summary>Booster cookie state of a player.</summary>
public enum CookieState
{
    /// <summary>Cookie is running</summary>
    Active,
    /// <summary>Cookie ran out</summary>
    Expired,
    /// <summary>Not known</summary>
    Unknown
}

/// <summary>Looks up whether a player has an active booster cookie.</summary>
public interface IBoosterCookieService
{
    /// <summary>Gets the cookie state for the minecraft name.</summary>
    Task<CookieState> Get(string mcName);
}

/// <inheritdoc/>
public class BoosterCookieService : IBoosterCookieService
{
    private readonly IPlayerStateApi stateApi;
    private readonly IMemoryCache cache;
    private readonly ILogger<BoosterCookieService> logger;

    /// <summary>Creates a new instance.</summary>
    public BoosterCookieService(IPlayerStateApi stateApi, IMemoryCache cache, ILogger<BoosterCookieService> logger)
    {
        this.stateApi = stateApi;
        this.cache = cache;
        this.logger = logger;
    }

    /// <summary>True if the user has a cookie according to state and their NoCookie setting.</summary>
    public static bool HasCookie(DescriptionSetting s, CookieState st) => !(s?.NoCookie ?? false) && st == CookieState.Active;

    /// <inheritdoc/>
    public async Task<CookieState> Get(string mcName)
    {
        if (string.IsNullOrEmpty(mcName))
            return CookieState.Unknown;
        if (cache.TryGetValue("boostercookie:" + mcName.ToLowerInvariant(), out CookieState cached))
            return cached;
        CookieState state;
        try
        {
            var info = await stateApi.PlayerStatePlayerIdExtractedGetAsync(mcName);
            var expires = info?.BoosterCookieExpires ?? default;
            state = expires == default ? CookieState.Unknown : expires > DateTime.UtcNow ? CookieState.Active : CookieState.Expired;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load booster cookie state of {Name}", mcName);
            return CookieState.Unknown;
        }
        cache.Set("boostercookie:" + mcName.ToLowerInvariant(), state, TimeSpan.FromMinutes(5));
        return state;
    }
}
