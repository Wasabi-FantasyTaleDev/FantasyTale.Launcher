using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Hosting;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;
using System.Text.Json.Serialization;

namespace FantasyTale.Launcher.Core.Stores;

/// <summary>Destination and source labels for one client download; expiring storage URLs stay private.</summary>
/// <param name="Kind">The installed resource category.</param>
/// <param name="GameId">Owning game definition.</param>
/// <param name="BuildId">Exact client build.</param>
/// <param name="InstanceId">Independent installation identity.</param>
/// <param name="Name">User's client name.</param>
/// <param name="FromVersion">Previously recorded release, when available.</param>
/// <param name="ToVersion">Selected catalog release.</param>
/// <param name="SourceName">Configured source display name.</param>
/// <param name="SourceKind">AList or HTTP adapter identifier.</param>
/// <param name="Directory">Validated installation directory.</param>
public sealed record ClientDownloadTarget(string Kind, string GameId, string BuildId, string InstanceId, string Name,
    string FromVersion, string ToVersion, string SourceName, string SourceKind, string Directory)
{
    /// <summary>Immutable resource identity used to match required plugin downloads independently of display names.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string PackageId { get; init; } = "";
    /// <summary>Catalog names for display in the current UI language; absent for user-named clients.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? NameLocalized { get; init; }
}

/// <summary>One file's independent transfer and verification progress.</summary>
/// <param name="Path">Relative installation path.</param>
/// <param name="Size">Declared file size.</param>
/// <param name="Received">Bytes transferred or recovered from a partial.</param>
/// <param name="Stage">queued, downloading, verifying or verified.</param>
public sealed record ClientDownloadFile(string Path, long Size, long Received, string Stage);

/// <summary>An immutable task snapshot shared by the Home and Downloads views.</summary>
/// <param name="Id">Stable task identity preserved across launcher restarts.</param>
/// <param name="Mode">Always live for host tasks.</param>
/// <param name="Target">Source and destination labels.</param>
/// <param name="Stage">Transfer, verification and installation lifecycle stage.</param>
/// <param name="Files">Bounded independent file progress.</param>
/// <param name="CurrentPath">Currently active relative path.</param>
/// <param name="BytesPerSecond">Recent network transfer rate.</param>
/// <param name="SpeedSampleCount">Samples available for an ETA.</param>
/// <param name="StageProgress">Installation percentage, or null.</param>
/// <param name="Failure">network, integrity, source or disk; never a credential-bearing exception.</param>
/// <param name="ResumeStage">Stage to resume when retried.</param>
public sealed record ClientDownloadTask(string Id, string Mode, ClientDownloadTarget Target, string Stage,
    IReadOnlyList<ClientDownloadFile> Files, string? CurrentPath, double BytesPerSecond, int SpeedSampleCount,
    int? StageProgress, string? Failure, string ResumeStage);

/// <summary>Owns bounded real download tasks and shares client leases with launch, deletion and configuration changes.</summary>
public sealed partial class ClientDownloadService : IAsyncDisposable
{
    private sealed class Job(SavedDownload saved, IReadOnlyList<ClientReleaseFile> files) : IDisposable
    {
        internal SavedDownload Saved = saved;
        internal ClientDownloadTask Snapshot { get => Saved.Snapshot; set => Saved = Saved with { Snapshot = value }; }
        internal readonly IReadOnlyList<ClientReleaseFile> Files = files;
        internal ClientInstallReceipt Receipt => new(Saved.SourceId, Saved.Package.Id, Saved.Package.Version, Saved.Package.ManifestSha256, Saved.SourceIdentity, Saved.Package.Revision);
        internal CancellationTokenSource Cancellation = new();
        internal Task Worker = Task.CompletedTask;
        internal bool Running;
        internal string? ArchivePassword;
        internal long LastSave;
        internal string Interrupted = "paused";
        public void Dispose() => Cancellation.Dispose();
    }
    private readonly InstanceStore store;
    private readonly HttpClient http;
    private readonly DownloadTaskStore _persistence;
    private readonly Func<string, StoreSource?> _findSource;
    private readonly Func<StoreSource, IDownloadSource> _openSource;
    private readonly PluginLibrary? _plugins;
    private readonly Func<string, GameDefinition>? _game;
    private readonly ServerPackageLibrary? _serverPackages;
    private readonly string _pluginTasksRoot;
    private readonly object _gate = new();
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private bool _closing;
    private string? _storageError;

