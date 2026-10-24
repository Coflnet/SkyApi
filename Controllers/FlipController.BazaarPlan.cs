using System;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Bazaar;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Api.Services.Mod;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Controller
{
    public partial class FlipController
    {
        /// <summary>
        /// Personal bazaar plan for the verified account of the caller: Active, Investing or both from one budget.
        /// Free users get three sampled rows and the number of hidden ones.
        /// </summary>
        /// <param name="request">Optional overrides, every field has a smart default</param>
        /// <param name="planService">The planner</param>
        [HttpPost("bazaar/plan")]
        [Authorize]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None, Duration = 0)]
        [ProducesResponseType(typeof(BazaarPlanResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<ActionResult<BazaarPlanResponse>> PostBazaarPlan([FromBody] BazaarPlanRequest request,
            [FromServices] BazaarPlanService planService)
        {
            var (user, tier) = await premiumTierService.GetUserAndTier(this);
            if (user == null)
                return Unauthorized();
            try
            {
                return Ok(await planService.Plan(user, tier, request, user.Id.ToString()));
            }
            catch (CoflnetException e) when (e.Slug == "plan_unavailable")
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { slug = "plan_unavailable", message = e.Message });
            }
            catch (CoflnetException e)
            {
                return BadRequest(new { slug = e.Slug, message = e.Message });
            }
        }

        /// <summary>
        /// Tracks one order of a returned plan in game as a guided bazaar flip goal.
        /// </summary>
        /// <param name="request">Quote and item of the order</param>
        /// <param name="planService">The planner holding the quotes</param>
        /// <param name="goals">The goal list of the user</param>
        [HttpPost("bazaar/plan/track")]
        [Authorize]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None, Duration = 0)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> PostBazaarPlanTrack([FromBody] BazaarPlanTrackRequest request,
            [FromServices] BazaarPlanService planService, [FromServices] GoalService goals)
        {
            var requestId = Guid.NewGuid().ToString("N")[..8];
            var userTimer = System.Diagnostics.Stopwatch.StartNew();
            var (user, _) = await premiumTierService.GetUserAndTier(this);
            var userLookupMs = userTimer.ElapsedMilliseconds;
            if (user == null)
                return Unauthorized();
            var userId = user.Id.ToString();
            var plan = planService.GetQuote(userId, request?.QuoteId);
            if (plan == null)
                return StatusCode(StatusCodes.Status409Conflict, new { slug = "quote_expired", message = "This plan expired, please refresh it." });
            var order = plan.Orders.Find(o => !o.IsSell && o.ItemTag == request.ItemTag);
            if (order == null)
                return NotFound(new { slug = "order_not_found", message = "The plan has no such order." });
            try
            {
                await goals.AddBazaarPlan(userId, order, plan.Quote, requestId, userLookupMs);
            }
            catch (CoflnetException e) when (e.Slug is "bazaar_goal_active" or "plan_order_incomplete")
            {
                return StatusCode(StatusCodes.Status409Conflict, new { slug = e.Slug, message = e.Message });
            }
            return NoContent();
        }

        /// <summary>
        /// Re-plans for the caller and tracks the buy order of one item in game. Accepts the Google token or the
        /// SkyCofl api key in the x-api-key header (only this route accepts the key alone).
        /// </summary>
        /// <param name="request">Item and the plan options</param>
        /// <param name="planService">The planner</param>
        /// <param name="goals">The goal list of the user</param>
        /// <param name="keys">Validates api keys</param>
        [HttpPost("bazaar/plan/track-item")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None, Duration = 0)]
        [ProducesResponseType(typeof(BazaarPlanTrackItemResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<ActionResult<BazaarPlanTrackItemResponse>> PostBazaarPlanTrackItem([FromBody] BazaarPlanTrackItemRequest request,
            [FromServices] BazaarPlanService planService, [FromServices] GoalService goals, [FromServices] ApiKeyService keys)
        {
            var requestId = Guid.NewGuid().ToString("N")[..8];
            var userTimer = System.Diagnostics.Stopwatch.StartNew();
            GoogleUser user;
            string tier;
            // an authenticated Google user wins, otherwise a non blank api key header is used
            if (User?.Identity?.IsAuthenticated == true)
                (user, tier) = await premiumTierService.GetUserAndTier(this);
            else if (Request.Headers.TryGetValue("x-api-key", out var apiKeyHeader) && !string.IsNullOrWhiteSpace(apiKeyHeader))
                (user, tier) = await ResolveApiKeyUser(keys);
            else
                return Unauthorized();
            if (user == null)
                return Unauthorized();
            var userLookupMs = userTimer.ElapsedMilliseconds;
            if (string.IsNullOrWhiteSpace(request?.ItemTag))
                return BadRequest(new { slug = "item_tag_required", message = "itemTag is required." });

            var userId = user.Id.ToString();
            BazaarPlanResponse plan;
            try
            {
                plan = await planService.Plan(user, tier, new BazaarPlanRequest
                {
                    Modes = request.Modes,
                    InvestingHorizonHours = request.InvestingHorizonHours,
                    BudgetCoins = request.BudgetCoins,
                    SellTaxRate = request.SellTaxRate
                }, userId, forTracking: true);
            }
            catch (CoflnetException e) when (e.Slug == "plan_unavailable")
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { slug = "plan_unavailable", message = e.Message });
            }
            catch (CoflnetException e)
            {
                return BadRequest(new { slug = e.Slug, message = e.Message });
            }

            var order = plan.Orders.Find(o => !o.IsSell && o.ItemTag == request.ItemTag);
            if (order == null && !BazaarPlanService.IsPaid(tier))
                // the same answer for hidden and missing items, otherwise the response reveals the hidden rows
                return StatusCode(StatusCodes.Status409Conflict, new { slug = "premium_required", message = "Tracking this item needs SkyCofl Starter Premium. Free accounts can track their three sampled orders." });
            if (order == null || plan.Quote == null)
                return StatusCode(StatusCodes.Status409Conflict, new { slug = "not_in_plan", message = $"{request.ItemTag} is not in your plan right now, refresh it." });
            try
            {
                await goals.AddBazaarPlan(userId, order, plan.Quote, requestId, userLookupMs);
            }
            catch (CoflnetException e) when (e.Slug is "bazaar_goal_active" or "plan_order_incomplete")
            {
                return StatusCode(StatusCodes.Status409Conflict, new { slug = e.Slug, message = e.Message });
            }
            var changed = request.ExpectedEntryPrice is { } expected
                && Math.Abs(order.EntryPrice - expected) > Math.Max(0.1, Math.Abs(expected) * 0.02);
            return Ok(new BazaarPlanTrackItemResponse
            {
                ItemTag = order.ItemTag,
                ItemName = order.ItemName,
                OriginalQuantity = order.OriginalQuantity,
                EntryPrice = order.EntryPrice,
                ExitPrice = order.ExitPrice,
                EstimatedNetProfit = order.EstimatedNetProfit,
                CapitalLockHorizonHours = order.CapitalLockHorizonHours,
                QuoteId = plan.Quote.QuoteId,
                PriceChanged = changed
            });
        }

        /// <summary>Validates the x-api-key header; the key is never logged, not even in error messages.</summary>
        private async Task<(GoogleUser user, string tier)> ResolveApiKeyUser(ApiKeyService keys)
        {
            try
            {
                var info = await keys.GetKeyInfo(this);
                if (info == null || info.IsActive != true || !int.TryParse(info.UserId, out var id) || id <= 0)
                    return (null, null);
                var user = new GoogleUser { Id = id };
                return (user, await premiumTierService.GetTier(user));
            }
            catch (CoflnetException e) when (e.Slug == "unauthorized")
            {
                return (null, null);
            }
            catch (Exception e)
            {
                // only the type, the message of a failed validation call may contain the request url with the key
                logger.LogWarning("Api key validation failed for bazaar plan tracking: {ExceptionType}", e.GetType().Name);
                return (null, null);
            }
        }
    }
}
