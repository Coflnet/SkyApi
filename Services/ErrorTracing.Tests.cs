using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Coflnet.Sky.Api;
using Coflnet.Sky.Api.Controller;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OpenTelemetry;
using OpenTelemetry.Trace;

using Coflnet.Sky.Core;

namespace SkyApi.Services;

/// <summary>Support references must identify a recorded error even for unsampled requests.</summary>
public class ErrorTracingTests
{
    /// <summary>Uses the original routed action after exception handling clears the current endpoint.</summary>
    [TestCase("/api/premium/subscription/premium/playstore/complete", false)]
    [TestCase("/API/PREMIUM/SUBSCRIPTION/premium/PLAYSTORE/COMPLETE", false)]
    [TestCase("/api/premium/subscription/premium/playstore/complete/", false)]
    [TestCase("/api/premium/subscription/premium/playstore", true)]
    [TestCase("/api/topup/playstore/complete", true)]
    [TestCase("/api/topup/rates", true)]
    public async Task RequestBodyCaptureUsesOriginalRoutedAction(string path, bool capture)
    {
        using var host = await new HostBuilder().ConfigureWebHost(web => web
            .UseTestServer()
            .ConfigureServices(services => services.AddControllers().AddApplicationPart(typeof(PremiumController).Assembly))
            .Configure(app =>
            {
                app.UseExceptionHandler(errors => errors.Run(context =>
                {
                    Assert.That(context.GetEndpoint(), Is.Null);
                    Assert.That(context.Features.Get<IExceptionHandlerFeature>()?.Endpoint?.Metadata
                        .GetMetadata<ControllerActionDescriptor>(), Is.Not.Null);
                    return context.Response.WriteAsync(Startup.ShouldCaptureRequestBody(context).ToString());
                }));
                app.UseRouting();
                app.Use(_ => context => throw new InvalidOperationException("test failure"));
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            })).StartAsync();

        using var response = await host.GetTestClient().PostAsync(path, null);

        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(capture.ToString()));
    }

    /// <summary>Requests without an original action retain the shared handler's default capture behavior.</summary>
    [Test]
    public void RequestBodyCaptureDefaultsToEnabledWithoutExceptionEndpoint() =>
        Assert.That(Startup.ShouldCaptureRequestBody(new DefaultHttpContext()), Is.True);

    /// <summary>Exercises the error response and completed spans with local and remote parents.</summary>
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ReturnedTraceIdIdentifiesRecordedError(bool remoteParent, bool sampledParent)
    {
        using var source = new ActivitySource($"error-test-{Guid.NewGuid()}");
        // ASP.NET keeps a request activity for propagation even when tracing drops it.
        using var requestListener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
                options.Name == "request" ? ActivitySamplingResult.PropagationData : ActivitySamplingResult.None
        };
        ActivitySource.AddActivityListener(requestListener);
        var spans = new List<Activity>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(source.Name)
            .SetSampler(new JaegerSercieExtention.ErrorPreservingSampler(new AlwaysOffSampler()))
            .AddProcessor(new CaptureProcessor(spans))
            .Build();
        using var services = new ServiceCollection().AddSingleton(source).BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        ErrorHandler.Add(NullLogger.Instance, app, "test");
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Request.Path = "/failed-request";
        context.Response.Body = new MemoryStream();
        context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature
        {
            Error = new InvalidOperationException("test failure"), Path = context.Request.Path
        });
        var parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(),
            sampledParent ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None, isRemote: remoteParent);
        using var request = source.StartActivity("request", ActivityKind.Server, parent);
        Assert.That(request, Is.Not.Null);
        Assert.That(request.Recorded, Is.EqualTo(sampledParent));
        using (var ordinary = source.StartActivity("ordinary"))
            Assert.That(ordinary?.Recorded ?? false, Is.EqualTo(sampledParent));

        await app.Build()(context);

        var error = spans.Find(span => span.OperationName == "error");
        Assert.That(error, Is.Not.Null, "The error must reach the processor even under an unsampled request.");
        Assert.That(error.Recorded, Is.True);
        Assert.That(error.Status, Is.EqualTo(ActivityStatusCode.Error));
        Assert.That(request.Status, Is.EqualTo(ActivityStatusCode.Error));
        Assert.That(error.TraceId, Is.EqualTo(request.TraceId));
        Assert.That(error.ParentSpanId, Is.EqualTo(request.SpanId));
        Assert.That(error.Events, Has.Some.Property("Name").EqualTo("error"));
        context.Response.Body.Position = 0;
        var response = JObject.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync());
        Assert.That(context.Response.StatusCode, Is.EqualTo(500));
        Assert.That(response["trace"].Value<string>(), Does.EndWith("." + error.TraceId));
        Assert.That(response["message"].Value<string>(), Does.Contain(error.TraceId.ToString()));
        Assert.That(response["slug"].Value<string>(), Is.EqualTo("internal_error"));
    }

    private sealed class CaptureProcessor(List<Activity> spans) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity activity) => spans.Add(activity);
    }
}
