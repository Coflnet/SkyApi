using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Client;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Api.Helper;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Logging;

#nullable enable annotations

namespace Coflnet.Sky.Api.Controller;

public partial class PremiumController
{
    /// <summary>Checks whether the signed-in user can start a Google Play subscription.</summary>
    /// <param name="productSlug">SkyCofl subscription plan slug.</param>
    /// <param name="playApi">Payments Google Play subscription client.</param>
    /// <param name="accountLabel">Resolves the label displayed by the app.</param>
    /// <response code="200">Play Billing identifiers and a signed checkout that expires after ten minutes.</response>
    /// <response code="400">The plan is unavailable or blocked by an existing subscription.</response>
    /// <response code="401">The user is unauthenticated.</response>
    /// <response code="428">The user must accept the current SkyCofl agreement.</response>
    /// <response code="503">Checkout signing or the terms publication time is unavailable.</response>
    [HttpPost("premium/subscription/{productSlug}/playstore")]
    [Authorize]
    [ProducesResponseType<PlaySubscriptionCheckout>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PlaySubscriptionCheckout>> StartPlayStoreSubscription(string productSlug,
        [FromServices] IGooglePlaySubscriptionApi playApi, [FromServices] PlayCheckoutAccountLabel accountLabel)
    {
        var user = await GetUserOrDefault(authenticatedOnly: true);
        if (user == null)
            return Unauthorized("no googletoken header");
        if (await MustRejectNewContract(user.Id, configuration))
            return TermsAcceptanceRequired();

        using var key = ECDsa.Create();
        if (!PlayCheckoutSigner.TryLoadKey(key, configuration[PlayCheckoutSigner.ConfigurationKey]))
            return PlayCheckoutUnavailable("Google Play checkout signing key is missing or invalid");

        var userId = user.Id.ToString(CultureInfo.InvariantCulture);
        var eligibility = await playApi.ApiGoogleplaySubscriptionEligibilityGetAsync(userId, productSlug);
        if (!eligibility.Eligible)
        {
            var message = eligibility.Reason == "active_subscription"
                ? "You already have an active subscription in this plan's line."
                : "This subscription plan is unavailable in Google Play.";
            if (!string.IsNullOrWhiteSpace(eligibility.BlockingProvider))
                message += $" The blocking subscription is billed by {eligibility.BlockingProvider}.";
            throw new CoflnetException(eligibility.Reason ?? "plan_unavailable", message);
        }

        var offerId = eligibility.OfferId;
        var discountPercent = eligibility.DiscountPercent;
        if (!HasValidPlayOffer(offerId, discountPercent))
            return PlayCheckoutUnavailable("Google Play eligibility has invalid sale offer fields");

        var label = await accountLabel.GetAsync(userId, User.FindFirstValue(ClaimTypes.Email));
        var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 600;
        var signature = PlayCheckoutSigner.Sign(key, userId, eligibility.PlayProductId, eligibility.BasePlanId,
            productSlug, label, expiresAt, offerId);
        if (signature == null)
            return PlayCheckoutUnavailable("Google Play checkout is missing valid signing fields");

        return Ok(new PlaySubscriptionCheckout(userId, eligibility.PlayProductId, eligibility.BasePlanId,
            label, expiresAt, signature, offerId, discountPercent));
    }

    private static bool HasValidPlayOffer(string? offerId, int? discountPercent) => offerId == null
        ? discountPercent == null
        : discountPercent is >= 1 and <= 99 && Regex.IsMatch(offerId, @"\A[a-z0-9][a-z0-9-]{0,62}\z");

    private ObjectResult PlayCheckoutUnavailable(string reason)
    {
        logger?.LogError("{Reason}", reason);
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse
        {
            Slug = "play_checkout_signing_unavailable",
            Message = "Google Play checkout signing is temporarily unavailable."
        });
    }

