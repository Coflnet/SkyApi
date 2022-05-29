using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

public sealed class CacheControlAttribute : Attribute, IResultFilter
{
    public int Duration { get; set; } = 0;

    public CacheControlAttribute(int duration)
    {
        Duration = duration;
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

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