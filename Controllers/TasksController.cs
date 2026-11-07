using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Tasks;
using Coflnet.Sky.Api.Services.Tasks;
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
                NoStore();
                return NotFound(new ErrorResponse { Slug = "task_not_found", Message = $"No task with slug {slug}" });
            }
            return Ok(task);
        }
        catch (CoflnetException e)
        {
            return Unavailable(e);
        }
    }

    private void NoStore() => Response.Headers.CacheControl = "no-store";

    private ObjectResult Unavailable(CoflnetException e)
    {
        NoStore();
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse { Slug = e.Slug, Message = e.Message });
    }
}
