using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>An update result for one installed plugin version and the selected client build.</summary>
/// <param name="Id">Plugin identity within the selected game.</param>
/// <param name="InstalledVersion">Exact installed baseline; checking never changes the client's selection.</param>
/// <param name="Status">current, available, installed, unmanaged, integrity, network or disk.</param>
/// <param name="Version">Higher compatible version advertised by the original source.</param>
/// <param name="ManifestSha256">Catalog digest required to download the exact displayed release.</param>
/// <param name="SourceName">Locally configured display label of the source serving the selected release.</param>
/// <param name="TargetId">Canonical identity of a renamed release; omitted for same-identity updates.</param>
public sealed record PluginUpdateState(string Id, string InstalledVersion, string Status, string? Version = null,
    string? ManifestSha256 = null, string? SourceName = null, string? TargetId = null);

/// <summary>Discovers plugin upgrades from enabled sources and reuses immutable installation without activating code.</summary>
public sealed partial class PluginUpdateService : IAsyncDisposable
{
    private readonly PluginLibrary _library;
    private readonly InstanceStore _instances;
    private readonly StoreSourceStore _sources;
    private readonly HttpClient _http;
    private readonly Func<StoreSource, IDownloadSource> _openSource;
    private readonly Func<StoreSource, CancellationToken, Task<StoreCatalog>>? _readCatalog;
    private readonly bool _allowRunningUpdates;
    private readonly Action<StoreSource, StoreCatalog>? _applyLifecycle;
    private readonly Func<string, GameDefinition> _game;
    private readonly Func<string, bool> _running;
    private readonly Func<StoreSource, StorePackage, string, CancellationToken, Task<ClientDownloadTask>> _install;
    private readonly Func<StoreSource, StorePackage, string, string, CancellationToken, Task<ClientDownloadTask>>? _installRequired;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Connects validated library receipts and current source definitions to the shared download queue.</summary>
    public PluginUpdateService(PluginLibrary library, InstanceStore instances, StoreSourceStore sources, HttpClient http,
        Func<StoreSource, IDownloadSource> openSource, Func<string, GameDefinition> game, Func<string, bool> running,
        Func<StoreSource, StorePackage, string, CancellationToken, Task<ClientDownloadTask>> install,
        Func<StoreSource, StorePackage, string, string, CancellationToken, Task<ClientDownloadTask>>? installRequired = null,
        Func<StoreSource, CancellationToken, Task<StoreCatalog>>? readCatalog = null, bool allowRunningUpdates = false,
        Action<StoreSource, StoreCatalog>? applyLifecycle = null)
    { _library = library; _instances = instances; _sources = sources; _http = http; _openSource = openSource; _game = game; _running = running; _install = install; _installRequired = installRequired; _readCatalog = readCatalog; _allowRunningUpdates = allowRunningUpdates; _applyLifecycle = applyLifecycle; }

