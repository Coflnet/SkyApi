using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Coflnet.Sky.Api.Services;

[TestFixture]
[NonParallelizable]
public class LegalManifestServiceTests
{
    [TearDown]
    public void TearDown() => TermsAcceptancePolicy.ResetForTests();

    [Test]
    public async Task FutureRootDoesNotBlockStartupAndActivatesWhenEffective()
    {
        var now = DateTimeOffset.Parse("2026-08-07T07:59:00Z");
        var effective = now.AddMinutes(1);
        var delayEntered = NewSignal();
        var resume = NewSignal();
        using var service = CreateService(
            new ManifestHandler(new Fixture("future", effective)),
            () => now,
            (_, token) =>
            {
                delayEntered.TrySetResult();
                return resume.Task.WaitAsync(token);
            });

        await service.StartAsync(CancellationToken.None);
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.That(service.Agreement, Is.Null);

        now = effective;
        resume.TrySetResult();
        await WaitUntil(() => service.Agreement?.Version == "future");
        Assert.Multiple(() =>
        {
            Assert.That(service.Agreement.Id, Is.EqualTo("skycofl"));
            Assert.That(service.Agreement.Documents, Has.Count.EqualTo(4));
            Assert.That(service.Agreement.Hash, Has.Length.EqualTo(64));
            Assert.That(service.Withdrawal?.Version, Is.EqualTo("future"));
            Assert.That(service.PremiumEarlyStart?.Sha256?["en"], Has.Length.EqualTo(64));
        });
    }

    [Test]
    public async Task UnavailableManifestRetriesAndLaterActivates()
    {
        var now = DateTimeOffset.Parse("2026-08-07T08:00:00Z");
        var delayEntered = NewSignal();
        var resume = NewSignal();
        using var service = CreateService(
            new ManifestHandler(new Fixture("retry", now.AddMinutes(-1)), 1),
            () => now,
            (_, token) =>
            {
                delayEntered.TrySetResult();
                return resume.Task.WaitAsync(token);
            });

        await service.StartAsync(CancellationToken.None);
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.That(service.Agreement, Is.Null);

        resume.TrySetResult();
        await WaitUntil(() => service.Agreement?.Version == "retry");
    }

    [Test]
    public async Task TamperedRootIsNotActivated()
    {
        var now = DateTimeOffset.Parse("2026-08-08T08:00:00Z");
        var retry = NewSignal();
        using var service = CreateService(
            new ManifestHandler(new Fixture("tampered", now, tamperRoot: true)),
            () => now,
            (_, token) =>
            {
                retry.TrySetResult();
                return Task.Delay(Timeout.InfiniteTimeSpan, token);
            });

        await service.StartAsync(CancellationToken.None);
        await retry.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.That(service.Agreement, Is.Null);
    }