    /// <summary>Loads bounded task history, revalidates pinned manifests, and queues previously active work with at most two workers.</summary>
    public ClientDownloadService(InstanceStore store, HttpClient http, string taskRoot,
        Func<string, StoreSource?> findSource, Func<StoreSource, IDownloadSource> openSource,
        PluginLibrary? plugins = null, Func<string, GameDefinition>? game = null, ServerPackageLibrary? serverPackages = null)
    {
        this.store = store; this.http = http; _findSource = findSource; _openSource = openSource;
        _plugins = plugins; _game = game; _pluginTasksRoot = Path.GetFullPath(Path.Combine(taskRoot, "plugin-tasks"));
        _serverPackages = serverPackages;
        _persistence = new DownloadTaskStore(taskRoot);
        try
        {
            foreach (var saved in _persistence.Load())
            {
                var snapshot = saved.Snapshot with { BytesPerSecond = 0, SpeedSampleCount = 0 };
                if (saved.ResumeOnRestart && snapshot.Stage is not ("completed" or "canceled"))
                    snapshot = snapshot with { Stage = "queued", Failure = null, StageProgress = null };
                _jobs.Add(snapshot.Id, new(saved with { Snapshot = snapshot }, ClientRelease.Parse(saved.Package, saved.Manifest)));
            }
            lock (_gate) Schedule();
        }
        catch (Exception error) when (Expected(error))
        { _storageError = "Saved download history could not be read. The original records were retained; downloads are stopped."; }
    }

    /// <summary>Returns immutable snapshots and reports persistence failures instead of silently losing task history.</summary>
    public IReadOnlyList<ClientDownloadTask> List()
    { lock (_gate) { CheckStorage(); return _jobs.Values.Select(DisplaySnapshot).Reverse().ToArray(); } }

    internal bool TryRunWhileIdle(Func<bool> maintenance)
    {
        lock (_gate)
        {
            if (_jobs.Values.Any(job => job.Running || job.Snapshot.Stage is not ("completed" or "canceled" or "failed"))) return false;
            // Keep queue admission excluded until maintenance releases its client leases.
            return maintenance();
        }
    }

    private static ClientDownloadTask DisplaySnapshot(Job job)
        => job.Snapshot with { Target = job.Snapshot.Target with
        { NameLocalized = job.Saved.Package.Kind is "plugin" or "server" ? job.Saved.Package.Name : null, PackageId = job.Saved.Package.Id } };

