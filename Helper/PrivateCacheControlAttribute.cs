using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

/// <summary>
/// Custom cache control
/// </summary>
public sealed class CacheControlAttribute : Attribute, IResultFilter
{
    /// <summary>
    /// Cache duration in seconds
    /// </summary>
    public int Duration { get; set; } = 0;

    /// <summary>
    /// Creates a new instance of <see cref="CacheControlAttribute"/>.
    /// </summary>
    /// <param name="duration"></param>
    public CacheControlAttribute(int duration)
    {
        Duration = duration;
    }

    /// <summary>
    /// Called after the action result executes.
    /// </summary>
    /// <param name="context"></param>
    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    /// <summary>
    /// Called before the action result executes.
    /// </summary>
    /// <param name="context"></param>
    public void OnResultExecuting(ResultExecutingContext context)
    {
        context.HttpContext.Response.OnStarting(state =>
        {
            var httpContext = ((ResultExecutingContext)state).HttpContext;

            if (httpContext.Response.StatusCode != 200)
                httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
                {
                    Private = true,
                    NoCache = true,
                    NoStore = true,
                    MaxAge = TimeSpan.FromSeconds(0)
                };
            else
                httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
                {
                    Public = true,
                    MaxAge = TimeSpan.FromSeconds(Duration)
                };
            return Task.CompletedTask;
        }, context);
    }
}