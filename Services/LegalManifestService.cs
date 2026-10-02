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
using Prometheus;

namespace Coflnet.Sky.Api.Services;

/// <summary>Provides legal manifest operations.</summary>
public sealed class LegalManifestService : BackgroundService
{
    private const string AgreementId = "skycofl";
    private const string AgreementKind = "coflnet-legal-agreement-node";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(300);
    private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumRefreshInterval = TimeSpan.FromSeconds(3600);
    private static readonly Gauge LoadedTimestamp = Metrics.CreateGauge(
        "sky_api_legal_manifest_loaded_timestamp_seconds",
        "Unix time of the last successful legal manifest load");
    private static readonly Gauge WithdrawalVersionInfo = Metrics.CreateGauge(
        "sky_api_legal_manifest_withdrawal_version_info",
        "The withdrawal version currently stamped on purchases (value is always 1)",
        new GaugeConfiguration { LabelNames = new[] { "version" } });
    private static readonly Counter RefreshFailures = Metrics.CreateCounter(
        "sky_api_legal_manifest_refresh_failures_total",
        "Failed legal manifest load attempts");
    private static readonly Uri CoflnetOrigin = new("https://coflnet.com/");
    private readonly IHttpClientFactory clients;
    private readonly IConfiguration configuration;
    private readonly ILogger<LegalManifestService> logger;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private Uri manifestUri;
    private TimeSpan refreshInterval = DefaultRefreshInterval;
    private LegalManifestSnapshot current;

    /// <summary>Gets the current immutable snapshot, or null before the first successful load.</summary>
    /// <remarks>Callers that need several values must read this once and use that single reference.</remarks>
    public LegalManifestSnapshot Current => Volatile.Read(ref current);
    /// <summary>Gets the agreement of the current snapshot.</summary>
    public LegalAgreementSnapshot Agreement => Current?.Agreement;
    /// <summary>Gets the withdrawal of the current snapshot.</summary>
    public LegalDocumentSnapshot Withdrawal => Current?.Withdrawal;
    /// <summary>Gets the premium early start declaration of the current snapshot.</summary>
    public LegalDeclarationSnapshot PremiumEarlyStart => Current?.PremiumEarlyStart;

    /// <summary>Initializes a new instance of the <see cref="LegalManifestService"/> class.</summary>
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

