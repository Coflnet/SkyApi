using System.Diagnostics;
#nullable enable
namespace Coflnet.Sky.Core;

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
        return activity?.AddEvent(new ActivityEvent("log", System.DateTimeOffset.Now, new ActivityTagsCollection(new[] { new KeyValuePair<string, object?>("message", message.Truncate(38_000)) })));
    }
}
#nullable restore