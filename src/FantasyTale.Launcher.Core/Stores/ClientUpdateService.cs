using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Stores;

/// <summary>Update discovery for one independent client, scoped to its installation receipt.</summary>
/// <param name="InstanceId">Client to display or update.</param>
/// <param name="Status">current, available, unmanaged, manual, source, integrity, network, disk or busy.</param>
/// <param name="InstalledVersion">Committed release label.</param>
/// <param name="Version">New release label, when one is available.</param>
/// <param name="ManifestSha256">Immutable selection token for the update button.</param>
/// <param name="CheckedAt">Time of this check in UTC.</param>
public sealed record ClientUpdateState(string InstanceId, string Status, string InstalledVersion, string? Version, string? ManifestSha256, DateTimeOffset CheckedAt);

/// <summary>Bounded update results and progress for the client library.</summary>
/// <param name="Checking">Whether one shared check is in progress.</param>
/// <param name="Clients">Latest client results.</param>
/// <param name="Error">Safe global failure code, without source addresses or credentials.</param>
public sealed record ClientUpdateOverview(bool Checking, IReadOnlyList<ClientUpdateState> Clients, string? Error);

/// <summary>Discovers monotonic client releases from pinned sources and optionally schedules installation through the shared installer.</summary>
public sealed class ClientUpdateService : IAsyncDisposable
{
    private readonly InstanceStore _instances;
    private readonly StoreSourceStore _sources;
    private readonly HttpClient _http;
    private readonly ClientDownloadService _downloads;
    private readonly ClientUpdateSettings _settings;
    private readonly Func<StoreSource, IDownloadSource> _openSource;
    private readonly Func<StoreSource, StorePackage, string, CancellationToken, Task<ClientDownloadTask>> _install;
    private readonly Func<string, bool> _running;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private ClientUpdateOverview _overview = new(false, [], null);
    private Task? _monitor;

    /// <summary>Connects discovery to configured sources, receipts, process ownership and the existing verified installer.</summary>
    public ClientUpdateService(InstanceStore instances, StoreSourceStore sources, HttpClient http, ClientDownloadService downloads,
        ClientUpdateSettings settings, Func<StoreSource, IDownloadSource> openSource,
        Func<StoreSource, StorePackage, string, CancellationToken, Task<ClientDownloadTask>> install, Func<string, bool> running,
        Func<StoreSource, CancellationToken, Task<StoreCatalog>>? readCatalog = null)
    { _instances = instances; _sources = sources; _http = http; _downloads = downloads; _settings = settings; _openSource = openSource; _install = install; _running = running; _readCatalog = readCatalog; }

    private readonly Func<StoreSource, CancellationToken, Task<StoreCatalog>>? _readCatalog;

    /// <summary>Starts the single lifetime-owned monitor; it does not block window creation.</summary>
    public void StartMonitoring() { lock (_gate) _monitor ??= Task.Run(MonitorAsync); }

    /// <summary>Reads the latest immutable state without starting network work.</summary>
    public ClientUpdateOverview List() { lock (_gate) return _overview; }

    /// <summary>Checks installed clients, fetching each source catalog once; concurrent checks share the existing operation.</summary>
    public Task<ClientUpdateOverview> CheckAsync(CancellationToken cancellationToken = default) => CheckCoreAsync(false, cancellationToken);

