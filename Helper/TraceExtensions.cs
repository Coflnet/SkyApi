using System;
using System.Diagnostics;
#nullable enable
namespace Coflnet.Sky.Core;

/// <summary>Provides tracing extension methods.</summary>
public static class TraceExtensions
{
    /// <summary>
    /// Log a string to the activity
    /// </summary>
    /// <param name="activity"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public static Activity? Log(this Activity? activity, string message)
    {
        if (activity is not { IsAllDataRequested: true })
            return activity;
        return activity.AddEvent(new ActivityEvent("log", System.DateTimeOffset.Now, new ActivityTagsCollection(new[] { new KeyValuePair<string, object?>("message", message.Truncate(38_000)) })));
    }

    /// <summary>
    /// Log a lazily built string to the activity, only building it when the activity is recorded
    /// </summary>
    /// <param name="activity"></param>
    /// <param name="message">Factory that is only invoked when the activity records data</param>
    /// <returns></returns>
    public static Activity? Log(this Activity? activity, Func<string> message)
    {
        if (activity is not { IsAllDataRequested: true })
            return activity;
        return activity.Log(message());
    }
}
#nullable restore
