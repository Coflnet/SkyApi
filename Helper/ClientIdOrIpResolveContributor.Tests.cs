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
                "IP_WHITELIST_BYPASS");

            var resolvedClientId = await contributor.ResolveClientAsync(context);

            Assert.That(resolvedClientId, Is.EqualTo("IP_WHITELIST_BYPASS"));
        }
    }
}