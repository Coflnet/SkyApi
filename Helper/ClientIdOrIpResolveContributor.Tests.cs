using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Helper
{
    /// <summary>Contains client ID and IP resolution tests.</summary>
    public class ClientIdOrIpResolveContributorTests
    {
        /// <summary>Resolves client async uses whitelist bypass for mapped cluster ipv4.</summary>
        [Test]
        public async Task ResolveClientAsync_UsesWhitelistBypassForMappedClusterIpv4()
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:10.42.197.199");

            var accessor = new HttpContextAccessor { HttpContext = context };
            var contributor = new ClientIdOrIpResolveContributor(
                accessor,
                "X-ClientId",
                "CF-Connecting-IP",
                new IpRateLimitOptions
                {
                    IpWhitelist = new List<string> { "10.0.0.0/8" }
                },
                new EndpointIpRateLimitOptions(),
                new ClientRateLimitOptions(),
                new ClientRateLimitPolicies(),
                "IP_WHITELIST_BYPASS");

            var resolvedClientId = await contributor.ResolveClientAsync(context);

            Assert.That(resolvedClientId, Is.EqualTo("IP_WHITELIST_BYPASS"));
        }

        /// <summary>Resolves client async bypasses listed ip only for configured endpoint.</summary>
        [Test]
        public async Task ResolveClientAsync_BypassesListedIpOnlyForConfiguredEndpoint()
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("74.91.113.221");
            context.Request.Path = "/api/price/nbt";
            context.Request.Headers["X-ClientId"] = "auction-uploader";

            var contributor = new ClientIdOrIpResolveContributor(
                new HttpContextAccessor { HttpContext = context },
                "X-ClientId",
                "CF-Connecting-IP",
                new IpRateLimitOptions(),
                new EndpointIpRateLimitOptions
                {
                    IpWhitelist = new Dictionary<string, List<string>>
                    {
                        ["/api/price/nbt"] = new List<string> { "74.91.113.221" }
                    }
                },
                new ClientRateLimitOptions(),
                new ClientRateLimitPolicies
                {
                    ClientRules = new List<ClientRateLimitPolicy>
                    {
                        new ClientRateLimitPolicy { ClientId = "auction-uploader" }
                    }
                },
                "IP_WHITELIST_BYPASS");

            Assert.That(await contributor.ResolveClientAsync(context), Is.EqualTo("IP_WHITELIST_BYPASS"));

            context.Request.Path = "/api/auctions";

            Assert.That(await contributor.ResolveClientAsync(context), Is.EqualTo("auction-uploader"));
        }

        /// <summary>Resolves client async unknown client ids use same ip bucket.</summary>
        [Test]
        public async Task ResolveClientAsync_UnknownClientIdsUseSameIpBucket()
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
            var contributor = new ClientIdOrIpResolveContributor(
                new HttpContextAccessor { HttpContext = context },
                "X-ClientId",
                "CF-Connecting-IP",
                new IpRateLimitOptions(),
                new EndpointIpRateLimitOptions(),
                new ClientRateLimitOptions(),
                new ClientRateLimitPolicies(),
                "IP_WHITELIST_BYPASS");

            context.Request.Headers["X-ClientId"] = "missing-client-one";
            Assert.That(await contributor.ResolveClientAsync(context), Is.EqualTo("ip:203.0.113.10"));

            context.Request.Headers["X-ClientId"] = "missing-client-two";
            Assert.That(await contributor.ResolveClientAsync(context), Is.EqualTo("ip:203.0.113.10"));
        }

        /// <summary>Resolves client async accepts whitelisted and policy clients.</summary>
        [Test]
        public async Task ResolveClientAsync_AcceptsWhitelistedAndPolicyClients()
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
            var contributor = new ClientIdOrIpResolveContributor(
                new HttpContextAccessor { HttpContext = context },
                "X-ClientId",
                "CF-Connecting-IP",
                new IpRateLimitOptions(),
                new EndpointIpRateLimitOptions(),
                new ClientRateLimitOptions
                {
                    ClientWhitelist = new List<string> { "whitelisted-client" }
                },
                new ClientRateLimitPolicies
                {
                    ClientRules = new List<ClientRateLimitPolicy>
                    {
                        new ClientRateLimitPolicy { ClientId = "policy-client" }
                    }
                },
                "IP_WHITELIST_BYPASS");

            context.Request.Headers["X-ClientId"] = "whitelisted-client";
            Assert.That(await contributor.ResolveClientAsync(context), Is.EqualTo("whitelisted-client"));

            context.Request.Headers["X-ClientId"] = "policy-client";
            Assert.That(await contributor.ResolveClientAsync(context), Is.EqualTo("policy-client"));
        }

        /// <summary>Resolves client async does not trust spoofed whitelist bypass client id.</summary>
        [Test]
        public async Task ResolveClientAsync_DoesNotTrustSpoofedWhitelistBypassClientId()
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
            context.Request.Headers["X-ClientId"] = "IP_WHITELIST_BYPASS";
            var contributor = new ClientIdOrIpResolveContributor(
                new HttpContextAccessor { HttpContext = context },
                "X-ClientId",
                "CF-Connecting-IP",
                new IpRateLimitOptions(),
                new EndpointIpRateLimitOptions(),
                new ClientRateLimitOptions
                {
                    ClientWhitelist = new List<string> { "IP_WHITELIST_BYPASS" }
                },
                new ClientRateLimitPolicies(),
                "IP_WHITELIST_BYPASS");

            Assert.That(await contributor.ResolveClientAsync(context), Is.EqualTo("ip:203.0.113.10"));
        }
    }
}