    /// <summary>Persists an exact catalog-pinned manifest before queuing a client release.</summary>
    public ClientDownloadTask Start(StoreSource source, StorePackage package, byte[] manifest, string instanceId, bool updateOnly = false,
        string? recommendationSelection = null, bool serverRecommended = false, string? archivePassword = null,
        IReadOnlyList<string>? activationTargets = null)
    {
        if (package.Kind == "plugin") LauncherCompatibility.Require(package.MinimumLauncherVersion, package.Id);
        var files = ClientRelease.Parse(package, manifest);
        var instance = store.Load(instanceId);
        if (activationTargets is { Count: > 0 })
        {
            if (package.Kind != "plugin" || activationTargets.Any(id => id.Length is < 1 or > 128))
                throw new ArgumentException("Invalid plugin activation targets.");
            foreach (var targetId in activationTargets)
            {
                var target = store.Load(targetId);
                if (target.Game != instance.Game || package.Builds.Count > 0 && !package.Builds.Contains(target.Build, StringComparer.Ordinal))
                    throw new InvalidDataException("Plugin targets must use compatible builds of the same game.");
            }
        }
        if (package.Kind == "client" && _game is not null)
            _game(instance.Game).Builds.Single(build => build.Id == instance.Build).RequireEnabled();
        if (serverRecommended && recommendationSelection is null) throw new ArgumentException("Server recommendations require explicit activation intent.");
        if (recommendationSelection is not null && (package.Kind != "plugin" || _game is null || recommendationSelection.Length != 64 || !recommendationSelection.All(char.IsAsciiHexDigit)
            || !ClientPolicyActivation.SelectionMatches(instance, package.Id, recommendationSelection)
            || !serverRecommended && !ProfileClientPolicy.Offered(_game!(instance.Game).Builds.Single(b => b.Id == instance.Build)).Contains(package.Id, StringComparer.Ordinal)
                && !ProfileClientPolicy.Has(_game(instance.Game).Builds.Single(b => b.Id == instance.Build), package.Id, "latest")))
            throw new InvalidOperationException("The recommendation selection changed before download.");
        if (package.Kind is not ("client" or "plugin") || package.Game != instance.Game ||
            package.Kind == "client" && (package.Builds.Count != 1 || package.Builds[0] != instance.Build) ||
            package.Kind == "plugin" && (package.Builds.Count > 0 && !package.Builds.Contains(instance.Build, StringComparer.Ordinal) || _plugins is null || _game is null))
            throw new InvalidDataException("The selected release does not match this client.");
        var identity = StoreSourceIdentity.Of(source);
        var current = _findSource(source.Id);
        if (current is not { Enabled: true, BaseUrl: not null } || StoreSourceIdentity.Of(current) != identity)
            throw new InvalidOperationException("The source changed or was disabled. Refresh the store first.");
        var taskId = Guid.NewGuid().ToString("N");
        var client = package.Kind == "plugin" ? PluginTaskFolder(taskId) : store.ClientFolder(instanceId);
        lock (_gate)
        {
            CheckStorage();
            if (_closing) throw new InvalidOperationException("The download service is stopping.");
            if (_jobs.Values.Any(j => j.Saved.Package.Kind != "server" && j.Snapshot.Target.InstanceId == instanceId && (j.Running || j.Snapshot.Stage is not ("completed" or "canceled"))
                && (package.Kind != "plugin" || j.Saved.Package.Kind != "plugin" || j.Saved.Package.Id == package.Id)))
                throw new InvalidOperationException("This client already has a download task.");
            var previous = package.Kind == "plugin" ? null : ClientInstallTransaction.ReadReceipt(client);
            if (package.Kind == "plugin" && updateOnly) throw new InvalidOperationException("Use explicit plugin version installation.");
            if (updateOnly && (previous is null || previous.SourceId != source.Id || previous.SourceIdentity != identity || previous.PackageId != package.Id ||
                previous.ManifestSha256 == package.ManifestSha256 || package.UpdateMode != "files" && package.Revision > 0 && previous.Revision > 0 && package.Revision <= previous.Revision))
                throw new InvalidOperationException("The installed release changed. Check for updates again.");
            var snapshot = new ClientDownloadTask(taskId, "live",
                new(package.Kind, instance.Game, instance.Build, instance.Id, package.Kind == "plugin" ? package.Name["en"] : instance.Name, previous?.Version ?? "", package.Version, source.Name, source.Kind, client),
                "queued", files.Select(f => new ClientDownloadFile(f.Path, f.Size, 0, "queued")).ToArray(), null, 0, 0, null, null, "downloading");
            snapshot = snapshot with { Target = snapshot.Target with { PackageId = package.Id } };
            if (archivePassword is { Length: > 256 } || archivePassword?.Any(char.IsControl) == true)
                throw new ArgumentException("Archive password exceeds its limit.");
            var job = new Job(new SavedDownload(1, source.Id, identity, package, manifest.ToArray(), snapshot, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, previous)
                { RecommendationSelection = recommendationSelection, ServerRecommended = serverRecommended,
                    ActivationTargets = activationTargets?.Distinct(StringComparer.Ordinal).ToArray(),
                    ActivationSelections = activationTargets?.Distinct(StringComparer.Ordinal).ToDictionary(id => id,
                        id => ClientPolicyActivation.SelectionStamp(store.Load(id), package.Id), StringComparer.Ordinal),
                    UpgradeSelection = package.Kind == "plugin" && recommendationSelection is null &&
                        instance.Plugins.Any(p => p.Id == package.Id && PluginCatalog.CompareVersions(package.Version, p.Version) > 0)
                        ? ClientPolicyActivation.SelectionStamp(instance, package.Id) : null }, files)
            { ArchivePassword = archivePassword };
            Save(job); _jobs.Add(snapshot.Id, job); Schedule(); return DisplaySnapshot(job);
        }
    }

