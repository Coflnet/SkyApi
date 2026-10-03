using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Models.Mod;

/// <summary>
/// Verdict for a mod jar returned by the upstream isthisarat.com signature lookup.
/// Deserialized tolerantly (case-insensitive, unknown fields ignored) since the real
/// success shape could not be captured while the upstream sat behind a Cloudflare challenge.
/// </summary>
public class RatCheckingResponse
{
    /// <summary>
    /// Verdict text for the scanned jar as reported by isthisarat.com.
    /// The website matches on "Yes", "No" and "No matching signature"
    /// </summary>
    [JsonProperty("rat")]
    public string Rat { get; set; }

    /// <summary>
    /// Md5 hash isthisarat.com matched the lookup against
    /// </summary>
    [JsonProperty("md5return")]
    public string Md5Return { get; set; }
}
