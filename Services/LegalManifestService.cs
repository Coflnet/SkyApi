using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Api.Services;

public sealed class LegalManifestService : BackgroundService
{
    private const string AgreementId = "skycofl";
    private const string AgreementKind = "coflnet-legal-agreement-node";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromHours(1);
    private static readonly Uri CoflnetOrigin = new("https://coflnet.com/");
    private readonly IHttpClientFactory clients;
    private readonly IConfiguration configuration;
    private readonly ILogger<LegalManifestService> logger;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private Uri manifestUri;

    public LegalAgreementSnapshot Agreement { get; private set; }
    public LegalDocumentSnapshot Withdrawal { get; private set; }
    public LegalDeclarationSnapshot PremiumEarlyStart { get; private set; }

    public LegalManifestService(
        IHttpClientFactory clients,
        IConfiguration configuration,
        ILogger<LegalManifestService> logger)
        : this(
            clients,
            configuration,
            logger,
            () => DateTimeOffset.UtcNow,
            (duration, token) => Task.Delay(duration, token))
    {
    }

    internal LegalManifestService(
        IHttpClientFactory clients,
        IConfiguration configuration,
        ILogger<LegalManifestService> logger,
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        this.clients = clients;
        this.configuration = configuration;
        this.logger = logger;
        this.utcNow = utcNow;
        this.delay = delay;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        manifestUri = new Uri(
            configuration["LEGAL_MANIFEST_URL"]
            ?? "https://coflnet.com/legal/manifest.json");
        if (!IsCoflnetHttpsOrigin(manifestUri))
            throw new InvalidOperationException("LEGAL_MANIFEST_URL must use the Coflnet HTTPS origin.");
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var loaded = await Load(manifestUri, stoppingToken);
                var untilEffective = loaded.Agreement.EffectiveFromUtc - utcNow().UtcDateTime;
                if (untilEffective > TimeSpan.Zero)
                {
                    logger.LogInformation(
                        "The legal agreement becomes effective at {EffectiveFromUtc}; activation is deferred.",
                        loaded.Agreement.EffectiveFromUtc);
                    await delay(Min(untilEffective, MaximumDelay), stoppingToken);
                    continue;
                }

                Agreement = loaded.Agreement;
                Withdrawal = loaded.Withdrawal;
                PremiumEarlyStart = loaded.PremiumEarlyStart;
                TermsAcceptancePolicy.Initialize(Agreement, PremiumEarlyStart);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Loading the legal manifest failed; retrying in {RetryDelay}.", RetryDelay);
                await delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task<LoadedManifest> Load(Uri uri, CancellationToken cancellationToken)
    {
        var client = clients.CreateClient(nameof(LegalManifestService));
        var manifestBytes = await client.GetByteArrayAsync(uri, cancellationToken);
        var manifest = Deserialize<Manifest>(manifestBytes, "The legal manifest is invalid.");
        if (manifest.SchemaVersion != 1
            || manifest.AgreementTreeVersion != 1
            || !Uri.TryCreate(manifest.Source, UriKind.Absolute, out var source)
            || source != CoflnetOrigin)
            throw new InvalidOperationException("The legal manifest source or schema is invalid.");
        if (!manifest.Agreements.TryGetValue(AgreementId, out var summary)
            || summary.Type != "service"
            || !IsSha256(summary.AgreementHash)
            || !TryAgreementUri(summary.AgreementUrl, summary.AgreementHash, out var agreementUri))
            throw new InvalidOperationException("The SkyCofl agreement root is incomplete.");

        var loadedRoot = await LoadAgreement(
            client,
            agreementUri,
            AgreementId,
            summary.AgreementHash,
            new Dictionary<string, LoadedAgreement>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            cancellationToken);
        if (loadedRoot.Descriptor.Type != "service")
            throw new InvalidOperationException("The SkyCofl agreement root has the wrong type.");

        var resolved = ResolveDocuments(loadedRoot);
        if (summary.ResolvedDocuments.Count != resolved.Count
            || summary.ResolvedDocuments.Select(item => item.Key).Distinct().Count()
                != summary.ResolvedDocuments.Count)
            throw new InvalidOperationException("The SkyCofl resolved document list is invalid.");

        var documents = new List<LegalAgreementDocumentSnapshot>();
        foreach (var item in summary.ResolvedDocuments)
        {
            if (!resolved.TryGetValue(item.Key, out var descriptorDocument)
                || descriptorDocument.Version != item.Version
                || !string.Equals(
                    descriptorDocument.AcceptanceHash,
                    item.AcceptanceHash,
                    StringComparison.OrdinalIgnoreCase)
                || !manifest.Documents.TryGetValue(item.Key, out var manifestDocument))
                throw new InvalidOperationException("The SkyCofl resolved document list does not match its root.");
            manifestDocument.Key = item.Key;
            if (!SameDocument(descriptorDocument, manifestDocument))
                throw new InvalidOperationException("The SkyCofl resolved document list does not match its root.");
            await VerifyDocument(client, manifestDocument, cancellationToken);
            documents.Add(ToSnapshot(item.Key, manifestDocument));
        }

        var ownServiceTerms = loadedRoot.Descriptor.Documents.SingleOrDefault(
            item => item.Key == "skycoflTerms")
            ?? throw new InvalidOperationException("The SkyCofl root does not contain its service terms.");
        var publishedAt = documents.Max(item => item.PublishedAtUtc);
        var effectiveFrom = documents.Max(item => item.EffectiveFromUtc);
        var agreement = new LegalAgreementSnapshot(
            AgreementId,
            ownServiceTerms.Version,
            summary.AgreementHash.ToLowerInvariant(),
            agreementUri.ToString(),
            publishedAt,
            effectiveFrom,
            documents);

        if (!manifest.Documents.TryGetValue("withdrawal", out var withdrawal))
            throw new InvalidOperationException("The withdrawal entry in the legal manifest is missing.");
        await VerifyDocument(client, withdrawal, cancellationToken, false);

        if (!manifest.Declarations.TryGetValue("premiumEarlyStart", out var premium)
            || string.IsNullOrWhiteSpace(premium.Version)
            || premium.Locales.Count != 2
            || !premium.Locales.ContainsKey("en")
            || !premium.Locales.ContainsKey("de"))
            throw new InvalidOperationException("The Premium declaration is missing.");
        foreach (var locale in premium.Locales.Values)
            if (string.IsNullOrWhiteSpace(locale.Text)
                || !Sha256(Encoding.UTF8.GetBytes(locale.Text)).Equals(
                    locale.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A Premium declaration hash is invalid.");

        return new(
            agreement,
            new LegalDocumentSnapshot(
                withdrawal.Version,
                withdrawal.Locales.ToDictionary(item => item.Key, item => item.Value.Sha256)),
            new LegalDeclarationSnapshot(
                premium.Version,
                premium.Locales.ToDictionary(item => item.Key, item => item.Value.Text),
                premium.Locales.ToDictionary(item => item.Key, item => item.Value.Sha256)));
    }

    private static async Task<LoadedAgreement> LoadAgreement(
        HttpClient client,
        Uri uri,
        string expectedId,
        string expectedHash,
        Dictionary<string, LoadedAgreement> loaded,
        HashSet<string> active,
        CancellationToken cancellationToken)
    {
        if (active.Contains(expectedHash))
            throw new InvalidOperationException("The agreement graph contains a cycle.");
        if (loaded.TryGetValue(expectedHash, out var cached))
        {
            if (cached.Descriptor.Id != expectedId)
                throw new InvalidOperationException("An agreement hash was reused for another ID.");
            return cached;
        }
        active.Add(expectedHash);

        var bytes = await client.GetByteArrayAsync(uri, cancellationToken);
        if (!Sha256(bytes).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Agreement hash mismatch for {uri}.");
        var descriptor = Deserialize<AgreementDescriptor>(bytes, "An agreement descriptor is invalid.");
        if (descriptor.SchemaVersion != 1
            || descriptor.Kind != AgreementKind
            || descriptor.Id != expectedId
            || descriptor.Type is not ("shared" or "service" or "role"))
            throw new InvalidOperationException("An agreement descriptor identity is invalid.");

        var result = new LoadedAgreement(descriptor, []);
        loaded.Add(expectedHash, result);
        foreach (var dependency in descriptor.Dependencies)
        {
            if (!IsSha256(dependency.AgreementHash)
                || !TryAgreementUri(dependency.Path, dependency.AgreementHash, out var dependencyUri))
                throw new InvalidOperationException("An agreement dependency is invalid.");
            result.Dependencies.Add(await LoadAgreement(
                client,
                dependencyUri,
                dependency.Id,
                dependency.AgreementHash,
                loaded,
                active,
                cancellationToken));
        }
        active.Remove(expectedHash);
        return result;
    }

    private static Dictionary<string, Document> ResolveDocuments(LoadedAgreement root)
    {
        var resolved = new Dictionary<string, Document>(StringComparer.Ordinal);
        void Visit(LoadedAgreement agreement)
        {
            foreach (var document in agreement.Descriptor.Documents)
            {
                if (resolved.TryGetValue(document.Key, out var existing)
                    && !SameDocument(existing, document))
                    throw new InvalidOperationException("The agreement graph contains conflicting documents.");
                resolved[document.Key] = document;
            }
            foreach (var dependency in agreement.Dependencies)
                Visit(dependency);
        }
        Visit(root);
        return resolved;
    }

    private static LegalAgreementDocumentSnapshot ToSnapshot(string key, Document document) =>
        new(
            key,
            document.Title,
            document.Version,
            document.AcceptanceHash,
            DateTimeOffset.Parse(document.PublishedAtUtc).UtcDateTime,
            DateTimeOffset.Parse(document.EffectiveFromUtc).UtcDateTime,
            document.Locales.ToDictionary(
                item => item.Key,
                item => new LegalLocaleSnapshot(item.Value.Url, item.Value.Sha256)));

    private static async Task VerifyDocument(
        HttpClient client,
        Document document,
        CancellationToken cancellationToken,
        bool acceptanceRequired = true)
    {
        if (string.IsNullOrWhiteSpace(document.Version)
            || !DateTimeOffset.TryParse(document.PublishedAtUtc, out _)
            || !DateTimeOffset.TryParse(document.EffectiveFromUtc, out _)
            || document.Locales.Count != 2
            || !document.Locales.TryGetValue("en", out var english)
            || !document.Locales.TryGetValue("de", out var german))
            throw new InvalidOperationException("A legal document entry is incomplete.");
        if (acceptanceRequired)
        {
            var canonical = Encoding.UTF8.GetBytes(
                $"version={document.Version}\nen={english.Sha256}\nde={german.Sha256}\n");
            if (!Sha256(canonical).Equals(document.AcceptanceHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A legal document acceptance hash is invalid.");
        }
        foreach (var locale in document.Locales.Values)
        {
            if (!Uri.TryCreate(locale.Url, UriKind.Absolute, out var documentUri)
                || !IsCoflnetHttpsOrigin(documentUri)
                || !IsSha256(locale.Sha256))
                throw new InvalidOperationException("A legal document location is invalid.");
            var content = await client.GetByteArrayAsync(documentUri, cancellationToken);
            if (!Sha256(content).Equals(locale.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Legal document hash mismatch for {documentUri}.");
        }
    }

    private static bool SameDocument(Document left, Document right) =>
        left.Key == right.Key
        && left.Version == right.Version
        && left.PublishedAtUtc == right.PublishedAtUtc
        && left.EffectiveFromUtc == right.EffectiveFromUtc
        && string.Equals(left.AcceptanceHash, right.AcceptanceHash, StringComparison.OrdinalIgnoreCase)
        && left.Locales.Count == right.Locales.Count
        && left.Locales.All(item => right.Locales.TryGetValue(item.Key, out var other)
            && item.Value.Url == other.Url
            && string.Equals(item.Value.Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase));

    private static bool TryAgreementUri(string value, string hash, out Uri uri)
    {
        if (!Uri.TryCreate(CoflnetOrigin, value, out uri)
            || !IsCoflnetHttpsOrigin(uri)
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
            || uri.AbsolutePath != $"/legal/agreements/{hash}.json")
        {
            uri = null;
            return false;
        }
        return true;
    }

    private static bool IsCoflnetHttpsOrigin(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && uri.IdnHost.Equals("coflnet.com", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsSha256(string value) =>
        value?.Length == 64 && value.All(Uri.IsHexDigit);

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static T Deserialize<T>(byte[] bytes, string message) =>
        JsonSerializer.Deserialize<T>(
            bytes,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException(message);

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;

    private sealed class Manifest
    {
        public int SchemaVersion { get; set; }
        public int AgreementTreeVersion { get; set; }
        public string Source { get; set; }
        public Dictionary<string, Document> Documents { get; set; } = [];
        public Dictionary<string, AgreementSummary> Agreements { get; set; } = [];
        public Dictionary<string, Declaration> Declarations { get; set; } = [];
    }

    private sealed class AgreementSummary
    {
        public string Type { get; set; }
        public string AgreementHash { get; set; }
        public string AgreementUrl { get; set; }
        public List<DocumentSummary> ResolvedDocuments { get; set; } = [];
    }

    private sealed class DocumentSummary
    {
        public string Key { get; set; }
        public string Version { get; set; }
        public string AcceptanceHash { get; set; }
    }

    private sealed class AgreementDescriptor
    {
        public int SchemaVersion { get; set; }
        public string Kind { get; set; }
        public string Id { get; set; }
        public string Type { get; set; }
        public List<Document> Documents { get; set; } = [];
        public List<AgreementDependency> Dependencies { get; set; } = [];
    }

    private sealed class AgreementDependency
    {
        public string Id { get; set; }
        public string AgreementHash { get; set; }
        public string Path { get; set; }
    }

    private sealed class Document
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }
        public string PublishedAtUtc { get; set; }
        public string EffectiveFromUtc { get; set; }
        public Dictionary<string, Locale> Locales { get; set; } = [];
        public string AcceptanceHash { get; set; }
    }

    private sealed class Locale
    {
        public string Url { get; set; }
        public string Sha256 { get; set; }
    }

    private sealed class Declaration
    {
        public string Version { get; set; }
        public Dictionary<string, DeclarationLocale> Locales { get; set; } = [];
    }

    private sealed class DeclarationLocale
    {
        public string Text { get; set; }
        public string Sha256 { get; set; }
    }

    private sealed record LoadedAgreement(
        AgreementDescriptor Descriptor,
        List<LoadedAgreement> Dependencies);

    private sealed record LoadedManifest(
        LegalAgreementSnapshot Agreement,
        LegalDocumentSnapshot Withdrawal,
        LegalDeclarationSnapshot PremiumEarlyStart);
}

public sealed record LegalAgreementSnapshot(
    string Id,
    string Version,
    string Hash,
    string Url,
    DateTime PublishedAtUtc,
    DateTime EffectiveFromUtc,
    IReadOnlyList<LegalAgreementDocumentSnapshot> Documents);

public sealed record LegalAgreementDocumentSnapshot(
    string Key,
    string Title,
    string Version,
    string AcceptanceHash,
    DateTime PublishedAtUtc,
    DateTime EffectiveFromUtc,
    IReadOnlyDictionary<string, LegalLocaleSnapshot> Locales);

public sealed record LegalLocaleSnapshot(string Url, string Sha256);

public sealed record LegalDocumentSnapshot(
    string Version,
    IReadOnlyDictionary<string, string> Sha256);

public sealed record LegalDeclarationSnapshot(
    string Version,
    IReadOnlyDictionary<string, string> Locales,
    IReadOnlyDictionary<string, string> Sha256 = null);
