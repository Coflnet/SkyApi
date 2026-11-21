using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Api.Controller;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Coflnet.Sky.Api.Helper;

internal sealed class PlaySubscriptionCompletionFilter(IConnectionMultiplexer redis, IConfiguration configuration,
    ILogger<PlaySubscriptionCompletionFilter> logger) : IAsyncResourceFilter, IActionFilter
{
    private const string QuotaKey = "{ratelimit:play-subscription-complete}";

    // Check both rolling windows before writing. Only the last limit timestamps can affect the next decision.
    internal const string ReserveQuota = """
        local time = redis.call('TIME')
        local now = time[1] * 1000 + math.floor(time[2] / 1000)
        local cutoff = now - 60000

        local function retryAfter(key, limit)
            local timestamp = tonumber(redis.call('LINDEX', key, -limit)) or cutoff
            return math.max(0, math.ceil((timestamp - cutoff) / 1000))
        end

        local function reserve(key, limit)
            redis.call('RPUSH', key, now)
            redis.call('LTRIM', key, -limit, -1)
            redis.call('PEXPIRE', key, 60000)
        end

        local retry = retryAfter(KEYS[1], 10)
        if retry > 0 then return retry end
        retry = retryAfter(KEYS[2], 1000)
        if retry > 0 then return retry end

        reserve(KEYS[1], 10)
        reserve(KEYS[2], 1000)
        return 0
        """;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        // Configure only immediate proxy peers that exclusively forward traffic from Cloudflare.
        var trustedProxies = configuration["PLAY_CHECKOUT_TRUSTED_PROXIES"]?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        var client = GetClientBucket(PremiumController.GetClientIp(context.HttpContext, trustedProxies) ?? "unknown");
        long retryAfter;
        try
        {
            retryAfter = (long)await redis.GetDatabase().ScriptEvaluateAsync(ReserveQuota,
                [$"{QuotaKey}:clients:{client}", $"{QuotaKey}:global"]);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogError("Google Play subscription rate limiter unavailable ({ExceptionType})", ex.GetType().Name);
            context.Result = new ObjectResult(PremiumController.PlayVerificationUnavailable())
                { StatusCode = StatusCodes.Status503ServiceUnavailable };
            return;
        }

        if (retryAfter > 0)
        {
            context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
            context.Result = new ObjectResult(new GooglePlaySubscriptionResult(false, "rate_limited", "rate_limited"))
                { StatusCode = StatusCodes.Status429TooManyRequests };
            return;
        }

        await next();
    }

    private static string GetClientBucket(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetworkV6)
            return ip;

        var bytes = address.GetAddressBytes();
        bytes.AsSpan(8).Clear();
        return $"{new IPAddress(bytes)}/64";
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!context.ModelState.IsValid)
            context.Result = PremiumController.InvalidPlayPurchase();
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
