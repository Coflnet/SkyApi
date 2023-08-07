using Coflnet.Payments.Client.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Coflnet.Sky.Api.Services;

public class PremiumTierService
{
    private GoogletokenService tokenService;
    private UserApi userApi;

    public PremiumTierService(GoogletokenService tokenService, UserApi userApi)
    {
        this.tokenService = tokenService;
        this.userApi = userApi;
    }
    /// <summary>
    /// The name of the header being checked for the token
    /// </summary>
    public readonly string HeaderName = "GoogleToken";

    public bool HasPremium(ControllerBase controllerInstance)
    {
        var name = "premium";
        return OwnsProduct(controllerInstance, name);
    }
    public bool HasStarterPremium(ControllerBase controllerInstance)
    {
        var name = "starter_premium";
        return OwnsProduct(controllerInstance, name);
    }

    private bool OwnsProduct(ControllerBase controllerInstance, string name)
    {
        if (!controllerInstance.Request.Headers.TryGetValue(HeaderName, out StringValues value))
            return false;
        var user = tokenService.GetUserWithToken(value);
        return userApi.UserUserIdOwnsUntilPostAsync(user.Id.ToString(), new List<string>() { name }, 0).Result.TryGetValue(name, out var time) && time > DateTime.Now;
    }
}