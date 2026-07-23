using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Coflnet.Sky.Api.Services.Ai;

/// <summary>Creates embeddings through an OpenAI-compatible endpoint, with a local lexical fallback.</summary>
public class EmbeddingService
{
    private const string QueryInstruction = "Represent this sentence for searching relevant passages: ";
    private static readonly Regex Tokens = new("[a-z0-9_]{2,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly IHttpClientFactory httpClientFactory;
    private readonly IConfiguration configuration;

    public int Dimensions { get; }

    /// <summary>Stable identifier used to invalidate vectors when the embedding backend changes.</summary>
    public string Profile => string.IsNullOrWhiteSpace(configuration["EMBEDDING_API_URL"])
        ? $"local-lexical-v1:{Dimensions}"
        : $"{configuration["EMBEDDING_MODEL"] ?? "BAAI/bge-small-en-v1.5"}:{Dimensions}";

    public EmbeddingService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        this.httpClientFactory = httpClientFactory;
        this.configuration = configuration;
        Dimensions = int.TryParse(configuration["EMBEDDING_DIMENSIONS"], out var dimensions) ? dimensions : 384;
    }

    public Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken) =>
        EmbedAsync(inputs, cancellationToken);

    public async Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken)
    {
        var endpoint = configuration["EMBEDDING_API_URL"];
        if (string.IsNullOrWhiteSpace(endpoint))
            return CreateLocalEmbedding(query);
        return (await EmbedAsync([QueryInstruction + query], cancellationToken))[0];
    }

    private async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        if (inputs.Count == 0)
            return [];

        var endpoint = configuration["EMBEDDING_API_URL"];
        if (string.IsNullOrWhiteSpace(endpoint))
            return inputs.Select(CreateLocalEmbedding).ToList();

        var batchSize = int.TryParse(configuration["EMBEDDING_BATCH_SIZE"], out var configuredBatchSize)
            ? Math.Clamp(configuredBatchSize, 1, 32)
            : 32;
        var vectors = new List<float[]>(inputs.Count);
        for (var offset = 0; offset < inputs.Count; offset += batchSize)
            vectors.AddRange(await EmbedBatchAsync(inputs.Skip(offset).Take(batchSize).ToList(), endpoint, cancellationToken));
        return vectors;
    }

    private async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> inputs,
        string endpoint,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        var key = configuration["EMBEDDING_API_KEY"];
        if (!string.IsNullOrWhiteSpace(key))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(new JObject
        {
            ["model"] = configuration["EMBEDDING_MODEL"] ?? "BAAI/bge-small-en-v1.5",
            ["input"] = JArray.FromObject(inputs)
        }.ToString(), Encoding.UTF8, "application/json");

        using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var vectors = json["data"]?.OrderBy(value => value.Value<int>("index"))
            .Select(value => value["embedding"]?.Values<float>().ToArray() ?? [])
            .ToList() ?? [];
        if (vectors.Count != inputs.Count || vectors.Any(vector => vector.Length != Dimensions))
            throw new InvalidOperationException($"Embedding endpoint must return {inputs.Count} vectors with {Dimensions} dimensions.");
        return vectors;
    }

    private float[] CreateLocalEmbedding(string text)
    {
        var vector = new float[Dimensions];
        foreach (Match match in Tokens.Matches(text.ToLowerInvariant()))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(match.Value));
            var index = (int)(BitConverter.ToUInt32(hash, 0) % (uint)Dimensions);
            vector[index] += (hash[4] & 1) == 0 ? 1 : -1;
        }

        var magnitude = Math.Sqrt(vector.Sum(value => value * value));
        if (magnitude > 0)
            for (var i = 0; i < vector.Length; i++)
                vector[i] = (float)(vector[i] / magnitude);
        return vector;
    }
}