    /// <inheritdoc/>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        manifestUri = new Uri(
            configuration["LEGAL_MANIFEST_URL"]
            ?? "https://coflnet.com/legal/manifest.json");
        if (!IsCoflnetHttpsOrigin(manifestUri))
            throw new InvalidOperationException("LEGAL_MANIFEST_URL must use the Coflnet HTTPS origin.");
        refreshInterval = ResolveRefreshInterval(configuration);
        return base.StartAsync(cancellationToken);
    }

    /// <summary>Reads LEGAL_MANIFEST_REFRESH_SECONDS (default 300) clamped to 30..3600 seconds.</summary>
    internal static TimeSpan ResolveRefreshInterval(IConfiguration configuration)
    {
        if (!double.TryParse(
                configuration["LEGAL_MANIFEST_REFRESH_SECONDS"],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var seconds)
            || double.IsNaN(seconds))
            return DefaultRefreshInterval;
        return TimeSpan.FromSeconds(Math.Clamp(
            seconds,
            MinimumRefreshInterval.TotalSeconds,
            MaximumRefreshInterval.TotalSeconds));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var untilEffective = await RefreshOnceAsync(stoppingToken);
                if (untilEffective > TimeSpan.Zero)
                    // Before the first load there is nothing to serve, so wake exactly when the agreement
                    // becomes effective; afterwards keep refreshing on the regular interval.
                    await delay(
                        Current == null
                            ? Min(untilEffective, MaximumDelay)
                            : Min(untilEffective, refreshInterval),
                        stoppingToken);
                else
                    await delay(refreshInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                RefreshFailures.Inc();
                if (Current == null)
                    logger.LogWarning(exception, "Loading the legal manifest failed; retrying in {RetryDelay}.", RetryDelay);
                else
                    logger.LogWarning(
                        exception,
                        "Refreshing the legal manifest failed; keeping the previous snapshot and retrying in {RetryDelay}.",
                        RetryDelay);
                await delay(RetryDelay, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Loads the manifest once and publishes it as the new snapshot when it differs.
    /// Returns the time until a not yet effective agreement starts (it is only staged), otherwise zero.
    /// </summary>
    internal async Task<TimeSpan> RefreshOnceAsync(CancellationToken cancellationToken)
    {
        var loaded = await Load(manifestUri, cancellationToken);
        var untilEffective = loaded.Agreement.EffectiveFromUtc - utcNow().UtcDateTime;
        if (untilEffective > TimeSpan.Zero)
        {
            TermsAcceptancePolicy.Stage(loaded.Agreement, loaded.PremiumEarlyStart);
            logger.LogInformation(
                "The legal agreement becomes effective at {EffectiveFromUtc}; enforcement is deferred.",
                loaded.Agreement.EffectiveFromUtc);
            return untilEffective;
        }

        var previous = Current;
        var changed = previous == null || !Describe(previous).Equals(Describe(loaded));
        // Keep the old instance when nothing changed so readers holding it stay valid.
        var snapshot = changed ? loaded : previous;
        if (changed)
        {
            Volatile.Write(ref current, snapshot);
            logger.LogInformation(
                "Legal manifest changed: withdrawal version {OldWithdrawalVersion} -> {NewWithdrawalVersion}, "
                + "agreement hash {OldAgreementHash} -> {NewAgreementHash}.",
                previous?.Withdrawal.Version,
                snapshot.Withdrawal.Version,
                previous?.Agreement.Hash,
                snapshot.Agreement.Hash);
            if (previous != null)
                WithdrawalVersionInfo.RemoveLabelled(previous.Withdrawal.Version);
            WithdrawalVersionInfo.WithLabels(snapshot.Withdrawal.Version).Set(1);
        }
        TermsAcceptancePolicy.Initialize(snapshot.Agreement, snapshot.PremiumEarlyStart);
        LoadedTimestamp.Set(utcNow().ToUnixTimeSeconds());
        return TimeSpan.Zero;
    }

    private static string Describe(LegalManifestSnapshot snapshot) =>
        string.Join(
            "|",
            snapshot.Agreement.Hash,
            snapshot.Agreement.EffectiveFromUtc.Ticks,
            snapshot.Withdrawal.Version,
            string.Join(",", snapshot.Withdrawal.Sha256.OrderBy(item => item.Key).Select(item => $"{item.Key}={item.Value}")),
            snapshot.PremiumEarlyStart.Version,
            string.Join(",", snapshot.PremiumEarlyStart.Sha256.OrderBy(item => item.Key).Select(item => $"{item.Key}={item.Value}")));

    private async Task<LegalManifestSnapshot> Load(Uri uri, CancellationToken cancellationToken)
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

}

/// <summary>An immutable view of the legal manifest. Read all values of one operation from the same instance.</summary>
/// <param name="Agreement">The agreement identity.</param>
/// <param name="Withdrawal">The withdrawal document identity.</param>
/// <param name="PremiumEarlyStart">The premium early start declaration.</param>
public sealed record LegalManifestSnapshot(
    LegalAgreementSnapshot Agreement,
    LegalDocumentSnapshot Withdrawal,
    LegalDeclarationSnapshot PremiumEarlyStart);

/// <summary>Represents a legal agreement snapshot.</summary>
/// <param name="Id">The agreement ID.</param>
/// <param name="Version">The agreement version.</param>
/// <param name="Hash">The agreement root hash.</param>
/// <param name="Url">The agreement URL.</param>
/// <param name="PublishedAtUtc">When the agreement was published.</param>
/// <param name="EffectiveFromUtc">When the agreement becomes effective.</param>
/// <param name="Documents">The agreement documents.</param>
public sealed record LegalAgreementSnapshot(
    string Id,
    string Version,
    string Hash,
    string Url,
    DateTime PublishedAtUtc,
    DateTime EffectiveFromUtc,
    IReadOnlyList<LegalAgreementDocumentSnapshot> Documents);

/// <summary>Represents a legal agreement document snapshot.</summary>
/// <param name="Key">The document key.</param>
/// <param name="Title">The document title.</param>
/// <param name="Version">The document version.</param>
/// <param name="AcceptanceHash">The hash used to record acceptance.</param>
/// <param name="PublishedAtUtc">When the document was published.</param>
/// <param name="EffectiveFromUtc">When the document becomes effective.</param>
/// <param name="Locales">The localized document variants.</param>
public sealed record LegalAgreementDocumentSnapshot(
    string Key,
    string Title,
    string Version,
    string AcceptanceHash,
    DateTime PublishedAtUtc,
    DateTime EffectiveFromUtc,
    IReadOnlyDictionary<string, LegalLocaleSnapshot> Locales);

/// <summary>Represents a legal locale snapshot.</summary>
/// <param name="Url">The document URL.</param>
/// <param name="Sha256">The document SHA-256 hash.</param>
public sealed record LegalLocaleSnapshot(string Url, string Sha256);

/// <summary>Represents a legal document snapshot.</summary>
/// <param name="Version">The document version.</param>
/// <param name="Sha256">The SHA-256 hash by locale.</param>
public sealed record LegalDocumentSnapshot(
    string Version,
    IReadOnlyDictionary<string, string> Sha256);

/// <summary>Represents a legal declaration snapshot.</summary>
/// <param name="Version">The declaration version.</param>
/// <param name="Locales">The declaration text by locale.</param>
/// <param name="Sha256">The SHA-256 hash by locale.</param>
public sealed record LegalDeclarationSnapshot(
    string Version,
    IReadOnlyDictionary<string, string> Locales,
    IReadOnlyDictionary<string, string> Sha256 = null);
