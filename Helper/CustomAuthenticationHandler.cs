using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Services;
using System;
using System.Linq;

namespace Coflnet.Sky.Api.Helper
{
    /// <summary>
    /// Custom authentication scheme options
    /// </summary>
    public class CustomAuthenticationSchemeOptions : AuthenticationSchemeOptions
    {
    }

    /// <summary>
    /// Custom authentication handler that integrates with the existing Google token system
    /// </summary>
    public class CustomAuthenticationHandler : AuthenticationHandler<CustomAuthenticationSchemeOptions>
    {
        private readonly PremiumTierService premiumTierService;

        /// <summary>
        /// Initializes a new instance of the CustomAuthenticationHandler
        /// </summary>
        public CustomAuthenticationHandler(IOptionsMonitor<CustomAuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder,
            PremiumTierService premiumTierService)
            : base(options, logger, encoder)
        {
            this.premiumTierService = premiumTierService;
        }

        /// <summary>
        /// Handles the authentication
        /// </summary>
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Check for Authorization header with Bearer token or GoogleToken header
            var authorizationHeader = Request.Headers["Authorization"].FirstOrDefault();
            var googleTokenHeader = Request.Headers["GoogleToken"].FirstOrDefault();

            string token = null;
            if (!string.IsNullOrEmpty(authorizationHeader) && authorizationHeader.StartsWith("Bearer "))
            {
                token = authorizationHeader.Substring("Bearer ".Length).Trim();
            }
            else if (!string.IsNullOrEmpty(googleTokenHeader))
            {
                token = googleTokenHeader;
            }

            if (string.IsNullOrEmpty(token))
            {
                return AuthenticateResult.NoResult();
            }

            try
            {
                // Use the existing GoogleTokenService to validate the token
                var googleTokenService = Context.RequestServices.GetRequiredService<GoogletokenService>();
                var user = await googleTokenService.GetUserWithToken(token);

                if (user == null)
                {
                    return AuthenticateResult.Fail("Invalid token");
                }

                // Create claims for the authenticated user
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Email, user.Email ?? ""),
                    new Claim("GoogleId", user.GoogleId ?? ""),
                };

                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                return AuthenticateResult.Success(ticket);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error validating token");
                return AuthenticateResult.Fail("Token validation failed");
            }
        }
    }
}
