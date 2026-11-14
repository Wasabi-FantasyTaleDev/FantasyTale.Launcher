using FantasyTale.Launcher.Core.Servers;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Downloads.AList;
using FantasyTale.Launcher.Sdk;
using FantasyTale.Launcher.Core.Updates;
using FantasyTale.Launcher.Core.Hosting;
using FantasyTale.Launcher.Core.Multiplayer;
using FantasyTale.Launcher.Host.Window;

namespace FantasyTale.Launcher.Host.Bridge;

/// <summary>Connects the instance and plugin services to the host bridge without game-specific branches.</summary>
internal sealed partial class LauncherBackend : IAsyncDisposable
{
    private readonly Dictionary<string, CancellationTokenSource> _resourcePreparations = new(StringComparer.Ordinal);
    private sealed class InlineResourceProgress(Action<GameResourceProgress> report) : IProgress<GameResourceProgress>
    {
        public void Report(GameResourceProgress value) => report(value);
    }

    private static readonly JsonSerializerOptions UpdateSourceJson = new() { PropertyNameCaseInsensitive = true };
    private readonly string _gamesRoot;
    private readonly InstanceStore _store;
    private readonly LaunchService _launches;
    private readonly ServerStore _servers;
    private readonly LauncherApiClient _api;
    private readonly NetworkSettings _networkSettings;
    private readonly GitHubMirrors _githubMirrors;
    private readonly CancellationTokenSource _mirrorDefaultStop = new();
    private readonly Task _mirrorDefaultTask;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private readonly NetworkPolicy _network;
    private readonly StoreSourceStore _sources;
    private readonly StoreCatalogCache _catalogs;
    private readonly Task<IReadOnlyList<StoreCatalogSyncState>> _catalogSync;
    private readonly ClientDownloadService _downloads;
    private readonly ClientUpdateSettings _updateSettings;
    private readonly SeenNoticeStore _seenNotices;
    private readonly ServerSectionCache _sectionCache;
    private readonly ClientUpdateService _updates;
    private readonly LauncherUpdateService _launcherUpdates;
    private readonly LocalServerProfiles _hostingProfiles;
    private readonly LocalServerService _hosting;
    private readonly Dictionary<string, (string Profile, string Run)> _localClients = new(StringComparer.Ordinal);
    private readonly ServerPackageLibrary _serverPackages;
    private readonly DirectorySourceStore _directories;
    private readonly DirectoryClient _directoryApi;
    private readonly HostedRoomPublisher _roomPublisher;
    private readonly DirectoryConnections _directoryConnections;
    private readonly ServerDirectoryResolver _serverDirectoryResolver;
    private readonly StoreServerDiscovery _serverDiscovery;
    private readonly string _dataRoot;
    private readonly HostLifecycleLog _launchDiagnostics;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private bool _restartPending;
    private bool _exitPending;
    private readonly PluginLibrary _plugins;
    private readonly PluginUpdateService _pluginUpdates;
    private readonly PluginDeprecationService _pluginDeprecations;
    private readonly PluginUpdateMonitor _pluginMonitor;
    private readonly PluginUpgradeActivationService _pluginUpgrades;
    private readonly PluginSupportOffers _supportOffers;
    private readonly CancellationTokenSource _pluginUpgradeStop = new();
    private readonly Dictionary<string, long> _launchDeadlines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _cancelledLaunchDeadlines = new(StringComparer.Ordinal);
    private Task? _pluginUpgradeLoop;
    private readonly OverlayProcessManager _overlay;
    private readonly LauncherServiceManager _sessionServices;
    private readonly HttpClient _transferHttp;
    private readonly HttpClient _sourceHttp;
    private readonly SemaphoreSlim _catalogRequests = new(4, 4);

    internal LauncherBackend(string gamesRoot, string dataRoot, HostLifecycleLog? lifecycleLog = null)
    {
        _gamesRoot = gamesRoot;
        _dataRoot = dataRoot;
        _networkSettings = new NetworkSettings(dataRoot);
        _githubMirrors = GitHubMirrorConfiguration.Load(Path.Combine(AppContext.BaseDirectory, "github-mirrors.toml"), out var countryLookup, out var lookupTimeout);
        _githubMirrors.RestoreSelection(_networkSettings.GitHubMirrorSource);
        _network = new NetworkPolicy(() => _networkSettings.IpPreference);
        _transferHttp = new(new GitHubRoutingHandler(() => _networkSettings.GitHubMirror, () => _networkSettings.GitHubMirrorSource, _githubMirrors,
            CreateSourceHttpHandler(2))) { Timeout = Timeout.InfiniteTimeSpan };
        _sourceHttp = new(new GitHubRoutingHandler(() => _networkSettings.GitHubMirror, () => _networkSettings.GitHubMirrorSource, _githubMirrors,
            CreateSourceHttpHandler(4))) { Timeout = TimeSpan.FromSeconds(25) };
        _api = new LauncherApiClient(network: _network,
            sessionStore: OperatingSystem.IsWindows() ? new ProtectedAccountSessionStore(dataRoot) : null,
            loginStore: OperatingSystem.IsWindows() ? new ProtectedRememberedLoginStore(dataRoot) : null);
        _mirrorDefaultTask = InitializeMirrorDefaultAsync(countryLookup, lookupTimeout);
        _store = new InstanceStore(Path.Combine(dataRoot, "instances"));
        _plugins = new PluginLibrary(Path.Combine(dataRoot, "plugins"));
        var launchDiagnostics = lifecycleLog ?? new HostLifecycleLog(dataRoot);
        _launchDiagnostics = launchDiagnostics;
        _serverDirectoryResolver = new(diagnostics: launchDiagnostics);
        _launches = new LaunchService(_store, diagnosticSink: launchDiagnostics);
        _overlay = new OverlayProcessManager(AppContext.BaseDirectory, launchDiagnostics, OverlayTestSettings.Load(dataRoot));
        _sessionServices = new LauncherServiceManager(_overlay.GetHost, CreateCompanionAsync, _overlay.GetEntryPanel,
            (name, detail) => _launchDiagnostics.Record(name, detail));
        _servers = new ServerStore(Path.Combine(dataRoot, "servers"));
        _hostingProfiles = new LocalServerProfiles(Path.Combine(dataRoot, "hosting", "profiles"));
        _hosting = new LocalServerService(Path.Combine(dataRoot, "hosting", "runs"), Path.Combine(dataRoot, "hosting", "data"));
        _directories = new DirectorySourceStore(Path.Combine(dataRoot, "directory-sources"));
        _directoryApi = new DirectoryClient(network: _network);
        _roomPublisher = new HostedRoomPublisher(_hosting, _directoryApi);
        _sources = new StoreSourceStore(Path.Combine(dataRoot, "sources"));
        _catalogs = new StoreCatalogCache(Path.Combine(dataRoot, "catalog-cache"), _sourceHttp, OpenSource);
        _serverDiscovery = new StoreServerDiscovery(_catalogs);
        _directoryConnections = new DirectoryConnections(_directoryApi, _api.Forget, readListing: ReadDirectoryListingAsync);
        _catalogSync = _catalogs.SynchronizeAsync(_sources.List());
        _serverPackages = new ServerPackageLibrary(Path.Combine(dataRoot, "server-packages"));
        _downloads = new ClientDownloadService(_store, _transferHttp, Path.Combine(dataRoot, "downloads"),
            id => _sources.List().SingleOrDefault(s => s.Id == id), OpenSource, _plugins, Game, _serverPackages);
        _updateSettings = new ClientUpdateSettings(dataRoot);
        _seenNotices = new SeenNoticeStore(dataRoot);
        _sectionCache = new ServerSectionCache(dataRoot);
        _pluginUpgrades = new PluginUpgradeActivationService(dataRoot, _store, _plugins, _downloads, Game,
            id => _launches.IsActive(id), id => _sources.List().SingleOrDefault(source => source.Id == id),
            () => _updateSettings.Value.RemoveOldPluginVersions);
        _supportOffers = new PluginSupportOffers(dataRoot);
        _plugins.PackageInstalled += OnPluginInstalled;
        _pluginDeprecations = new PluginDeprecationService(_plugins, _pluginUpgrades,
            id => Games().SingleOrDefault(game => game.Id == id && game.Status == GameStatus.Enabled));
        _pluginUpdates = new PluginUpdateService(_plugins, _store, _sources, _sourceHttp, OpenSource, Game,
            id => _launches.IsActive(id), async (source, package, id, ct) =>
            {
                await EnsureRequirementsAsync(source, package, ct).ConfigureAwait(false);
                var manifest = await SourceDocumentReader.ReadBytesAsync(_sourceHttp, OpenSource(source), package.Manifest, ct).ConfigureAwait(false);
                return _pluginUpgrades.Enqueue(source, package, manifest, id);
            }, async (source, package, id, stamp, ct) =>
            {
                await EnsureRequirementsAsync(source, package, ct).ConfigureAwait(false);
                var manifest = await SourceDocumentReader.ReadBytesAsync(_sourceHttp, OpenSource(source), package.Manifest, ct).ConfigureAwait(false);
                return _pluginUpgrades.Enqueue(source, package, manifest, id, explicitSelection: true, expectedSelection: stamp);
            }, readCatalog: (source, ct) => _catalogs.GetAsync(source, cancellationToken: ct), allowRunningUpdates: true,
            applyLifecycle: _pluginDeprecations.Apply);
        _pluginMonitor = new PluginUpdateMonitor(() => _store.List().Where(i => Game(i.Game).SupportsPlugins)
            .Select(i => new PluginUpdateTarget(i.Id, i.Game + "/" + i.Build)).ToArray(),
            _pluginUpdates.CheckAsync, () => _updateSettings.Value.AutoCheckPlugins,
            autoUpdate: () => _updateSettings.Value.AutoUpdatePlugins,
            install: AutoInstallPluginUpdatesAsync);
        _updates = new ClientUpdateService(_store, _sources, _sourceHttp, _downloads, _updateSettings, OpenSource,
            (source, package, id, ct) => InstallVerifiedAsync(source, package, id, ct, updateOnly: true), id => _launches.IsActive(id),
            (source, ct) => _catalogs.GetAsync(source, force: true, cancellationToken: ct));
        _updates.StartMonitoring();
        _launcherUpdates = new LauncherUpdateService(AppContext.BaseDirectory, dataRoot, _transferHttp);
        _pluginMonitor.StartMonitoring();
        StartPluginActivation();
    }

