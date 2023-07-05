using Prometheus;
using Coflnet.Sky.Core;
using Google.Apis.Auth;

namespace Coflnet.Sky.Api;
/// <summary>
/// 
/// </summary>
public class GoogletokenService
{
    private Counter GoogleWebSignaturesValidatedSuccessfully = Metrics.CreateCounter("sky_api_google_web_signatures_validated_successfully", "The number of google web signatures validated successfully");

    private Counter GoogleWebSignaturesValidationFailed = Metrics.CreateCounter("sky_api_google_web_signatures_validations_failed", "The number of exceptions while validating google web signatures");

    /// <summary>
    /// 
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public GoogleUser GetUserWithToken(string token)
    {
        return UserService.Instance.GetOrCreateUser(ValidateToken(token).Subject);
    }

    /// <summary>
    /// Validates and extracts the token content
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    /// <exception cref="CoflnetException">Token is invalid</exception>
    public GoogleJsonWebSignature.Payload ValidateToken(string token)
    {
        try
        {
            var client = GoogleJsonWebSignature.ValidateAsync(token);
            client.Wait();
            var tokenData = client.Result;

            GoogleWebSignaturesValidatedSuccessfully.Inc();
            return tokenData;
        }
        catch (Exception e)
        {
            GoogleWebSignaturesValidationFailed.Inc();
            throw new CoflnetException("invalid_token", $"{e.InnerException.Message}");
        }
    }
}