    [TestCase("http://coflnet.com/legal/manifest.json")]
    [TestCase("https://legal.coflnet.com/manifest.json")]
    [TestCase("https://coflnet.com:444/manifest.json")]
    public void ManifestUrlRequiresExactCoflnetHttpsOrigin(string url)
    {
        using var service = CreateService(
            new ManifestHandler(new Fixture("origin", DateTimeOffset.UtcNow)),
            () => DateTimeOffset.UtcNow,
            (_, _) => Task.CompletedTask,
            url);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await service.StartAsync(CancellationToken.None));
    }

    private static LegalManifestService CreateService(
        HttpMessageHandler handler,
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delay,
        string url = "https://coflnet.com/legal/manifest.json") =>
        new(
            new ClientFactory(new HttpClient(handler)),
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string> { ["LEGAL_MANIFEST_URL"] = url }).Build(),
            NullLogger<LegalManifestService>.Instance,
            utcNow,
            delay);

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class Fixture
    {
        private readonly Dictionary<string, byte[]> routes = [];
        public byte[] Manifest { get; }

        public Fixture(string version, DateTimeOffset effective, bool tamperRoot = false)
        {
            var documents = new[]
            {
                new DocumentFixture("terms", "Core Terms", version, effective),
                new DocumentFixture("commerceTerms", "Commerce Terms", version, effective),
                new DocumentFixture("aiTerms", "AI Terms", version, effective),
                new DocumentFixture("skycoflTerms", "SkyCofl Terms", version, effective)
            };
            foreach (var document in documents)
            {
                routes[$"/{document.Key}-en"] = document.English;
                routes[$"/{document.Key}-de"] = document.German;
            }

            var core = Node("core", "shared", [documents[0]], []);
            var commerce = Node("commerce", "shared", [documents[1]], [Dependency("core", core)]);
            var ai = Node("ai", "shared", [documents[2]], [Dependency("core", core)]);
            var root = Node("skycofl", "service", [documents[3]],
                [Dependency("ai", ai), Dependency("commerce", commerce)]);
            foreach (var node in new[] { core, commerce, ai, root })
                routes[$"/legal/agreements/{node.Hash}.json"] = node.Bytes;
            if (tamperRoot)
                routes[$"/legal/agreements/{root.Hash}.json"] = Encoding.UTF8.GetBytes("{}");

            var withdrawal = new DocumentFixture("withdrawal", "Withdrawal", version, effective);
            routes["/withdrawal-en"] = withdrawal.English;
            routes["/withdrawal-de"] = withdrawal.German;
            const string premiumEn = "I want Premium to start now.";
            const string premiumDe = "Premium soll jetzt beginnen.";
            Manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                source = "https://coflnet.com",
                agreementTreeVersion = 1,
                documents = documents.ToDictionary(item => item.Key, item => item.Json)
                    .Append(new("withdrawal", withdrawal.Json))
                    .ToDictionary(item => item.Key, item => item.Value),
                agreements = new Dictionary<string, object>
                {
                    ["skycofl"] = new
                    {
                        type = "service",
                        agreementHash = root.Hash,
                        agreementUrl = $"https://coflnet.com/legal/agreements/{root.Hash}.json",
                        resolvedDocuments = documents.Select(item => item.Summary)
                    }
                },
                declarations = new Dictionary<string, object>
                {
                    ["premiumEarlyStart"] = new
                    {
                        version = "premium-v1",
                        locales = new Dictionary<string, object>
                        {
                            ["en"] = new { text = premiumEn, sha256 = Hash(Encoding.UTF8.GetBytes(premiumEn)) },
                            ["de"] = new { text = premiumDe, sha256 = Hash(Encoding.UTF8.GetBytes(premiumDe)) }
                        }
                    }
                }
            });
        }

        public byte[] Get(string path) => routes.GetValueOrDefault(path);

        private static NodeFixture Node(
            string id,
            string type,
            IEnumerable<DocumentFixture> documents,
            IEnumerable<object> dependencies)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                kind = "coflnet-legal-agreement-node",
                id,
                type,
                documents = documents.Select(item => item.Json),
                dependencies
            });
            return new(bytes, Hash(bytes));
        }

        private static object Dependency(string id, NodeFixture node) => new
        {
            id,
            agreementHash = node.Hash,
            path = $"/legal/agreements/{node.Hash}.json"
        };
    }

    private sealed class DocumentFixture
    {
        public string Key { get; }
        public byte[] English { get; }
        public byte[] German { get; }
        public object Json { get; }
        public object Summary { get; }

        public DocumentFixture(string key, string title, string version, DateTimeOffset effective)
        {
            Key = key;
            English = Encoding.UTF8.GetBytes($"{key} English");
            German = Encoding.UTF8.GetBytes($"{key} German");
            var englishHash = Hash(English);
            var germanHash = Hash(German);
            var acceptanceHash = Hash(Encoding.UTF8.GetBytes(
                $"version={version}\nen={englishHash}\nde={germanHash}\n"));
            Json = new
            {
                key,
                title,
                version,
                publishedAtUtc = effective.ToString("O"),
                effectiveFromUtc = effective.ToString("O"),
                acceptanceHash,
                locales = new Dictionary<string, object>
                {
                    ["en"] = new { url = $"https://coflnet.com/{key}-en", sha256 = englishHash },
                    ["de"] = new { url = $"https://coflnet.com/{key}-de", sha256 = germanHash }
                }
            };
            Summary = new { key, version, acceptanceHash };
        }
    }

    private sealed record NodeFixture(byte[] Bytes, string Hash);

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class ManifestHandler(Fixture fixture, int failures = 0) : HttpMessageHandler
    {
        private int manifestRequests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/legal/manifest.json"
                && Interlocked.Increment(ref manifestRequests) <= failures)
                throw new HttpRequestException("temporarily unavailable");
            var content = path == "/legal/manifest.json" ? fixture.Manifest : fixture.Get(path);
            return Task.FromResult(new HttpResponseMessage(
                content == null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = content == null ? null : new ByteArrayContent(content)
            });
        }
    }
}
