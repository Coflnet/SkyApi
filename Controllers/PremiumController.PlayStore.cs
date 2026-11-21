using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Client;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

#nullable enable annotations

namespace Coflnet.Sky.Api.Controller;

public partial class PremiumController
{
    /// <summary>Checks whether the signed-in user can start a Google Play subscription.</summary>
    /// <param name="productSlug">SkyCofl subscription plan slug.</param>
    /// <param name="playApi">Payments Google Play subscription client.</param>
    /// <response code="200">Identifiers to use when opening Play Billing.</response>
    /// <response code="400">The plan is unavailable or blocked by an existing subscription.</response>
    /// <response code="401">The user is unauthenticated.</response>
    /// <response code="428">The user must accept the current SkyCofl agreement.</response>
    /// <response code="503">The terms publication time is unavailable.</response>
    [HttpPost("premium/subscription/{productSlug}/playstore")]
    [Authorize]
    [ProducesResponseType<PlaySubscriptionCheckout>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PlaySubscriptionCheckout>> StartPlayStoreSubscription(string productSlug,
        [FromServices] IGooglePlaySubscriptionApi playApi)
    {
        var user = await GetUserOrDefault(authenticatedOnly: true);
        if (user == null)
            return Unauthorized("no googletoken header");
        if (await MustRejectNewContract(user.Id, configuration))
            return TermsAcceptanceRequired();

        var userId = user.Id.ToString();
        var eligibility = await playApi.ApiGoogleplaySubscriptionEligibilityGetAsync(userId, productSlug);
        if (eligibility.Eligible)
            return Ok(new PlaySubscriptionCheckout(userId, eligibility.PlayProductId, eligibility.BasePlanId));

        var message = eligibility.Reason == "active_subscription"
            ? "You already have an active subscription in this plan's line."
            : "This subscription plan is unavailable in Google Play.";
        if (!string.IsNullOrWhiteSpace(eligibility.BlockingProvider))
            message += $" The blocking subscription is billed by {eligibility.BlockingProvider}.";
        throw new CoflnetException(eligibility.Reason ?? "plan_unavailable", message);
    }

    /// <summary>Verifies a Google Play subscription purchase and grants the paid period.</summary>
    /// <param name="productSlug">SkyCofl plan used to start checkout. Google Play supplies the verified plan.</param>
    /// <param name="purchase">The purchase token and product ID returned by Play Billing.</param>
    /// <param name="playApi">Payments Google Play subscription client.</param>
    /// <response code="200">The purchase was granted or recorded. Keep an ungranted purchase for later completion.</response>
    /// <response code="400">The purchase was rejected.</response>
    /// <response code="401">The user is unauthenticated.</response>
    /// <response code="409">The purchase is processing. Retry later.</response>
    /// <response code="503">Verification is unavailable. Retry later.</response>
    [HttpPost("premium/subscription/{productSlug}/playstore/complete")]
    [Authorize]
    [ProducesResponseType<GooglePlaySubscriptionResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<GooglePlaySubscriptionResult>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<GooglePlaySubscriptionResult>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<GooglePlaySubscriptionResult>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GooglePlaySubscriptionResult>> CompletePlayStoreSubscription(string productSlug,
        [FromBody] PlaySubscriptionPurchase purchase, [FromServices] IGooglePlaySubscriptionApi playApi)
    {
        var user = await GetUserOrDefault(authenticatedOnly: true);
        if (user == null)
            return Unauthorized("no googletoken header");
        if (string.IsNullOrWhiteSpace(purchase?.PurchaseToken))
            return BadRequest(new GooglePlaySubscriptionResult(false, "invalid_purchase", "A purchase token is required."));

        var userId = user.Id.ToString();
        var subscriptionId = string.IsNullOrWhiteSpace(purchase.SubscriptionId) ? productSlug : purchase.SubscriptionId;
        var request = new GooglePlaySubscriptionRequest(subscriptionId, purchase.PurchaseToken, "com.coflnet.sky", userId);
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
            logger?.LogWarning("Google Play subscription completion failed for user {UserId} ({ExceptionType}, HTTP {StatusCode})",
                userId, e.GetType().Name, (e as ApiException)?.ErrorCode);
            result = null;
        }

        result ??= new GooglePlaySubscriptionResult(false, "verification_unavailable", "verification_unavailable");
        var statusCode = GetPlaySubscriptionStatusCode(result);
        if (statusCode != StatusCodes.Status200OK)
            result.Status = result.ErrorMessage ?? result.Status ?? "invalid_purchase";
        logger?.LogInformation("Google Play subscription completion for user {UserId}: {Status}", userId, result.Status);
        return StatusCode(statusCode, result);
    }

    private static int GetPlaySubscriptionStatusCode(GooglePlaySubscriptionResult result) => result switch
    {
        { IsValid: true } => StatusCodes.Status200OK,
        { Status: "processing" } => StatusCodes.Status409Conflict,
        { Status: "verification_unavailable" or "unsupported_state" }
            or { ErrorMessage: "verification_unavailable" or "unsupported_state" } => StatusCodes.Status503ServiceUnavailable,
        { Status: not null and not "expired", ErrorMessage: null } => StatusCodes.Status200OK,
        _ => StatusCodes.Status400BadRequest
    };

    /// <summary>Identifiers the app passes to Google Play Billing.</summary>
    /// <param name="ObfuscatedAccountId">The signed-in user's identifier.</param>
    /// <param name="PlayProductId">The Google Play subscription product ID.</param>
    /// <param name="BasePlanId">The Google Play base plan ID.</param>
    public record PlaySubscriptionCheckout(string ObfuscatedAccountId, string PlayProductId, string BasePlanId);

    /// <summary>A purchase returned by Google Play Billing.</summary>
    /// <param name="PurchaseToken">The token returned by Play Billing.</param>
    /// <param name="SubscriptionId">The optional Google Play subscription product ID.</param>
    public record PlaySubscriptionPurchase(string? PurchaseToken, string? SubscriptionId)
    {
        /// <summary>Keeps the token out of logs: MVC writes action arguments at Trace level.</summary>
        public override string ToString() => nameof(PlaySubscriptionPurchase);
    }
}