    /// <summary>Checks installed compatible versions with at most one catalog request per source and no activation.</summary>
    public async Task<IReadOnlyList<PluginUpdateState>> CheckAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token); var ct = linked.Token;
        await _operation.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var instance = _instances.Load(instanceId); var game = _game(instance.Game); var build = game.Builds.Single(b => b.Id == instance.Build);
            var packages = _library.Packages(game);
            FilePluginResolver.EnsureInstalled(_instances, instanceId, game, build, packages);
            instance = _instances.Load(instanceId);
            packages = _library.Packages(game);
            var cache = new Dictionary<string, Task<StoreCatalog>>(StringComparer.Ordinal);
            var results = new List<PluginUpdateState>();
            foreach (var plugin in PluginIdentityMigration.VisiblePackages(packages, [build]).Where(p => PluginCatalog.IsAvailable(p, build)))
            {
                ct.ThrowIfCancellationRequested();
                var (result, _, _) = await DiscoverAsync(plugin, build, packages, cache, ct).ConfigureAwait(false);
                if (result.Status != "deprecated") results.Add(result);
            }
            foreach (var issue in _library.LoadIssues().Where(issue => issue.Game == game.Id && issue.Id is not null && issue.Version is not null))
            {
                if (PluginIdentityMigration.InstalledSuccessor(issue.Id!, build, packages) is not null) continue;
                var (result, _, _) = await DiscoverRejectedAsync(issue, build, packages, cache, ct).ConfigureAwait(false);
                if (result.Status != "deprecated") results.Add(result);
            }
            return results;
        }
        finally { _operation.Release(); }
    }

    /// <summary>Revalidates source, baseline and catalog before downloading; successful installation switches only the target client's unchanged enabled selection.</summary>
    public async Task<ClientDownloadTask> InstallAsync(string instanceId, string id, string installedVersion, string version,
        string manifestSha256, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token); var ct = linked.Token;
        await _operation.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_allowRunningUpdates && _running(instanceId)) throw new InvalidOperationException("End the running game before updating its plugins.");
            var instance = _instances.Load(instanceId); var game = _game(instance.Game); var build = game.Builds.Single(b => b.Id == instance.Build);
            var packages = _library.Packages(game);
            FilePluginResolver.EnsureInstalled(_instances, instanceId, game, build, packages);
            instance = _instances.Load(instanceId);
            packages = _library.Packages(game);
            var plugin = packages.SingleOrDefault(p => p.Id == id && p.Version == installedVersion && PluginCatalog.IsAvailable(p, build));
            var rejected = plugin is null
                ? _library.LoadIssues().SingleOrDefault(issue => issue.Game == game.Id && issue.Id == id && issue.Version == installedVersion) : null;
            var (result, source, package) = plugin is not null
                ? await DiscoverAsync(plugin, build, packages, new(), ct).ConfigureAwait(false)
                : rejected is not null ? await DiscoverRejectedAsync(rejected, build, packages, new(), ct).ConfigureAwait(false)
                : await DiscoverAcrossSources(id, game.Id, installedVersion, null, null, build, packages, new(), ct).ConfigureAwait(false);
            if (result.Status is not ("available" or "installed") || result.Version != version || result.ManifestSha256 != manifestSha256)
                throw new InvalidOperationException("This plugin update changed or is unavailable. Check for updates again.");
            return await _install(source!, package!, instanceId, ct).ConfigureAwait(false);
        }
        finally { _operation.Release(); }
    }

    private Task<(PluginUpdateState State, StoreSource? Source, StorePackage? Package)> DiscoverAsync(PluginDefinition plugin,
        BuildDefinition build, IReadOnlyList<PluginDefinition> installed, Dictionary<string, Task<StoreCatalog>> cache, CancellationToken ct)
        => DiscoverAcrossSources(plugin.Id, plugin.Game, plugin.Version, _library.Origin(plugin), plugin, build, installed, cache, ct);

    private async Task<StoreCatalog> ReadCatalogAsync(StoreSource source, CancellationToken ct)
    {
        var catalog = _readCatalog is not null ? await _readCatalog(source, ct).ConfigureAwait(false)
            : StoreCatalogFile.Parse(source.Name, await SourceDocumentReader.ReadAsync(_http, _openSource(source), source.CatalogPath, ct).ConfigureAwait(false));
        ct.ThrowIfCancellationRequested();
        _applyLifecycle?.Invoke(source, catalog);
        return catalog;
    }

    private Task<(PluginUpdateState State, StoreSource? Source, StorePackage? Package)> DiscoverRejectedAsync(PluginLoadIssue issue,
        BuildDefinition build, IReadOnlyList<PluginDefinition> installed, Dictionary<string, Task<StoreCatalog>> cache, CancellationToken ct)
    {
        var origin = _library.Origin(issue.Folder);
        return DiscoverAcrossSources(issue.Id!, issue.Game!, issue.Version!, origin, null, build, installed, cache, ct);
    }

    private async Task<(PluginUpdateState State, StoreSource? Source, StorePackage? Package)> DiscoverAcrossSources(
        string id, string game, string version, PluginStoreOrigin? origin, PluginDefinition? definition, BuildDefinition build,
        IReadOnlyList<PluginDefinition> installed, Dictionary<string, Task<StoreCatalog>> cache, CancellationToken ct)
    {
        var state = new PluginUpdateState(id, version, "unmanaged");
        var sources = _sources.List().Where(source => source.Enabled && source.BaseUrl is not null).ToArray();
        if (sources.Length == 0) return (state, null, null);

        var candidates = new List<(StoreSource Source, StorePackage Package)>();
        var successfulCatalogs = 0;
        string? failure = null;
        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!cache.TryGetValue(source.Id, out var request))
                {
                    request = ReadCatalogAsync(source, ct);
                    cache.Add(source.Id, request);
                }
                var catalog = await request.ConfigureAwait(false);
                successfulCatalogs++;
                if (catalog.DeprecatedPlugins.Any(retired => retired.Id == id && retired.Game == game))
                    return (state with { Status = "deprecated" }, null, null);
                var expected = origin ?? new PluginStoreOrigin(string.Empty, string.Empty, string.Empty);
                var (status, next) = definition is not null
                    ? Select(definition, expected, build, catalog)
                    : Select(id, game, version, expected, build, catalog);
                if (status == "integrity") return (state with { Status = "integrity" }, null, null);
                if (next is not null) candidates.Add((source, next));
            }
            catch (Exception error) when (!ct.IsCancellationRequested && error is IOException or InvalidDataException or DefinitionException or
                HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or ArgumentException or FormatException or InvalidOperationException or UnauthorizedAccessException)
            {
                failure = error is HttpRequestException or OperationCanceledException ? "network" :
                    error is IOException or UnauthorizedAccessException ? "disk" : "integrity";
            }
        }

        if (candidates.Count == 0)
            return (state with { Status = failure ?? (successfulCatalogs > 0 ? "current" : "unmanaged") }, null, null);

        string canonical;
        try { canonical = PreferredIdentity(id, candidates.Select(candidate => candidate.Package)); }
        catch (DefinitionException) { return (state with { Status = "integrity" }, null, null); }
        var canonicalOffers = candidates.Where(candidate => candidate.Package.Id == canonical).ToArray();
        var newest = canonicalOffers.OrderByDescending(candidate => PluginVersion.Parse(candidate.Package.Version)).First();
        var precedence = PluginVersion.Parse(newest.Package.Version);
        var peers = canonicalOffers.Where(candidate => PluginVersion.Parse(candidate.Package.Version).CompareTo(precedence) == 0).ToArray();
        if (peers.Any(candidate => !candidate.Package.ManifestSha256.Equals(newest.Package.ManifestSha256, StringComparison.OrdinalIgnoreCase)))
            return (state with { Status = "integrity" }, null, null);

        var statusForVersion = "available";
        if (installed.SingleOrDefault(package => package.Id == newest.Package.Id && package.Version == newest.Package.Version) is { } existing)
        {
            var receipt = _library.Origin(existing);
            statusForVersion = receipt is not null && receipt.ManifestSha256.Equals(newest.Package.ManifestSha256, StringComparison.OrdinalIgnoreCase)
                && PluginCatalog.IsAvailable(existing, build) ? "installed" : "integrity";
        }
        return (state with { Status = statusForVersion, Version = newest.Package.Version,
            ManifestSha256 = newest.Package.ManifestSha256, SourceName = newest.Source.Name,
            TargetId = newest.Package.Id == id ? null : newest.Package.Id }, newest.Source, newest.Package);
    }

    internal static (string Status, StorePackage? Package) Select(PluginDefinition plugin, PluginStoreOrigin origin, BuildDefinition build, StoreCatalog catalog)
        => Select(plugin.Id, plugin.Game, plugin.Version, origin, build, catalog);

    private static (string Status, StorePackage? Package) Select(string id, string game, string version, PluginStoreOrigin origin, BuildDefinition build, StoreCatalog catalog)
    {
        var releases = catalog.Packages.Where(p => MatchesIdentity(p, id)).ToArray();
        if (releases.Any(p => p.Kind != "plugin" || p.Game != game)) return ("integrity", null);
        var baseline = PluginVersion.Parse(version);
        var rules = ProfileClientPolicy.Rules(build);
        var eligible = releases.Where(p => p.Builds.Count == 0 || p.Builds.Contains(build.Id, StringComparer.Ordinal))
            .Where(p => LauncherCompatibility.Supports(p.MinimumLauncherVersion))
            .Where(p => rules.Where(rule => rule.Id == id || MatchesIdentity(p, rule.Id)).All(rule => !rule.Disabled && rule.Allows(p.Version)))
            .Select(p => (Package: p, Version: PluginVersion.Parse(p.Version)))
            .Where(p => (p.Package.Id != id || p.Version.CompareTo(baseline) > 0) && (baseline.Pre.Length > 0 || p.Version.Pre.Length == 0))
            .ToArray();
        if (eligible.Length == 0) return ("current", null);
        var canonical = PreferredIdentity(id, eligible.Select(item => item.Package));
        eligible = eligible.Where(item => item.Package.Id == canonical).OrderByDescending(p => p.Version).ToArray();
        // Build metadata does not order SemVer; a source must choose one release at each precedence.
        if (eligible.Skip(1).Any(p => p.Version.CompareTo(eligible[0].Version) == 0)) return ("integrity", null);
        return ("available", eligible[0].Package);
    }

    private static string PreferredIdentity(string id, IEnumerable<StorePackage> candidates) =>
        PluginCatalog.CanonicalId(candidates.Select(package => package.PluginDefinition).OfType<PluginDefinition>().ToArray(), id);

    private static bool MatchesIdentity(StorePackage package, string id) =>
        string.Equals(package.Id, id, StringComparison.Ordinal)
        || package.PluginDefinition is { } definition && PluginCatalog.MatchesIdentity(definition, id);

    /// <summary>Cancels discovery and drains the current operation before the library and transports close.</summary>
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); await _operation.WaitAsync().ConfigureAwait(false); _operation.Release();
    }
}