    private async Task<ClientUpdateOverview> CheckCoreAsync(bool automatic, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token); var ct = linked.Token;
        if (!await _operation.WaitAsync(0, ct).ConfigureAwait(false)) return List();
        lock (_gate) _overview = _overview with { Checking = true, Error = null };
        try
        {
            var clients = _instances.List();
            var cache = new Dictionary<string, Task<StoreCatalog>>(StringComparer.Ordinal);
            var results = new List<ClientUpdateState>();
            foreach (var client in clients)
            {
                ct.ThrowIfCancellationRequested();
                if (automatic && !client.CheckIntegrity) continue;
                var (result, source, package) = await DiscoverAsync(client, cache, ct).ConfigureAwait(false);
                if (result.Status == "available" && _settings.Value.AutoDownload && !_running(client.Id))
                {
                    try
                    {
                        await _install(source!, package!, client.Id, ct).ConfigureAwait(false);
                        result = result with { Status = "busy" };
                    }
                    catch (Exception error) when (Expected(error) && !ct.IsCancellationRequested)
                    { result = result with { Status = Failure(error) }; }
                }
                results.Add(result);
            }
            lock (_gate) _overview = new(false, results.ToArray(), null);
        }
        catch (Exception error) when (Expected(error) && !ct.IsCancellationRequested)
        { lock (_gate) _overview = _overview with { Error = Failure(error) }; }
        finally
        {
            lock (_gate) _overview = _overview with { Checking = false };
            _operation.Release();
        }
        return List();
    }

    /// <summary>Rechecks the receipt and catalog before installing the exact update the user selected.</summary>
    public async Task<ClientDownloadTask> InstallAsync(string instanceId, string manifestSha256, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token); var ct = linked.Token;
        await _operation.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_running(instanceId)) throw new InvalidOperationException("End the running game before updating this client.");
            var (result, source, package) = await DiscoverAsync(_instances.Load(instanceId), new(), ct).ConfigureAwait(false);
            if (result.Status != "available" || result.ManifestSha256 != manifestSha256)
                throw new InvalidOperationException("This update changed or is no longer available. Check for updates again.");
            var task = await _install(source!, package!, instanceId, ct).ConfigureAwait(false);
            lock (_gate) _overview = _overview with { Clients = _overview.Clients.Where(c => c.InstanceId != instanceId).Append(result with { Status = "busy" }).ToArray() };
            return task;
        }
        finally { _operation.Release(); }
    }

    private async Task<(ClientUpdateState Result, StoreSource? Source, StorePackage? Package)> DiscoverAsync(InstanceDefinition client, Dictionary<string, Task<StoreCatalog>> cache, CancellationToken ct)
    {
        var result = new ClientUpdateState(client.Id, "unmanaged", "", null, null, DateTimeOffset.UtcNow);
        try
        {
            if (_downloads.List().Any(d => d.Target.InstanceId == client.Id && d.Stage is not ("completed" or "canceled"))) return (result with { Status = "busy" }, null, null);
            var receipt = ClientInstallTransaction.ReadReceipt(_instances.ClientFolder(client.Id));
            if (receipt is null) return (result, null, null);
            result = result with { InstalledVersion = receipt.Version };
            var source = _sources.List().SingleOrDefault(s => s.Id == receipt.SourceId);
            if (source is not { Enabled: true, BaseUrl: not null } || receipt.SourceIdentity != StoreSourceIdentity.Of(source)) return (result with { Status = "source" }, null, null);
            if (!cache.TryGetValue(source.Id, out var request)) { request = ReadCatalogAsync(source, ct); cache.Add(source.Id, request); }
            var catalog = await request.ConfigureAwait(false);
            var (status, package) = Select(receipt, client, catalog);
            return (result with { Status = status, Version = package?.Version, ManifestSha256 = package?.ManifestSha256 }, source, package);
        }
        catch (Exception error) when (Expected(error) && !ct.IsCancellationRequested)
        { return (result with { Status = Failure(error) }, null, null); }
    }

    private async Task<StoreCatalog> ReadCatalogAsync(StoreSource source, CancellationToken ct) => _readCatalog is not null
        ? await _readCatalog(source, ct).ConfigureAwait(false)
        : StoreCatalogFile.Parse(source.Name, await SourceDocumentReader.ReadAsync(_http, _openSource(source), source.CatalogPath, ct).ConfigureAwait(false));

    internal static (string Status, StorePackage? Package) Select(ClientInstallReceipt receipt, InstanceDefinition client, StoreCatalog catalog)
    {
        var releases = catalog.Packages.Where(p => p.Id == receipt.PackageId).ToArray();
        if (releases.Any(p => p.Kind != "client" || p.Game != client.Game || p.Builds.Count != 1 || p.Builds[0] != client.Build)) return ("integrity", null);
        // File synchronization follows the current content snapshot; game build metadata never changes.
        if (releases.Length == 1 && releases[0].UpdateMode == "files")
            return releases[0].ManifestSha256.Equals(receipt.ManifestSha256, StringComparison.OrdinalIgnoreCase) ? ("current", null) : ("available", releases[0]);
        var current = releases.SingleOrDefault(p => p.ManifestSha256.Equals(receipt.ManifestSha256, StringComparison.OrdinalIgnoreCase));
        var labelled = receipt.Version.Length > 0 ? releases.SingleOrDefault(p => p.Version == receipt.Version) : null;
        if (labelled is not null && !labelled.ManifestSha256.Equals(receipt.ManifestSha256, StringComparison.OrdinalIgnoreCase) ||
            current is not null && receipt.Revision > 0 && current.Revision != receipt.Revision) return ("integrity", null);
        var baseline = receipt.Revision > 0 ? receipt.Revision : current?.Revision ?? 0;
        if (baseline == 0 || releases.Length == 0) return ("manual", null);
        var next = releases.Where(p => p.Revision > baseline).MaxBy(p => p.Revision);
        return next is null ? ("current", null) : ("available", next);
    }

    private async Task MonitorAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if (_settings.Value.AutoCheck) await CheckCoreAsync(true, _stop.Token).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromMinutes(30), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private static bool Expected(Exception error) => error is IOException or InvalidDataException or HttpRequestException or OperationCanceledException or
        UnauthorizedAccessException or ArgumentException or FormatException or System.Text.Json.JsonException or DefinitionException or InvalidOperationException;
    private static string Failure(Exception error) => error is InvalidDataException or DefinitionException or System.Text.Json.JsonException ? "integrity" :
        error is HttpRequestException or OperationCanceledException ? "network" : "disk";

    /// <summary>Cancels discovery and waits for the monitor and any user-initiated check before transports are disposed.</summary>
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); if (_monitor is not null) await _monitor.ConfigureAwait(false);
        await _operation.WaitAsync().ConfigureAwait(false); _operation.Release();
    }
}