    /// <summary>Downloads a verified plugin into the shared library without requiring or enabling a client installation.</summary>
    public ClientDownloadTask PreloadPlugin(StoreSource source, StorePackage package, byte[] manifest)
    {
        LauncherCompatibility.Require(package.MinimumLauncherVersion, package.Id);
        if (package.Kind != "plugin" || _plugins is null || _game is null) throw new InvalidOperationException("Plugin download services are unavailable.");
        var game = _game(package.Game);
        if (!game.SupportsPlugins) throw new InvalidOperationException("This game does not support plugins.");
        var build = game.Builds.FirstOrDefault(b => package.Builds.Count == 0 || package.Builds.Contains(b.Id, StringComparer.Ordinal))
            ?? throw new InvalidDataException("This plugin requires a client version not supported by this launcher.");
        var files = ClientRelease.Parse(package, manifest);
        var identity = StoreSourceIdentity.Of(source);
        var current = _findSource(source.Id);
        if (current is not { Enabled: true, BaseUrl: not null } || StoreSourceIdentity.Of(current) != identity)
            throw new InvalidOperationException("The source changed or was disabled. Refresh the store first.");
        lock (_gate)
        {
            CheckStorage();
            var duplicate = _jobs.Values.FirstOrDefault(j => j.Saved.LibraryOnly
                && j.Saved.SourceId == source.Id && j.Saved.SourceIdentity == identity
                && j.Saved.Package.Id == package.Id && j.Saved.Package.Version == package.Version
                && j.Snapshot.Stage is not ("completed" or "canceled"));
            if (duplicate is not null)
            {
                if (duplicate.Saved.Package.ManifestSha256 != package.ManifestSha256) throw new InvalidDataException("The plugin release changed without a version increase.");
                return DisplaySnapshot(duplicate);
            }
            var inventory = _plugins.Packages(game);
            var installed = inventory.SingleOrDefault(p => p.Id == package.Id && p.Version == package.Version);
            var previousVersion = inventory.Where(p => p.Id == package.Id || package.PluginDefinition?.Aliases.Contains(p.Id, StringComparer.Ordinal) == true)
                .OrderByDescending(p => PluginVersion.Parse(p.Version)).FirstOrDefault()?.Version ?? "";
            if (installed is not null && _plugins.Origin(installed) is null) installed = null;
            if (installed is not null && (_plugins.Origin(installed) != new PluginStoreOrigin(source.Id, identity, package.ManifestSha256)
                || !_plugins.MatchesInstalledSnapshot(installed))) throw new InvalidDataException("The installed plugin does not match this store release.");
            if (installed is not null && _jobs.Values.FirstOrDefault(j => j.Saved.LibraryOnly && j.Snapshot.Stage == "completed"
                && j.Saved.SourceId == source.Id && j.Saved.SourceIdentity == identity && j.Saved.Package.Id == package.Id
                && j.Saved.Package.Version == package.Version && j.Saved.Package.ManifestSha256 == package.ManifestSha256) is { } completed)
                return DisplaySnapshot(completed);
            if (_closing) throw new InvalidOperationException("The download service is stopping.");
            var taskId = Guid.NewGuid().ToString("N");
            var snapshot = new ClientDownloadTask(taskId, "live",
                new("plugin", game.Id, build.Id, "plugin-library", package.Name["en"], previousVersion, package.Version, source.Name, source.Kind, PluginTaskFolder(taskId)),
                installed is null ? "queued" : "completed", files.Select(f => new ClientDownloadFile(f.Path, f.Size, installed is null ? 0 : f.Size, installed is null ? "queued" : "verified")).ToArray(), null, 0, 0, installed is null ? null : 100, null, "downloading");
            var job = new Job(new SavedDownload(1, source.Id, identity, package, manifest.ToArray(), snapshot, installed is null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null) { LibraryOnly = true }, files);
            Save(job); _jobs.Add(taskId, job); Schedule(); return DisplaySnapshot(job);
        }
    }

    /// <summary>Queues a source-pinned server release in a separate content-addressed package directory without creating a client or starting a server.</summary>
    public ClientDownloadTask StartServer(StoreSource source, StorePackage package, byte[] manifest)
    {
        if (package.Kind != "server" || _serverPackages is null) throw new InvalidOperationException("Server package installation is unavailable.");
        var files = ClientRelease.Parse(package, manifest);
        var identity = StoreSourceIdentity.Of(source);
        var current = _findSource(source.Id);
        if (current is not { Enabled: true, BaseUrl: not null } || StoreSourceIdentity.Of(current) != identity)
            throw new InvalidOperationException("The source changed or was disabled. Refresh the store first.");
        var targetId = ServerPackageLibrary.Identity(source.Id, identity, package);
        var folder = _serverPackages.Folder(source.Id, identity, package);
        lock (_gate)
        {
            CheckStorage();
            if (_closing) throw new InvalidOperationException("The download service is stopping.");
            if (_jobs.Values.Any(j => j.Saved.Package.Kind == "server" && j.Snapshot.Target.InstanceId == targetId && (j.Running || j.Snapshot.Stage is not ("completed" or "canceled"))))
                throw new InvalidOperationException("This server package already has a download task.");
            var previous = ClientInstallTransaction.ReadReceipt(folder);
            var snapshot = new ClientDownloadTask(Guid.NewGuid().ToString("N"), "live",
                new("server", package.Game, "", targetId, package.Name["en"], previous?.Version ?? "", package.Version, source.Name, source.Kind, folder),
                "queued", files.Select(f => new ClientDownloadFile(f.Path, f.Size, 0, "queued")).ToArray(), null, 0, 0, null, null, "downloading");
            var job = new Job(new(1, source.Id, identity, package, manifest.ToArray(), snapshot, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, previous), files);
            Save(job); _jobs.Add(snapshot.Id, job); Schedule(); return DisplaySnapshot(job);
        }
    }

