using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models.Mod;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.Api.Services.Description;

/// <summary>Outcome of an instance settings lookup</summary>
public enum InstanceSettingsStatus
{
    /// <summary>Settings were returned</summary>
    Found,
    /// <summary>The instance has no stored settings (204/404)</summary>
    None,
    /// <summary>The lookup failed (error, timeout, exception), the state is unknown</summary>
    Failed
}

/// <summary>Result of <see cref="IInstanceSettingsClient.GetDescription"/></summary>
/// <param name="Status">what happened</param>
/// <param name="Setting">the settings, only set for <see cref="InstanceSettingsStatus.Found"/></param>
public record InstanceSettingsResult(InstanceSettingsStatus Status, DescriptionSetting Setting = null)
{
    /// <summary>No settings stored</summary>
    public static readonly InstanceSettingsResult None = new(InstanceSettingsStatus.None);
    /// <summary>Lookup failed</summary>
    public static readonly InstanceSettingsResult Failed = new(InstanceSettingsStatus.Failed);
    /// <summary>Settings found</summary>
    public static InstanceSettingsResult Found(DescriptionSetting setting) =>
        setting == null ? None : new(InstanceSettingsStatus.Found, setting);
}

/// <summary>
/// Loads the lore settings that signed-out users stored per mod instance in SkyModCommands
/// </summary>
public interface IInstanceSettingsClient
{
    /// <summary>
    /// Gets the instance description settings
    /// </summary>
    /// <param name="sessionId">hashed connection id (name + raw session id), never a client supplied hash</param>
    /// <param name="cancellationToken">cancellation</param>
    /// <returns>found, none or failed</returns>
    Task<InstanceSettingsResult> GetDescription(string sessionId, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IInstanceSettingsClient"/> backed by the internal SkyModCommands endpoint
/// </summary>
public class InstanceSettingsClient : IInstanceSettingsClient
{
    /// <summary>Name of the registered http client</summary>
    public const string HttpClientName = "ModCommands";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WarningInterval = TimeSpan.FromMinutes(1);

    private readonly HttpClient httpClient;
    private readonly ILogger<InstanceSettingsClient> logger;
    private long lastWarningTicks;

    /// <summary>Creates a new instance</summary>
    public InstanceSettingsClient(IHttpClientFactory httpClientFactory, ILogger<InstanceSettingsClient> logger)
    {
        httpClient = httpClientFactory.CreateClient(HttpClientName);
        this.logger = logger;
    }

    /// <inheritdoc/>
    public async Task<InstanceSettingsResult> GetDescription(string sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sessionId))
            return InstanceSettingsResult.None;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            return await Fetch(sessionId, timeout.Token);
        }
        catch (Exception e)
        {
            LogRateLimited(e);
            return InstanceSettingsResult.Failed;
        }
    }

    private async Task<InstanceSettingsResult> Fetch(string sessionId, CancellationToken token)
    {
        var url = $"api/instance/description?sessionId={Uri.EscapeDataString(sessionId)}";
        using var response = await httpClient.GetAsync(url, token);
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound)
            return InstanceSettingsResult.None;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Instance description returned {(int)response.StatusCode}");
        var body = await response.Content.ReadAsStringAsync(token);
        return InstanceSettingsResult.Found(JsonConvert.DeserializeObject<DescriptionSetting>(body));
    }

    private void LogRateLimited(Exception e)
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref lastWarningTicks);
        if (now - last < WarningInterval.Ticks || Interlocked.CompareExchange(ref lastWarningTicks, now, last) != last)
        {
            logger.LogDebug(e, "Instance description lookup failed");
            return;
        }
        logger.LogWarning(e, "Instance description lookup failed; using defaults (further failures logged at debug for {Interval})", WarningInterval);
    }
}
