using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using AspNetCoreRateLimit;
using Coflnet.Sky.Api.Helper;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services
{
    public class ScrapingDetectionServiceTests
    {
        [Test]
        public void IsBanned_ReturnsFalseForMappedClusterIpv4InWhitelist()
        {
            var service = new ScrapingDetectionService(
                NullLogger<ScrapingDetectionService>.Instance,
                clientRateLimitOptions: Options.Create(new ClientRateLimitOptions()),
                clientRateLimitPolicies: Options.Create(new ClientRateLimitPolicies()),
                ipRateLimitOptions: Options.Create(new IpRateLimitOptions
                {
                    IpWhitelist = new List<string> { "10.0.0.0/8" }
                }));

            var bannedIpsField = typeof(ScrapingDetectionService).GetField("_bannedIps", BindingFlags.Instance | BindingFlags.NonPublic);
            var bannedIps = (ConcurrentDictionary<string, bool>)bannedIpsField.GetValue(service);
            bannedIps.TryAdd("10.42.197.199", true);

            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:10.42.197.199");

            Assert.That(service.IsBanned(context), Is.False);
        }

        [Test]
        public void IsBanned_ReturnsFalseForEndpointWhitelistedIpOnlyOnThatEndpoint()
        {
            var service = new ScrapingDetectionService(
                NullLogger<ScrapingDetectionService>.Instance,
                clientRateLimitOptions: Options.Create(new ClientRateLimitOptions()),
                clientRateLimitPolicies: Options.Create(new ClientRateLimitPolicies()),
                ipRateLimitOptions: Options.Create(new IpRateLimitOptions()),
                endpointIpRateLimitOptions: Options.Create(new EndpointIpRateLimitOptions
                {
                    IpWhitelist = new Dictionary<string, List<string>>
                    {
                        ["/api/price/nbt"] = new List<string> { "74.91.113.221" }
                    }
                }));

            var bannedIpsField = typeof(ScrapingDetectionService).GetField("_bannedIps", BindingFlags.Instance | BindingFlags.NonPublic);
            var bannedIps = (ConcurrentDictionary<string, bool>)bannedIpsField.GetValue(service);
            bannedIps.TryAdd("74.91.113.221", true);

            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("74.91.113.221");
            context.Request.Path = "/api/price/nbt";

            Assert.That(service.IsBanned(context), Is.False);

            context.Request.Path = "/api/auctions";

            Assert.That(service.IsBanned(context), Is.True);
        }
    }
}