    internal Task DrainAsync() => DisposeAsync().AsTask();
    internal bool HasRunningClients => _launches.HasPendingStarts || _launches.Running.Count != 0 || _hosting.List().Any(s => s.State is "starting" or "ready" or "stopping");
    internal async Task StopAllAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            _exitPending = true;
            // Stopping owned processes can fail. Dispose services only after the native window closes.
            await _launches.StopAllAsync(_hosting.StopAllAsync).ConfigureAwait(false);
        }
        catch { _exitPending = false; throw; }
        finally { _lifecycle.Release(); }
    }
    internal void SetUpdateStartup(bool pending)
    {
        _restartPending = pending;
        // An update child must wait for the helper's durable receipt before checking its installed revision.
        if (!pending) _launcherUpdates.BeginStartupCheck();
    }
    internal void FrontendReady()
    {
        _launcherUpdates.BeginStartupCheck();
        _restartPending = false; _launcherUpdates.StartMonitoring(); _pluginMonitor.StartMonitoring();
        StartPluginActivation();
    }
    private void StartPluginActivation()
    {
        _pluginUpgradeLoop ??= Task.Run(async () =>
        {
            try
            {
                while (!_pluginUpgradeStop.IsCancellationRequested)
                {
                    _pluginUpgrades.ApplyReady(cancellationToken: _pluginUpgradeStop.Token);
                    await Task.Delay(TimeSpan.FromSeconds(_pluginUpgrades.Failure is null ? 2 : 30), _pluginUpgradeStop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_pluginUpgradeStop.IsCancellationRequested) { }
        });
    }
    private async Task AutoInstallPluginUpdatesAsync(PluginUpdateTarget target, IReadOnlyList<PluginUpdateState> updates, CancellationToken ct)
    {
        foreach (var update in updates.Where(update => update.Status is "available" or "installed" && update.Version is not null && update.ManifestSha256 is not null))
        {
            try
            {
                var task = await _pluginUpdates.InstallAsync(target.InstanceId, update.Id, update.InstalledVersion, update.Version!, update.ManifestSha256!, ct).ConfigureAwait(false);
                _automaticPluginTasks.TryAdd(task.Id, new(update.InstalledVersion));
            }
            catch (InvalidOperationException) when (_launches.IsActive(target.InstanceId))
            {
                // A game can start between discovery and installation; retain its pending update.
                return;
            }
        }
    }
    internal async Task PrepareUpdateRestartAsync(CancellationToken ct, string? manifestSha256 = null, bool stopRunningClients = false)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_exitPending || _restartPending || (!stopRunningClients && HasRunningClients)) throw new InvalidOperationException("End running games before restarting the launcher to update.");
            var selection = _launcherUpdates.State;
            if (manifestSha256 is not null && (selection.Status != "ready" || selection.ManifestSha256 != manifestSha256))
                throw new InvalidOperationException("The selected launcher update changed. Check for updates again.");
            // Verify readiness before stopping anything; consent never applies to another release.
            var ready = _launcherUpdates.ReadyDirectory();
            _restartPending = true;
            try
            {
                if (stopRunningClients) await _launches.StopForUpdateAsync(_hosting.StopAllAsync).ConfigureAwait(false);
                await LauncherUpdateHandoff.PrepareAsync(AppContext.BaseDirectory, _dataRoot, ready, selection.ManifestSha256!, ct).ConfigureAwait(false);
            }
            catch { _restartPending = false; throw; }
        }
        finally { _lifecycle.Release(); }
    }

    /// <summary>Waits for owned clients to exit before dropping API credentials and transport resources.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync() { _mirrorDefaultStop.Cancel(); await _mirrorDefaultTask.ConfigureAwait(false); _pluginUpgradeStop.Cancel(); if (_pluginUpgradeLoop is not null) await _pluginUpgradeLoop.ConfigureAwait(false); try { await _launcherUpdates.DisposeAsync().ConfigureAwait(false); await _updates.DisposeAsync().ConfigureAwait(false); await _pluginMonitor.DisposeAsync().ConfigureAwait(false); await _pluginUpdates.DisposeAsync().ConfigureAwait(false); await _downloads.DisposeAsync().ConfigureAwait(false); await _catalogs.DisposeAsync().ConfigureAwait(false); await _launches.DrainAsync().ConfigureAwait(false); } finally { try { await _sessionServices.DisposeAsync().ConfigureAwait(false); await _overlay.DisposeAsync().ConfigureAwait(false); await _roomPublisher.DisposeAsync().ConfigureAwait(false); await _directoryConnections.DisposeAsync().ConfigureAwait(false); } finally { await _hosting.DisposeAsync().ConfigureAwait(false); _directoryApi.Dispose(); _plugins.Dispose(); _api.Dispose(); _sourceHttp.Dispose(); _transferHttp.Dispose(); _githubMirrors.Dispose(); _mirrorDefaultStop.Dispose(); } } }

    private static Action<PluginImportProgress> ImportProgress(BridgeRouter bridge, JsonNode? parameters)
    {
        var operationId = parameters?["operationId"]?.GetValue<string>();
        if (operationId is null) return _ => { };
        if (!Guid.TryParseExact(operationId, "N", out _)) throw new ArgumentException("Invalid plugin import operation.");
        string? stage = null; int? percent = null;
        var last = System.Diagnostics.Stopwatch.StartNew();
        return update =>
        {
            var rounded = update.Percent is { } measured ? (int?)Math.Floor(measured) : null;
            if (update.Stage == stage && (rounded == percent || last.ElapsedMilliseconds < 100 && rounded != 100)) return;
            stage = update.Stage; percent = rounded; last.Restart();
            bridge.Emit("plugins.importProgress", new { operationId, update.Stage, update.Percent });
        };
    }

    internal void Register(BridgeRouter bridge)
    {
        bridge.Add("store.sync", async (p, ct) =>
        {
            var result = p?["force"]?.GetValue<bool>() == true
                ? await _catalogs.SynchronizeAsync(_sources.List(), ct).ConfigureAwait(false)
                : await _catalogSync.WaitAsync(ct).ConfigureAwait(false);
            bridge.Emit("store.changed", null); return result;
        });
        bridge.Add("store.syncState", _ => _sources.List().Where(source => source.Enabled && source.BaseUrl is not null).Select(_catalogs.State).ToArray());
        bridge.Add("instances.services", p => { Keys(p, "id"); return _sessionServices.Status(Text(p, "id")); });
        RegisterDirectory(bridge);
        RegisterServerProfiles(bridge);
        RegisterPluginUpdates(bridge);
        RegisterClientPolicyActivation(bridge);
        RegisterPluginPolicy(bridge);
        RegisterServerPackages(bridge);
        bridge.Add("hosting.list", _ => new { profiles = _hostingProfiles.List(), sessions = _hosting.List() });
        bridge.Add("hosting.availablePorts", _ => LocalServerNetwork.AvailablePorts());
        bridge.Add("hosting.localAddresses", _ => LocalServerNetwork.Addresses());
        bridge.Add("hosting.log", p => { Keys(p, "id", "runId"); return _hosting.Log(Text(p, "id"), Text(p, "runId")); });
        bridge.Add("hosting.destination", p =>
        {
            Keys(p, "id", "runId");
            Game(_hostingProfiles.List().Single(profile => profile.Id == Text(p, "id")).Game).RequireLocalHostingEnabled();
            return _hosting.Destination(Text(p, "id"), Text(p, "runId"))
                ?? throw new InvalidOperationException("The local server invocation changed or is no longer ready.");
        });
        bridge.Add("hosting.save", async (p, ct) =>
        {
            Keys(p, "id", "game", "build", "name", "packagePath", "mode", "advertisedHost", "gamePort", "apiPort", "adminPort", "temporaryData", "storageMode", "effectiveStorageMode", "requireAuthentication", "clientInstanceId", "relayPort", "effectiveRelayPort");
            var profile = p!.Deserialize<LocalServerProfile>(JsonSerializerOptions.Web) ?? throw new ArgumentException("Missing hosting profile.");
            if (profile.Id.Length > 58) throw new ArgumentException("Hosting id exceeds its limit.");
            var game = Game(profile.Game);
            game.RequireLocalHostingEnabled();
            if (profile.Mode == "room") game.RequireRoomsEnabled();
            if (!game.Builds.Any(b => b.Id == profile.Build && b.HostingAdapter.Length > 0) || GameModules.Load(game, AppContext.BaseDirectory) is not ILocalServerModule)
                throw new InvalidOperationException("This build does not support local hosting.");
            await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try { _hostingProfiles.Save(profile); return profile; }
            finally { _lifecycle.Release(); }
        });
        bridge.Add("hosting.delete", async (p, ct) =>
        {
            Keys(p, "id"); var id = Text(p, "id"); await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try { EnsureHostingIdle(id); _hostingProfiles.Delete(id); _api.Forget("local-" + id); return null; }
            finally { _lifecycle.Release(); }
        });
        bridge.Add("hosting.releasePrivate", async (p, ct) =>
        {
            Keys(p, "keepServerId");
            await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try { await ReleasePrivateServersAsync(p?["keepServerId"]?.GetValue<string>()).ConfigureAwait(false); return null; }
            finally { _lifecycle.Release(); }
        });
        bridge.Add("hosting.start", async (p, ct) =>
        {
            Keys(p, "id"); await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_exitPending || _restartPending) throw new InvalidOperationException("Wait for the launcher update or exit to finish.");
                var profile = _hostingProfiles.List().Single(s => s.Id == Text(p, "id")); var game = Game(profile.Game);
                game.RequireLocalHostingEnabled();
                var module = GameModules.Load(game, AppContext.BaseDirectory) as ILocalServerModule ?? throw new InvalidOperationException("This game does not support local hosting.");
                var candidates = _store.List().Where(instance => instance.Game == profile.Game && instance.Build == profile.Build);
                var client = profile.ClientInstanceId is { } clientId ? candidates.SingleOrDefault(instance => instance.Id == clientId) : candidates.FirstOrDefault();
                if (profile.ClientInstanceId is not null && client is null) throw new InvalidOperationException("local_client_data_missing");
                var result = await _hosting.StartAsync(profile, game, module, client is null ? null : _store.ClientFolder(client.Id), ct).ConfigureAwait(false); _api.Forget("local-" + profile.Id); return result;
            }
            finally { _lifecycle.Release(); }
        });
        bridge.Add("hosting.stop", async (p, ct) =>
        {
            Keys(p, "id", "runId", "allowConnected"); await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (p?["allowConnected"]?.GetValue<bool>() != true && (_launches.HasPendingStarts || _launches.Running.Count != 0)) throw new InvalidOperationException("End running or starting games before stopping a local server.");
                await _roomPublisher.WithdrawAsync(Text(p, "id"), Text(p, "runId"), ct).ConfigureAwait(false);
                var result = await _hosting.StopAsync(Text(p, "id"), Text(p, "runId")).ConfigureAwait(false); _api.Forget("local-" + Text(p, "id")); return result;
            }
            finally { _lifecycle.Release(); }
        });
        bridge.Add("launcherUpdates.state", _ => _launcherUpdates.State);
        bridge.Add("launcherUpdates.lastAttempt", _ => _launcherUpdates.LastAttempt);
        bridge.Add("launcherUpdates.manualDownload", _ => new { url = LauncherManualDownload.Load(AppContext.BaseDirectory), version = _launcherUpdates.State.Version });
        bridge.Add("launcherUpdates.check", async (_, ct) => await _launcherUpdates.CheckAsync(ct).ConfigureAwait(false));
        bridge.Add("launcherUpdates.download", async (p, ct) => { Keys(p, "manifestSha256"); return await _launcherUpdates.DownloadAsync(Text(p, "manifestSha256"), ct).ConfigureAwait(false); });
        bridge.Add("launcherUpdates.preferences", _ => _launcherUpdates.Preferences);
        bridge.Add("launcherUpdates.sources", _ => _launcherUpdates.Sources());
        bridge.Add("launcherUpdates.sourceResults", _ => _launcherUpdates.SourceResults);
        bridge.Add("launcherUpdates.openLog", _ =>
        {
            var path = Path.Combine(_dataRoot, "host-lifecycle.jsonl");
            if (!File.Exists(path)) throw new FileNotFoundException("The launcher log does not exist yet.", path);
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open", path) { UseShellExecute = false });
            return null;
        });
        bridge.Add("launcherUpdates.setSources", async (p, ct) =>
        {
            Keys(p, "sources");
            var sources = p?["sources"]?.Deserialize<LauncherUpdateSourceSettings[]>(UpdateSourceJson)
                ?? throw new ArgumentException("Missing update sources.");
            return await _launcherUpdates.SaveSourcesAsync(sources, ct).ConfigureAwait(false);
        });
        bridge.Add("launcherUpdates.setPreferences", p =>
        {
            Keys(p, "autoCheck", "autoDownload");
            _launcherUpdates.SavePreferences(new(p?["autoCheck"]?.GetValue<bool>() ?? throw new ArgumentException("Missing autoCheck."), p?["autoDownload"]?.GetValue<bool>() ?? throw new ArgumentException("Missing autoDownload.")));
            return _launcherUpdates.Preferences;
        });
        bridge.Add("updates.list", _ => _updates.List());
        bridge.Add("updates.check", async (_, ct) => await _updates.CheckAsync(ct).ConfigureAwait(false));
        bridge.Add("updates.install", async (p, ct) =>
        { Keys(p, "instanceId", "manifestSha256"); return await _updates.InstallAsync(Text(p, "instanceId"), Text(p, "manifestSha256"), ct).ConfigureAwait(false); });
        bridge.Add("settings.updates", _ => _updateSettings.Value);
        bridge.Add("settings.setUpdates", p =>
        {
            Keys(p, "autoCheck", "autoDownload", "autoCheckPlugins", "autoUpdatePlugins", "removeOldPluginVersions");
            _updateSettings.Save(new(p?["autoCheck"]?.GetValue<bool>() ?? throw new ArgumentException("Missing autoCheck."),
                p?["autoDownload"]?.GetValue<bool>() ?? throw new ArgumentException("Missing autoDownload."),
                p?["autoCheckPlugins"]?.GetValue<bool>() ?? _updateSettings.Value.AutoCheckPlugins,
                p?["autoUpdatePlugins"]?.GetValue<bool>() ?? _updateSettings.Value.AutoUpdatePlugins,
                p?["removeOldPluginVersions"]?.GetValue<bool>() ?? _updateSettings.Value.RemoveOldPluginVersions));
            _pluginMonitor.Wake();
            return _updateSettings.Value;
        });
        IReadOnlyList<ClientDownloadTask> Downloads() => _launcherUpdates.List().Concat(_downloads.List()).ToArray();
        long notifiedPluginRevision = _pluginUpgrades.Revision;
        bridge.Add("downloads.list", _ =>
        {
            var retired = _pluginDeprecations.DrainNotices();
            if (retired.Count > 0) { bridge.Emit("plugins.deprecated", retired); bridge.Emit("plugins.changed", null); }
            var revision = _pluginUpgrades.Revision;
            if (Interlocked.Exchange(ref notifiedPluginRevision, revision) != revision) bridge.Emit("instances.changed", null);
            var tasks = Downloads();
            NotifyAutomaticPlugins(bridge, tasks);
            return tasks;
        });
        bridge.Add("plugins.pendingUpgrades", p =>
        {
            Keys(p, "instanceId");
            return _pluginUpgrades.Status(p?["instanceId"]?.GetValue<string>());
        });
        bridge.Add("downloads.action", async (p, _) =>
        {
            Keys(p, "id", "action", "archivePassword");
            if (Text(p, "id") == "launcher-update") await _launcherUpdates.ActionAsync(Text(p, "action")).ConfigureAwait(false);
            else await _downloads.ActionAsync(Text(p, "id"), Text(p, "action"), p?["archivePassword"]?.GetValue<string>()).ConfigureAwait(false);
            return Downloads();
        });
        bridge.Add("downloads.clearFinished", _ => { _launcherUpdates.ClearFinished(); _downloads.ClearFinished(); return Downloads(); });
        bridge.Add("store.downloadPlugin", async (p, ct) =>
        {
            Keys(p, "sourceId", "expectedSource", "packageId", "version", "manifestSha256");
            var source = PluginSource(p);
            if (!await _catalogRequests.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("Too many catalog requests.");
            try
            {
                var catalog = await _catalogs.GetAsync(source, cancellationToken: ct).ConfigureAwait(false);
                var package = catalog.Packages.SingleOrDefault(item => item.Kind == "plugin" && item.Id == Text(p, "packageId") && item.Version == Text(p, "version") && item.ManifestSha256 == Text(p, "manifestSha256"))
                    ?? throw new InvalidOperationException("The release changed. Refresh the store before downloading.");
                await EnsureRequirementsAsync(source, package, ct).ConfigureAwait(false);
                var manifest = await SourceDocumentReader.ReadBytesAsync(_sourceHttp, OpenSource(source), package.Manifest, ct).ConfigureAwait(false);
                return _downloads.PreloadPlugin(source, package, manifest);
            }
            finally { _catalogRequests.Release(); }
        });
        bridge.Add("store.installPlugin", async (p, ct) =>
        {
            Keys(p, "sourceId", "expectedSource", "packageId", "version", "manifestSha256", "instanceId", "serverId");
            var source = PluginSource(p);
            var instance = _store.Load(Text(p, "instanceId")); var game = Game(instance.Game);
            var capabilities = await CatalogForAsync(game.Id, p?["serverId"]?.GetValue<string>(), ct).ConfigureAwait(false);
            if (!(capabilities?.SupportsPlugins ?? game.SupportsPlugins)) throw new InvalidOperationException("This server does not support plugins.");
            if (!await _catalogRequests.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("Too many catalog requests.");
            try
            {
                var catalog = await _catalogs.GetAsync(source, cancellationToken: ct).ConfigureAwait(false);
                var package = catalog.Packages.SingleOrDefault(item => item.Kind == "plugin" && item.Id == Text(p, "packageId") && item.Version == Text(p, "version") && item.ManifestSha256 == Text(p, "manifestSha256"))
                    ?? throw new InvalidOperationException("The release changed. Refresh the store before installing.");
                return await InstallPluginAsync(source, package, instance.Id, ct).ConfigureAwait(false);
            }
            finally { _catalogRequests.Release(); }
        });
        bridge.Add("store.installPluginTargets", async (p, ct) =>
        {
            Keys(p, "sourceId", "expectedSource", "packageId", "version", "manifestSha256", "game", "instanceIds", "serverId");
            var source = PluginSource(p);
            var ids = p?["instanceIds"]?.Deserialize<string[]>(BridgeRouter.Json) ?? throw new ArgumentException("Missing plugin targets.");
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) throw new ArgumentException("Invalid plugin targets.");
            var game = Game(p?["game"]?.GetValue<string>() ?? (ids.Length > 0 ? _store.Load(ids[0]).Game : throw new ArgumentException("Missing plugin game.")));
            foreach (var targetId in ids)
            {
                var target = _store.Load(targetId);
                if (target.Game != game.Id) throw new InvalidDataException("Plugin targets must belong to the same game.");
            }
            var capabilities = await CatalogForAsync(game.Id, p?["serverId"]?.GetValue<string>(), ct).ConfigureAwait(false);
            if (!(capabilities?.SupportsPlugins ?? game.SupportsPlugins)) throw new InvalidOperationException("This server does not support plugins.");
            if (!await _catalogRequests.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("Too many catalog requests.");
            try
            {
                var catalog = await _catalogs.GetAsync(source, cancellationToken: ct).ConfigureAwait(false);
                var package = catalog.Packages.SingleOrDefault(item => item.Kind == "plugin" && item.Id == Text(p, "packageId") && item.Version == Text(p, "version") && item.ManifestSha256 == Text(p, "manifestSha256"))
                    ?? throw new InvalidOperationException("The release changed. Refresh the store before installing.");
                if (package.Game != game.Id) throw new InvalidDataException("The plugin belongs to another game.");
                if (ids.Length == 0)
                {
                    await EnsureRequirementsAsync(source, package, ct).ConfigureAwait(false);
                    var manifest = await SourceDocumentReader.ReadBytesAsync(_sourceHttp, OpenSource(source), package.Manifest, ct).ConfigureAwait(false);
                    return _downloads.PreloadPlugin(source, package, manifest);
                }
                return await InstallPluginAsync(source, package, ids[0], ct, ids).ConfigureAwait(false);
            }
            finally { _catalogRequests.Release(); }
        });
        bridge.Add("store.installClient", async (p, ct) =>
        {
            Keys(p, "sourceId", "packageId", "version", "manifestSha256", "instanceId", "archivePassword");
            var source = _sources.List().SingleOrDefault(s => s.Id == Text(p, "sourceId")) ?? throw new ArgumentException("Unknown source.");
            if (!source.Enabled || source.BaseUrl is null) throw new InvalidOperationException("Configure and enable this source first.");
            if (!await _catalogRequests.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("Too many catalog requests.");
            try
            {
                var catalog = await _catalogs.GetAsync(source, cancellationToken: ct).ConfigureAwait(false);
                var package = catalog.Packages.SingleOrDefault(item => item.Id == Text(p, "packageId") && item.Version == Text(p, "version") && item.ManifestSha256 == Text(p, "manifestSha256"))
                    ?? throw new InvalidOperationException("The release changed. Refresh the store before installing.");
                return await InstallVerifiedAsync(source, package, Text(p, "instanceId"), ct,
                    archivePassword: p?["archivePassword"]?.GetValue<string>()).ConfigureAwait(false);
            }
            finally { _catalogRequests.Release(); }
        });
        bridge.Add("store.sources", _ => _sources.List());
        bridge.Add("store.saveSource", p =>
        {
            Keys(p, "id", "name", "kind", "baseUrl", "catalogPath", "enabled", "preferIp");
            var id = Text(p, "id"); var previous = _sources.List().SingleOrDefault(s => s.Id == id);
            var source = new StoreSource(id, Text(p, "name"), Text(p, "kind"), p?["baseUrl"]?.GetValue<string>(),
                Text(p, "catalogPath"), p?["enabled"]?.GetValue<bool>() ?? true, previous?.BuiltIn ?? false)
            { PreferIp = p is JsonObject fields && fields.ContainsKey("preferIp") ? p["preferIp"]?.GetValue<bool>() : previous?.PreferIp };
            _sources.Save(source); bridge.Emit("store.changed", null); return _sources.List().Single(s => s.Id == id);
        });
        bridge.Add("store.removeSource", p => { Keys(p, "id"); _sources.Delete(Text(p, "id")); bridge.Emit("store.changed", null); return null; });
        bridge.Add("store.catalog", async (p, ct) =>
        {
            Keys(p, "id", "force"); var source = _sources.List().SingleOrDefault(s => s.Id == Text(p, "id")) ?? throw new ArgumentException("Unknown source.");
            if (!source.Enabled || source.BaseUrl is null) throw new InvalidOperationException("Configure and enable this source first.");
            if (!await _catalogRequests.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("Too many catalog requests.");
            try
            {
                var previous = _catalogs.Cached(source);
                var catalog = await _catalogs.GetAsync(source, force: p?["force"]?.GetValue<bool>() ?? false, cancellationToken: ct).ConfigureAwait(false);
                _pluginDeprecations.Apply(source, catalog);
                if (!ReferenceEquals(previous, catalog)) bridge.Emit("store.changed", null);
                var hidden = Games().SelectMany(game => game.Builds.Where(build => !build.Enabled || build.Hidden)
                    .Select(build => (game.Id, build.Id))).ToHashSet();
                return catalog with { Packages = catalog.Packages.Where(package => package.Kind is not ("client" or "server") ||
                    !package.Builds.All(build => hidden.Contains((package.Game, build))))
                    .Select(package => package with { LauncherCompatible = LauncherCompatibility.Supports(package.MinimumLauncherVersion) }).ToArray() };
            }
            finally { _catalogRequests.Release(); }
        });
        RegisterNetworkSettings(bridge);
        bridge.Add("settings.overlayTest", _ => new { enabled = _overlay.TestSettings.StartupMessage, _overlay.TestSettings.PassThroughOutside });
        bridge.Add("settings.setOverlayTest", async (p, ct) =>
        {
            Keys(p, "enabled", "passThroughOutside");
            var enabled = p?["enabled"]?.GetValue<bool>() ?? throw new ArgumentException("enabled is required.");
            await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var settings = _overlay.TestSettings with { StartupMessage = enabled,
                    PassThroughOutside = p?["passThroughOutside"]?.GetValue<bool>() ?? _overlay.TestSettings.PassThroughOutside };
                settings.Save(_dataRoot);
                _overlay.SetTestSettings(settings);
                return new { enabled, settings.PassThroughOutside };
            }
            finally { _lifecycle.Release(); }
        });
        bridge.Add("instances.list", p =>
        {
            p ??= new JsonObject();
            Keys(p, "preferredId");
            var games = Games();
            var pending = _downloads.List().Where(task => task.Stage is not ("completed" or "canceled")).Select(task => task.Target.InstanceId).ToHashSet(StringComparer.Ordinal);
            try { _store.ConsolidateRegistrations(p["preferredId"]?.GetValue<string>(), id => _launches.IsActive(id) || pending.Contains(id)); }
            catch (InstanceBusyException) { }
            var instances = _store.List();
            var packages = games.ToDictionary(game => game.Id, game => Packages(game), StringComparer.Ordinal);
            return instances.Select(instance =>
            {
                var build = games.SingleOrDefault(g => g.Id == instance.Game)?.Builds.SingleOrDefault(b => b.Id == instance.Build);
                var installed = build is not null && ClientExecutable.FindInstalled(_store.ClientFolder(instance.Id), build) is not null;
                var game = games.SingleOrDefault(g => g.Id == instance.Game);
                var diagnostics = game is not null && build is not null && packages[game.Id].Any(plugin => plugin.Diagnostics && PluginCatalog.IsAvailable(plugin, build)
                    && instance.Plugins.Any(selection => selection.Id == plugin.Id && selection.Version == plugin.Version && selection.Enabled));
                return new { instance.Id, instance.Name, instance.Game, instance.Build, instance.Endpoint, instance.Plugins, instance.Language, instance.CheckIntegrity,
                    DiagnosticsEnabled = diagnostics,
                    InstallationPath = _store.ClientFolder(instance.Id), ExternalInstallation = instance.InstallationPath is not null, ClientInstalled = installed,
                    RecoveryRequired = InstanceRecovery.Required(_store.ClientFolder(instance.Id)) && !_launches.IsActive(instance.Id) };
            }).ToArray();
        });
        bridge.Add("instances.recoverStopped", _ =>
        {
            var games = Games();
            var changed = false;
            foreach (var instance in _store.List())
            {
                if (_launches.IsActive(instance.Id) || !InstanceRecovery.Required(_store.ClientFolder(instance.Id))) continue;
                var game = games.SingleOrDefault(item => item.Id == instance.Game);
                if (game is not null) changed |= InstanceRecovery.TryRecoverStopped(_store, instance.Id, game);
            }
            if (changed) bridge.Emit("instances.changed", null);
            return null;
        });
        bridge.Add("instances.running", _ => _launches.Running.Select(r => new { r.InstanceId, r.ProcessId, r.Hook }));
        RegisterNativeUi(bridge);
        bridge.Add("instances.stop", async (p, _) =>
        {
            Keys(p, "id", "processId"); var id = Text(p, "id");
            var result = await _launches.StopAsync(id, p?["processId"]?.GetValue<int>() ?? 0).ConfigureAwait(false);
            bridge.Emit("instances.changed", null);
            return result;
        });
        bridge.Add("instances.hookPing", async (p, ct) =>
        {
            Keys(p, "id"); var running = _launches.Running.SingleOrDefault(r => r.InstanceId == Text(p, "id"));
            return await (running?.Control ?? throw new InvalidOperationException("This client has no active hook channel.")).RequestAsync("ping", ct).ConfigureAwait(false);
        });
        bridge.Add("instances.openFolder", p =>
        {
            Keys(p, "id"); var id = Text(p, "id"); _ = _store.Load(id);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_store.ClientFolder(id)) { UseShellExecute = true });
            return null;
        });
        bridge.Add("instances.recover", async (p, ct) =>
        {
            Keys(p, "id", "confirmedStopped"); var id = Text(p, "id");
            if (_launches.IsActive(id)) throw new InvalidOperationException("End the running or starting game before recovering this client.");
            var instance = _store.Load(id);
            await Task.Run(() => InstanceRecovery.Recover(_store, id, Game(instance.Game), p?["confirmedStopped"]?.GetValue<bool>() ?? false), ct).ConfigureAwait(false);
            bridge.Emit("instances.changed", new { id }); return null;
        });
        bridge.Add("instances.inspectInstallation", p =>
        {
            Keys(p, "id", "installationPath", "game");
            var selectedPath = p?["installationPath"]?.GetValue<string>();
            var found = selectedPath is not null && p?["game"]?.GetValue<string>() is { } selectedGame
                ? ClientInstallationDiscovery.Find(selectedPath, Game(selectedGame).Builds) : null;
            var registered = found is not null ? _store.List().SingleOrDefault(instance => instance.Game == Text(p, "game") && found.BuildIds.Contains(instance.Build, StringComparer.Ordinal)
                && (_store.ClientFolder(instance.Id).Equals(found.Path, StringComparison.OrdinalIgnoreCase)
                    || instance.InstallationPath is not null && found.Path.StartsWith(Path.TrimEndingDirectorySeparator(instance.InstallationPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && ClientExecutable.FindInstalled(instance.InstallationPath, Game(instance.Game).Builds.Single(build => build.Id == instance.Build)) is null)) : null;
            var destination = registered is not null ? new InstallationPathInfo(found!.Path, true)
                : _store.InspectInstallationPath(Text(p, "id"), found?.Path ?? selectedPath);
            var matches = p?["game"]?.GetValue<string>() is { } gameId
                ? Game(gameId).Builds.Where(build => ClientExecutable.FindInstalled(destination.Path, build) is not null).Select(build => build.Id).ToArray() : [];
            return new { destination.Path, destination.ContainsFiles, DetectedBuildIds = matches };
        });
        bridge.Add("instances.openDiagnostics", p =>
        {
            Keys(p, "id"); var id = Text(p, "id"); _ = _store.Load(id);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_store.LogFolder(id)) { UseShellExecute = true });
            return null;
        });
        bridge.Add("instances.launchDiagnostics", p =>
        {
            Keys(p, "id"); _ = _store.Load(Text(p, "id"));
            return new { lifecycle = _launchDiagnostics.ReadLaunchRecords() };
        });
        bridge.Add("instances.create", async (p, ct) =>
        {
            Keys(p, "id", "name", "game", "build", "sourceClient", "installationPath", "overwrite", "endpoint", "language", "serverId", "presetId", "existingClient", "checkIntegrity");
            var game = Game(Text(p, "game"));
            var build = game.Builds.SingleOrDefault(b => b.Id == Text(p, "build")) ?? throw new ArgumentException("Unknown build.");
            build.RequireEnabled();
            var catalog = await CatalogForAsync(game.Id, p?["serverId"]?.GetValue<string>(), ct).ConfigureAwait(false);
            var preset = p?["presetId"]?.GetValue<string>() is { } presetId ? catalog?.Instances.SingleOrDefault(i => i.Id == presetId && i.Build == build.Id) ?? throw new ArgumentException("Unknown server instance preset.") : catalog?.Instances.FirstOrDefault(i => i.Build == build.Id);
            var serverLanguages = preset?.ClientLanguages ?? catalog?.ClientLanguages;
            game = game with { SupportsPlugins = catalog?.SupportsPlugins ?? game.SupportsPlugins };
            var packages = Packages(game);

            var definition = new InstanceDefinition(Text(p, "id"), Text(p, "name"), game.Id, build.Id, Endpoint(p?["endpoint"]), PluginResolver.Defaults(game, build, packages)) { Language = Language(game, build, p?["language"]?.GetValue<string>(), serverLanguages), CheckIntegrity = p?["checkIntegrity"]?.GetValue<bool>() ?? true };
            _ = PluginResolver.Resolve(definition, build, packages, requireLibraries: false, requireRequiredPlugins: false);
            var sourceClient = p?["sourceClient"]?.GetValue<string>();
            var installationPath = p?["installationPath"]?.GetValue<string>();
            if (sourceClient is not null && installationPath is not null) throw new ArgumentException("An import source and installation destination cannot be combined.");
            var detected = installationPath is not null ? ClientInstallationDiscovery.Find(installationPath, [build]) : null;
            if (p?["existingClient"]?.GetValue<bool>() == true && detected is null) throw new IOException("client_executable_missing");
            var created = sourceClient is not null
                ? await _store.CreateAsync(definition, sourceClient, ct).ConfigureAwait(false)
                : detected is not null ? await _store.RegisterDetectedAsync(definition, detected.Path, build, ct).ConfigureAwait(false)
                : await _store.CreateAtAsync(definition, installationPath, p?["overwrite"]?.GetValue<bool>() ?? false, ct).ConfigureAwait(false);
            bridge.Emit("instances.changed", new { id = created.Id });
            return created;
        });
        bridge.Add("instances.clone", async (p, ct) =>
        {
            Keys(p, "sourceId", "id", "name");
            var original = _store.Load(Text(p, "sourceId"));
            Game(original.Game).Builds.Single(build => build.Id == original.Build).RequireEnabled();
            var created = await _store.CloneAsync(Text(p, "sourceId"), Text(p, "id"), Text(p, "name"), ct).ConfigureAwait(false);
            bridge.Emit("instances.changed", new { id = created.Id });
            return created;
        });
        bridge.Add("instances.delete", async (p, ct) =>
        {
            Keys(p, "id", "deleteFiles");
            var id = Text(p, "id");
            var deleteFiles = p?["deleteFiles"]?.GetValue<bool>() ?? throw new ArgumentException("Choose whether to delete files or remove only the registration.");
            var instance = _store.Load(id);
            var result = await Task.Run(() => _store.RemoveAsync(id, deleteFiles, Game(instance.Game), ct), ct).ConfigureAwait(false);
            bridge.Emit("instances.changed", new { id }); return result;
        });
        bridge.Add("instances.update", async (p, ct) =>
        {
            Keys(p, "id", "name", "endpoint", "language", "serverId", "checkIntegrity");
            var id = Text(p, "id");
            var original = _store.Load(id);
            var game = Game(original.Game); var build = game.Builds.Single(b => b.Id == original.Build);
            var catalog = await CatalogForAsync(game.Id, p?["serverId"]?.GetValue<string>(), ct).ConfigureAwait(false);
            var serverLanguages = catalog?.Instances.FirstOrDefault(i => i.Build == build.Id)?.ClientLanguages ?? catalog?.ClientLanguages;
            var name = Text(p, "name");
            var endpoint = Endpoint(p?["endpoint"]);
            var checkIntegrity = p?["checkIntegrity"]?.GetValue<bool>() ?? original.CheckIntegrity;
            var language = p?["language"]?.GetValue<string>() ?? original.Language;
            for (var attempt = 0; ; attempt++)
            {
                var changed = original with { Name = name, Endpoint = endpoint,
                    CheckIntegrity = checkIntegrity,
                    Language = Language(game, build, language, serverLanguages) };
                try
                {
                    _store.Save(changed, original); bridge.Emit("instances.changed", new { id = changed.Id }); return changed;
                }
                catch (InvalidOperationException error) when (attempt == 0 && error.Message == "The client settings changed while this request was pending. Refresh and try again.")
                {
                    var latest = _store.Load(id);
                    if (latest.Name != original.Name || latest.Endpoint != original.Endpoint || latest.Language != original.Language || latest.CheckIntegrity != original.CheckIntegrity)
                        throw;
                    original = latest;
                    game = Game(original.Game); build = game.Builds.Single(b => b.Id == original.Build);
                }
            }
        });
        bridge.Add("servers.list", _ =>
        {
            // Temporary destinations remain selectable on Home, but are not editable saved servers.
            var games = Games();
            var saved = SavedServers(games).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            var activeProfiles = _hosting.List().Where(s => s.State is "starting" or "ready" or "stopping")
                .ToDictionary(s => s.ProfileId, s => _hosting.StartedProfile(s.ProfileId, s.RunId));
            var privateDestinations = _hostingProfiles.List().Where(p => (activeProfiles.GetValueOrDefault(p.Id) ?? p).Mode == "private")
                .Select(p => "local-" + p.Id).ToHashSet(StringComparer.Ordinal);
            return Servers().Select(s => new { s.Id, s.Kind, s.Game, s.Name, s.ApiUrl, s.Endpoint, s.BundledInstances, s.BuiltIn, s.PreferIp, s.UseHttps, s.Presentation, s.IconAsset, s.Builds, s.ClientPolicy, Transient = !saved.Contains(s.Id), LocalOnly = privateDestinations.Contains(s.Id) }).ToArray();
        });
        bridge.Add("servers.save", p =>
        {
            Keys(p, "id", "game", "name", "apiUrl", "endpoint", "bundledInstances", "preferIp", "useHttps", "presentation", "createOnly");
            var games = Games(); var gameId = Text(p, "game");
            if (!games.Any(g => g.Id == gameId)) throw new ArgumentException("Unknown game.");
            var id = p?["id"]?.GetValue<string>() ?? "";
            if (string.IsNullOrEmpty(id)) id = ServerStore.DefaultId(p?["apiUrl"]?.GetValue<string>(), Endpoint(p?["endpoint"]));
            ServerStore.ValidateId(id);
            if (id.StartsWith("local-", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Local hosting destinations are managed by their hosting profile.");
            if (_directoryConnections.List().Any(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Room destinations are managed by their directory.");
            var original = SavedServers(games).SingleOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            var createOnly = p?["createOnly"]?.GetValue<bool>() ?? false;
            if (createOnly && original is not null) throw new InvalidOperationException("server_id_exists");
            id = original?.Id ?? id;
            if (original is not null && original.Game != gameId) throw new ArgumentException("A server cannot change games.");
            var presentation = original?.Presentation;
            if (p!.AsObject().ContainsKey("presentation"))
            {
                var local = p["presentation"];
                if (local is null) presentation = null;
                else
                {
                    Keys(local, "motd", "motdTranslations", "summary", "summaryTranslations", "iconUrl", "iconUrlTranslations", "images", "imagesTranslations");
                    presentation = new ServerPresentation
                    {
                        Motd = local["motd"]?.GetValue<string>() ?? "",
                        MotdTranslations = local["motdTranslations"]?.Deserialize<Dictionary<string, string>>(BridgeRouter.Json) ?? [],
                        Summary = local["summary"]?.GetValue<string>() ?? "",
                        SummaryTranslations = local["summaryTranslations"]?.Deserialize<Dictionary<string, string>>(BridgeRouter.Json) ?? [],
                        IconUrl = local["iconUrl"]?.GetValue<string>(),
                        IconUrlTranslations = local["iconUrlTranslations"]?.Deserialize<Dictionary<string, string>>(BridgeRouter.Json) ?? [],
                        Images = local["images"]?.Deserialize<string[]>(BridgeRouter.Json) ?? [],
                        ImagesTranslations = local["imagesTranslations"]?.Deserialize<Dictionary<string, IReadOnlyList<string>>>(BridgeRouter.Json) ?? [],
                        OnlinePlayers = original?.Presentation?.OnlinePlayers,
                        PeakOnlinePlayers = original?.Presentation?.PeakOnlinePlayers,
                        PlayerLimit = original?.Presentation?.PlayerLimit,
                        State = original?.Presentation?.State, Whitelist = original?.Presentation?.Whitelist ?? false,
                        RosterEnabled = original?.Presentation?.RosterEnabled ?? false,
                    };
                }
            }
            var changed = new ServerDefinition(id, gameId, Text(p, "name"), p?["apiUrl"]?.GetValue<string>(), Endpoint(p?["endpoint"]), p?["bundledInstances"]?.GetValue<bool>() ?? false, original?.BuiltIn ?? false)
            { PreferIp = p?["preferIp"]?.GetValue<bool>(), UseHttps = p?["useHttps"]?.GetValue<bool>() ?? original?.UseHttps ?? false, Presentation = presentation, IconAsset = original?.IconAsset, Builds = original?.Builds ?? [], ClientPolicy = original?.ClientPolicy ?? ClientPolicy.Empty };
            _servers.Save(changed, createOnly); _api.Forget(id); return changed;
        });
        bridge.Add("servers.delete", p => { Keys(p, "id"); var server = Server(Text(p, "id")); _servers.Delete(server); _api.Forget(server.Id); _seenNotices.Forget(server.Id);
            foreach (var (_, sectionServer) in AccountSections(server).Skip(1)) { _api.Forget(sectionServer.Id); _seenNotices.Forget(sectionServer.Id); }
            _sectionCache.Forget(server.Id); return null; });
        bridge.Add("servers.catalog", async (p, ct) =>
        {
            Keys(p, "id"); var server = Server(Text(p, "id"));
            var game = Games().Single(g => g.Id == server.Game);
            if (StoppedLocalCatalog(server) is { } localCatalog) return localCatalog;
            if (server.ApiUrl is not null)
            {
                var catalog = await ReadCatalogAsync(server, ct).ConfigureAwait(false);
                var hidden = game.Builds.Where(build => !build.Enabled || build.Hidden).Select(build => build.Id).ToHashSet(StringComparer.Ordinal);
                return catalog with { Instances = catalog.Instances.Where(instance => !hidden.Contains(instance.Build)).ToArray() };
            }
            return new LauncherCatalog(1, game.Id, server.Name, false, [], server.BundledInstances
                ? game.Builds.Where(b => b.Enabled && !b.Hidden).Select(b => new ServerInstance(b.Id, b.Id, b.Name.Values, null, b.LauncherLogin ?? game.LauncherLogin) { ReleaseVersion = b.ReleaseVersion }).ToArray() : []);
        });
        bridge.Add("servers.onlinePlayers", async (p, ct) =>
        {
            Keys(p, "id"); return await _api.OnlinePlayersAsync(Server(Text(p, "id")), ct).ConfigureAwait(false);
        });
        RegisterServerClients(bridge);
        RegisterServerStatus(bridge);
        // Account calls take an optional section: a separate section's accounts belong to its own API (see ServerStatus).
        bridge.Add("accounts.list", async (p, ct) =>
        {
            Keys(p, "serverId");
            var server = await ConnectedServerAsync(Text(p, "serverId"), ct).ConfigureAwait(false);
            if (StoppedLocalCatalog(server) is not null) return new AccountSessionList(null, []);
            return await SectionAccountsAsync(server, refresh: true, ct).ConfigureAwait(false);
        });
        bridge.Add("accounts.select", async (p, ct) =>
        {
            Keys(p, "serverId", "username", "section");
            var server = Server(Text(p, "serverId")); var section = OptionalText(p, "section");
            _api.Select(SectionServer(server, section), Text(p, "username"));
            return await UseSectionAsync(server, section, ct).ConfigureAwait(false);
        });
        bridge.Add("accounts.current", async (p, ct) => { Keys(p, "serverId"); return (await SectionAccountsAsync(Server(Text(p, "serverId")), refresh: false, ct).ConfigureAwait(false)).Current; });
        bridge.Add("accounts.rememberedLogin", p => { Keys(p, "serverId", "section"); return _api.RememberedLogin(SectionServer(Server(Text(p, "serverId")), OptionalText(p, "section"))); });
        bridge.Add("accounts.refresh", async (p, ct) =>
        {
            Keys(p, "serverId", "section");
            var server = await ConnectedServerAsync(Text(p, "serverId"), ct).ConfigureAwait(false);
            await _api.RefreshAsync(LaunchAccountServer(server, OptionalText(p, "section")), ct).ConfigureAwait(false);
            return (await SectionAccountsAsync(server, refresh: false, ct).ConfigureAwait(false)).Current;
        });
        bridge.Add("accounts.login", async (p, ct) =>
        {
            Keys(p, "serverId", "section", "username", "password", "rememberPassword");
            var server = await ConnectedServerAsync(Text(p, "serverId"), ct).ConfigureAwait(false); var section = OptionalText(p, "section");
            var profile = await _api.LoginAsync(SectionServer(server, section), Text(p, "username"), Text(p, "password"),
                rememberPassword: p?["rememberPassword"]?.GetValue<bool>() ?? false, ct: ct).ConfigureAwait(false);
            var list = await UseSectionAsync(server, section, ct).ConfigureAwait(false);
            return list.Current ?? profile with { Section = null };
        });
        bridge.Add("accounts.register", async (p, ct) =>
        {
            Keys(p, "serverId", "section", "username", "password", "fields");
            var fields = p?["fields"]?.Deserialize<Dictionary<string, string>>(BridgeRouter.Json) ?? [];
            var server = await ConnectedServerAsync(Text(p, "serverId"), ct).ConfigureAwait(false);
            await _api.RegisterAsync(SectionServer(server, OptionalText(p, "section")), Text(p, "username"), Text(p, "password"), fields, ct).ConfigureAwait(false); return null;
        });
        bridge.Add("accounts.logout", async (p, ct) =>
        {
            Keys(p, "serverId", "username", "section");
            var server = Server(Text(p, "serverId"));
            await _api.LogoutAsync(SectionServer(server, OptionalText(p, "section")), p?["username"]?.GetValue<string>(), ct).ConfigureAwait(false); return null;
        });
        bridge.Add("plugins.launchWarnings", async (p, ct) =>
        {
            Keys(p, "instanceId", "serverId");
            var id = Text(p, "instanceId");
            await Task.Run(() => _pluginUpgrades.ApplyReady(id, ct), ct).ConfigureAwait(false);
            return LaunchComponentWarnings(id, p?["serverId"]?.GetValue<string>(), bridge);
        });
        bridge.Add("instances.resourceOverrides", p =>
        {
            Keys(p, "id");
            var instance = _store.Load(Text(p, "id"));
            var snapshot = ResourceOverrideDiagnostics.Read(_store, instance.Id);
            return snapshot is null ? null : new { snapshot.PreparedAt, files = snapshot.Files.Select(file => new
            {
                file.Target, file.Size, file.Sha256, file.OriginalSha256, file.CacheReused, file.RestoreOnExit, file.State,
                changes = file.Changes.Select(change => new { change.PluginId, change.Version, name = change.Name.Values,
                    change.Member, change.Action, change.OriginalSha256, change.Sha256 })
            }) };
        });
        bridge.Add("instances.cancelResourcePreparation", p =>
        {
            Keys(p, "id");
            lock (_resourcePreparations) if (_resourcePreparations.TryGetValue(Text(p, "id"), out var preparation)) preparation.Cancel();
            return null;
        });
        bridge.Add("instances.cancelLaunch", async (p, _) =>
        {
            Keys(p, "id", "deadlineUnixMilliseconds", "processId"); var id = Text(p, "id");
            _store.Load(id);
            var stop = false;
            lock (_resourcePreparations)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                foreach (var expired in _cancelledLaunchDeadlines.Where(item => item.Value < now).Select(item => item.Key).ToArray())
                    _cancelledLaunchDeadlines.Remove(expired);
                if (p?["deadlineUnixMilliseconds"] is { } requested)
                {
                    var deadline = requested.GetValue<long>();
                    if (deadline > now + 60000) throw new ArgumentException("Invalid launch deadline.");
                    _cancelledLaunchDeadlines[id] = Math.Max(deadline, _cancelledLaunchDeadlines.GetValueOrDefault(id));
                    stop = _launchDeadlines.TryGetValue(id, out var activeDeadline) && activeDeadline == deadline;
                    if (stop && _resourcePreparations.TryGetValue(id, out var preparation)) preparation.Cancel();
                }
            }
            if (stop) await _launches.CancelStartingAsync(id).ConfigureAwait(false);
            else if (p?["processId"] is { } processId && _launches.Running.SingleOrDefault(client => client.InstanceId == id && client.ProcessId == processId.GetValue<int>()) is { } running)
                await _launches.StopAsync(id, running.ProcessId).ConfigureAwait(false);
            bridge.Emit("instances.changed", null);
            return null;
        });
        bridge.Add("instances.launch", async (p, ct) =>
        {
            var callerCancellation = ct;
            using var launchDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var deadline = DateTimeOffset.UtcNow.AddMinutes(1);
            if (p?["deadlineUnixMilliseconds"] is { } requestedDeadline)
                deadline = DateTimeOffset.FromUnixTimeMilliseconds(Math.Min(requestedDeadline.GetValue<long>(), deadline.ToUnixTimeMilliseconds()));
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) return new { timeout = true };
            launchDeadline.CancelAfter(remaining);
            ct = launchDeadline.Token;
            var launchInstanceId = Text(p, "id");
            lock (_resourcePreparations)
            {
                if (_resourcePreparations.ContainsKey(launchInstanceId)) throw new InvalidOperationException("This client is already starting.");
                if (_cancelledLaunchDeadlines.TryGetValue(launchInstanceId, out var cancelledDeadline) && cancelledDeadline >= deadline.ToUnixTimeMilliseconds())
                    return new { cancelled = true };
                _launchDeadlines[launchInstanceId] = deadline.ToUnixTimeMilliseconds();
                _resourcePreparations.Add(launchInstanceId, launchDeadline);
            }
            var lifecycleEntered = false;
            var launchPhase = "lifecycle";
            try
            {
            _launchDiagnostics.Record("launch-requested");
            if (_launcherUpdates.State.Mandatory) throw new InvalidOperationException("Update Fantasy Tale Launcher before starting a game.");
            // The account a launch uses may belong to a separate section, which signs in and issues tickets through its own API.
            var launchSection = OptionalText(p, "section");
            var launchSelection = _api.CaptureLaunchSelection(Servers().SingleOrDefault(known => known.Id == Text(p, "serverId")) is { } knownServer
                ? LaunchAccountServer(knownServer, launchSection).Id : Text(p, "serverId"));
            await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
            lifecycleEntered = true;
            if (_exitPending || _restartPending) throw new InvalidOperationException("Wait until the launcher update or exit finishes before starting a game.");
            // "section" and "unavailableServers" come from the server-section selector; the gateway routing that uses them follows.
            Keys(p, "id", "serverId", "section", "unavailableServers", "account", "confirmedWarnings", "confirmedPendingPlugins", "confirmedRequiredPluginUpdates", "resourceConflictChoices", "launcherLanguage", "deadlineUnixMilliseconds");
            var resourceConflictChoices = ResourceConflictBridge.Parse(p?["resourceConflictChoices"]);
            _launchDiagnostics.Record("launch-plugins-start");
            launchPhase = "plugins";
            await Task.Run(() => _pluginUpgrades.ApplyReady(launchInstanceId, ct), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            _launchDiagnostics.Record("launch-plugins-complete");
            var instance = _store.Load(Text(p, "id")); var game = Game(instance.Game); var build = game.Builds.Single(b => b.Id == instance.Build);
            build.RequireEnabled();
            var launchPackages = Packages(game);
            instance = RecoverPluginSelections(instance, game, build, bridge);
            var confirmations = p?["confirmedWarnings"] is JsonArray warningKeys && warningKeys.Count <= 128
                ? warningKeys.Select(w => w?.GetValue<string>() ?? throw new ArgumentException("Invalid warning confirmation.")).ToArray() : [];
            if (FilePluginResolver.Warnings(instance, build, launchPackages).Any(plugin => !confirmations.Contains(plugin.Id + "@" + plugin.Version, StringComparer.Ordinal)))
                throw new InvalidOperationException("Disabled startup components require explicit confirmation for this launch.");
            _launchDiagnostics.Record("launch-server-start");
            launchPhase = "server";
            var server = await ConnectedServerAsync(Text(p, "serverId"), ct).ConfigureAwait(false);
            if (server.Game != game.Id) throw new ArgumentException("The selected server belongs to a different game.");
            var accountServer = LaunchAccountServer(server, launchSection);
            await ReleasePrivateServersAsync(server.Id).ConfigureAwait(false);
            var confirmedRequiredUpdates = p?["confirmedRequiredPluginUpdates"]?.Deserialize<string[]>(BridgeRouter.Json) ?? [];
            if (RequiredPluginUpdates(instance.Id, server.Id).Any(update => !confirmedRequiredUpdates.Contains(update.Key, StringComparer.Ordinal)))
                throw new InvalidOperationException("plugin_updates_changed");
            var pendingConfirmations = p?["confirmedPendingPlugins"]?.Deserialize<string[]>(BridgeRouter.Json) ?? [];
            if (pendingConfirmations.Length > 32 || PendingPluginTransfers(instance.Id).Any(task => !pendingConfirmations.Contains(task.Id, StringComparer.Ordinal)))
                throw new InvalidOperationException("plugin_updates_changed");
            instance = _store.Load(instance.Id);
            var endpoint = server.Endpoint;
            var launcherLogin = build.LauncherLogin ?? game.LauncherLogin;
            var required = launcherLogin == "required";
            string? hostPolicy = null, region = null;
            ServerCenterConfiguration? center = null;
            if (server.ApiUrl is not null)
            {
                var catalog = await ReadCatalogAsync(server, ct, useCached: true).ConfigureAwait(false);
                center = catalog.Center;
                var preset = catalog.Instances.FirstOrDefault(i => i.Build == build.Id);
                if (preset is null) throw new InvalidOperationException("The server does not support this client build.");
                if (preset.ClusterServed == false) throw new InvalidOperationException("No node of this server serves this client build.");
                endpoint = preset.Endpoint ?? endpoint;
                launcherLogin = LauncherLoginPolicy.Resolve(build.LauncherLogin, game.LauncherLogin, preset.LauncherLogin ?? catalog.LauncherLogin, server.Kind == "room");
                required = launcherLogin == "required";
                hostPolicy = catalog.HostPolicy; region = catalog.Region;
                var languages = preset.ClientLanguages ?? catalog.ClientLanguages;
                var selectedLanguage = instance.Language ?? ClientLanguagePolicy.Native(game, build);
                _ = Language(game, build, selectedLanguage.Length > 0 ? selectedLanguage : null, languages);
                game = game with { SupportsPlugins = catalog.SupportsPlugins ?? game.SupportsPlugins, Builds = game.Builds.Select(b => b.Id == build.Id ? build : b).ToArray() };
            }
            IReadOnlyList<GameServerTarget> gameTargets = [];
            IReadOnlyList<GameServerChoice> gameChoices = [];
            if (launcherLogin is not ("none" or "game")) _api.RequireLaunchSelection(accountServer, launchSelection);
            ct.ThrowIfCancellationRequested();
            _launchDiagnostics.Record("launch-routing-start");
            launchPhase = "routing";
            var module = GameModules.Load(game, AppContext.BaseDirectory);
            var supportsAutomatic = module.GetCapability<IAutomaticServerSelectionCapability>()?.SupportsAutomaticSelection(build) == true;
            var automaticRequested = false;
            ServerDirectory? automaticDirectory = null;
            if (server.ApiUrl is not null)
            {
                // Servers the player was told are unavailable (maintenance or offline) are left out of the in-game list,
                // and automatic placement never lands on a node under maintenance.
                var unavailable = p?["unavailableServers"] is JsonArray unavailableIds && unavailableIds.Count <= 4096
                    ? unavailableIds.Select(id => id?.GetValue<int>() ?? throw new ArgumentException("Invalid server identity.")).ToHashSet() : [];
                var route = await ServerCenterRouting.PrepareAsync(center,
                    async token => await StatusDirectoryAsync(server, build.Id, token).ConfigureAwait(false) is { } routed
                        ? routed with { Servers = routed.Servers.Where(selector => selector.Automatic || !unavailable.Contains(selector.Id))
                            .Select(selector => selector.Automatic ? selector with { Targets = selector.Targets.Where(target => target.Maintenance is null).ToArray() } : selector).ToArray() }
                        : null,
                    _serverDirectoryResolver, supportsAutomatic, launcherLogin, ct).ConfigureAwait(false);
                automaticRequested = route.AutomaticRequested;
                automaticDirectory = route.Directory;
                gameTargets = route.Targets;
                gameChoices = route.Choices;
            }
            if (launcherLogin is not ("none" or "game")) _api.RequireLaunchSelection(accountServer, launchSelection);
            if (endpoint is not null)
                endpoint = endpoint with { Host = await _network.HostAsync(endpoint.Host, server, hostPolicy, region, ct).ConfigureAwait(false) };
            if (server.ApiUrl is null && server.NativeDirectory is { Enabled: true, Mode: "gateway" } nativeDirectory && nativeDirectory.Builds.Contains(build.Id, StringComparer.Ordinal))
            {
                var resolvedTargets = new List<GameServerTarget>(nativeDirectory.Targets.Count);
                foreach (var target in nativeDirectory.Targets)
                {
                    var host = await _network.HostAsync(target.Endpoint.Host, server, "ip", region, ct).ConfigureAwait(false);
                    if (!System.Net.IPAddress.TryParse(host, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                        throw new InvalidOperationException($"Native server directory target '{target.Id}' did not resolve to an IPv4 address.");
                    resolvedTargets.Add(target with { Endpoint = target.Endpoint with { Host = address.ToString() } });
                }
                gameTargets = resolvedTargets;
            }
            if (endpoint is null && gameTargets.Count == 0 && !automaticRequested)
                throw new InvalidOperationException("Configure the selected server's game endpoint before launch.");
            ServerPluginPolicy.RequireEnabled(instance, build, launchPackages, ProfileClientPolicy.ForDestination(build, _api.ClientPolicy(server)));
            // A none policy represents a client-owned login flow. Do not ask the
            // launcher API for a ticket even when an unrelated launcher session is
            // selected, because /v1/launch is intentionally authenticated-only.
            var overlayProfile = module.GetOverlayProfile(build);
            ClientLanguagePolicy.RequireAvailable(game, build, instance, launchPackages);
            var languageMetadata = new ClientLanguageMetadata(p?["launcherLanguage"]?.GetValue<string>() ?? "en",
                ClientLanguagePolicy.Native(game, build), instance.Language ?? ClientLanguagePolicy.Native(game, build));
            _launchDiagnostics.Record("launch-credentials-start");
            launchPhase = "credentials";
            var versionMetadata = await Task.Run(() => module.ReadVersionMetadata(build, _store.ClientFolder(instance.Id)), ct).ConfigureAwait(false);
            // A game policy keeps every launch request; only the sign-in moves to the game's own login screen.
            var gameLogin = launcherLogin == "game" && server.ApiUrl is not null
                ? await _api.OpenGameLoginAsync(accountServer, build, versionMetadata, languageMetadata, ct).ConfigureAwait(false) : null;
            var credentials = launcherLogin is "none" or "game"
                ? null
                : automaticRequested
                    ? await _api.SelectionCredentialsAsync(accountServer, build, versionMetadata, languageMetadata, launchSelection, ct).ConfigureAwait(false)
                    : await _api.CredentialsAsync(accountServer, build, versionMetadata, languageMetadata, required, launchSelection, ct).ConfigureAwait(false);
            AutomaticServerSelection? automaticSelection = null;
            if (automaticRequested)
            {
                var admittedCredentials = credentials ?? throw new InvalidDataException("Native automatic selection requires authenticated admission.");
                var admittedApiUrl = _api.EffectiveApiUrl(accountServer);
                automaticSelection = await AutomaticServerSelection.CreateAsync(admittedCredentials.ServerSelection!, admittedApiUrl,
                    token => _api.RefreshServerSelectionAsync(accountServer, launchSelection, admittedCredentials, admittedApiUrl, token),
                    ct: ct, directory: automaticDirectory, diagnostics: _launchDiagnostics).ConfigureAwait(false);
                gameTargets = automaticSelection.Targets;
            }
            var serverPlugins = ProfileClientPolicy.ForDestination(build, _api.ClientPolicy(server));
            ServerPluginPolicy.RequireEnabled(instance, build, launchPackages, serverPlugins);
            var packageList = launchPackages;
            instance = ServerPluginPolicy.ForLaunch(instance, build, packageList, serverPlugins);
            var servicePlan = game.SupportsPlugins ? LauncherServicePlan.Resolve(instance, build, launchPackages) : [];
            var nativeConsumers = game.SupportsPlugins ? LauncherServicePlan.NativeConsumers(instance, build, launchPackages)
                : new Dictionary<string, IReadOnlyDictionary<string, int>>();
            var panelConsumers = game.SupportsPlugins ? LauncherServicePlan.PanelConsumers(instance, build, launchPackages)
                : new Dictionary<string, IReadOnlyDictionary<string, int>>();
            var controllerConsumers = game.SupportsPlugins ? LauncherServicePlan.ControllerConsumers(instance, build, launchPackages)
                : new Dictionary<string, IReadOnlyDictionary<string, int>>();
            _ = LauncherServicePlan.MessageGrants(servicePlan, nativeConsumers.Concat(panelConsumers).Concat(controllerConsumers).ToDictionary(StringComparer.Ordinal));

            // The game's own HTTP clients take the launcher's routing: a mainland host answers plain HTTP for an
            // unregistered domain with its own registration block (403), so those addresses must use the server's IP.
            var messenger = credentials?.Messenger is { } advertised
                ? advertised with { Url = await _network.RouteAsync(advertised.Url, server, hostPolicy, region, ct).ConfigureAwait(false) } : null;
            var gameLoginApi = gameLogin is null ? null : await _network.RouteAsync(_api.EffectiveApiUrl(accountServer), server, hostPolicy, region, ct).ConfigureAwait(false);
            var overlayEnabled = _overlay.IsEnabledForClient(instance, build, packageList);
            using var resourceCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _launchDiagnostics.Record("launch-process-preparation-start");
            launchPhase = "preparation";
            ct.ThrowIfCancellationRequested();
            RunningClient client;
            try
            {
            client = await _launches.StartAsync(instance.Id, game, module, packageList,
                credentials?.Account ?? p?["account"]?.GetValue<string>(), credentials?.Ticket, endpoint, useServerOverride: true,
                confirmedWarnings: confirmations, requiredPlugins: serverPlugins.Where(item => item.Required).Select(item => item.Id).ToArray(),
                enableOverlayBroker: overlayEnabled, enableCompanionBroker: nativeConsumers.Count > 0,
                disabledPlugins: serverPlugins.Where(item => item.Disabled).Select(item => item.Id).ToArray(), messenger: messenger, nameLogin: server.Kind == "room" && launcherLogin == "none",
                gameLogin: gameLogin is null ? null : new SessionGameLogin(gameLoginApi!, gameLogin.LaunchId, gameLogin.LaunchKey,
                    _api.LauncherVersion, LauncherApiClient.WireBuild(build), build.ProtocolVersion, versionMetadata.ReleaseVersion, versionMetadata.PatchVersion,
                    languageMetadata.LauncherLanguage, languageMetadata.NativeLanguage, languageMetadata.ClientLanguage),
                resourceProgress: new InlineResourceProgress(progress => bridge.Emit("instances.resourceProgress", new { instanceId = instance.Id, progress })),
                preparationCancellation: resourceCancellation.Token, gameTargets: gameTargets, gameChoices: gameChoices,
                automaticSelection: automaticSelection, startupTimeout: deadline - DateTimeOffset.UtcNow, launcherLanguage: languageMetadata.LauncherLanguage,
                resourceConflictChoices: resourceConflictChoices).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (resourceCancellation.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= deadline && !callerCancellation.IsCancellationRequested)
                    return new { timeout = true };
                return new { cancelled = true };
            }
            finally
            {
                bridge.Emit("instances.resourceProgress", new { instanceId = instance.Id, progress = (object?)null });
                var recoveries = DrainPluginRecoveries();
                if (recoveries.Length > 0) bridge.Emit("plugins.recovered", recoveries);
            }
            OfferCompanion(instance.Id, server, credentials, game.Id, languageMetadata.LauncherLanguage, gameLogin);
            _sessionServices.StartForClient(instance, build, servicePlan, client.Completion, nativeConsumers, client.Control, panelConsumers, controllerConsumers);
            _overlay.StartForClient(instance, build with { Overlay = overlayProfile }, packageList, client, languageMetadata.LauncherLanguage,
                module.GetCapability<IGameOverlayControllerProvider>(),
                (owner, token) => _sessionServices.GetControllerChannelAsync(instance.Id, owner, token));
            if (server.Id.StartsWith("local-", StringComparison.Ordinal)
                && _hosting.List().SingleOrDefault(s => s.ProfileId == server.Id[6..] && s.State == "ready") is { } hosted
                && _hosting.StartedProfile(hosted.ProfileId, hosted.RunId).Mode == "private")
            {
                _localClients[client.InstanceId] = (hosted.ProfileId, hosted.RunId);
                _ = StopLocalAfterClientAsync(bridge, client, hosted.ProfileId, hosted.RunId);
            }
            _ = ReportExitAsync(bridge, client);
            _ = ReportReadinessAsync(bridge, client);
            return new { client.InstanceId, client.ProcessId, client.Hook };
            }
            catch (OperationCanceledException) when (!callerCancellation.IsCancellationRequested)
            {
                if (launchDeadline.IsCancellationRequested && DateTimeOffset.UtcNow < deadline) return new { cancelled = true };
                _launchDiagnostics.Record("launch-request-timeout", "phase-" + launchPhase);
                return new { timeout = true };
            }
            catch (Exception error)
            {
                _launchDiagnostics.Record("launch-request-failed", "phase-" + launchPhase + "-" + error.GetType().Name);
                throw;
            }
            finally
            {
                lock (_resourcePreparations) _resourcePreparations.Remove(launchInstanceId);
                if (lifecycleEntered) _lifecycle.Release();
                _launchDiagnostics.Record("launch-request-complete");
            }
        });
        bridge.Add("plugins.recoveryNotices", _ => DrainPluginRecoveries());
        bridge.Add("plugins.deprecationNotices", _ => _pluginDeprecations.DrainNotices());
        bridge.Add("plugins.dismissRecoveryNotice", p =>
        {
            Keys(p, "game", "pluginId", "version");
            if (p is null) return null;
            var game = Text(p, "game"); var id = Text(p, "pluginId"); var version = Text(p, "version");
            lock (_pluginRecoveries)
            {
                var retained = _pluginRecoveries.Where(notice => notice.Game != game || notice.PluginId != id || notice.Version != version).ToArray();
                _pluginRecoveries.Clear();
                foreach (var notice in retained) _pluginRecoveries.Enqueue(notice);
            }
            return null;
        });
        bridge.Add("plugins.prepareImport", async (p, ct) =>
        {
            Keys(p, "game", "path", "operationId");
            var game = Game(Text(p, "game"));
            var preview = await Task.Run(() => _plugins.Prepare(Text(p, "path"), game, ImportProgress(bridge, p), ct), ct).ConfigureAwait(false);
            return new { preview.Token, preview.FileCount, preview.Size, preview.Plugin.Id, preview.Plugin.Version, preview.Plugin.Kind,
                name = preview.Plugin.Name.Values, description = preview.Plugin.Description?.Values };
        });
        bridge.Add("plugins.installImport", async (p, ct) =>
        {
            Keys(p, "game", "token", "operationId", "enable");
            var game = Game(Text(p, "game"));
            var fresh = false;
            var installed = await Task.Run(() =>
            {
                var before = Packages(game);
                var result = _plugins.Install(Text(p, "token"), game, ImportProgress(bridge, p), ct);
                fresh = !before.Any(item => PluginCatalog.MatchesIdentity(result, item.Id));
                return result;
            }, ct).ConfigureAwait(false);
            // Existing selections move to the new version with their enabled state; a first installation is enabled on
            // every supported client when the page asks for it ("turn on after installation", on by default in the dialog).
            _pluginUpgrades.EnqueueImported(installed, game);
            _pluginUpgrades.ApplyReady(cancellationToken: ct);
            var skipped = new List<string>();
            if (fresh && p?["enable"]?.GetValue<bool>() == true)
                foreach (var instance in _store.List().Where(item => item.Game == game.Id))
                {
                    try { if (!EnableEverywhereSelection(instance.Id, installed)) skipped.Add(instance.Id); }
                    catch (Exception error) when (error is InstanceBusyException or InvalidOperationException or DefinitionException or IOException)
                    { skipped.Add(instance.Id); }
                }
            _pluginUpgrades.RetireSupersededImports();
            bridge.Emit("instances.changed", null);
            bridge.Emit("plugins.changed", new { game = game.Id });
            return new { installed.Id, installed.Version, skipped };
        });
        bridge.Add("plugins.discardImport", p => { Keys(p, "token"); _plugins.Discard(Text(p, "token")); return null; });
        bridge.Add("plugins.loadIssues", p =>
        {
            Keys(p, "game"); var game = Game(Text(p, "game")); _ = _plugins.Packages(game);
            return _plugins.LoadIssues().Where(issue => issue.Game is null || issue.Game == game.Id).ToArray();
        });
        bridge.Add("plugins.uninstall", async (p, ct) =>
        {
            Keys(p, "game", "id"); var game = Game(Text(p, "game")); var id = Text(p, "id");
            await Task.Run(() => _pluginUpgrades.Uninstall(game, id), ct).ConfigureAwait(false);
            bridge.Emit("plugins.changed", new { game = game.Id }); bridge.Emit("instances.changed", null); return null;
        });
        bridge.Add("plugins.uninstallRejected", async (p, ct) =>
        {
            Keys(p, "game", "folder"); var game = Game(Text(p, "game")); var folder = Text(p, "folder");
            await Task.Run(() => _plugins.UninstallRejected(game, folder, _store, _launches.IsActive), ct).ConfigureAwait(false);
            bridge.Emit("plugins.changed", new { game = game.Id }); bridge.Emit("instances.changed", null);
            return null;
        });
        bridge.Add("plugins.list", p =>
        {
            p ??= new JsonObject();
            Keys(p, "instanceId", "serverId", "language");
            var instance = p?["instanceId"] is null ? null : _store.Load(Text(p, "instanceId"));
            var serverRequirements = instance is not null && p?["serverId"]?.GetValue<string>() is { Length: > 0 } destination
                ? ServerPluginPolicy.ForBuild(_api.ClientPolicy(Server(destination)), instance.Build) : [];
            var games = Games();
            var build = instance is null ? null : games.Single(game => game.Id == instance.Game).Builds.Single(b => b.Id == instance.Build);
            if (instance is not null && build is not null && !_launches.IsActive(instance.Id))
            {
                try { instance = RecoverPluginSelections(instance, games.Single(g => g.Id == instance.Game), build, bridge); }
                // Listing must remain readable while a download owns the mutation lease.
                catch (InstanceBusyException) { instance = _store.Load(instance.Id); }
            }
            var bundledIdentities = games.Where(game => game.SupportsPlugins)
                .SelectMany(game => PluginCatalog.Load(Path.Combine(game.Folder, "plugins")))
                .Select(plugin => (plugin.Game, plugin.Id)).ToHashSet();
            var packages = games.OrderBy(game => game.Id, StringComparer.Ordinal)
                .SelectMany(game => PluginIdentityMigration.VisiblePackages(Packages(game),
                    build is not null && game.Id == instance!.Game ? [build] : game.Builds)).ToArray();
            return packages.Select(plugin =>
            {
                var ui = PluginConfigurationUi.Load(plugin);
                return new
            {
                plugin.Id, plugin.Aliases, plugin.Game, plugin.Version, plugin.Kind, plugin.Authors, requires = plugin.Requires.Select(item => item.ToString()), name = plugin.Name.Values, description = plugin.Description?.Values,
                policy = SerializePolicyRules(plugin, instance, build, p?["language"]?.GetValue<string>() ?? LauncherLanguage.Current),
                builtIn = bundledIdentities.Contains((plugin.Game, plugin.Id)), canUninstall = packages.Any(item => item.Game == plugin.Game && item.Id == plugin.Id && _plugins.CanUninstall(item)),
                plugin.MinimumLauncherVersion, launcherCompatible = LauncherCompatibility.Supports(plugin.MinimumLauncherVersion),
                languagePackCompatible = build is not null && ClientLanguagePolicy.IsTranslationPackage(build, plugin),
                available = build is not null && plugin.Game == instance!.Game && PluginCatalog.IsAvailable(plugin, build),
                required = build is not null && plugin.Game == instance!.Game && (PluginCatalog.IsRequired(plugin, build) || instance.Plugins.Any(p => p.Id == plugin.Id && p.Required)
                    || serverRequirements.Any(item => item.Id == plugin.Id && item.Required)),
                warningRequired = build is not null && plugin.Game == instance!.Game && PluginCatalog.NeedsWarning(plugin, build),
                selection = instance is not null && plugin.Game == instance.Game ? instance.Plugins.SingleOrDefault(p => p.Id == plugin.Id && p.Version == plugin.Version) : null,
                resourceSettings = plugin.Resources is not null,
                configurationPage = ui.Page is not null || ui.Pages?.Any(page => page.Kind == "html") == true,
                configurationPages = ui.Pages?.Select(page => new { page.Id, page.Kind, title = page.Title.Values, page.Options }),
                options = plugin.Options.Select(o => new { o.Key, o.Type, o.Default, o.Minimum, o.Maximum, o.MaxLength, o.Values,
                    valueLabels = ui.Labels.TryGetValue(o.Key, out var labels) ? labels.ToDictionary(v => v.Key, v => v.Value.Values) : null,
                    label = o.Label.Values, description = o.Description?.Values }),
            }; });
        });
        bridge.Add("plugins.scheme.parse", p =>
        {
            Keys(p, "text");
            var document = PluginSchemeFile.Parse("plugin-scheme.toml", p!["text"]!.GetValue<string>());
            var game = Game(document.Game);
            document = PluginSchemeFile.Migrate(document, game, Packages(game));
            return new { document.Game, document.Name, builds = document.Builds.Select(build => new { build.Id, plugins = build.Plugins.Select(plugin => new { plugin.Id, plugin.Version, plugin.Enabled, plugin.Options }) }) };
        });
        bridge.Add("plugins.scheme.format", p =>
        {
            Keys(p, "document");
            var root = p!["document"] as JsonObject ?? throw new ArgumentException("Missing scheme document.");
            var game = root["game"]?.GetValue<string>() ?? throw new ArgumentException("Missing scheme game.");
            var name = root["name"]?.GetValue<string>() ?? throw new ArgumentException("Missing scheme name.");
            var builds = root["builds"]?.AsArray().Select(build =>
            {
                var row = build?.AsObject() ?? throw new ArgumentException("Invalid scheme build.");
                var id = row["id"]?.GetValue<string>() ?? throw new ArgumentException("Missing scheme build.");
                var plugins = row["plugins"]?.AsArray().Select(plugin =>
                {
                    var item = plugin?.AsObject() ?? throw new ArgumentException("Invalid scheme plugin.");
                    var options = new Dictionary<string, object>(StringComparer.Ordinal);
                    if (item["options"] is JsonObject optionTable) foreach (var pair in optionTable)
                        options[pair.Key] = pair.Value switch { JsonValue value when value.TryGetValue<bool>(out var boolean) => boolean, JsonValue value when value.TryGetValue<long>(out var integer) => integer, JsonValue value when value.TryGetValue<string>(out var text) => text, _ => throw new ArgumentException("Plugin options must be boolean, integer or string values.") };
                    return new PluginSchemePlugin(item["id"]?.GetValue<string>() ?? throw new ArgumentException("Missing scheme plugin."), item["version"]?.GetValue<string>() ?? throw new ArgumentException("Missing scheme plugin version."), item["enabled"]?.GetValue<bool>() ?? false, options);
                }).ToArray() ?? [];
                return new PluginSchemeBuild(id, plugins);
            }).ToArray() ?? [];
            var definition = Game(game);
            return PluginSchemeFile.Render(PluginSchemeFile.Migrate(new PluginSchemeDocument(game, name, builds), definition, Packages(definition)));
        });
        bridge.Add("plugins.configurationContext", p =>
        {
            Keys(p, "instanceId", "id", "version");
            var instance = _store.Load(Text(p, "instanceId")); var game = Game(instance.Game);
            var build = game.Builds.Single(b => b.Id == instance.Build);
            var plugin = Packages(game).Single(x => x.Id == Text(p, "id") && x.Version == Text(p, "version"));
            return plugin.Resources is null ? new Dictionary<string, object>()
                : GameModules.Load(game, AppContext.BaseDirectory) is IGameResourceModule module
                    ? module.ResourceSettings(new LaunchContext(game, build, _store.ClientFolder(instance.Id), null, null), plugin)
                    : throw new InvalidOperationException("Update the game module to configure resource plugins.");
        });
        bridge.Add("plugins.configurationPage", p =>
        {
            Keys(p, "instanceId", "id", "version", "pageId");
            var instance = _store.Load(Text(p, "instanceId")); var game = Game(instance.Game);
            var build = game.Builds.Single(b => b.Id == instance.Build);
            var plugin = Packages(game).Single(x => x.Id == Text(p, "id") && x.Version == Text(p, "version"));
            if (!PluginCatalog.IsAvailable(plugin, build)) throw new InvalidOperationException("Plugin is unavailable on this client.");
            var ui = PluginConfigurationUi.Load(plugin);
            var pageId = p?["pageId"]?.GetValue<string>();
            var path = pageId is null ? ui.Page : ui.Pages?.SingleOrDefault(page => page.Id == pageId && page.Kind == "html")?.Path;
            return path is null ? throw new InvalidOperationException("The requested HTML settings page is not declared by the plugin.") : PluginConfigurationUi.ReadPage(plugin.Folder, path);
        });
        bridge.Add("plugins.resolveScheme", p =>
        {
            Keys(p, "instanceId", "plugins");
            var instance = _store.Load(Text(p, "instanceId")); var game = Game(instance.Game);
            var build = game.Builds.Single(build => build.Id == instance.Build); var packages = Packages(game);
            return PluginIdentityMigration.ResolveSelections(ReadSchemeSelections(p), build, packages);
        });
        bridge.Add("plugins.applyScheme", p =>
        {
            Keys(p, "instanceId", "plugins", "serverId");
            var instance = _store.Load(Text(p, "instanceId")); var game = Game(instance.Game);
            if (!game.SupportsPlugins) throw new InvalidOperationException("This game does not support plugins.");
            var build = game.Builds.Single(b => b.Id == instance.Build);
            var requested = ReadSchemeSelections(p);
            if (requested.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != requested.Length) throw new ArgumentException("Duplicate plugin selection.");
            var packages = Packages(game);
            instance = RecoverPluginSelections(instance, game, build, bridge);
            var rules = ProfileClientPolicy.CanonicalRules(ProfileClientPolicy.ForDestination(build, p?["serverId"]?.GetValue<string>() is { Length: > 0 } destination ? _api.ClientPolicy(Server(destination)) : ClientPolicy.Empty), packages);
            var selections = new List<InstancePlugin>();
            foreach (var selection in PluginIdentityMigration.ResolveSelections(requested, build, packages))
            {
                if (selections.Any(item => item.Id == selection.Id)) throw new InvalidDataException("The scheme selects both a former plugin identity and its successor.");
                var package = packages.SingleOrDefault(item => item.Id == selection.Id && item.Version == selection.Version && PluginCatalog.IsAvailable(item, build));
                if (package is null) continue;
                var options = PluginCatalog.ResolveOptionValues(package, selection.Options);
                if (package.Resources is not null)
                {
                    var module = GameModules.Load(game, AppContext.BaseDirectory) as IGameResourceModule
                        ?? throw new InvalidOperationException("Update the game module to configure resource plugins.");
                    module.ValidateResourceSettings(new LaunchContext(game, build, _store.ClientFolder(instance.Id), null, null),
                        new GameResourceSelection(package, options));
                }
                var required = PluginCatalog.IsRequired(package, build) || instance.Plugins.Any(item => item.Id == selection.Id && item.Required)
                    || rules.Any(item => item.Id == selection.Id && item.Required && !item.Disabled);
                selections.Add(selection with { Required = required, Enabled = required || selection.Enabled });
            }
            foreach (var old in instance.Plugins.Where(item => selections.All(selected => selected.Id != item.Id)))
            {
                var required = old.Required || rules.Any(item => item.Id == old.Id && item.Required && !item.Disabled)
                    || packages.Any(item => item.Id == old.Id && item.Version == old.Version && PluginCatalog.IsRequired(item, build));
                selections.Add(old with { Enabled = required, Required = required });
            }
            var changed = instance with { Plugins = selections.ToArray() };
            // A scheme is a preference, not a launch. Missing mandatory packages are handled by launch preflight.
            if (InstanceFile.Render(changed) == InstanceFile.Render(instance))
            {
                _ = PluginResolver.Resolve(changed, build, packages, requireLibraries: false, requireRequiredPlugins: false);
                return instance;
            }
            _store.SaveValidated(changed, instance, () => PluginResolver.Resolve(changed, build, packages,
                requireLibraries: false, requireRequiredPlugins: false));
            return changed;
        });
        bridge.Add("plugins.configure", (p, ct) =>
        {
            Keys(p, "instanceId", "id", "version", "enabled", "options", "serverId", "selectVersion", "expectedVersion", "deferRefresh");
            var instance = _store.Load(Text(p, "instanceId")); var game = Game(instance.Game);
            // Configuring an installed plugin is a local operation. A server catalog
            // may be unavailable while the user is offline, so live discovery cannot
            // block enable, disable, or settings changes. Launch validation still
            // applies the current client policy when a game is started.
            if (!game.SupportsPlugins) throw new InvalidOperationException("This game does not support plugins.");
            var build = game.Builds.Single(b => b.Id == instance.Build);
            instance = RecoverPluginSelections(instance, game, build, bridge);
            var id = Text(p, "id");
            var existing = instance.Plugins.SingleOrDefault(x => x.Id == id);
            if (p?["enabled"]?.GetValue<bool>() == false && p?["serverId"]?.GetValue<string>() is { Length: > 0 } serverId)
            {
                // Use the last policy known for this server, if any. Refreshing it
                // belongs to launch/discovery and must not make local configuration
                // depend on network availability.
                var cachedRules = ProfileClientPolicy.ForDestination(build, _api.ClientPolicy(Server(serverId)));
                if (cachedRules.Any(item => item.Offered && !item.Disabled && item.Required && item.Id == id))
                    throw new InvalidOperationException("server_plugin_required:" + id);
            }
            var selectVersion = p?["selectVersion"]?.GetValue<bool>() == true;
            if (selectVersion && (!p!.AsObject().ContainsKey("expectedVersion") || p["expectedVersion"]?.GetValue<string>() != existing?.Version))
                throw new InvalidOperationException("The selected plugin version changed. Refresh before applying this selection.");
            if (!selectVersion && p?["enabled"]?.GetValue<bool>() == false && existing is not null && existing.Version != Text(p, "version"))
                throw new InvalidOperationException("Only the selected plugin version can be disabled.");
            var options = ReadOptions(p?["options"]);
            if (p?["options"] is null && existing is not null)
            {
                var target = Packages(game).SingleOrDefault(plugin => plugin.Id == id && plugin.Version == Text(p, "version"))
                    ?? throw new InvalidOperationException("The requested plugin version is not installed.");
                // Preserve shared settings during an explicit version switch; the
                // resolver below rejects values incompatible with the new schema.
                options = existing.Options.Where(pair => target.Options.Any(option => option.Key == pair.Key))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            }
            var selected = new InstancePlugin(id, Text(p, "version"), p?["enabled"]?.GetValue<bool>() ?? throw new ArgumentException("Missing enabled."),
                instance.Plugins.Any(x => x.Id == id && x.Required), options);
            var selectedPackage = Packages(game).Single(x => x.Id == selected.Id && x.Version == selected.Version);
            if (selectedPackage.Resources is not null)
            {
                var module = GameModules.Load(game, AppContext.BaseDirectory) as IGameResourceModule
                    ?? throw new InvalidOperationException("Update the game module to configure resource plugins.");
                module.ValidateResourceSettings(new LaunchContext(game, build, _store.ClientFolder(instance.Id), null, null),
                    new GameResourceSelection(selectedPackage, PluginCatalog.ResolveOptionValues(selectedPackage, selected.Options)));
            }
            var changed = PluginSelectionCompatibility.Select(instance, selected, build, Packages(game));
            void ValidateSettings()
            {
                var packages = Packages(game);
                PluginResolver.Resolve(changed, build, packages, requireLibraries: false);
                PluginPolicy.RequireConfigured(selectedPackage, selected.Enabled, changed, build, LauncherLanguage.Current, packages);
            }
            if (_launches.IsActive(instance.Id)) _store.SavePluginSettingsValidated(changed, instance, ValidateSettings);
            else _store.SaveValidated(changed, instance, ValidateSettings);
            if (p?["deferRefresh"]?.GetValue<bool>() != true) bridge.Emit("instances.changed", new { id = changed.Id });
            return Task.FromResult<object?>(changed);
        });
    }

    private async Task<ClientDownloadTask> InstallVerifiedAsync(StoreSource source, StorePackage package, string instanceId, CancellationToken ct,
        bool updateOnly = false, string? archivePassword = null)
    {
        var game = Game(package.Game); var build = game.Builds.SingleOrDefault(b => b.Id == package.Builds.Single()) ?? throw new ArgumentException("Unsupported client build.");
        build.RequireEnabled();
        var manifest = await SourceDocumentReader.ReadBytesAsync(_sourceHttp, OpenSource(source), package.Manifest, ct).ConfigureAwait(false);
        var files = ClientRelease.Parse(package, manifest);
        if (!files.Any(f => f.IsArchive) && !files.Any(f => new[] { build.Executable }.Concat(build.ExecutableAliases).Contains(f.Path, StringComparer.OrdinalIgnoreCase))
            && ClientExecutable.FindInstalled(_store.ClientFolder(instanceId), build) is null) throw new InvalidDataException("The release does not contain the client's executable.");
        return _downloads.Start(source, package, manifest, instanceId, updateOnly, archivePassword: archivePassword);
    }

    private static SocketsHttpHandler CreateSourceHttpHandler(int connections)
    {
        var handler = NetworkPolicy.CreateHttpHandler();
        handler.AllowAutoRedirect = true;
        handler.MaxConnectionsPerServer = connections;
        handler.MaxAutomaticRedirections = 5;
        handler.ConnectTimeout = TimeSpan.FromSeconds(15);
        return handler;
    }

    private IDownloadSource OpenSource(StoreSource source)
    {
        var root = SourceAddress.BaseUrl(source.BaseUrl ?? throw new InvalidOperationException("Source URL is missing."));
        return SourceTransport.Open(_sourceHttp, source.Kind, root, source.PreferIp ?? _networkSettings.IpPreference != "disabled");
    }

    internal IReadOnlyList<GameDefinition> Games() => _catalogs.Apply(GameCatalog.Load(_gamesRoot), _sources.List());

    private GameDefinition Game(string id) => Games().SingleOrDefault(g => g.Id == id && g.Status == GameStatus.Enabled)
        ?? throw new ArgumentException("Unknown or unavailable game.");

    private readonly Queue<PluginRecoveryNotice> _pluginRecoveries = new();

    private static InstancePlugin[] ReadSchemeSelections(JsonNode? parameters) =>
        (parameters?["plugins"] as JsonArray ?? throw new ArgumentException("Missing plugin selections.")).Select(row =>
        {
            Keys(row, "id", "version", "enabled", "required", "options");
            return new InstancePlugin(Text(row, "id"), Text(row, "version"),
                row?["enabled"]?.GetValue<bool>() ?? throw new ArgumentException("Missing plugin enabled state."), false, ReadOptions(row?["options"]));
        }).ToArray();

    private PluginRecoveryNotice[] DrainPluginRecoveries()
    {
        lock (_pluginRecoveries)
        {
            var notices = _pluginRecoveries.Concat(_launches.DrainRecoveryNotices())
                .DistinctBy(notice => (notice.Game, notice.PluginId, notice.Version)).ToArray();
            _pluginRecoveries.Clear();
            return notices;
        }
    }

    private InstanceDefinition RecoverPluginSelections(InstanceDefinition instance, GameDefinition game, BuildDefinition build, BridgeRouter bridge)
    {
        var packages = Packages(game);
        var updated = FilePluginResolver.RecoverInstalled(_store, instance.Id, game, build, packages,
            _launches.IsActive(instance.Id), out var recoveries);
        if (recoveries.Count == 0) return updated;
        var repaired = recoveries.ToList();
        if (recoveries.Count > 0)
        {
            // Package inventory is shared: repair the same failed releases in other
            // clients now, allowing each build to migrate to its own healthy replacement.
            var failed = recoveries.Select(notice => (notice.PluginId, notice.Version)).ToHashSet();
            foreach (var other in _store.List().Where(candidate => candidate.Game == game.Id && candidate.Id != instance.Id
                && candidate.Plugins.Any(selection => failed.Contains((selection.Id, selection.Version)))))
            {
                var otherBuild = game.Builds.SingleOrDefault(candidate => candidate.Id == other.Build);
                if (otherBuild is null) continue;
                try
                {
                    _ = FilePluginResolver.RecoverInstalled(_store, other.Id, game, otherBuild, packages,
                        _launches.IsActive(other.Id), out var otherRecoveries);
                    repaired.AddRange(otherRecoveries);
                }
                catch (InstanceBusyException) { /* A file operation retains its client lease; retry when it releases it. */ }
            }
        }
        var notices = repaired.DistinctBy(notice => (notice.Game, notice.PluginId, notice.Version)).Select(notice =>
        {
            var issue = _plugins.RejectedSelection(notice.Game, notice.PluginId, notice.Version);
            if (issue is null) return notice;
            try { _plugins.UninstallRejected(game, issue.Folder, _store, _launches.IsActive); }
            catch (InstanceBusyException) { /* The invalid selection is detached; do not interrupt another client's operation. */ }
            return notice with { Reason = "corrupt" };
        }).ToArray();
        lock (_pluginRecoveries)
        {
            foreach (var notice in notices)
            {
                if (_pluginRecoveries.Contains(notice)) continue;
                while (_pluginRecoveries.Count >= 32) _pluginRecoveries.Dequeue();
                _pluginRecoveries.Enqueue(notice);
            }
        }
        if (notices.Length > 0) bridge.Emit("plugins.recovered", notices);
        return updated;
    }

    private IReadOnlyList<PluginDefinition> Packages(GameDefinition game) => _plugins.Packages(game);

    private IReadOnlyList<ServerDefinition> SavedServers(IReadOnlyList<GameDefinition> games) => _servers.List(games);

    private DirectorySource[] DirectorySources()
    {
        var stores = _sources.List().Where(source => source.Enabled && source.BaseUrl is not null).ToArray();
        var publicSources = stores.Select(StoreServerDiscovery.Source).ToArray();
        var all = new DirectorySource("store-catalogs", "Stores", stores.FirstOrDefault()?.BaseUrl, stores.Length != 0, true) { StoreSourceId = "*" };
        return new[] { all }.Concat(publicSources).Concat(_directories.List(_catalogs.DirectorySources(stores))).ToArray();
    }

    private Task<DirectoryListing> ReadDirectoryListingAsync(DirectorySource source, CancellationToken ct) => source.StoreSourceId is { } storeId
        ? _serverDiscovery.ListAsync(_sources.List().Where(store => storeId == "*" || store.Id == storeId).ToArray(), ct)
        : _directoryApi.ListAsync(source, ct);

    private ServerDefinition[] Servers() => SavedServers(Games())
        .Concat(_hostingProfiles.List().Select(p => _hosting.Destination(p.Id) ?? new ServerDefinition("local-" + p.Id, p.Game, p.Name,
            $"http://{(p.Mode == "lan" ? p.AdvertisedHost : "127.0.0.1")}:{p.ApiPort}/", new("127.0.0.1", p.GamePort), true, true) { Kind = "room" })).Concat(_directoryConnections.List()).ToArray();

    private LauncherCatalog? StoppedLocalCatalog(ServerDefinition server)
    {
        if (!server.Id.StartsWith("local-", StringComparison.Ordinal) || _hosting.Destination(server.Id[6..]) is not null) return null;
        var profile = _hostingProfiles.List().Single(p => p.Id == server.Id[6..]);
        var game = Game(profile.Game); var build = game.Builds.Single(b => b.Id == profile.Build);
        var login = profile.RequireAuthentication ? "required" : "none";
        return new LauncherCatalog(1, game.Id, profile.Name, false, [], [new ServerInstance(build.Id, build.Id, build.Name.Values, server.Endpoint, login)])
        { LauncherLogin = login, SupportsPlugins = game.SupportsPlugins };
    }

    private async Task ReleasePrivateServersAsync(string? keepServerId)
    {
        foreach (var session in _hosting.List().Where(s => "local-" + s.ProfileId != keepServerId && s.State is "starting" or "ready" or "stopping"))
            if (_hosting.StartedProfile(session.ProfileId, session.RunId).Mode == "private")
            {
                await _hosting.StopAsync(session.ProfileId, session.RunId).ConfigureAwait(false);
                _api.Forget("local-" + session.ProfileId);
            }
    }

    private async Task StopLocalAfterClientAsync(BridgeRouter bridge, RunningClient client, string profileId, string runId)
    {
        try { await client.Completion.ConfigureAwait(false); }
        catch (Exception) { /* Exit reporting preserves the original client failure. */ }
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            _localClients.Remove(client.InstanceId);
            if (!_localClients.Values.Any(value => value.Profile == profileId && value.Run == runId)
                && _hosting.List().Any(s => s.ProfileId == profileId && s.RunId == runId && s.State is "starting" or "ready" or "stopping"))
            {
                await _roomPublisher.WithdrawAsync(profileId, runId, CancellationToken.None).ConfigureAwait(false);
                await _hosting.StopAsync(profileId, runId).ConfigureAwait(false);
                _api.Forget("local-" + profileId);
                bridge.Emit("hosting.changed", new { id = profileId });
            }
        }
        catch (Exception error) { bridge.Emit("hosting.error", new { id = profileId, error = error.Message }); }
        finally { _lifecycle.Release(); }
    }

    private ServerDefinition Server(string id) => Servers().SingleOrDefault(s => s.Id == id) ?? throw new ArgumentException("Unknown or stopped server.");

    private void EnsureHostingIdle(string id)
    {
        if (_hosting.List().Any(s => s.ProfileId == id && s.State is "starting" or "ready" or "stopping"))
            throw new InvalidOperationException("Stop the local server before editing its settings.");
    }

    private static string? Language(GameDefinition game, BuildDefinition build, string? language, IReadOnlyList<string>? serverLanguages = null)
    {
        var allowed = ClientLanguagePolicy.Supported(game, build);
        if (serverLanguages is not null) allowed = allowed.Where(value => serverLanguages.Contains(value, StringComparer.Ordinal)).ToArray();
        if (language is not null && !allowed.Contains(language, StringComparer.Ordinal)) throw new ArgumentException("Unsupported client language.");
        return language;
    }

    private async Task<LauncherCatalog?> CatalogForAsync(string gameId, string? serverId, CancellationToken ct)
    {
        if (serverId is null) return null;
        var server = Server(serverId);
        if (server.Game != gameId) throw new ArgumentException("The server belongs to another game.");
        return server.ApiUrl is null ? null : await ReadCatalogAsync(server, ct).ConfigureAwait(false);
    }

    private async Task<LauncherCatalog> ReadCatalogAsync(ServerDefinition server, CancellationToken ct, bool useCached = false)
    {
        if (StoppedLocalCatalog(server) is { } localCatalog) return localCatalog;
        if (!useCached) server = await _directoryConnections.ResolveAsync(server.Id, ct).ConfigureAwait(false) ?? server;
        var catalog = (useCached ? _api.CachedCatalog(server) : null) ?? await _api.CatalogAsync(server, ct).ConfigureAwait(false);
        if (!server.Id.StartsWith("local-", StringComparison.Ordinal)) return catalog;
        var owned = _hosting.Destination(server.Id[6..]) ?? throw new InvalidOperationException("The local server stopped during discovery.");
        var profile = _hostingProfiles.List().Single(p => p.Id == server.Id[6..]);
        var login = profile.RequireAuthentication ? "required" : "none";
        // The host player reaches its own listener directly even when friends use an external address.
        return catalog with
        {
            LauncherLogin = login, RegistrationEnabled = profile.RequireAuthentication && catalog.RegistrationEnabled,
            Instances = catalog.Instances.Select(p => p with { Endpoint = owned.Endpoint, LauncherLogin = login }).ToArray(),
        };
    }

    private static ServerEndpoint? Endpoint(JsonNode? node)
    {
        if (node is null) return null;
        Keys(node, "host", "port");
        return new ServerEndpoint(Text(node, "host"), node["port"]?.GetValue<int>() ?? throw new ArgumentException("Missing port."));
    }

    private static string Text(JsonNode? p, string key) => p?[key]?.GetValue<string>() ?? throw new ArgumentException($"Missing {key}.");

    private static void Keys(JsonNode? node, params string[] keys)
    {
        if (node is not JsonObject obj) throw new ArgumentException("Parameters must be an object.");
        if (obj.Any(pair => !keys.Contains(pair.Key, StringComparer.Ordinal))) throw new ArgumentException("Unknown parameter.");
    }

    private static Dictionary<string, object> ReadOptions(JsonNode? node)
    {
        if (node is null) return new Dictionary<string, object>(StringComparer.Ordinal);
        if (node is not JsonObject obj) throw new ArgumentException("Options must be an object.");
        return obj.ToDictionary(pair => pair.Key, pair =>
        {
            if (pair.Value is JsonValue value)
            {
                if (value.TryGetValue<bool>(out var boolean)) return (object)boolean;
                if (value.TryGetValue<long>(out var integer)) return integer;
                if (value.TryGetValue<string>(out var text)) return text;
            }
            throw new ArgumentException("Options must be booleans, integers or strings.");
        }, StringComparer.Ordinal);
    }

    internal static async Task ReportExitAsync(BridgeRouter bridge, RunningClient client)
    {
        try { bridge.Emit("instances.exited", await client.Completion.ConfigureAwait(false)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { bridge.Emit("instances.error", new { client.InstanceId, client.ProcessId, error = "launch_cleanup_failed",
            phase = e is LaunchCleanupException cleanup ? cleanup.Phase : "unknown",
            exitConfirmed = e is LaunchCleanupException exited && exited.ExitConfirmed,
            exitCode = (e as LaunchCleanupException)?.ExitCode,
            failureType = (e.InnerException ?? e).GetType().Name, utc = DateTimeOffset.UtcNow }); }
        finally { bridge.Emit("instances.changed", new { client.InstanceId }); }
    }

    private static async Task ReportReadinessAsync(BridgeRouter bridge, RunningClient client)
    {
        var hook = await client.Readiness.ConfigureAwait(false);
        bridge.Emit("instances.readiness", new { client.InstanceId, client.ProcessId, Hook = hook });
    }
}
