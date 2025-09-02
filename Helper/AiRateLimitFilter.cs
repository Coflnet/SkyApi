using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Coflnet.Sky.Api.Helper
{
    /// <summary>
    /// Limits calls to the AI endpoint to 10 per day per client IP, synced via Redis.
    /// Respects Cloudflare proxy headers to determine the real client IP.
    /// </summary>
    public class AiRateLimitFilter : IAsyncActionFilter
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<AiRateLimitFilter> _logger;
        private const int DAILY_LIMIT = 10;

        public AiRateLimitFilter(IConnectionMultiplexer redis, ILogger<AiRateLimitFilter> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            try
            {
                var http = context.HttpContext;
                var ip = GetClientIp(http) ?? "unknown";
                var now = DateTimeOffset.UtcNow;
                var key = $"ratelimit:ai:ip:{ip}:{now:yyyyMMdd}";

                var db = _redis.GetDatabase();
                var count = await db.StringIncrementAsync(key).ConfigureAwait(false);

                if (count == 1)
                {
                    // set key to expire at next UTC midnight
                    var ttl = now.Date.AddDays(1) - now;
                    await db.KeyExpireAsync(key, ttl).ConfigureAwait(false);
                }

                if (count > DAILY_LIMIT)
                {
                    var retryIn = now.Date.AddDays(1) - now;
                    _logger.LogInformation("AI rate limit exceeded for {Ip}. Count={Count}", ip, count);
                    http.Response.Headers["Retry-After"] = ((int)retryIn.TotalSeconds).ToString();
                    context.Result = new ContentResult
                    {
                        StatusCode = StatusCodes.Status429TooManyRequests,
                        Content = $"Rate limit exceeded. You can make up to {DAILY_LIMIT} requests per day to this endpoint.",
                        ContentType = "text/plain"
                    };
                    return;
                }
                _logger.LogInformation("AI rate limit check for {Ip}. Count={Count}", ip, count);
            }
            catch (Exception ex)
            {
                // Fail-open on rate limiter errors
                _logger.LogError(ex, "AI rate limiter error");
            }

            await next();
        }

        private static string? GetClientIp(HttpContext context)
        {
            // Prefer Cloudflare headers
            var headers = context.Request.Headers;
            var ip = headers["CF-Connecting-IP"].FirstOrDefault()
                     ?? headers["True-Client-IP"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(ip))
            {
                // X-Forwarded-For may contain multiple: client, proxy1, proxy2
                var xff = headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(xff))
                {
                    ip = xff.Split(',').Select(p => p.Trim()).FirstOrDefault();
                }
            }

            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = context.Connection.RemoteIpAddress?.ToString();
            }

            // Normalize IPv6-mapped IPv4 (e.g., ::ffff:192.0.2.128)
            if (!string.IsNullOrWhiteSpace(ip) && IPAddress.TryParse(ip, out var addr))
            {
                if (addr.IsIPv4MappedToIPv6)
                    ip = addr.MapToIPv4().ToString();
                else
                    ip = addr.ToString();
            }

            return ip;
        }
    }
}
