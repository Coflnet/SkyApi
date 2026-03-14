using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services
{
    public interface IScrapingDetectionService
    {
        bool IsBanned(HttpContext context);
        Task RecordRateLimitExceededAsync(HttpContext context);
    }

    public class ScrapingDetectionService : IScrapingDetectionService
    {
        private readonly ILogger<ScrapingDetectionService> _logger;
        // Keep track of IP addresses and how many times they have exceeded limits.
        private readonly ConcurrentDictionary<string, int> _ipViolationCounts = new ConcurrentDictionary<string, int>();
        // Keep track of subnets (first 3 octets of IPv4 or /48 for IPv6) to detect proxy rotation.
        private readonly ConcurrentDictionary<string, int> _subnetViolationCounts = new ConcurrentDictionary<string, int>();
        // A set of permanently banned IP addresses.
        private readonly ConcurrentDictionary<string, bool> _bannedIps = new ConcurrentDictionary<string, bool>(
            StringComparer.Ordinal
        );
        private readonly ConcurrentDictionary<string, bool> _bannedSubnets = new ConcurrentDictionary<string, bool>(
            StringComparer.Ordinal
        );

        private const int MaxViolationsBeforeBan = 500; 
        private const int MaxSubnetViolationsBeforeBan = 3000;

        public ScrapingDetectionService(ILogger<ScrapingDetectionService> logger)
        {
            _logger = logger;
            // Pre-ban requested IPs
            _bannedIps.TryAdd("45.74.244.124", true);
            _bannedIps.TryAdd("77.179.164.33", true);
        }

        public bool IsBanned(HttpContext context)
        {
            var ip = GetClientIp(context);
            if (string.IsNullOrEmpty(ip)) return false;

            if (_bannedIps.ContainsKey(ip))
            {
                return true;
            }

            var subnet = GetSubnet(ip);
            if (subnet != null && _bannedSubnets.ContainsKey(subnet))
            {
                // also ban the IP outright to save on subnet checks
                _bannedIps.TryAdd(ip, true);
                return true;
            }

            return false;
        }

        public Task RecordRateLimitExceededAsync(HttpContext context)
        {
            var ip = GetClientIp(context);
            if (string.IsNullOrEmpty(ip)) return Task.CompletedTask;

            // Only track if it's not the excluded endpoint
            var path = context.Request.Path.Value ?? string.Empty;
            if (path.StartsWith("/api/description/modifications", StringComparison.OrdinalIgnoreCase) || 
                path.StartsWith("/description/modifications", StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            // Exclude already banned
            if (_bannedIps.ContainsKey(ip))
            {
                return Task.CompletedTask;
            }

            var newIpCount = _ipViolationCounts.AddOrUpdate(ip, 1, (_, count) => count + 1);

            if (newIpCount >= MaxViolationsBeforeBan)
            {
                _bannedIps.TryAdd(ip, true);
                _logger.LogWarning("IP {Ip} has been permanently banned due to continuous scraping.", ip);
                return Task.CompletedTask; // skip subnet adding to not double count
            }

            var subnet = GetSubnet(ip);
            if (subnet != null)
            {
                var newSubnetCount = _subnetViolationCounts.AddOrUpdate(subnet, 1, (_, count) => count + 1);
                if (newSubnetCount >= MaxSubnetViolationsBeforeBan)
                {
                    _bannedSubnets.TryAdd(subnet, true);
                    _logger.LogWarning("Subnet {Subnet} has been permanently banned due to continuous proxy scraping evasion.", subnet);
                }
            }

            return Task.CompletedTask;
        }

        private string GetClientIp(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue("CF-Connecting-IP", out var realIp) && !string.IsNullOrEmpty(realIp))
            {
                return realIp.ToString().Split(',').First().Trim();
            }

            if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrEmpty(forwardedFor))
            {
                return forwardedFor.ToString().Split(',').First().Trim();
            }

            return context.Connection.RemoteIpAddress?.ToString();
        }

        private string GetSubnet(string ip)
        {
            if (!System.Net.IPAddress.TryParse(ip, out var parsedIp)) return null;

            var bytes = parsedIp.GetAddressBytes();
            if (parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // /24 subnet for IPv4
                return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
            }
            if (parsedIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                // /48 subnet for IPv6
                return $"{bytes[0]:X2}{bytes[1]:X2}:{bytes[2]:X2}{bytes[3]:X2}:{bytes[4]:X2}{bytes[5]:X2}::/48";
            }

            return null;
        }
    }
}