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

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientIdOrIpResolveContributor"/> class.
        /// </summary>
        public ClientIdOrIpResolveContributor(
            IHttpContextAccessor httpContextAccessor,
            string clientIdHeader,
            string realIpHeader)
        {
            _httpContextAccessor = httpContextAccessor;
            _clientIdHeader = clientIdHeader;
            _realIpHeader = realIpHeader;
        }

        /// <summary>
        /// Resolves the client ID from the request. Returns the X-ClientId header value
        /// if present, otherwise returns the client IP address.
        /// </summary>
        public Task<string> ResolveClientAsync(HttpContext httpContext)
        {
            // First try to get client ID from header
            if (httpContext.Request.Headers.TryGetValue(_clientIdHeader, out var clientId) 
                && !string.IsNullOrEmpty(clientId))
            {
                return Task.FromResult(clientId.ToString());
            }

            // Fall back to IP address
            var ip = ResolveIp(httpContext);
            return Task.FromResult($"ip:{ip}");
        }

        private string ResolveIp(HttpContext httpContext)
        {
            // Try real IP header first (e.g., CF-Connecting-IP from Cloudflare)
            if (!string.IsNullOrEmpty(_realIpHeader) && 
                httpContext.Request.Headers.TryGetValue(_realIpHeader, out var realIp) &&
                !string.IsNullOrEmpty(realIp))
            {
                return realIp.ToString().Split(',').First().Trim();
            }

            // Try X-Forwarded-For
            if (httpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) &&
                !string.IsNullOrEmpty(forwardedFor))
            {
                return forwardedFor.ToString().Split(',').First().Trim();
            }

            // Fall back to connection remote IP
            return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }

    /// <summary>
    /// Custom rate limit configuration that uses ClientIdOrIpResolveContributor.
    /// </summary>
    public class CustomRateLimitConfiguration : RateLimitConfiguration
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="CustomRateLimitConfiguration"/> class.
        /// </summary>
        public CustomRateLimitConfiguration(
            IHttpContextAccessor httpContextAccessor,
            IOptions<IpRateLimitOptions> ipOptions,
            IOptions<ClientRateLimitOptions> clientOptions)
            : base(ipOptions, clientOptions)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Registers the custom client resolver that falls back to IP when no client ID is provided.
        /// </summary>
        public override void RegisterResolvers()
        {
            base.RegisterResolvers();
            
            var clientIdHeader = ClientRateLimitOptions?.ClientIdHeader ?? "X-ClientId";
            var realIpHeader = IpRateLimitOptions?.RealIpHeader ?? "CF-Connecting-IP";
            
            // Add our custom resolver that falls back to IP
            ClientResolvers.Add(new ClientIdOrIpResolveContributor(
                _httpContextAccessor, 
                clientIdHeader,
                realIpHeader));
        }
    }
}
