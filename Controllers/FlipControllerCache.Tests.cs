using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Services;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Items.Client.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Controller;

/// <summary>Contains flip controller cache tests.</summary>
[TestFixture]
public class FlipControllerCacheTests
{
    /// <summary>Performs the mayor flips do not share a premium response with non premium users operation.</summary>
    [Test]
    public async Task Mayor_flips_do_not_share_a_premium_response_with_non_premium_users()
    {
        using var host = CreateHost(out var premium);
        using var client = host.GetTestClient();

        var premiumResponse = await GetMayor(client, "premium");
        var nonPremiumResponse = await GetMayor(client, "basic");

        var premiumBody = await premiumResponse.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(premiumResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), premiumBody);
            Assert.That(nonPremiumResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(premium.Evaluations, Is.EqualTo(2));
        });
    }

    /// <summary>Performs the mayor flips keep standard authentication challenges operation.</summary>
    [Test]
    public async Task Mayor_flips_keep_standard_authentication_challenges()
    {
        using var host = CreateHost(out var premium);
        using var client = host.GetTestClient();

        var anonymousResponse = await client.GetAsync("/api/flip/mayor");
        var termsRejectedResponse = await GetMayor(client, "terms-rejected");

        Assert.That(anonymousResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(termsRejectedResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(premium.Evaluations, Is.Zero);
    }

    private static IHost CreateHost(out TestPremiumTierService premium)
    {
        premium = new TestPremiumTierService();
        var premiumService = premium;
        return new HostBuilder()
            .ConfigureWebHost(builder => builder
                .UseTestServer()
                .UseEnvironment(Environments.Production)
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseSetting("REDIS_HOST", "localhost")
                .UseSetting("PREMIUM_CLIENT_IDS", "cache-regression-test")
                .UseStartup<Startup>()
                .ConfigureTestServices(services =>
                {
                    services.RemoveAll<IHostedService>();
                    services.RemoveAll<IConfigureOptions<AuthenticationOptions>>();
                    services.AddAuthentication("CustomScheme")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                            "CustomScheme", _ => { });
                    services.AddSingleton<PremiumTierService>(premiumService);
                    services.AddSingleton(CreateProxy<IBazaarFlipperApi>());
                    services.AddSingleton(CreateProxy<IFleetApi>());
                    services.AddSingleton(CreateProxy<IItemsApi>());
                }))
            .Start();
    }

    private static async Task<HttpResponseMessage> GetMayor(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/flip/mayor");
        request.Headers.Add("GoogleToken", token);
        request.Headers.Add("X-ClientId", "cache-regression-test");
        return await client.SendAsync(request);
    }

    private static T CreateProxy<T>() where T : class =>
        DispatchProxy.Create<T, EmptyAsyncProxy>();

    /// <summary>Represents an empty async proxy.</summary>
    public class EmptyAsyncProxy : DispatchProxy
    {
        /// <summary>Performs the invoke operation.</summary>
        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            var returnType = targetMethod.ReturnType;
            if (returnType == typeof(Task))
                return Task.CompletedTask;
            if (returnType.IsGenericType
                && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GenericTypeArguments[0];
                var result = EmptyValue(resultType);
                return typeof(Task).GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, [result]);
            }
            return EmptyValue(returnType);
        }

        private static object EmptyValue(Type type)
        {
            if (type.IsArray)
                return Array.CreateInstance(type.GetElementType()!, 0);
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(IEnumerable<>)
                    || definition == typeof(ICollection<>)
                    || definition == typeof(IList<>))
                    return Array.CreateInstance(type.GenericTypeArguments[0], 0);
            }
            return Activator.CreateInstance(type);
        }
    }

    private sealed class TestPremiumTierService : PremiumTierService
    {
        public TestPremiumTierService() : base(null, null) { }

        public int Evaluations { get; private set; }

        public override Task<bool> HasPremium(ControllerBase controllerInstance)
        {
            Evaluations++;
            return Task.FromResult(
                controllerInstance.Request.Headers["GoogleToken"] == "premium");
        }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var token = Request.Headers["GoogleToken"].ToString();
            if (string.IsNullOrEmpty(token))
                return Task.FromResult(AuthenticateResult.NoResult());
            if (token == "terms-rejected")
                return Task.FromResult(AuthenticateResult.Fail("terms_acceptance_required"));

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, token)], Scheme.Name);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
