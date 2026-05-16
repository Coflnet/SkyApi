using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace Coflnet.Sky.Api.Helper
{
    public static class RequestIpUtility
    {
        public static string ResolveClientIp(HttpContext context, string realIpHeader)
        {
            if (TryGetHeaderIp(context, realIpHeader, out var ip))
            {
                return ip;
            }

            if (TryGetHeaderIp(context, "X-Forwarded-For", out ip))
            {
                return ip;
            }

            return NormalizeIp(context.Connection.RemoteIpAddress);
        }

        public static string ResolveWhitelistIp(HttpContext context, string realIpHeader)
        {
            if (TryGetHeaderIp(context, realIpHeader, out var ip))
            {
                return ip;
            }

            return NormalizeIp(context.Connection.RemoteIpAddress);
        }

        public static string NormalizeIp(IPAddress address)
        {
            if (address == null)
            {
                return null;
            }

            return NormalizeParsedIp(address);
        }

        public static string NormalizeIp(string ipString)
        {
            if (string.IsNullOrWhiteSpace(ipString))
            {
                return null;
            }

            var candidate = ipString.Split(',').First().Trim();
            if (!IPAddress.TryParse(candidate, out var address))
            {
                return candidate;
            }

            return NormalizeIp(address);
        }

        public static bool IsIpWhitelisted(string ipString, IEnumerable<string> ipWhitelist)
        {
            if (!TryParseNormalized(ipString, out var ip) || ipWhitelist == null)
            {
                return false;
            }

            foreach (var entry in ipWhitelist)
            {
                var trimmed = entry?.Trim();
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                if (!trimmed.Contains("/"))
                {
                    if (TryParseNormalized(trimmed, out var whitelistedIp) && whitelistedIp.Equals(ip))
                    {
                        return true;
                    }

                    continue;
                }

                var parts = trimmed.Split('/');
                if (parts.Length != 2)
                {
                    continue;
                }

                if (!TryParseNormalized(parts[0], out var network) || !int.TryParse(parts[1], out var prefix))
                {
                    continue;
                }

                if (IsInSubnet(ip, network, prefix))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetHeaderIp(HttpContext context, string headerName, out string ip)
        {
            ip = null;
            if (string.IsNullOrWhiteSpace(headerName))
            {
                return false;
            }

            if (!context.Request.Headers.TryGetValue(headerName, out var values) || string.IsNullOrWhiteSpace(values))
            {
                return false;
            }

            ip = NormalizeIp(values.ToString());
            return !string.IsNullOrWhiteSpace(ip);
        }

        private static string NormalizeParsedIp(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            return address.ToString();
        }

        private static bool TryParseNormalized(string ipString, out IPAddress address)
        {
            address = null;
            var normalized = NormalizeIp(ipString);
            if (string.IsNullOrWhiteSpace(normalized) || !IPAddress.TryParse(normalized, out address))
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            return true;
        }

        private static bool IsInSubnet(IPAddress address, IPAddress network, int prefixLength)
        {
            var addressBytes = address.GetAddressBytes();
            var networkBytes = network.GetAddressBytes();

            if (addressBytes.Length != networkBytes.Length)
            {
                return false;
            }

            var bitsRemaining = prefixLength;
            for (var index = 0; index < addressBytes.Length; index++)
            {
                var bitsToCheck = bitsRemaining > 8 ? 8 : bitsRemaining;
                if (bitsToCheck <= 0)
                {
                    break;
                }

                var mask = (byte)(0xFF << (8 - bitsToCheck));
                if ((addressBytes[index] & mask) != (networkBytes[index] & mask))
                {
                    return false;
                }

                bitsRemaining -= bitsToCheck;
            }

            return true;
        }
    }
}