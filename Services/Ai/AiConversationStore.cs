using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StackExchange.Redis;

namespace Coflnet.Sky.Api.Services.Ai;

/// <summary>Stores exact DeepSeek transcripts so thinking-mode tool calls survive follow-ups and replica changes.</summary>
public class AiConversationStore
{
    private const int ContextUserTurns = 5;
    private static readonly Regex ConversationIdPattern = new("^[a-f0-9]{32}$", RegexOptions.Compiled);
    private static readonly IReadOnlyDictionary<string, int> TranscriptLimits = new Dictionary<string, int>
    {
        ["anonymous"] = 100 * 1024,
        ["logged_in"] = 100 * 1024,
        ["starter_premium"] = 128 * 1024,
        ["premium"] = 192 * 1024,
        ["premium_plus"] = 256 * 1024
    };

    private readonly IDatabase database;
    private readonly TimeSpan ttl;

    public AiConversationStore(IConnectionMultiplexer redis, IConfiguration configuration)
    {
        database = redis.GetDatabase();
        var hours = int.TryParse(configuration["AI_CONVERSATION_TTL_HOURS"], out var configuredHours) ? configuredHours : 24;
        ttl = TimeSpan.FromHours(Math.Clamp(hours, 1, 168));
    }

    public async Task<AiConversationHandle> OpenAsync(string requestedId, string owner, string tier)
    {
        var id = string.IsNullOrWhiteSpace(requestedId)
            ? RandomNumberGenerator.GetHexString(16).ToLowerInvariant()
            : requestedId.ToLowerInvariant();
        if (!ConversationIdPattern.IsMatch(id))
            throw new BadHttpRequestException("Invalid conversation id");

        var key = (RedisKey)$"ai:conversation:{id}";
        var lockKey = (RedisKey)$"{key}:lock";
        var lockToken = RandomNumberGenerator.GetHexString(16);
        if (!await database.LockTakeAsync(lockKey, lockToken, TimeSpan.FromMinutes(15)))
            throw new AiConversationBusyException(id);

        try
        {
            var ownerHash = Hash(owner);
            var stored = await database.StringGetAsync(key);
            var state = stored.HasValue
                ? JsonConvert.DeserializeObject<AiConversationState>(stored.ToString())
                : new AiConversationState { OwnerHash = ownerHash };
            if (state == null || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(state.OwnerHash ?? ""),
                    Encoding.UTF8.GetBytes(ownerHash)))
                throw new AiConversationAccessException();

            state.Messages ??= [];
            var limit = TranscriptLimits.GetValueOrDefault(tier, TranscriptLimits["anonymous"]);
            var bytes = Size(state);
            if (bytes >= limit)
                throw new AiConversationLimitException(id, bytes, limit);

            TrimForNextTurn(state.Messages);
            return new AiConversationHandle(id, key, lockKey, lockToken, ownerHash, state, limit);
        }
        catch
        {
            await database.LockReleaseAsync(lockKey, lockToken);
            throw;
        }
    }

    public async Task<int> SaveAsync(AiConversationHandle handle)
    {
        var json = JsonConvert.SerializeObject(handle.State);
        await database.StringSetAsync(handle.Key, json, ttl);
        return Encoding.UTF8.GetByteCount(json);
    }

    public async Task ReleaseAsync(AiConversationHandle handle)
    {
        await database.LockReleaseAsync(handle.LockKey, handle.LockToken);
    }

    public async Task DeleteAsync(string conversationId, string owner)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || !ConversationIdPattern.IsMatch(conversationId))
            return;

        var key = (RedisKey)$"ai:conversation:{conversationId}";
        var stored = await database.StringGetAsync(key);
        if (!stored.HasValue)
            return;
        var state = JsonConvert.DeserializeObject<AiConversationState>(stored.ToString());
        if (state?.OwnerHash != Hash(owner))
            throw new AiConversationAccessException();
        await database.KeyDeleteAsync(key);
    }

    private static int Size(AiConversationState state) =>
        Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(state));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? ""))).ToLowerInvariant();

    private static void TrimForNextTurn(JArray messages)
    {
        var users = messages.Select((message, index) => (message, index))
            .Where(value => value.message?.Value<string>("role") == "user")
            .Select(value => value.index)
            .ToList();
        if (users.Count < ContextUserTurns)
            return;

        var first = users[users.Count - (ContextUserTurns - 1)];
        var system = messages.FirstOrDefault(message => message?.Value<string>("role") == "system")?.DeepClone();
        var retained = messages.Skip(first).Select(message => message.DeepClone()).ToList();
        messages.RemoveAll();
        if (system != null)
            messages.Add(system);
        foreach (var message in retained)
            messages.Add(message);
    }
}

public sealed class AiConversationHandle(
    string id,
    RedisKey key,
    RedisKey lockKey,
    string lockToken,
    string ownerHash,
    AiConversationState state,
    int limit)
{
    public string Id { get; } = id;
    public RedisKey Key { get; } = key;
    public RedisKey LockKey { get; } = lockKey;
    public string LockToken { get; } = lockToken;
    public string OwnerHash { get; } = ownerHash;
    public AiConversationState State { get; } = state;
    public int Limit { get; } = limit;
}

public sealed class AiConversationState
{
    public string OwnerHash { get; set; }
    public JArray Messages { get; set; } = [];
}

public sealed class AiConversationLimitException(string conversationId, int transcriptBytes, int limit)
    : Exception("This conversation is full. Export it if needed, then clear it to start a new session.")
{
    public string ConversationId { get; } = conversationId;
    public int TranscriptBytes { get; } = transcriptBytes;
    public int Limit { get; } = limit;
}

public sealed class AiConversationBusyException(string conversationId)
    : Exception("This conversation is already processing another message.")
{
    public string ConversationId { get; } = conversationId;
}

public sealed class AiConversationAccessException : Exception;
