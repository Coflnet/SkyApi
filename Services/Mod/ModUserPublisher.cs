using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using StackExchange.Redis;

namespace Coflnet.Sky.Api.Services.Mod;

/// <summary>Pushes messages to the connected mod sessions of a user.</summary>
public interface IModUserPublisher
{
    /// <summary>Runs a chat command in the user's mod, returns the number of receiving sessions (0 = not connected).</summary>
    Task<long> ExecuteForUser(string userId, string command, string mcUuid = null);
    /// <summary>Shows an info display in the user's mod.</summary>
    Task<long> DisplayForUser(string userId, object infoDisplay);
    /// <summary>Tells the mod which command the user is expected to type next.</summary>
    Task<long> ExpectForUser(string userId, string command, int ttlSeconds);
}

/// <inheritdoc/>
public class ModUserPublisher : IModUserPublisher
{
    private static readonly JsonSerializer CamelCase = JsonSerializer.Create(new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore
    });
    private readonly IConnectionMultiplexer redis;
    private readonly ILogger<ModUserPublisher> logger;

    /// <summary>Creates a new instance.</summary>
    public ModUserPublisher([FromKeyedServices("bazaar")] IConnectionMultiplexer redis, ILogger<ModUserPublisher> logger)
    {
        this.redis = redis;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public Task<long> ExecuteForUser(string userId, string command, string mcUuid = null)
        => Publish(userId, new ModUserChannel.UserMessage { Kind = ModUserChannel.ExecuteKind, Command = command, MinecraftUuid = mcUuid });

    /// <inheritdoc/>
    public Task<long> DisplayForUser(string userId, object infoDisplay)
        => Publish(userId, new ModUserChannel.UserMessage { Kind = ModUserChannel.DisplayKind, Display = JObject.FromObject(infoDisplay, CamelCase) });

    /// <inheritdoc/>
    public Task<long> ExpectForUser(string userId, string command, int ttlSeconds)
        => Publish(userId, new ModUserChannel.UserMessage { Kind = ModUserChannel.ExpectKind, Command = command, Ttl = ttlSeconds });

    private async Task<long> Publish(string userId, ModUserChannel.UserMessage message)
    {
        try
        {
            return await ModUserChannel.PublishAsync(redis, userId, message);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to publish {Kind} to mod user {UserId}", message.Kind, userId);
            return 0;
        }
    }
}
