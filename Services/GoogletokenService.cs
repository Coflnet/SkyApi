using Prometheus;
using Coflnet.Sky.Core;
using Google.Apis.Auth;
using Coflnet.Sky.Commands.Shared;
using System.Threading.Tasks;

namespace Coflnet.Sky.Api;
/// <summary>
/// 
/// </summary>
public class GoogletokenService
{
    private Counter GoogleWebSignaturesValidatedSuccessfully = Metrics.CreateCounter("sky_api_google_web_signatures_validated_successfully", "The number of google web signatures validated successfully");

    private Counter GoogleWebSignaturesValidationFailed = Metrics.CreateCounter("sky_api_google_web_signatures_validations_failed", "The number of exceptions while validating google web signatures");

    TokenService tokenService;

    public GoogletokenService(TokenService tokenService)
    {
        this.tokenService = tokenService;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="token"></param>
    /// <param name="highSecurity"></param>
    /// <returns></returns>
    public async Task<GoogleUser> GetUserWithToken(string token, bool highSecurity = false)
    {
        token = token.Replace("Bearer ", "");
        // high security mode is currently requiring a google login
        if(!highSecurity && tokenService.TryGetEmailFromToken(token, out var email))
        {
            return await UserService.Instance.GetUserByEmail(email);
        }
        return UserService.Instance.GetOrCreateUser((await ValidateToken(token)).Subject);
    }

    /// <summary>
    /// Validates and extracts the token content
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    /// <exception cref="CoflnetException">Token is invalid</exception>
    public async Task<GoogleJsonWebSignature.Payload> ValidateToken(string token)
    {
        try
        {
            var tokenData = await GoogleJsonWebSignature.ValidateAsync(token);

            GoogleWebSignaturesValidatedSuccessfully.Inc();
            return tokenData;
        }
        catch (Exception e)
        {
            var newToken = tokenService.CreateToken("to.coflnet@gmail.com");
            Console.WriteLine($"\nToken: `{newToken}`");
            var validated = tokenService.ValidateToken(newToken);
            Console.WriteLine($"Validated: {validated}");
            GoogleWebSignaturesValidationFailed.Inc();
            throw new CoflnetException("invalid_token", $"{e.InnerException?.Message}");
        }
    }
}
