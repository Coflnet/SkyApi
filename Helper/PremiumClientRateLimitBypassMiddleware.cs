using System.Linq;
using System.Threading.Tasks;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Coflnet.Sky.Api.Helper
{
    /// <summary>
    /// Custom client resolver that uses the X-ClientId header if present,
    /// otherwise falls back to using the client's IP address as the identifier.
    /// This allows premium clients to have their own rate limits while
    /// anonymous users are rate limited by IP.
    /// </summary>
    public class ClientIdOrIpResolveContributor : IClientResolveContributor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly string _clientIdHeader;
        private readonly string _realIpHeader;
        private readonly AspNetCoreRateLimit.IpRateLimitOptions _ipOptions;

        private readonly string _ipWhitelistBypassClientId;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientIdOrIpResolveContributor"/> class.
        /// </summary>
        public ClientIdOrIpResolveContributor(
            IHttpContextAccessor httpContextAccessor,
            string clientIdHeader,
            string realIpHeader,
            AspNetCoreRateLimit.IpRateLimitOptions ipOptions,
            string ipWhitelistBypassClientId)
        {
            _httpContextAccessor = httpContextAccessor;
            _clientIdHeader = clientIdHeader;
            _realIpHeader = realIpHeader;
            _ipOptions = ipOptions ?? new AspNetCoreRateLimit.IpRateLimitOptions();
            _ipWhitelistBypassClientId = string.IsNullOrEmpty(ipWhitelistBypassClientId) ? "IP_WHITELIST_BYPASS" : ipWhitelistBypassClientId;
        }

        /// <summary>
        /// Resolves the client ID from the request. Returns the X-ClientId header value
        /// if present, otherwise returns the client IP address. If the request IP matches
        /// an entry in the IpWhitelist (CIDR-aware), a special bypass client id is returned
        /// so that ClientRateLimiting can honor the IP whitelist.
        /// </summary>
        public Task<string> ResolveClientAsync(HttpContext httpContext)
        {
            // First try to get client ID from header
            if (httpContext.Request.Headers.TryGetValue(_clientIdHeader, out var clientId) 
                && !string.IsNullOrEmpty(clientId))
            {
                var headerValue = clientId.ToString();

                // If the header is the special whitelist-bypass token, only honor it
                // when the request IP is actually whitelisted. This prevents clients
                // from spoofing the bypass by setting the header from non-whitelisted IPs.
                if (string.Equals(headerValue, _ipWhitelistBypassClientId, System.StringComparison.Ordinal))
                {
                    var ipForHeaderCheck = RequestIpUtility.ResolveWhitelistIp(httpContext, _realIpHeader);
                    if (RequestIpUtility.IsIpWhitelisted(ipForHeaderCheck, _ipOptions?.IpWhitelist))
                    {
                        return Task.FromResult(_ipWhitelistBypassClientId);
                    }
                    // Otherwise ignore the special header and fall through to IP-based identification
                }

                // For any other header value, return it as the client id
                if (!string.Equals(headerValue, _ipWhitelistBypassClientId, System.StringComparison.Ordinal))
                {
                    return Task.FromResult(headerValue);
                }
            }

            // Fall back to IP address for normal client id (this still prefers X-Forwarded-For)
            var ip = RequestIpUtility.ResolveClientIp(httpContext, _realIpHeader) ?? "unknown";

            // Determine which IP to use for whitelist checks: if the configured real-ip header
            // (e.g. CF-Connecting-IP) is present, use that for whitelist decisions. Otherwise
            // use the connection remote IP (this is the "inside cluster" scenario).
            var ipForWhitelist = RequestIpUtility.ResolveWhitelistIp(httpContext, _realIpHeader);

            // If IP matches IpWhitelist (supports CIDR entries), return special bypass id
            if (RequestIpUtility.IsIpWhitelisted(ipForWhitelist, _ipOptions?.IpWhitelist))
            {
                return Task.FromResult(_ipWhitelistBypassClientId);
            }

            return Task.FromResult($"ip:{ip}");
        }
    }

    /// <summary>
    /// Custom rate limit configuration that uses ClientIdOrIpResolveContributor.
    /// </summary>
    public class CustomRateLimitConfiguration : RateLimitConfiguration
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly string _ipWhitelistBypassClientId;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="CustomRateLimitConfiguration"/> class.
        /// </summary>
        public CustomRateLimitConfiguration(
            IHttpContextAccessor httpContextAccessor,
            IOptions<IpRateLimitOptions> ipOptions,
            IOptions<ClientRateLimitOptions> clientOptions,
            string ipWhitelistBypassClientId)
            : base(ipOptions, clientOptions)
        {
            _httpContextAccessor = httpContextAccessor;
            _ipWhitelistBypassClientId = string.IsNullOrEmpty(ipWhitelistBypassClientId) ? "IP_WHITELIST_BYPASS" : ipWhitelistBypassClientId;
        }

        /// <summary>
        /// Registers the custom client resolver that falls back to IP when no client ID is provided.
        /// </summary>
        public override void RegisterResolvers()
        {
            base.RegisterResolvers();
            
            var clientIdHeader = ClientRateLimitOptions?.ClientIdHeader ?? "X-ClientId";
            var realIpHeader = IpRateLimitOptions?.RealIpHeader ?? "CF-Connecting-IP";
            
            // Add our custom resolver that falls back to IP and is aware of IpWhitelist
            ClientResolvers.Add(new ClientIdOrIpResolveContributor(
                _httpContextAccessor,
                clientIdHeader,
                realIpHeader,
                IpRateLimitOptions,
                _ipWhitelistBypassClientId));
        }
    }
}
