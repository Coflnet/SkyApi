using Microsoft.AspNetCore.Mvc;

namespace Coflnet.Sky.Api.Helper;

/// <summary>Cache control helpers for responses that depend on who is asking.</summary>
public static class ResponseCachingExtensions
{
    /// <summary>
    /// Marks the current response as no-store so neither the response caching middleware nor
    /// shared caches (Cloudflare ignores Vary) keep a tier or token dependent body.
    /// Call it from the gated branch, the ResponseCache attribute headers are written before the action runs.
    /// </summary>
    public static void DisableResponseCaching(this ControllerBase controller)
    {
        controller.Response.Headers.CacheControl = "no-store";
    }
}
