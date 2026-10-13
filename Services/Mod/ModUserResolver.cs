using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services.Mod;

/// <summary>The user a mod bridge request belongs to.</summary>
/// <param name="UserId">Coflnet user id</param>
/// <param name="MinecraftUuid">Most recently used verified minecraft account, null if none</param>
/// <param name="MinecraftName">Name of that account, null if unknown</param>
public record ModUser(string UserId, string MinecraftUuid, string MinecraftName);

/// <summary>Resolves the calling user from google token or api key.</summary>
public interface IModUserResolver
{
    /// <summary>Resolves the user of the current request.</summary>
    Task<ModUser> Resolve(ControllerBase c);
}

/// <inheritdoc/>
public class ModUserResolver : IModUserResolver
{
    private readonly GoogletokenService googletokenService;
    private readonly ApiKeyService apiKeyService;
    private readonly McConnect.Api.IConnectApi connectApi;
    private readonly ILogger<ModUserResolver> logger;

    /// <summary>Creates a new instance.</summary>
    public ModUserResolver(GoogletokenService googletokenService, ApiKeyService apiKeyService, McConnect.Api.IConnectApi connectApi, ILogger<ModUserResolver> logger)
    {
        this.googletokenService = googletokenService;
        this.apiKeyService = apiKeyService;
        this.connectApi = connectApi;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ModUser> Resolve(ControllerBase c)
    {
        var request = c.Request;
        if (request.Headers.ContainsKey("Authorization") || request.Headers.ContainsKey("GoogleToken"))
        {
            var userId = await googletokenService.GetUserId(c);
            string uuid = null;
            string name = null;
            try
            {
                var mcUser = await connectApi.ConnectUserUserIdGetAsync(userId);
                uuid = mcUser.Accounts?.Where(a => a.Verified).OrderByDescending(a => a.LastRequestedAt).FirstOrDefault()?.AccountUuid;
                if (uuid != null)
                    name = await DiHandler.GetService<PlayerName.PlayerNameService>().GetName(uuid);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Could not resolve minecraft account of user {UserId}", userId);
            }
            return new ModUser(userId, uuid, name);
        }
        if (request.Headers.ContainsKey("x-api-key") || request.Query.ContainsKey("apiKey"))
        {
            var info = await apiKeyService.GetKeyInfo(c);
            return new ModUser(info.UserId, info.MinecraftUuid, info.MinecraftName);
        }
        throw new CoflnetException("unauthorized", "Log in with Google or pass an api key");
    }
}
