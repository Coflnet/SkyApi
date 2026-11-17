using System;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;

namespace Coflnet.Sky.Api.Services;

/// <summary>Chooses the Payments subscribe endpoint for a provider.</summary>
public static class SubscriptionCheckoutRouter
{
    public const string LemonSqueezy = "lemonsqueezy";
    public const string Stripe = "stripe";
    public const string PayPal = "paypal";

    /// <summary>Normalizes the provider query value; absent means LemonSqueezy so older website versions keep working.</summary>
    /// <returns>false for an unknown provider</returns>
    public static bool TryNormalize(string provider, out string normalized)
    {
        normalized = string.IsNullOrWhiteSpace(provider) ? LemonSqueezy : provider.Trim().ToLowerInvariant();
        return normalized is LemonSqueezy or Stripe or PayPal;
    }

    /// <summary>Starts the subscription checkout with the given (normalized) provider.</summary>
    public static Task<TopUpIdResponse> Subscribe(ITopUpApiAsync api, string provider, string userId, string slug, TopUpOptions options)
    {
        return provider switch
        {
            Stripe => api.TopUpStripeSubscribePostAsync(userId, slug, options),
            PayPal => api.TopUpPaypalSubscribePostAsync(userId, slug, options),
            LemonSqueezy => api.TopUpLemonsqueezySubscribePostAsync(userId, slug, options),
            _ => throw new ArgumentException("Unknown subscription provider " + provider, nameof(provider))
        };
    }
}