    /// <summary>Pauses queued or transferring work, resumes it in FIFO order, or cancels before installation begins.</summary>
    public async Task ActionAsync(string id, string action, string? archivePassword = null)
    {
        Task? worker = null;
        lock (_gate)
        {
            CheckStorage();
            if (_closing) throw new InvalidOperationException("Downloads are shutting down.");
            var job = _jobs.GetValueOrDefault(id) ?? throw new ArgumentException("Unknown download task.");
            var stage = job.Snapshot.Stage;
            if (action == "pause" && stage is "queued" or "checking" or "downloading" or "verifying" || action == "cancel" && stage is "queued" or "checking" or "downloading" or "verifying" or "paused" or "failed")
            {
                job.Interrupted = action == "cancel" ? "canceled" : "paused";
                job.Saved = job.Saved with { ResumeOnRestart = false };
                // Persist the user's intent before cancellation can interrupt a network callback.
                job.Snapshot = job.Snapshot with { Stage = job.Interrupted, BytesPerSecond = 0 };
                Save(job); job.Cancellation.Cancel(); worker = job.Worker;
            }
            else if (action == "resume" && stage == "paused" || action == "retry" && stage == "failed")
            {
                if (job.Running) throw new InvalidOperationException("The previous transfer is still stopping.");
                if (archivePassword is { Length: > 256 } || archivePassword?.Any(char.IsControl) == true)
                    throw new ArgumentException("Archive password exceeds its limit.");
                if (archivePassword is not null) job.ArchivePassword = archivePassword;
                job.Saved = job.Saved with { ResumeOnRestart = true, QueuedAt = DateTimeOffset.UtcNow };
                job.Snapshot = job.Snapshot with { Stage = "queued", Failure = null, BytesPerSecond = 0 };
                Save(job); Schedule();
            }
            else throw new InvalidOperationException("This action is unavailable during the current download stage.");
        }
        if (worker is not null) await worker.ConfigureAwait(false);
    }

    /// <summary>Removes only completed or canceled history from memory and disk; active tasks and partial files stay intact.</summary>
    public void ClearFinished()
    {
        lock (_gate)
        {
            CheckStorage();
            foreach (var id in _jobs.Where(p => !p.Value.Running && p.Value.Snapshot.Stage is "completed" or "canceled").Select(p => p.Key).ToArray())
            {
                if (_jobs[id].Saved.Package.Kind == "plugin") CleanupPluginTask(id);
                _persistence.Delete(id); _jobs[id].Dispose(); _jobs.Remove(id);
            }
        }
    }

    private void Schedule()
    {
        if (_closing || _storageError is not null) return;
        var capacity = 2 - _jobs.Values.Count(j => j.Running);
        foreach (var job in _jobs.Values.Where(j => !j.Running && j.Snapshot.Stage == "queued").OrderBy(j => j.Saved.QueuedAt))
        {
            if (capacity == 0) break;
            if (_jobs.Values.Any(other => other.Running && other.Snapshot.Target.InstanceId == job.Snapshot.Target.InstanceId
                && (other.Saved.Package.Kind == "server") == (job.Saved.Package.Kind == "server"))) continue;
            job.Cancellation.Dispose(); job.Cancellation = new(); job.Interrupted = "paused";
            job.Snapshot = job.Snapshot with { Stage = "checking", Failure = null, BytesPerSecond = 0, SpeedSampleCount = 0, StageProgress = null };
            Save(job); job.Running = true; job.Worker = Task.Run(() => RunAsync(job));
            capacity--;
        }
    }

    private IDownloadSource Source(Job job)
    {
        var source = _findSource(job.Saved.SourceId);
        if (source is not { Enabled: true, BaseUrl: not null } || StoreSourceIdentity.Of(source) != job.Saved.SourceIdentity)
            throw new SourceUnavailableException();
        return job.Saved.Package.GitHubAsset is { } asset ? GitHubStoreReleases.Open(asset) : _openSource(source);
    }
    private sealed class SourceUnavailableException : Exception;
    private static bool Expected(Exception error) => error is IOException or InvalidDataException or ArchivePasswordRequiredException or HttpRequestException or OperationCanceledException or
        UnauthorizedAccessException or ArgumentException or FormatException or System.Text.Json.JsonException or DefinitionException or SourceUnavailableException or InvalidOperationException;
    private void CheckStorage() { if (_storageError is not null) throw new IOException(_storageError); }
    private void Save(Job job)
    {
        try { _persistence.Save(job.Saved); job.LastSave = Environment.TickCount64; }
        catch (Exception error) when (Expected(error))
        {
            _storageError = "Download history could not be saved. Check free disk space and folder permissions, then restart the launcher.";
            foreach (var active in _jobs.Values) active.Cancellation.Cancel();
            throw new IOException(_storageError, error);
        }
    }

