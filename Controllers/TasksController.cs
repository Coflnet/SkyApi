using Coflnet.Sky.Api.Helper;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Api.Services.Tasks;
using Coflnet.Sky.Api.Services.Tasks.Setups;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Coflnet.Sky.Api.Controller;

/// <summary>
/// Community money making task estimates
/// </summary>
[ApiController]
[Route("api/tasks")]
[ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any, NoStore = false)]
public class TasksController : ControllerBase
{
    private readonly IPublicTaskService taskService;

    /// <summary>
    /// Creates a new instance of <see cref="TasksController"/>
    /// </summary>
    public TasksController(IPublicTaskService taskService)
    {
        this.taskService = taskService;
    }

    /// <summary>
    /// All task estimates sorted by coins per hour, best first
    /// </summary>
    [HttpGet("")]
    [ProducesResponseType(typeof(List<PublicTaskEstimate>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<PublicTaskEstimate>>> GetAll()
    {
        try
        {
            return Ok(await taskService.GetAllAsync(HttpContext.RequestAborted));
        }
        catch (CoflnetException e)
        {
            return Unavailable(e);
        }
    }

    /// <summary>
    /// A single task estimate
    /// </summary>
    /// <param name="slug">Slug of the task, eg. t4-voidglooms-fd</param>
    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(PublicTaskEstimate), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PublicTaskEstimate>> GetBySlug(string slug)
    {
        try
        {
            var task = await taskService.GetBySlugAsync(slug, HttpContext.RequestAborted);
            if (task == null)
            {
                this.DisableResponseCaching();
                return NotFound(new ErrorResponse { Slug = "task_not_found", Message = $"No task with slug {slug}" });
            }
            return Ok(task);
        }
        catch (CoflnetException e)
        {
            return Unavailable(e);
        }
    }

    /// <summary>
    /// Gear setups of a task: which armor, tools and pets to use at different price points.
    /// The baseline and budget setups include their numbers, the mid and endgame setups are locked
    /// (numbers are null) and available with premium on the details endpoint. The endgame setup is never described here:
    /// it is a placeholder (tier endgame, locked, requiredTier premium_plus) without items, name or numbers.
    /// </summary>
    /// <param name="slug">Slug of the task, eg. t4-voidglooms-fd</param>
    [HttpGet("{slug}/setups")]
    [ProducesResponseType(typeof(PublicTaskSetups), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PublicTaskSetups>> GetSetups(string slug, [FromServices] ITaskSetupsService setups)
    {
        var result = await Serve(() => setups.GetPublicAsync(slug, HttpContext.RequestAborted));
        if (result.Result is OkObjectResult { Value: SetupsView { Degraded: true } })
            Response.Headers.CacheControl = "public, max-age=15";
        return UnwrapView(result);
    }

    /// <summary>
    /// Gear setups with coins per hour, buy cost, resale value and payback hours. Premium and premium plus only.
    /// The endgame setup is only included for premium plus, premium gets a placeholder (tier endgame, locked,
    /// requiredTier premium_plus) without items, name or numbers. The answer depends on the tier and is never cached by shared caches.
    /// </summary>
    /// <param name="slug">Slug of the task, eg. t4-voidglooms-fd</param>
    /// <param name="setups">Setups service</param>
    /// <param name="premiumTierService">Tier check</param>
    [HttpGet("{slug}/setups/details")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(PublicTaskSetups), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PublicTaskSetups>> GetSetupDetails(string slug,
        [FromServices] ITaskSetupsService setups, [FromServices] PremiumTierService premiumTierService)
    {
        this.DisableResponseCaching();
        var (denied, tier) = await CheckPremium(premiumTierService);
        if (denied != null)
            return denied;
        // the premium plus view is only reached after the tier gate, it has its own server side cache entry
        return UnwrapView(await Serve(() => tier == TaskSetupsComposer.PremiumPlusTier
            ? setups.GetPremiumPlusAsync(slug, HttpContext.RequestAborted)
            : setups.GetPremiumAsync(slug, HttpContext.RequestAborted)));
    }

    private async Task<(ObjectResult Denied, string Tier)> CheckPremium(PremiumTierService premiumTierService)
    {
        GoogleUser user;
        string tier;
        try
        {
            (user, tier) = await premiumTierService.GetUserAndTier(this);
        }
        catch (CoflnetException e) when (e.Slug == "invalid_token")
        {
            return (Error(StatusCodes.Status401Unauthorized, "invalid_token", "The login token is invalid or expired"), null);
        }
        catch (Exception) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            // eg. the payments service is down, don't turn that into a 500
            return (Error(StatusCodes.Status503ServiceUnavailable, "premium_check_unavailable", "The premium status could not be checked, try again later"), null);
        }
        if (user == null)
            return (HasAuthHeader(premiumTierService)
                ? Error(StatusCodes.Status401Unauthorized, "invalid_token", "The login token is invalid or expired")
                : Error(StatusCodes.Status401Unauthorized, "login_required", "Log in to see the details"), null);
        if (tier is "premium" or "premium_plus")
            return (null, tier);
        return (Error(StatusCodes.Status403Forbidden, "premium_required", "Gear setup details require premium"), null);
    }

    /// <summary>GetUserOrDefault accepts the token in either header</summary>
    private bool HasAuthHeader(PremiumTierService premiumTierService)
        => !string.IsNullOrEmpty(Request.Headers[premiumTierService.HeaderName].ToString())
            || !string.IsNullOrEmpty(Request.Headers["Authorization"].ToString());

    /// <summary>Serve returns the view with its degraded flag so the action can pick the cache time, the body is only the setups</summary>
    private static ActionResult<PublicTaskSetups> UnwrapView(ActionResult<SetupsView> result)
        => result.Result is OkObjectResult { Value: SetupsView view } ? new OkObjectResult(view.Setups) : result.Result;

    private async Task<ActionResult<SetupsView>> Serve(System.Func<Task<SetupsView>> load)
    {
        try
        {
            return Ok(await load());
        }
        catch (CoflnetException e) when (e.Slug is "task_not_found" or "gear_setups_not_found")
        {
            this.DisableResponseCaching();
            return Error(StatusCodes.Status404NotFound, e.Slug, e.Message);
        }
        catch (CoflnetException e)
        {
            return Unavailable(e);
        }
    }

    private static ObjectResult Error(int status, string slug, string message)
        => new(new ErrorResponse { Slug = slug, Message = message }) { StatusCode = status };

    private ObjectResult Unavailable(CoflnetException e)
    {
        this.DisableResponseCaching();
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse { Slug = e.Slug, Message = e.Message });
    }
}
