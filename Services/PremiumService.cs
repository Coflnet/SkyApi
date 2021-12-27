using System;
using Google.Apis.Auth;

namespace hypixel
{
    public class PremiumService
    {
        public GoogleUser GetUserWithToken(string token)
        {

            return UserService.Instance.GetOrCreateUser(ValidateToken(token).Subject);
        }
        public GoogleJsonWebSignature.Payload ValidateToken(string token)
        {
            try
            {
                var client = GoogleJsonWebSignature.ValidateAsync(token);
                client.Wait();
                var tokenData = client.Result;
                Console.WriteLine("google user: " + tokenData.Name);
                return tokenData;
            }
            catch (Exception e)
            {
                throw new CoflnetException("invalid_token", $"{e.InnerException.Message}");
            }
        }
    }
}