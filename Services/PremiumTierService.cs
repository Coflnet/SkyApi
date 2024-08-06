using System.Threading.Tasks;
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

    public async Task<bool> HasPremium(ControllerBase controllerInstance)
    {
        return await OwnsProduct(controllerInstance, "premium");
    }
    public async Task<bool> HasPremiumPlus(ControllerBase controllerInstance)
    {
        return await OwnsProduct(controllerInstance, "premium_plus");
    }
    public async Task<bool> HasStarterPremium(ControllerBase controllerInstance)
    {
        var name = "starter_premium";
        return await OwnsProduct(controllerInstance, name);
    }

    private async Task<bool> OwnsProduct(ControllerBase controllerInstance, string name)
    {
        if (!controllerInstance.Request.Headers.TryGetValue(HeaderName, out StringValues value)
                && !controllerInstance.Request.Headers.TryGetValue("Authorization", out value))
            return false;
        var user = await tokenService.GetUserWithToken(value);
        var owns = await userApi.UserUserIdOwnsUntilPostAsync(user.Id.ToString(), new List<string>() { name }, 0);
        return owns.TryGetValue(name, out var time) && time > DateTime.Now;
    }

    public async Task<bool> UnlockOrCheckUnlockOfExport(ControllerBase controllerInstance, string itemId)
    {
        if (!controllerInstance.Request.Headers.TryGetValue(HeaderName, out StringValues value)
                && !controllerInstance.Request.Headers.TryGetValue("Authorization", out value))
            return false;
        var googleUser = await tokenService.GetUserWithToken(value);
        try
        {
            var user = await userApi.UserUserIdServicePurchaseProductSlugPostAsync(googleUser.Id.ToString(), "export-unlock", itemId, 5);
            return user.Balance > 0;
        }
        catch (Exception e)
        {
            if (e.Message.Contains("already"))
            {
                return true;
            }
        }
        return false;
    }
}