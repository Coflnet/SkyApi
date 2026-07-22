using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Helper
{
    public class ClientIdOrIpResolveContributorTests
    {
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
                "IP_WHITELIST_BYPASS");

            var resolvedClientId = await contributor.ResolveClientAsync(context);

            Assert.That(resolvedClientId, Is.EqualTo("IP_WHITELIST_BYPASS"));
        }

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
                "IP_WHITELIST_BYPASS");

            Assert.That(await contributor.ResolveClientAsync(context), Is.EqualTo("IP_WHITELIST_BYPASS"));

            context.Request.Path = "/api/auctions";

            Assert.That(await contributor.ResolveClientAsync(context), Is.EqualTo("auction-uploader"));
        }
    }
}
