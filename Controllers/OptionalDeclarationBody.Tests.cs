using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

#pragma warning disable CS1591
/// <summary>
/// The subscription checkout must keep accepting the live website's POST (query only, no body, no Content-Type).
/// Binds the real <see cref="PremiumController"/> action with the production formatters (Newtonsoft) and
/// stops before the action runs.
/// </summary>
public class OptionalDeclarationBodyTests
{
    private const string Url = "/api/premium/subscription/premium?provider=stripe&country=DE";

    private static async Task<(HttpStatusCode status, string body)> Post(HttpContent content)
    {
        using var host = await new HostBuilder().ConfigureWebHost(web => web
            .UseTestServer()
            .ConfigureServices(ConfigureServices)
            .Configure(ConfigurePipeline)).StartAsync();
        var response = await host.GetTestClient().PostAsync(Url, content);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers(o => o.Filters.Add<ReturnBoundDeclaration>())
            .AddNewtonsoftJson()
            .AddApplicationPart(typeof(PremiumController).Assembly);
        services.AddSingleton<IControllerActivator, DependencylessPremiumController>();
        services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, AlwaysAuthenticated>("Test", _ => { });
        services.AddAuthorization();
    }

    private static void ConfigurePipeline(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(e => e.MapControllers());
    }

    [Test]
    public async Task LiveWebsiteRequestWithoutBodyOrContentTypeBindsNoDeclaration()
    {
        var websiteRequest = new ByteArrayContent(Array.Empty<byte>());

        var (status, body) = await Post(websiteRequest);

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.EqualTo("none"));
    }

    [Test]
    public async Task RequestWithoutContentBindsNoDeclaration()
    {
        var (status, body) = await Post(null);

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.EqualTo("none"));
    }

    [Test]
    public async Task EmptyJsonBodyBindsNoDeclaration()
    {
        var (status, body) = await Post(new StringContent("", Encoding.UTF8, "application/json"));

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.EqualTo("none"));
    }

    [Test]
    public async Task CamelCaseJsonBodyBindsTheDeclaration()
    {
        var json = "{\"declarationRequestId\":\"abc\",\"immediatePerformanceRequested\":true,\"legalLocale\":\"de\"}";

        var (status, body) = await Post(new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(body, Is.EqualTo("abc|True|de"));
    }

    /// <summary>Answers with the bound declaration instead of running the action.</summary>
    private sealed class ReturnBoundDeclaration : IActionFilter
    {
        public void OnActionExecuting(ActionExecutingContext context)
        {
            context.ActionArguments.TryGetValue("declaration", out var bound);
            context.Result = new ContentResult { Content = Describe(bound as SubscriptionDeclarationArgs) };
        }

        public void OnActionExecuted(ActionExecutedContext context) { }

        private static string Describe(SubscriptionDeclarationArgs args) => args == null
            ? "none"
            : $"{args.declarationRequestId}|{args.immediatePerformanceRequested}|{args.legalLocale}";
    }

    private sealed class DependencylessPremiumController : IControllerActivator
    {
        public object Create(ControllerContext context) =>
            new PremiumController(null, null, null, null, null, null, null, null, null) { ControllerContext = context };

        public void Release(ControllerContext context, object controller) { }
    }

    private sealed class AlwaysAuthenticated(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "buyer")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