    private async Task RunAsync(Job job)
    {
        var ct = job.Cancellation.Token; var client = job.Snapshot.Target.Directory;
        try
        {
            ct.ThrowIfCancellationRequested();
            using var lease = AcquireJob(job);
            var pluginTask = job.Saved.Package.Kind == "plugin";
            if (job.Saved.Package.Kind == "client" && InstanceRecovery.Required(client))
            {
                if (_game is null) throw new IOException("Recover the interrupted client resource transaction before repairing this client.");
                InstanceRecovery.RecoverLeased(store, job.Snapshot.Target.InstanceId, _game(job.Saved.Package.Game));
            }
            if (job.Saved.Package.Kind == "client" && _game is not null)
                _game(job.Saved.Package.Game).Builds.Single(build => build.Id == job.Snapshot.Target.BuildId).RequireEnabled();
            if (pluginTask && (_plugins is null || _game is null)) throw new InvalidOperationException("Plugin installation services are unavailable.");
            if (!pluginTask && File.Exists(Path.Combine(client, "session.toml"))) throw new IOException("A client session is still present.");
            if (!pluginTask) await ClientInstallTransaction.RecoverAsync(client).ConfigureAwait(false);
            var installedReceipt = pluginTask ? null : ClientInstallTransaction.ReadReceipt(client);
            if (installedReceipt != job.Saved.PreviousReceipt && installedReceipt != job.Receipt)
                throw new InvalidDataException("The installed release changed while this task was queued.");
            _ = Source(job);
            var workspace = ClientInstallTransaction.Workspace(client); Directory.CreateDirectory(workspace);
            var retained = job.Files.Select(f => f.Sha256).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (job.Saved.Package.PluginArchive is { } offeredArchive) retained.Add(offeredArchive.Sha256);
            Prune(workspace, retained);
            var engine = new HttpDownloadEngine(http);
            var cache = new ClientFileCache(client);
            var changed = new List<ClientReleaseFile>();
            if (pluginTask) await PreparePluginArchiveAsync(job, engine, ct).ConfigureAwait(false);
            foreach (var file in job.Files)
            {
                ct.ThrowIfCancellationRequested();
                Change(job, s => s with { Stage = "checking", CurrentPath = file.Path, BytesPerSecond = 0, SpeedSampleCount = 0 });
                if (pluginTask || file.IsArchive || !await cache.MatchesAsync(file, ct).ConfigureAwait(false)) changed.Add(file);
            }
            Change(job, s => s with { Files = changed.Select(f => new ClientDownloadFile(f.Path, f.Size, 0, "queued")).ToArray(), CurrentPath = null });
            for (var i = 0; i < changed.Count; i++)
            {
                ct.ThrowIfCancellationRequested(); var index = i; var file = changed[i];
                Change(job, s => s with { Stage = "checking", CurrentPath = file.Path, BytesPerSecond = 0, SpeedSampleCount = 0 });
                _ = Source(job);
                var staged = ClientInstallTransaction.Staged(client, file.Sha256);
                // Content-addressed files survive pause/restart and may serve several
                // release paths. Recheck their bytes before avoiding an expiring URL lookup.
                if (await HttpDownloadEngine.MatchesAsync(staged, file.Size, file.Sha256, ct).ConfigureAwait(false))
                {
                    Change(job, s => s with { Files = Replace(s.Files, index, new(file.Path, file.Size, file.Size, "verified")), BytesPerSecond = 0 });
                    continue;
                }
                // Resolve every attempt anew: signed storage URLs may expire while a task is paused.
                var resolved = await Source(job).ResolveAsync(file.SourcePath, ct).ConfigureAwait(false);
                await engine.DownloadAsync(new(resolved, staged, file.Size, file.Sha256),
                    new InlineProgress(p => Change(job, s => s with
                    {
                        Stage = p.Received == p.Total ? "verifying" : "downloading", BytesPerSecond = p.BytesPerSecond,
                        SpeedSampleCount = p.BytesPerSecond > 0 ? s.SpeedSampleCount + 1 : 0,
                        Files = Replace(s.Files, index, new(file.Path, file.Size, p.Received, p.Received == p.Total ? "verifying" : "downloading"))
                    })), ct).ConfigureAwait(false);
                Change(job, s => s with { Files = Replace(s.Files, index, new(file.Path, file.Size, file.Size, "verified")), BytesPerSecond = 0 });
            }
            lock (_gate)
            {
                ct.ThrowIfCancellationRequested();
                _ = Source(job);
                job.Snapshot = job.Snapshot with { Stage = "installing", BytesPerSecond = 0, StageProgress = 0 }; Save(job);
            }
            IReadOnlyList<ClientReleaseFile> installFiles = job.Files;
            var archiveFiles = job.Files.Where(file => file.IsArchive).ToArray();
            if (archiveFiles.Length != 0)
            {
                if (pluginTask) throw new InvalidDataException("Archive transport is not supported for plugin payloads.");
                var archivePassword = job.ArchivePassword ?? archiveFiles[0].ArchivePassword;
                installFiles = await ClientArchiveExtractor.ExtractAsync(client, archiveFiles, archivePassword,
                    value => Change(job, s => s with { StageProgress = value * 70 / 100 }), ct).ConfigureAwait(false);
                if (_game is not null && job.Saved.Package.Kind == "client")
                {
                    var build = _game(job.Saved.Package.Game).Builds.Single(b => b.Id == job.Snapshot.Target.BuildId);
                    installFiles = ArchiveInstallationRoot.Find(installFiles, new[] { build.Executable }.Concat(build.ExecutableAliases));
                }
                else if (job.Saved.Package.Kind == "server" && archiveFiles[0].ArchiveExecutable is { } executable)
                    installFiles = ArchiveInstallationRoot.Find(installFiles, [executable]);
                _ = Source(job);
                ct.ThrowIfCancellationRequested();
            }
            if (pluginTask)
            {
                var payload = ClientInstallTransaction.Safe(client, "payload");
                if (Directory.Exists(payload))
                {
                    // Only this task's bounded, verified file set may be cleaned on retry.
                    foreach (var file in job.Files) File.Delete(ClientInstallTransaction.Safe(payload, file.Path));
                }
                Directory.CreateDirectory(payload);
                for (var i = 0; i < job.Files.Count; i++)
                {
                    var file = job.Files[i]; var destination = ClientInstallTransaction.Safe(payload, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(ClientInstallTransaction.Staged(client, file.Sha256), destination, overwrite: false);
                    Change(job, s => s with { StageProgress = (i + 1) * 90 / job.Files.Count });
                }
                var game = _game!(job.Snapshot.Target.GameId); var build = game.Builds.Single(b => b.Id == job.Snapshot.Target.BuildId);
                _plugins!.InstallFromStore(payload, game, build, job.Saved.Package, job.Saved.Manifest, job.Saved.SourceId, job.Saved.SourceIdentity);
                if (job.Saved.ActivationTargets is { Count: > 0 } targets)
                {
                    foreach (var targetId in targets)
                    {
                        var targetGame = _game!(job.Saved.Package.Game);
                        var expected = job.Saved.ActivationSelections?.GetValueOrDefault(targetId);
                        if (targetId == job.Snapshot.Target.InstanceId)
                        {
                            var current = store.Load(targetId);
                            if (expected is not null && !ClientPolicyActivation.SelectionMatches(current, job.Saved.Package.Id, expected)) continue;
                            ClientPolicyActivation.EnableLeased(store, targetId, targetGame, _plugins.Packages(targetGame), job.Saved.Package.Id,
                                job.Saved.Package.Version, ClientPolicyActivation.SelectionStamp(current, job.Saved.Package.Id), explicitSelection: true);
                        }
                        else ClientPolicyActivation.EnableExplicit(store, targetId, targetGame, _plugins.Packages(targetGame), job.Saved.Package.Id, job.Saved.Package.Version, expected);
                    }
                }
                else if (job.Saved.RecommendationSelection is { } selection)
                    ClientPolicyActivation.EnableLeased(store, job.Snapshot.Target.InstanceId, game, _plugins.Packages(game),
                        job.Saved.Package.Id, job.Saved.Package.Version, selection, serverRecommended: job.Saved.ServerRecommended);
                else if (job.Saved.UpgradeSelection is { } upgrade)
                    ClientPolicyActivation.EnableLeased(store, job.Snapshot.Target.InstanceId, game, _plugins.Packages(game),
                        job.Saved.Package.Id, job.Saved.Package.Version, upgrade, upgrade: true);
                foreach (var file in job.Files) File.Delete(ClientInstallTransaction.Safe(payload, file.Path));
            }
            else
            {
                await ClientInstallTransaction.ApplyAsync(client, installFiles,
                    value => Change(job, s => s with { StageProgress = archiveFiles.Length == 0 ? value : 70 + value * 30 / 100 }),
                    CancellationToken.None, job.Receipt, cache).ConfigureAwait(false);
                cache.RecordInstalled(installFiles);
                if (job.Saved.Package.Kind == "server")
                    _serverPackages!.RecordInstalled(job.Saved.SourceId, job.Saved.SourceIdentity, job.Saved.Package, job.Saved.Manifest);
            }
            Prune(workspace, []);
            // Completion observers may immediately request another instance mutation.
            lease.Dispose();
            Change(job, s => s with { Stage = "completed", StageProgress = 100, CurrentPath = null });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { Change(job, s => s with { Stage = job.Interrupted, BytesPerSecond = 0 }); }
        catch (Exception error) when (Expected(error))
        {
            var failure = error is SourceUnavailableException ? "source" : error is ArchivePasswordRequiredException ? "archive-password" : error is InvalidDataException or DefinitionException ? "integrity" : error is HttpRequestException or OperationCanceledException ? "network" : StorageFailure.Code(error);
            Change(job, s => s with { Stage = "failed", Failure = failure, BytesPerSecond = 0 });
        }
        finally
        {
            lock (_gate)
            {
                job.ArchivePassword = null;
                job.Running = false;
                if (job.Snapshot.Stage is "completed" or "canceled" or "failed") job.Saved = job.Saved with { ResumeOnRestart = false };
                if (_storageError is null)
                {
                    try { Save(job); Schedule(); }
                    catch (IOException) { /* Save exposes the persistent failure through List and stops all workers. */ }
                }
            }
        }
    }

    private FileStream AcquireJob(Job job)
    {
        if (job.Saved.LibraryOnly)
        {
            if (job.Snapshot.Target.Directory != PluginTaskFolder(job.Snapshot.Id) || job.Saved.RecommendationSelection is not null || job.Saved.UpgradeSelection is not null)
                throw new InvalidDataException("Plugin library download identity changed.");
            Directory.CreateDirectory(job.Snapshot.Target.Directory);
            var path = Path.Combine(job.Snapshot.Target.Directory, ".download.lock"); InstanceStore.RejectLinks(path);
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        if (job.Saved.Package.Kind == "server")
        {
            if (_serverPackages is null || job.Snapshot.Target.InstanceId != ServerPackageLibrary.Identity(job.Saved.SourceId, job.Saved.SourceIdentity, job.Saved.Package) ||
                job.Snapshot.Target.Directory != _serverPackages.Folder(job.Saved.SourceId, job.Saved.SourceIdentity, job.Saved.Package))
                throw new InvalidDataException("Server package installation identity or location changed.");
            return _serverPackages.Acquire(job.Saved.SourceId, job.Saved.SourceIdentity, job.Saved.Package);
        }
        var lease = store.Acquire(job.Snapshot.Target.InstanceId);
        try
        {
            var instance = store.Load(job.Snapshot.Target.InstanceId);
            if (instance.Game != job.Snapshot.Target.GameId || instance.Build != job.Snapshot.Target.BuildId ||
                (job.Saved.Package.Kind == "plugin" ? PluginTaskFolder(job.Snapshot.Id) : store.ClientFolder(instance.Id)) != job.Snapshot.Target.Directory)
                throw new IOException("The installation identity or location changed.");
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    private string PluginTaskFolder(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid plugin task identity.");
        return ClientInstallTransaction.Safe(_pluginTasksRoot, id);
    }

    private void CleanupPluginTask(string id)
    {
        var root = PluginTaskFolder(id);
        if (!Directory.Exists(root)) return;
        var pending = new Queue<string>(); pending.Enqueue(root);
        while (pending.TryDequeue(out var folder))
        {
            InstanceStore.RejectLinks(folder);
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                InstanceStore.RejectLinks(path);
                if (Directory.Exists(path)) pending.Enqueue(path);
            }
        }
        Directory.Delete(root, recursive: true);
    }

    private static void Prune(string workspace, HashSet<string> keep)
    {
        var entries = Directory.EnumerateFileSystemEntries(workspace);
        foreach (var path in entries)
        {
            InstanceStore.RejectLinks(path); var name = Path.GetFileName(path);
            if (name.Length >= 73 && name[..64].All(char.IsAsciiHexDigit) &&
                (name[64..] == ".verified" || name.Length == 143 && name[64..74] == ".verified." && name[74..138].All(char.IsAsciiHexDigit) && name[138..] == ".part") && !keep.Contains(name[..64]))
                File.Delete(path);
        }
    }
    private void Change(Job job, Func<ClientDownloadTask, ClientDownloadTask> change)
    {
        lock (_gate)
        {
            var previous = job.Snapshot;
            job.Snapshot = change(previous);
            if (job.Cancellation.IsCancellationRequested && job.Snapshot.Stage is "checking" or "downloading" or "verifying")
                job.Snapshot = job.Snapshot with { Stage = job.Interrupted, BytesPerSecond = 0 };
            // Per-file transfer stages share the progress checkpoint interval; flushing the full
            // manifest at every checking/downloading/verifying boundary makes large inventories quadratic.
            var phaseChanged = previous.Stage != job.Snapshot.Stage &&
                (!IsTransferStage(previous.Stage) || !IsTransferStage(job.Snapshot.Stage));
            if (_storageError is null && (phaseChanged || Environment.TickCount64 - job.LastSave >= 2000)) Save(job);
        }
    }
    private static bool IsTransferStage(string stage) => stage is "checking" or "downloading" or "verifying";
    private static ClientDownloadFile[] Replace(IReadOnlyList<ClientDownloadFile> files, int index, ClientDownloadFile value)
    { var result = files.ToArray(); result[index] = value; return result; }
    private sealed class InlineProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    { public void Report(DownloadProgress value) => report(value); }

    /// <summary>Stops network work, retains partials and waits for any active installation transaction to finish.</summary>
    public async ValueTask DisposeAsync()
    {
        Task[] workers;
        lock (_gate)
        {
            if (_closing) { workers = _jobs.Values.Select(j => j.Worker).ToArray(); }
            else
            {
                _closing = true;
                foreach (var job in _jobs.Values.Where(j => j.Running))
                {
                    if (job.Saved.ResumeOnRestart) job.Interrupted = "queued";
                    job.Cancellation.Cancel();
                }
                workers = _jobs.Values.Select(j => j.Worker).ToArray();
            }
        }
        await Task.WhenAll(workers).ConfigureAwait(false);
        lock (_gate) foreach (var job in _jobs.Values) job.Dispose();
    }
}
