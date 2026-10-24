using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Bazaar;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Api.Services.Mod;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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
            catch (CoflnetException e) when (e.Slug == "bazaar_goal_active")
            {
                return StatusCode(StatusCodes.Status409Conflict, new { slug = e.Slug, message = e.Message });
            }
            return NoContent();
        }
    }
}
