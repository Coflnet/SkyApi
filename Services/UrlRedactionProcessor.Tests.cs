using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services;

/// <summary>The api key is part of the validation url path and must reach neither logs nor traces.</summary>
[TestFixture]
public class UrlRedactionProcessorTests
{
    private const string Key = "sk-very-secret-key-1234567890";

    /// <summary>Span url attributes and names lose the key.</summary>
    [Test]
    public void Spans_of_key_validation_are_redacted()
    {
        using var source = new ActivitySource("redaction-test");
        using var listener = new ActivityListener { ShouldListenTo = _ => true, Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData };
        ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("GET /ApiKey/validate/" + Key);
        activity.SetTag("url.full", "http://commands:8008/ApiKey/validate/" + Key + "?x=1");
        activity.SetTag("http.url", "http://commands:8008/apikey/validate/" + Key);
        activity.SetTag("url.path", "/ApiKey/validate/" + Key);
        activity.SetTag("http.target", "/ApiKey/validate/" + Key);
        activity.SetTag("url.other", "http://commands:8008/items/ITEM_A");

        new UrlRedactionProcessor().OnEnd(activity);

        var all = string.Join(" ", activity.TagObjects) + activity.DisplayName;
        Assert.Multiple(() =>
        {
            Assert.That(all, Does.Not.Contain(Key));
            Assert.That(activity.GetTagItem("url.full"), Is.EqualTo("http://commands:8008/ApiKey/validate/{redacted}?x=1"));
            Assert.That(activity.GetTagItem("url.path"), Is.EqualTo("/ApiKey/validate/{redacted}"));
            Assert.That(activity.DisplayName, Is.EqualTo("GET /ApiKey/validate/{redacted}"));
            Assert.That(activity.GetTagItem("url.other"), Is.EqualTo("http://commands:8008/items/ITEM_A"));
        });
    }

    /// <summary>Other urls stay untouched.</summary>
    [Test]
    public void Other_urls_are_not_changed()
    {
        Assert.That(UrlRedactionProcessor.Redact("/api/flip/bazaar/plan"), Is.EqualTo("/api/flip/bazaar/plan"));
        Assert.That(UrlRedactionProcessor.Redact(null), Is.Null);
    }

    /// <summary>The outbound http client category (which logs the url) is below Warning for the default and the OpenTelemetry provider.</summary>
    [Test]
    public void HttpClient_log_category_is_filtered_below_warning()
    {
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(System.AppContext.BaseDirectory, "appsettings.json")).Build();
        var general = new GeneralCapture();
        var otel = new OtelCapture();
        using var provider = new ServiceCollection().AddLogging(b => b
            .AddConfiguration(config.GetSection("Logging")).AddProvider(general).AddProvider(otel)).BuildServiceProvider();
        var factory = provider.GetRequiredService<ILoggerFactory>();

        foreach (var category in new[] { "System.Net.Http.HttpClient.IApiKeyApi.LogicalHandler", "System.Net.Http.HttpClient.IApiKeyApi.ClientHandler", "System.Net.Http.HttpClient.Default.LogicalHandler" })
        {
            var logger = factory.CreateLogger(category);
            logger.LogInformation("Sending HTTP request GET http://x/ApiKey/validate/{Key}", "k");
            logger.LogWarning("warn");
        }
        factory.CreateLogger("Coflnet.Sky.Api.Something").LogInformation("kept");

        Assert.Multiple(() =>
        {
            Assert.That(general.Messages, Is.EqualTo(new[] { "warn", "warn", "warn", "kept" }));
            Assert.That(otel.Messages, Is.EqualTo(new[] { "warn", "warn", "warn", "kept" }));
        });
    }

    [ProviderAlias("Console")]
    private sealed class GeneralCapture : Capture { }

    [ProviderAlias("OpenTelemetry")]
    private sealed class OtelCapture : Capture { }

    private abstract class Capture : ILoggerProvider
    {
        public List<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new L(Messages);
        public void Dispose() { }
        private sealed class L(List<string> messages) : ILogger
        {
            public System.IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, System.Exception exception, System.Func<TState, System.Exception, string> formatter) => messages.Add(formatter(state, exception));
        }
    }
}
