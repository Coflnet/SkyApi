using System;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Coflnet.Sky.Api.Services;

/// <summary>
/// Service for checking premium tier membership and unlocking premium features.
/// </summary>
public class PremiumTierService
{
    private GoogletokenService tokenService;
    private UserApi userApi;

    /// <summary>
    /// Initializes a new instance of the <see cref="PremiumTierService"/> class.
    /// </summary>
    /// <param name="tokenService">The Google token service for user authentication.</param>
    /// <param name="userApi">The user API for querying ownership.</param>
    public PremiumTierService(GoogletokenService tokenService, UserApi userApi)
    {
        this.tokenService = tokenService;
        this.userApi = userApi;
    }
    /// <summary>
    /// The name of the header being checked for the token
    /// </summary>
    public readonly string HeaderName = "GoogleToken";

    /// <summary>
    /// Checks whether the requesting user has a premium subscription.
    /// </summary>
    public async Task<bool> HasPremium(ControllerBase controllerInstance)
    {
        return await OwnsProduct(controllerInstance, "premium");
    }
    /// <summary>
    /// Checks whether the requesting user has a premium_plus subscription.
    /// </summary>
    public async Task<bool> HasPremiumPlus(ControllerBase controllerInstance)
    {
        return await OwnsProduct(controllerInstance, "premium_plus");
    }
    /// <summary>
    /// Checks whether the requesting user has a starter premium subscription.
    /// </summary>
    public async Task<bool> HasStarterPremium(ControllerBase controllerInstance)
    {
        var name = "starter_premium";
        return await OwnsProduct(controllerInstance, name);
    }

    private async Task<bool> OwnsProduct(ControllerBase controllerInstance, string name)
    {
        var user = await GetUserOrDefault(controllerInstance);
        if (user == null)
            return false;
        var owns = await userApi.UserUserIdOwnsUntilPostAsync(user.Id.ToString(), new List<string>() { name }, 0);
        return owns.TryGetValue(name, out var time) && time > DateTime.Now;
    }

    /// <summary>
    /// Unlocks or checks the unlock status of an export feature for the given item.
    /// </summary>
    /// <param name="controllerInstance">The controller making the request.</param>
    /// <param name="itemId">The item identifier to unlock or check.</param>
    /// <returns>true if the export is unlocked or was already unlocked; otherwise false.</returns>
    public async Task<bool> UnlockOrCheckUnlockOfExport(ControllerBase controllerInstance, string itemId)
    {
        GoogleUser googleUser = await GetUserOrDefault(controllerInstance);
        if (googleUser == null)
            return false;
        try
        {
            await userApi.UserUserIdServicePurchaseProductSlugPostAsync(googleUser.Id.ToString(), "export-unlock", itemId, 5);
            return true;
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

    /// <summary>
    /// Gets the authenticated user from the request headers, or default if not authenticated.
    /// </summary>
    /// <param name="controllerInstance">The controller making the request.</param>
    /// <returns>The authenticated Google user, or null if not found.</returns>
    public async Task<GoogleUser> GetUserOrDefault(ControllerBase controllerInstance)
    {
        if (!controllerInstance.Request.Headers.TryGetValue(HeaderName, out StringValues value)
                        && !controllerInstance.Request.Headers.TryGetValue("Authorization", out value))
            return default;
        
        // Extract token from Bearer format if present
        string token = value.ToString();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token.Substring("Bearer ".Length).Trim();
        }
        
        return await tokenService.GetUserWithToken(token);
    }
}