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

/// <summary>Contains legal manifest service tests.</summary>
[TestFixture]
[NonParallelizable]
public class LegalManifestServiceTests
{
    /// <summary>Resets shared agreement state before each test.</summary>
    [SetUp]
    public void SetUp() => TermsAcceptancePolicy.ResetForTests();

    /// <summary>Performs the tear down operation.</summary>
    [TearDown]
    public void TearDown() => TermsAcceptancePolicy.ResetForTests();

    /// <summary>Performs the future root does not block startup and activates when effective operation.</summary>
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
                return resume.Task.IsCompleted
                    ? Task.Delay(Timeout.InfiniteTimeSpan, token)
                    : resume.Task.WaitAsync(token);
            });

        await service.StartAsync(CancellationToken.None);
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.That(service.Agreement, Is.Null);
        var signupStatus = TermsAcceptancePolicy.GetStatus(
            false,
            utcNow: now.UtcDateTime,
            canContinueWithoutAccepting: false);
        var existingUserStatus = TermsAcceptancePolicy.GetStatus(
            false,
            utcNow: now.UtcDateTime,
            canContinueWithoutAccepting: true);
        var pendingAgreement = TermsAcceptancePolicy.GetAcceptanceAgreement(false);
        var acceptedAt = now.UtcDateTime;
        var acceptedSignupStatus = TermsAcceptancePolicy.GetStatus(
            true,
            acceptedAt,
            now.UtcDateTime,
            canContinueWithoutAccepting: true,
            agreementOverride: pendingAgreement);
        Assert.Multiple(() =>
        {
            Assert.That(signupStatus.Required, Is.True);
            Assert.That(signupStatus.Hash, Has.Length.EqualTo(64));
            Assert.That(signupStatus.Documents, Has.Count.EqualTo(4));
            Assert.That(signupStatus.CanStartNewContract, Is.False);
            Assert.That(existingUserStatus.Required, Is.False);
            Assert.That(existingUserStatus.Hash, Is.Empty);
            Assert.That(acceptedSignupStatus.Required, Is.False);
            Assert.That(acceptedSignupStatus.Hash, Is.EqualTo(pendingAgreement.Hash));
            Assert.That(acceptedSignupStatus.AcceptedAtUtc, Is.EqualTo(acceptedAt));
            Assert.That(TermsAcceptancePolicy.CanAcceptAgreement(false, now.UtcDateTime), Is.True);
            Assert.That(TermsAcceptancePolicy.CanAcceptAgreement(true, now.UtcDateTime), Is.False);
        });

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
            Assert.That(TermsAcceptancePolicy.GetStatus(
                false,
                utcNow: now.UtcDateTime,
                canContinueWithoutAccepting: true).Required, Is.True);
            Assert.That(TermsAcceptancePolicy.CanAcceptAgreement(true, now.UtcDateTime), Is.True);
        });
    }

    /// <summary>Performs the unavailable manifest retries and later activates operation.</summary>
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
                return resume.Task.IsCompleted
                    ? Task.Delay(Timeout.InfiniteTimeSpan, token)
                    : resume.Task.WaitAsync(token);
            });

        await service.StartAsync(CancellationToken.None);
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.That(service.Agreement, Is.Null);

        resume.TrySetResult();
        await WaitUntil(() => service.Agreement?.Version == "retry");
    }

    /// <summary>Performs the tampered root is not activated operation.</summary>
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

    /// <summary>Performs the manifest url requires exact coflnet https origin operation.</summary>
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

    /// <summary>Drives the refresh loop one delay at a time.</summary>
    private sealed class LoopDriver : IDisposable
    {
        private readonly SemaphoreSlim entered = new(0);
        private readonly SemaphoreSlim gate = new(0);
        public List<TimeSpan> Delays { get; } = [];

        public Task Delay(TimeSpan duration, CancellationToken token)
        {
            lock (Delays)
                Delays.Add(duration);
            entered.Release();
            return gate.WaitAsync(token);
        }

        /// <summary>Waits until the loop is waiting in a delay, i.e. one iteration finished.</summary>
        public Task Settled() => entered.WaitAsync(TimeSpan.FromSeconds(5));

        /// <summary>Lets the pending delay elapse and waits for the following iteration.</summary>
        public async Task Tick()
        {
            gate.Release();
            await Settled();
        }

        public void Dispose()
        {
            entered.Dispose();
            gate.Dispose();
        }
    }

    /// <summary>The first load publishes one snapshot and schedules the default refresh.</summary>
    [Test]
    public async Task FirstLoadPublishesSnapshotAndSchedulesRefresh()
    {
        var now = DateTimeOffset.Parse("2026-09-29T08:00:00Z");
        using var driver = new LoopDriver();
        using var service = CreateService(
            new ManifestHandler(new Fixture("v1", now.AddDays(-1))), () => now, driver.Delay);

        await service.StartAsync(CancellationToken.None);
        await driver.Settled();

        Assert.Multiple(() =>
        {
            Assert.That(service.Current.Withdrawal.Version, Is.EqualTo("v1"));
            Assert.That(service.Agreement, Is.SameAs(service.Current.Agreement));
            Assert.That(driver.Delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(300) }));
            Assert.That(TermsAcceptancePolicy.CurrentHash, Is.EqualTo(service.Agreement.Hash));
        });
    }

    /// <summary>A manifest changed after the first load is picked up on the next refresh.</summary>
    [Test]
    public async Task ChangedManifestIsPickedUpOnNextRefresh()
    {
        var now = DateTimeOffset.Parse("2026-09-30T08:00:00Z");
        using var driver = new LoopDriver();
        var handler = new ManifestHandler(new Fixture("v1", now.AddDays(-2)));
        using var service = CreateService(handler, () => now, driver.Delay);
        await service.StartAsync(CancellationToken.None);
        await driver.Settled();
        var first = service.Current;

        handler.Fixture = new Fixture("v2", now.AddDays(-1));
        await driver.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(service.Current, Is.Not.SameAs(first));
            Assert.That(service.Withdrawal.Version, Is.EqualTo("v2"));
            Assert.That(service.Withdrawal.Sha256["en"], Is.Not.EqualTo(first.Withdrawal.Sha256["en"]));
            Assert.That(service.Agreement.Hash, Is.Not.EqualTo(first.Agreement.Hash));
            Assert.That(TermsAcceptancePolicy.CurrentHash, Is.EqualTo(service.Agreement.Hash));
        });
    }

    /// <summary>An unchanged manifest keeps the same snapshot instance.</summary>
    [Test]
    public async Task UnchangedManifestKeepsSnapshotInstance()
    {
        var now = DateTimeOffset.Parse("2026-09-30T08:00:00Z");
        using var driver = new LoopDriver();
        using var service = CreateService(
            new ManifestHandler(new Fixture("v1", now.AddDays(-2))), () => now, driver.Delay);
        await service.StartAsync(CancellationToken.None);
        await driver.Settled();
        var first = service.Current;

        await driver.Tick();

        Assert.That(service.Current, Is.SameAs(first));
    }

    /// <summary>A failed refresh keeps the last good snapshot, retries soon and recovers.</summary>
    [Test]
    public async Task FailedRefreshKeepsPreviousSnapshot()
    {
        var now = DateTimeOffset.Parse("2026-09-30T08:00:00Z");
        using var driver = new LoopDriver();
        var handler = new ManifestHandler(new Fixture("v1", now.AddDays(-2)));
        using var service = CreateService(handler, () => now, driver.Delay);
        await service.StartAsync(CancellationToken.None);
        await driver.Settled();
        var first = service.Current;

        handler.FailAll = true;
        await driver.Tick();
        Assert.Multiple(() =>
        {
            Assert.That(service.Current, Is.SameAs(first));
            Assert.That(TermsAcceptancePolicy.CurrentHash, Is.EqualTo(first.Agreement.Hash));
            Assert.That(driver.Delays.Last(), Is.EqualTo(TimeSpan.FromSeconds(30)));
        });

        handler.FailAll = false;
        handler.Fixture = new Fixture("v2", now.AddDays(-1));
        await driver.Tick();
        Assert.That(service.Withdrawal.Version, Is.EqualTo("v2"));
    }

    /// <summary>A reader holding a snapshot taken before a swap never sees mixed values.</summary>
    [Test]
    public async Task SnapshotTakenBeforeSwapStaysConsistent()
    {
        var now = DateTimeOffset.Parse("2026-09-30T08:00:00Z");
        using var driver = new LoopDriver();
        var handler = new ManifestHandler(new Fixture("v1", now.AddDays(-2)));
        using var service = CreateService(handler, () => now, driver.Delay);
        await service.StartAsync(CancellationToken.None);
        await driver.Settled();
        var reader = service.Current;

        handler.Fixture = new Fixture("v2", now.AddDays(-1));
        await driver.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(reader.Withdrawal.Version, Is.EqualTo("v1"));
            Assert.That(reader.Agreement.Version, Is.EqualTo("v1"));
            Assert.That(reader.Agreement.Documents.All(item => item.Version == "v1"), Is.True);
            Assert.That(service.Current.Withdrawal.Version, Is.EqualTo("v2"));
            Assert.That(service.Current.Agreement.Version, Is.EqualTo("v2"));
        });
    }

    /// <summary>A not yet effective agreement is staged only and the previous snapshot is kept.</summary>
    [Test]
    public async Task FutureAgreementAfterFirstLoadKeepsCurrentSnapshot()
    {
        var now = DateTimeOffset.Parse("2026-09-30T08:00:00Z");
        using var driver = new LoopDriver();
        var handler = new ManifestHandler(new Fixture("v1", now.AddDays(-2)));
        using var service = CreateService(handler, () => now, driver.Delay);
        await service.StartAsync(CancellationToken.None);
        await driver.Settled();

        handler.Fixture = new Fixture("v2", now.AddHours(2));
        await driver.Tick();

        Assert.Multiple(() =>
        {
            Assert.That(service.Withdrawal.Version, Is.EqualTo("v1"));
            Assert.That(TermsAcceptancePolicy.GetAcceptanceAgreement(false).Version, Is.EqualTo("v2"));
            Assert.That(driver.Delays.Last(), Is.EqualTo(TimeSpan.FromSeconds(300)));
        });
    }

    /// <summary>The refresh interval is read from configuration and clamped to 30..3600 seconds.</summary>
    [TestCase(null, 300)]
    [TestCase("", 300)]
    [TestCase("abc", 300)]
    [TestCase("120", 120)]
    [TestCase("1", 30)]
    [TestCase("-5", 30)]
    [TestCase("86400", 3600)]
    public void RefreshIntervalIsClamped(string configured, int expectedSeconds)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string> { ["LEGAL_MANIFEST_REFRESH_SECONDS"] = configured }).Build();

        Assert.That(
            LegalManifestService.ResolveRefreshInterval(configuration),
            Is.EqualTo(TimeSpan.FromSeconds(expectedSeconds)));
    }

    /// <summary>The configured refresh interval drives the loop delay.</summary>
    [Test]
    public async Task ConfiguredRefreshIntervalIsUsed()
    {
        var now = DateTimeOffset.Parse("2026-09-30T08:00:00Z");
        using var driver = new LoopDriver();
        using var service = CreateService(
            new ManifestHandler(new Fixture("v1", now.AddDays(-2))),
            () => now,
            driver.Delay,
            refreshSeconds: "45");
        await service.StartAsync(CancellationToken.None);
        await driver.Settled();

        Assert.That(driver.Delays.Single(), Is.EqualTo(TimeSpan.FromSeconds(45)));
    }

    private static LegalManifestService CreateService(
        HttpMessageHandler handler,
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delay,
        string url = "https://coflnet.com/legal/manifest.json",
        string refreshSeconds = null) =>
        new(
            new ClientFactory(new HttpClient(handler)),
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["LEGAL_MANIFEST_URL"] = url,
                    ["LEGAL_MANIFEST_REFRESH_SECONDS"] = refreshSeconds
                }).Build(),
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
            English = Encoding.UTF8.GetBytes($"{key} English {version}");
            German = Encoding.UTF8.GetBytes($"{key} German {version}");
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
        public Fixture Fixture { get; set; } = fixture;
        public bool FailAll { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (FailAll)
                throw new HttpRequestException("manifest host down");
            if (path == "/legal/manifest.json"
                && Interlocked.Increment(ref manifestRequests) <= failures)
                throw new HttpRequestException("temporarily unavailable");
            var content = path == "/legal/manifest.json" ? Fixture.Manifest : Fixture.Get(path);
            return Task.FromResult(new HttpResponseMessage(
                content == null ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            {
                Content = content == null ? null : new ByteArrayContent(content)
            });
        }
    }
}
