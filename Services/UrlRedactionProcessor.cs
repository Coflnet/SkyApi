using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using OpenTelemetry;

namespace Coflnet.Sky.Api.Services;

/// <summary>Replaces the api key in the path of outbound key validation spans, the key must not reach the trace backend.</summary>
public class UrlRedactionProcessor : BaseProcessor<Activity>
{
    private static readonly Regex KeyInPath = new(@"(/ApiKey/validate/)[^/?#\s""]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] UrlTags = ["url.full", "http.url", "url.path", "http.target", "url.query"];

    /// <summary>Replaces the key in a text, returns the input when there is none.</summary>
    public static string Redact(string text) => string.IsNullOrEmpty(text) ? text : KeyInPath.Replace(text, "$1{redacted}");

    /// <inheritdoc/>
    public override void OnEnd(Activity data)
    {
        foreach (var tag in UrlTags)
        {
            if (data.GetTagItem(tag) is string value && Redact(value) is var redacted && redacted != value)
                data.SetTag(tag, redacted);
        }
        var name = Redact(data.DisplayName);
        if (name != data.DisplayName)
            data.DisplayName = name;
    }
}