    /// <summary>Verifies a Google Play subscription without requiring login. Google Play supplies the buyer and plan.</summary>
    /// <param name="productSlug">The checkout plan slug, or the Play product ID for a restored purchase.</param>
    /// <param name="purchase">The purchase token and optional product ID returned by Play Billing.</param>
    /// <param name="playApi">Payments Google Play subscription client.</param>
    /// <response code="200">The purchase was granted or recorded. Keep an ungranted purchase for later completion.</response>
    /// <response code="400">The token is invalid or the purchase was rejected, including missing or mismatched buyers.</response>
    /// <response code="409">The purchase is processing. Retry later.</response>
    /// <response code="429">The quota of ten requests per IPv4 address or IPv6 /64 or 1,000 globally in a rolling minute was exceeded. Retry after the indicated delay.</response>
    /// <response code="503">Verification or the plan is unavailable, or the purchase state is unsupported. Retry later.</response>
    [HttpPost("premium/subscription/{productSlug}/playstore/complete")]
    [AllowAnonymous]
    [TypeFilter(typeof(PlaySubscriptionCompletionFilter), Order = -2100)]
    [ProducesResponseType<GooglePlaySubscriptionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<GooglePlaySubscriptionResult>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<GooglePlaySubscriptionResult>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<GooglePlaySubscriptionResult>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<GooglePlaySubscriptionResult>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GooglePlaySubscriptionResult>> CompletePlayStoreSubscription(string productSlug,
        [FromBody] PlaySubscriptionPurchase purchase, [FromServices] IGooglePlaySubscriptionApi playApi)
    {
        var token = purchase?.PurchaseToken;
        if (token is not { Length: > 0 and <= 4096 } || !token.All(IsPlayTokenCharacter))
            return InvalidPlayPurchase();

        var subscriptionId = purchase?.SubscriptionId;
        if (string.IsNullOrWhiteSpace(subscriptionId) || subscriptionId.Length > 128 || subscriptionId.Any(char.IsControl))
            subscriptionId = productSlug;
        var request = new GooglePlaySubscriptionRequest(subscriptionId, token, "com.coflnet.sky", userId: null);
        GooglePlaySubscriptionResult? result;
        try
        {
            result = await playApi.ApiGoogleplaySubscriptionCompletePostAsync(request);
        }
        catch (ApiException ex) when (ex.ErrorCode == 409)
        {
            result = new GooglePlaySubscriptionResult(false, "processing", null);
        }
        catch (Exception e)
        {
            // Type and status only: a client exception can carry the request or response body
            logger?.LogWarning("Google Play subscription completion failed ({ExceptionType}, HTTP {StatusCode})",
                e.GetType().Name, (e as ApiException)?.ErrorCode);
            result = null;
        }

        result ??= PlayVerificationUnavailable();
        var statusCode = GetPlaySubscriptionStatusCode(result);
        if (statusCode != StatusCodes.Status200OK)
            result.Status = result.ErrorMessage ?? result.Status ?? "invalid_purchase";
        return StatusCode(statusCode, result);
    }

    // Google tokens are opaque. Accept every printable, non-whitespace ASCII character.
    private static bool IsPlayTokenCharacter(char c) => c is >= '!' and <= '~';

    internal static BadRequestObjectResult InvalidPlayPurchase() =>
        new(new GooglePlaySubscriptionResult(false, "invalid_purchase", "A valid Play purchase token is required."));

    internal static GooglePlaySubscriptionResult PlayVerificationUnavailable() =>
        new(false, "verification_unavailable", "verification_unavailable");

    internal static bool IsPlaySubscriptionCompletion(Endpoint? endpoint) =>
        endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>()?.ActionName == nameof(CompletePlayStoreSubscription);

    internal static string? GetClientIp(HttpContext context, string[] trustedCloudflareProxies)
    {
        var peer = RequestIpUtility.NormalizeIp(context.Connection.RemoteIpAddress);
        var header = context.Request.Headers["CF-Connecting-IP"];
        if (peer == null || header.Count != 1 || header[0]?.Contains('%') != false
            || !IPAddress.TryParse(header[0], out var clientIp))
            return peer;

        var peerAddress = IPAddress.Parse(peer);
        return trustedCloudflareProxies.Any(proxy => IPAddress.TryParse(proxy, out var address)
            ? RequestIpUtility.NormalizeIp(address) == peer
            : IPNetwork.TryParse(proxy, out var network) && network.PrefixLength > 0 && network.Contains(peerAddress))
            ? RequestIpUtility.NormalizeIp(clientIp) : peer;
    }

    private static int GetPlaySubscriptionStatusCode(GooglePlaySubscriptionResult result) => result switch
    {
        { IsValid: true } => StatusCodes.Status200OK,
        { Status: "missing_user" or "user_mismatch" } or { ErrorMessage: "missing_user" or "user_mismatch" }
            => StatusCodes.Status400BadRequest,
        { Status: "processing" } => StatusCodes.Status409Conflict,
        { Status: "verification_unavailable" or "unsupported_state" or "plan_unavailable" }
            or { ErrorMessage: "verification_unavailable" or "unsupported_state" or "plan_unavailable" }
            => StatusCodes.Status503ServiceUnavailable,
        { Status: not null and not "expired", ErrorMessage: null } => StatusCodes.Status200OK,
        _ => StatusCodes.Status400BadRequest
    };

    /// <summary>Signed checkout data for the app.</summary>
    /// <param name="ObfuscatedAccountId">The signed-in user's identifier.</param>
    /// <param name="PlayProductId">The Google Play subscription product ID.</param>
    /// <param name="BasePlanId">The Google Play base plan ID.</param>
    /// <param name="AccountLabel">The verified Minecraft name, masked email, or a generic label.</param>
    /// <param name="ExpiresAt">Expiration as Unix seconds.</param>
    /// <param name="Signature">Base64url-encoded DER ECDSA P-256 signature over the checkout data.</param>
    /// <param name="OfferId">The signed Google Play sale offer ID, or null when no sale applies.</param>
    /// <param name="DiscountPercent">The sale discount percentage from 1 to 99, or null when no sale applies.</param>
    public record PlaySubscriptionCheckout([property: Required] string ObfuscatedAccountId,
        [property: Required] string PlayProductId, [property: Required] string BasePlanId,
        [property: Required] string AccountLabel, [property: Required] long ExpiresAt, [property: Required] string Signature,
        string? OfferId = null, int? DiscountPercent = null);

    /// <summary>A purchase returned by Google Play Billing.</summary>
    /// <param name="PurchaseToken">The token returned by Play Billing.</param>
    /// <param name="SubscriptionId">The optional Google Play subscription product ID.</param>
    public record PlaySubscriptionPurchase(string? PurchaseToken, string? SubscriptionId)
    {
        /// <summary>Keeps the token out of logs: MVC writes action arguments at Trace level.</summary>
        public override string ToString() => nameof(PlaySubscriptionPurchase);
    }
}
