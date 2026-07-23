using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Ai;
using Coflnet.Sky.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Coflnet.Sky.Api.Helper;

/// <summary>Applies the daily AI message quota by user, or by IP for anonymous traffic.</summary>
public class AiRateLimitFilter : IAsyncActionFilter
{
    public const string QuotaItemKey = "AiQuota";
    public const string IdentityItemKey = "AiIdentity";
    public const string RefundItemKey = "AiQuotaRefund";
    private const string CounterKeyItemKey = "AiQuotaCounter";
    private static readonly IReadOnlyDictionary<string, int> Limits = new Dictionary<string, int>
    {
        ["anonymous"] = 3,
        ["logged_in"] = 10,
        ["starter_premium"] = 20,
        ["premium"] = 50,
        ["premium_plus"] = 200
    };

    private readonly IConnectionMultiplexer redis;
    private readonly PremiumTierService premiumTierService;
    private readonly ILogger<AiRateLimitFilter> logger;

    public AiRateLimitFilter(IConnectionMultiplexer redis, PremiumTierService premiumTierService, ILogger<AiRateLimitFilter> logger)
    {
        this.redis = redis;
        this.premiumTierService = premiumTierService;
        this.logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var now = DateTimeOffset.UtcNow;
        var reset = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        var tier = "anonymous";
        var identity = GetClientIp(context.HttpContext) ?? "unknown";

        try
        {
            if (context.Controller is ControllerBase controller)
            {
                var access = await premiumTierService.GetUserAndTier(controller);
                if (access.user != null)
                {
                    tier = access.tier;
                    identity = access.user.Id.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve AI premium tier; applying the anonymous quota");
        }

        var limit = Limits[tier];
        var quota = new AiQuota
        {
            Limit = limit,
            Remaining = limit,
            ResetsAt = reset,
            Tier = tier
        };
        context.HttpContext.Items[QuotaItemKey] = quota;
        context.HttpContext.Items[IdentityItemKey] = $"{(tier == "anonymous" ? "ip" : "user")}:{identity}";

        try
        {
            var key = $"ratelimit:ai:{(tier == "anonymous" ? "ip" : "user")}:{identity}:{now:yyyyMMdd}";
            var db = redis.GetDatabase();
            var count = await db.StringIncrementAsync(key).ConfigureAwait(false);
            if (count == 1)
                await db.KeyExpireAsync(key, reset - now).ConfigureAwait(false);

            quota.Remaining = Math.Max(0, limit - (int)count);
            context.HttpContext.Items[CounterKeyItemKey] = key;
            context.HttpContext.Response.Headers["RateLimit-Limit"] = limit.ToString();
            context.HttpContext.Response.Headers["RateLimit-Remaining"] = quota.Remaining.ToString();
            context.HttpContext.Response.Headers["RateLimit-Reset"] = reset.ToUnixTimeSeconds().ToString();

            if (count > limit)
            {
                context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)(reset - now).TotalSeconds).ToString();
                context.Result = new ObjectResult(new
                {
                    error = "daily_ai_limit_reached",
                    message = $"Your {tier.Replace('_', ' ')} allowance is {limit} messages per UTC day.",
                    quota
                }) { StatusCode = StatusCodes.Status429TooManyRequests };
                return;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI rate limiter unavailable");
            context.Result = new ObjectResult(new
            {
                error = "ai_rate_limit_unavailable",
                message = "AI chat is temporarily unavailable. Please try again shortly."
            }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
            return;
        }

        await next();
        if (context.HttpContext.Items[RefundItemKey] is true
            && context.HttpContext.Items[CounterKeyItemKey] is string counterKey
            && context.HttpContext.Items[QuotaItemKey] is AiQuota reservedQuota)
        {
            try
            {
                await redis.GetDatabase().StringDecrementAsync(counterKey).ConfigureAwait(false);
                reservedQuota.Remaining = Math.Min(reservedQuota.Limit, reservedQuota.Remaining + 1);
                context.HttpContext.Response.Headers["RateLimit-Remaining"] = reservedQuota.Remaining.ToString();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not refund unused AI quota");
            }
        }
    }

    public static string GetClientIp(HttpContext context)
    {
        var ip = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
            ?? context.Request.Headers["True-Client-IP"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(ip))
            ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim();
        if (string.IsNullOrWhiteSpace(ip))
            ip = context.Connection.RemoteIpAddress?.ToString();
        if (IPAddress.TryParse(ip, out var address))
            return address.IsIPv4MappedToIPv6 ? address.MapToIPv4().ToString() : address.ToString();
        return ip;
    }
}
