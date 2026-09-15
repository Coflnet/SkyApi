using System.Diagnostics;
using Prometheus;

namespace Coflnet.Sky.Api.Services;

internal static class BazaarTelemetry
{
    internal const string SourceName = "Coflnet.Sky.Api.Bazaar";
    internal static readonly ActivitySource Source = new(SourceName);
    internal static readonly Counter Uploads = Metrics.CreateCounter("sky_api_bazaar_uploads_total", "Direct price uploads by outcome", new CounterConfiguration { LabelNames = new[] { "result" } });
    internal static readonly Counter Reads = Metrics.CreateCounter("sky_api_bazaar_read_events_total", "Order snapshot read and fallback events", new CounterConfiguration { LabelNames = new[] { "result" } });
}
