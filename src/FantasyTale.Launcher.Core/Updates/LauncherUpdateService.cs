using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Updates;

/// <summary>Discovery status without exposing temporary source URLs.</summary>
/// <param name="Status">unconfigured, current, checking, available, downloading, ready or failed.</param>
/// <param name="Version">Optional selected display label.</param>
/// <param name="Revision">Source-bound monotonic release number.</param>
/// <param name="ManifestSha256">Selection identity required when starting a download.</param>
/// <param name="Failure">Safe integrity, network, disk or rolled-back startup failure code.</param>
/// <param name="FailureReason">Allowlisted discovery reason for plain-language guidance, without server replies or private URLs.</param>
/// <param name="HttpStatus">Optional HTTP or source API status for expandable diagnostics.</param>
public sealed record LauncherUpdateState(string Status, string? Version = null, long? Revision = null, string? ManifestSha256 = null, string? Failure = null,
    string? FailureReason = null, int? HttpStatus = null)
{
    /// <summary>Whether this installation must update before continuing normal use.</summary>
    public bool Mandatory { get; init; }
    /// <summary>Inclusive version boundary supplied by the verified release policy.</summary>
    public string? DeprecatedBeforeVersion { get; init; }
}

/// <summary>Self-update automation never restarts the application without an explicit user action.</summary>
/// <param name="AutoCheck">Repeat discovery every thirty minutes; startup always checks once.</param>
/// <param name="AutoDownload">Stage verified files automatically; installation still requires Restart.</param>
public sealed record LauncherUpdatePreferences(bool AutoCheck = true, bool AutoDownload = true);

/// <summary>Source-bound discovery and resumable staging; only the external updater may replace running application files.</summary>
public sealed class LauncherUpdateService : IAsyncDisposable
{
    private sealed record SeenRelease(long Revision, string Hash);
    private sealed record Seen(string Identity, long Revision, string Hash, IReadOnlyDictionary<string, SeenRelease>? Sources = null)
    {
        internal SeenRelease? ForSource(string identity) => Sources?.GetValueOrDefault(identity)
            ?? (Identity == identity ? new(Revision, Hash) : null);
    }
    private sealed record Candidate(LauncherUpdatePolicy Policy, LauncherRelease Release, byte[] Index, byte[]? Signature = null);
    private sealed record Intent(string Hash, string Stage);
    private readonly string _installation;
    private readonly string _root;
    private readonly HttpClient _http;
    private readonly ClientFileCache _fileCache;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _transfer;
    private Task _worker = Task.CompletedTask;
    private bool _transferRunning;
    private LauncherUpdateState _snapshot = new("unconfigured");
    private LauncherRelease? _requiredRelease;
    private readonly string _currentVersion;
    private LauncherUpdateState _state
    {
        get => _requiredRelease is { } required ? _snapshot with
        {
            Mandatory = true, DeprecatedBeforeVersion = required.DeprecatedBeforeVersion,
            Version = required.Version, Revision = required.Revision, ManifestSha256 = required.ManifestSha256,
            Status = _snapshot.Status is "current" or "unconfigured" ? "available" : _snapshot.Status
        } : _snapshot;
        set => _snapshot = value;
    }
    private IReadOnlyList<LauncherUpdateSourceResult> _sourceResults = [];
    private LauncherUpdateAttempt? _lastAttempt;
    private ClientDownloadTask? _task;
    private IReadOnlyList<ClientDownloadFile> _incrementalFiles = [];
    private LauncherRelease? _release;
    private LauncherUpdatePolicy? _policy;
    private string _interrupted = "paused";
    private string? _canceledHash;
    private bool _closing;
    private Task? _monitor;
    private Task<LauncherUpdateState>? _startupCheck;
    private LauncherUpdatePreferences _preferences = new();

    /// <summary>Binds update state to one installation while keeping transfers outside its running binaries.</summary>
    public LauncherUpdateService(string installation, string dataRoot, HttpClient http, string? currentVersion = null)
    {
        _currentVersion = currentVersion ?? LauncherCompatibility.CurrentVersion;
        _ = PluginVersion.Parse(_currentVersion);
        _installation = Path.GetFullPath(installation); _root = UpdateFiles.Safe(dataRoot, "launcher-update"); _http = http;
        _fileCache = new ClientFileCache(_installation);
        var path = UpdateFiles.Safe(_root, "preferences.toml");
        if (File.Exists(path))
        {
            var config = TomlReader.Load(path); config.OnlyKeys("auto_check", "auto_download");
            _preferences = new(config.Boolean("auto_check", true), config.Boolean("auto_download", true));
        }
    }

    /// <summary>Reads saved self-update choices independently of game updates.</summary>
    public LauncherUpdatePreferences Preferences { get { lock (_gate) return _preferences; } }
    /// <summary>Returns ordered sources, including verification settings needed to preserve their authority when editing.</summary>
    public IReadOnlyList<LauncherUpdateSourceSettings> Sources() => LauncherUpdatePolicy.LoadAll(_installation)
        .Select(source => new LauncherUpdateSourceSettings(source.Name, source.Kind, source.BaseUrl.AbsoluteUri, source.FeedPath,
            source.MinimumRevision, source.SignaturePath, source.SignaturePublicKey) { PreferIp = source.PreferIp }).ToArray();

    /// <summary>Validates and atomically replaces source settings between operations, preserving active transfers and baseline revisions.</summary>
    public async Task<IReadOnlyList<LauncherUpdateSourceSettings>> SaveSourcesAsync(IReadOnlyList<LauncherUpdateSourceSettings> sources, CancellationToken ct = default)
    {
        await _operations.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_gate)
                if (_requiredRelease is not null) throw new InvalidOperationException("Install the required launcher update before editing update sources.");
            lock (_gate)
                if (_task is { Stage: not ("completed" or "canceled") })
                    throw new InvalidOperationException("Finish or cancel the launcher update before editing its sources.");
            var baseline = LauncherUpdatePolicy.LoadAll(_installation).Select(source => source.MinimumRevision).DefaultIfEmpty(0).Max();
            if (sources.Any(source => source is null || source.MinimumRevision < 0)) throw new ArgumentException("Invalid update source baseline.");
            LauncherUpdatePolicy.SaveAll(_installation, sources.Select(source => source with { MinimumRevision = Math.Max(baseline, source.MinimumRevision) }).ToArray());
            lock (_gate) { _release = null; _policy = null; _sourceResults = []; _lastAttempt = null; _state = new("unconfigured"); }
            return Sources();
        }
        finally { _operations.Release(); }
    }
    /// <summary>Saves automation choices independently of client update preferences.</summary>
    public void SavePreferences(LauncherUpdatePreferences value)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_root); var path = UpdateFiles.Safe(_root, "preferences.toml"); var temp = UpdateFiles.Safe(_root, "preferences.tmp");
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { output.Write(Encoding.UTF8.GetBytes($"auto_check = {value.AutoCheck.ToString().ToLowerInvariant()}\nauto_download = {value.AutoDownload.ToString().ToLowerInvariant()}\n")); output.Flush(true); }
            File.Move(temp, path, true); _preferences = value;
        }
    }
    /// <summary>Begins discovery immediately so it can run while the native window and frontend initialize.</summary>
    public Task<LauncherUpdateState> BeginStartupCheck()
    {
        lock (_gate) return _startupCheck ??= Task.Run(async () =>
        {
            // Restore the selected snapshot before discovery can replace its saved index.
            RestoreRequiredRelease();
            await RestoreTransferAsync(_stop.Token).ConfigureAwait(false);
            return await CheckAsync(_stop.Token).ConfigureAwait(false);
        });
    }

    /// <summary>Starts a single lifetime-owned monitor after the normal or updated frontend is healthy.</summary>
    public void StartMonitoring() { lock (_gate) _monitor ??= Task.Run(MonitorAsync); }
    private async Task MonitorAsync()
    {
        try
        {
            var initial = BeginStartupCheck();
            var startup = true;
            while (!_stop.IsCancellationRequested)
            {
                if (startup || Preferences.AutoCheck)
                {
                    var state = startup
                        ? await initial.ConfigureAwait(false)
                        : await CheckAsync(_stop.Token).ConfigureAwait(false);
                    if (state.Status == "available" && Preferences.AutoDownload)
                    {
                        try { await DownloadAsync(state.ManifestSha256!, true, _stop.Token).ConfigureAwait(false); }
                        catch (InvalidOperationException) { /* A manual action may already have scheduled this release. */ }
                    }
                }
                startup = false;
                await Task.Delay(TimeSpan.FromMinutes(30), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (_stop.IsCancellationRequested && Expected(error))
        {
            // A transport racing disposal may report HttpRequestException instead of cancellation.
            // The saved update state remains intact; shutdown still drains transfers below.
        }
    }

    /// <summary>Restores a saved transfer only when this installation has configured update sources.</summary>
    internal async Task RestoreTransferAsync(CancellationToken ct)
    {
        await _operations.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_gate) if (_task is not null) return;
            var policies = LauncherUpdatePolicy.LoadAll(_installation);
            // Shared local data can outlive a distribution that configured automatic updates.
            if (policies.Count == 0) return;
            var intent = UpdateFiles.Read<Intent>(UpdateFiles.Safe(_root, "transfer.json"));
            if (intent is null) return;
            if (intent.Stage == "canceled")
            {
                if (intent.Hash is not { Length: 64 } || !intent.Hash.All(char.IsAsciiHexDigit)) throw new InvalidDataException("Invalid canceled launcher transfer.");
                lock (_gate) _canceledHash = intent.Hash;
                return;
            }
            if (intent.Stage is not ("queued" or "paused" or "ready") || intent.Hash is not { Length: 64 }) throw new InvalidDataException("Invalid saved launcher transfer.");
            var seen = UpdateFiles.Read<Seen>(UpdateFiles.Safe(_root, "seen.json"));
            var policy = policies.SingleOrDefault(item => item.Identity == seen?.Identity)
                ?? throw new InvalidDataException("Saved update source is unavailable."); policy.ValidateRuntime();
            var release = policy.ReadStagedRelease(_root);
            if (release.ManifestSha256 != intent.Hash || seen is null || seen.Identity != policy.Identity || seen.Revision != release.Revision || seen.Hash != intent.Hash)
                throw new InvalidDataException("Saved update does not match its saved selection.");
            if (release.Revision <= Math.Max(policy.MinimumRevision, ClientInstallTransaction.ReadReceipt(_installation)?.Revision ?? 0))
            { UpdateFiles.Write(UpdateFiles.Safe(_root, "transfer.json"), intent with { Stage = "canceled" }); return; }
            var changedPaths = await FindChangedPathsAsync(release, ct).ConfigureAwait(false);
            var lastAttempt = ReadLastAttempt(policy, release);
            lock (_gate)
            {
                _lastAttempt = lastAttempt;
                _policy = policy; _release = release; _task = NewTask(policy, release, changedPaths);
                RememberRequirement(release);
                _incrementalFiles = _task.Files;
                _state = new("downloading", release.Version, release.Revision, release.ManifestSha256);
                if (WasRolledBack(policy, release)) { ReportRollback(policy, release); return; }
                if (intent.Stage == "paused") _task = _task with { Stage = "paused" };
                else StartWorker();
            }
        }
        catch (Exception error) when (Expected(error) && (error is not OperationCanceledException || !ct.IsCancellationRequested))
        { lock (_gate) _state = CheckFailure(error); }
        finally { _operations.Release(); }
    }

    /// <summary>Returns the last discovery state; it performs no network access.</summary>
    public LauncherUpdateState State { get { lock (_gate) return _state; } }
    /// <summary>Returns cached, source-bound installation diagnostics without reading files or accessing the network.</summary>
    public LauncherUpdateAttempt? LastAttempt { get { lock (_gate) return _lastAttempt; } }
    /// <summary>Returns outcomes for sources attempted in priority order during the latest discovery.</summary>
    public IReadOnlyList<LauncherUpdateSourceResult> SourceResults { get { lock (_gate) return _sourceResults; } }
    /// <summary>Returns the single live task using the same progress contract as client and plugin downloads.</summary>
    public IReadOnlyList<ClientDownloadTask> List() { lock (_gate) return _task is null ? [] : [_task]; }

    /// <summary>Checks sources in configured order and stops at the first valid release manifest.</summary>
    public async Task<LauncherUpdateState> CheckAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token); ct = linked.Token;
        await _operations.WaitAsync(ct).ConfigureAwait(false);
        LauncherUpdateState previousState; IReadOnlyList<LauncherUpdateSourceResult> previousResults; LauncherUpdateAttempt? previousAttempt;
        lock (_gate) { previousState = _state; previousResults = _sourceResults; previousAttempt = _lastAttempt; }
        try
        {
            lock (_gate) { if (_task is { Stage: not ("completed" or "canceled"), Failure: not "startup" }) return _state; _state = new("checking"); }
            var policies = LauncherUpdatePolicy.LoadAll(_installation);
            lock (_gate) { _sourceResults = []; _lastAttempt = null; }
            if (policies.Count == 0) { lock (_gate) return _state = new("unconfigured"); }
            var seenPath = UpdateFiles.Safe(_root, "seen.json"); var seen = UpdateFiles.Read<Seen>(seenPath);
            var receipt = ClientInstallTransaction.ReadReceipt(_installation);
            Exception? firstFailure = null;
            LauncherUpdateState? firstRejection = null;
            Candidate? selected = null;
            for (var position = 0; position < policies.Count; position++)
            {
                var sourcePolicy = policies[position];
                lock (_gate) _sourceResults = [.. _sourceResults, new LauncherUpdateSourceResult(sourcePolicy.Name, new("checking"))];
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    // Resolution and the index transfer share the metadata reader's thirty-second budget.
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    sourcePolicy.ValidateRuntime();
                    var index = await SourceDocumentReader.ReadBytesAsync(_http, sourcePolicy.OpenSource(_http), sourcePolicy.FeedPath, timeout.Token).ConfigureAwait(false);
                    byte[]? signature = null;
                    if (sourcePolicy.SignaturePath is not null)
                    {
                        signature = await SourceDocumentReader.ReadBytesAsync(_http, sourcePolicy.OpenSource(_http), sourcePolicy.SignaturePath, timeout.Token).ConfigureAwait(false);
                        sourcePolicy.VerifySignature(index, signature);
                    }
                    var candidate = new Candidate(sourcePolicy, LauncherRelease.Parse(sourcePolicy, index), index, signature);
                    if (CandidateRejection(candidate, seen, receipt) is { } problem)
                    {
                        firstRejection ??= problem;
                        SourceResult(position, new(sourcePolicy.Name, problem));
                        continue;
                    }
                    var candidateRelease = candidate.Release;
                    SourceResult(position, new(sourcePolicy.Name, new(candidateRelease.Revision > Math.Max(sourcePolicy.MinimumRevision, receipt?.Revision ?? 0) ? "available" : "current", candidateRelease.Version, candidateRelease.Revision, candidateRelease.ManifestSha256)));
                    selected = candidate;
                    break;
                }
                catch (Exception error) when (Expected(error) && !ct.IsCancellationRequested)
                {
                    firstFailure ??= error;
                    SourceResult(position, new(sourcePolicy.Name, CheckFailure(error)));
                }
            }
            if (selected is null)
            {
                lock (_gate) return SetState(firstRejection ?? (firstFailure is not null ? CheckFailure(firstFailure) : new("failed", Failure: "sourceUnavailable")));
            }
            var policy = selected.Policy; var release = selected.Release; var indexBytes = selected.Index;
            var baseline = Math.Max(policy.MinimumRevision, receipt?.Revision ?? 0);
            // Fallback changes the selected source without erasing any source's accepted revision.
            var history = seen?.Sources is { } previousSources
                ? new Dictionary<string, SeenRelease>(previousSources, StringComparer.Ordinal)
                : new Dictionary<string, SeenRelease>(StringComparer.Ordinal);
            if (seen is not null) history.TryAdd(seen.Identity, new(seen.Revision, seen.Hash));
            history[policy.Identity] = new(release.Revision, release.ManifestSha256);
            UpdateFiles.Write(seenPath, new Seen(policy.Identity, release.Revision, release.ManifestSha256, history));
            policy.SaveSelection(_root);
            Directory.CreateDirectory(_root);
            // A partial snapshot fails closed; restore and the detached helper verify the same saved bytes.
            var signaturePath = UpdateFiles.Safe(_root, "release.toml.sig");
            if (selected.Signature is { } signatureBytes) UpdateFiles.WriteBytes(signaturePath, signatureBytes);
            else File.Delete(signaturePath);
            UpdateFiles.WriteBytes(UpdateFiles.Safe(_root, "release.toml"), indexBytes);
            var lastAttempt = ReadLastAttempt(policy, release);
            lock (_gate)
            {
                _lastAttempt = lastAttempt;
                _policy = policy; _release = release;
                RememberRequirement(release);
                if (release.Revision > baseline && WasRolledBack(policy, release)) return ReportRollback(policy, release);
                if (_task?.Failure == "startup") _task = null;
                _state = new(release.Revision > baseline ? "available" : "current", release.Version, release.Revision, release.ManifestSha256);
                return _state;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { lock (_gate) { _state = previousState; _sourceResults = previousResults; _lastAttempt = previousAttempt; } throw; }
        catch (Exception error) when (Expected(error) && !ct.IsCancellationRequested)
        { lock (_gate) return SetState(CheckFailure(error)); }
        finally { _operations.Release(); }
    }

    private void SourceResult(int position, LauncherUpdateSourceResult result)
    {
        lock (_gate)
        {
            var results = _sourceResults.ToArray(); results[position] = result; _sourceResults = results;
        }
    }

    private LauncherUpdateState SetState(LauncherUpdateState value) { _state = value; return _state; }

    private void RememberRequirement(LauncherRelease release)
    {
        if (release.RequiresUpdate(_currentVersion)) _requiredRelease = release;
        else if (_requiredRelease is not null) _requiredRelease = release;
    }

    private void RestoreRequiredRelease()
    {
        try
        {
            if (!File.Exists(UpdateFiles.Safe(_root, "release.toml"))) return;
            var policy = LauncherUpdatePolicy.LoadSelected(_installation, _root);
            if (policy is null) return;
            var release = policy.ReadStagedRelease(_root);
            var seen = UpdateFiles.Read<Seen>(UpdateFiles.Safe(_root, "seen.json"))?.ForSource(policy.Identity);
            if (seen is null || seen.Revision != release.Revision || seen.Hash != release.ManifestSha256) return;
            lock (_gate)
            {
                RememberRequirement(release);
                if (_requiredRelease is not null)
                {
                    _policy = policy; _release = release;
                    _state = new("available", release.Version, release.Revision, release.ManifestSha256);
                }
            }
        }
        catch (Exception error) when (Expected(error))
        { lock (_gate) _state = CheckFailure(error); }
    }

    private static LauncherUpdateState? CandidateRejection(Candidate candidate, Seen? seen, ClientInstallReceipt? receipt)
    {
        var policy = candidate.Policy; var release = candidate.Release;
        var previous = seen?.ForSource(policy.Identity);
        if (previous is not null &&
            (release.Revision < previous.Revision || release.Revision == previous.Revision && release.ManifestSha256 != previous.Hash))
            return new("failed", Failure: "integrity", FailureReason: release.Revision < previous.Revision ? "feedOutdated" : "feedChanged");
        var baseline = Math.Max(policy.MinimumRevision, receipt?.Revision ?? 0);
        if (release.Revision < baseline || receipt is not null && release.Revision == receipt.Revision && release.ManifestSha256 != receipt.ManifestSha256)
            return new("failed", Failure: "integrity", FailureReason: release.Revision < baseline ? "feedOutdated" : "feedChanged");
        return null;
    }

    /// <summary>Starts a selected release without changing installed files.</summary>
    public Task<ClientDownloadTask> DownloadAsync(string manifestSha256, CancellationToken ct = default) => DownloadAsync(manifestSha256, false, ct);

    private async Task<ClientDownloadTask> DownloadAsync(string manifestSha256, bool automatic, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _stop.Token); ct = linked.Token;
        await _operations.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            LauncherRelease release;
            lock (_gate)
            {
                if (_closing || (_state.Status != "available" && !(_state.Mandatory && _state.Status == "failed" && _state.Failure != "startup" && _task is null)) || _release is null || _policy is null || manifestSha256 != _release.ManifestSha256 || _transferRunning)
                    throw new InvalidOperationException("Check for launcher updates again before downloading.");
                if (automatic && _canceledHash == manifestSha256)
                    throw new InvalidOperationException("Automatic download was canceled for this launcher release.");
                release = _release;
            }
            // Version discovery reads metadata only; local payload comparison belongs to staging.
            var changedPaths = await FindChangedPathsAsync(release, ct).ConfigureAwait(false);
            lock (_gate)
            {
                ct.ThrowIfCancellationRequested();
                if (_closing) throw new InvalidOperationException("The launcher is closing.");
                _task = NewTask(_policy!, release, changedPaths);
                _incrementalFiles = _task.Files;
                StartWorker(); return _task;
            }
        }
        finally { _operations.Release(); }
    }

    /// <summary>Pauses, resumes or cancels the bounded transfer; restart uses the host's separate handoff gate.</summary>
    public async Task ActionAsync(string action)
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try
        {
            Task wait;
            lock (_gate)
            {
                if (_closing || _task is null) throw new InvalidOperationException("No launcher update is active.");
                if (_state.Mandatory && action is "pause" or "cancel")
                    throw new InvalidOperationException("This launcher update is required and cannot be paused or canceled.");
                if (action is "resume" or "retry" && _task.Failure == "startup")
                    throw new InvalidOperationException("This release failed startup and was rolled back. Check for a newer launcher update.");
                wait = _worker;
                if (action is "pause" or "cancel")
                {
                    _interrupted = action == "pause" ? "paused" : "canceled";
                    SaveIntent(_interrupted);
                    if (action == "cancel") _canceledHash = _release!.ManifestSha256;
                    _transfer?.Cancel();
                    _task = _task with { Stage = _interrupted, BytesPerSecond = 0 };
                    _state = _state with { Status = _task.Failure == "startup" ? "failed" : action == "cancel" ? "available" : "downloading" };
                }
                else if (action is "resume" or "retry" && _task.Stage is "paused" or "failed" && !_transferRunning) StartWorker();
                else throw new InvalidOperationException("This launcher update action is unavailable.");
            }
            if (action is "pause" or "cancel") await wait.ConfigureAwait(false);
        }
        finally { _operations.Release(); }
    }

    /// <summary>Returns the exact staged update directory only after the worker verified every payload.</summary>
    public string ReadyDirectory()
    {
        lock (_gate)
        {
            if (_closing || _transferRunning || _task?.Stage != "ready") throw new InvalidOperationException("The launcher update is not ready.");
            return _root;
        }
    }

    /// <summary>Removes a canceled task from the UI while retaining verified resume objects.</summary>
    public void ClearFinished() { lock (_gate) if (!_transferRunning && _task?.Stage is "completed" or "canceled") { _task = null; _state = new("unconfigured"); } }

    private void StartWorker()
    {
        SaveIntent("queued");
        _canceledHash = null;
        _transfer?.Dispose(); _transfer = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        // Archive progress rows are transport metadata, never the plan for the next incremental attempt.
        _interrupted = "paused"; _task = _task! with { Stage = "queued", Files = _incrementalFiles, CurrentPath = null,
            Failure = null, BytesPerSecond = 0, SpeedSampleCount = 0, StageProgress = null };
        _state = _state with { Status = "downloading", Failure = null, FailureReason = null, HttpStatus = null };
        _transferRunning = true;
        _worker = Task.Run(() => RunAsync(_transfer.Token));
    }
    private async Task RunAsync(CancellationToken ct)
    {
        string? terminalStage = null;
        LauncherUpdateState? failure = null;
        try
        {
            var policy = LauncherUpdatePolicy.LoadAll(_installation).SingleOrDefault(item => item.Identity == _policy!.Identity)
                ?? throw new InvalidDataException("Update source was removed.");
            var release = policy.ReadStagedRelease(_root);
            if (release.ManifestSha256 != _release!.ManifestSha256) throw new InvalidDataException("Staged release selection changed.");
            var source = policy.OpenSource(_http); var engine = new HttpDownloadEngine(_http);
            Task<Candidate[]>? fallbackDiscovery = null;
            Candidate? preferredMirror = null;
            async Task DownloadFileAsync(ClientReleaseFile file, string destination, IProgress<DownloadProgress> progress)
            {
                Candidate? preferred;
                lock (_gate) preferred = preferredMirror;
                var preferredFile = preferred?.Release.Files.Single(item => item.Path == file.Path) ?? file;
                var preferredSource = preferred?.Policy.OpenSource(_http) ?? source;
                Exception firstFailure;
                try
                {
                    var resolved = await preferredSource.ResolveAsync(preferredFile.SourcePath, ct).ConfigureAwait(false);
                    await engine.DownloadAsync(new(resolved, destination, file.Size, file.Sha256), progress, ct).ConfigureAwait(false);
                    return;
                }
                catch (Exception error) when (IsSourceTransferFailure(error, ct)) { firstFailure = error; }
                Task<Candidate[]> discovery;
                lock (_gate) discovery = fallbackDiscovery ??= EquivalentSourcesAsync(policy, release, ct);
                var mirrors = await discovery.ConfigureAwait(false);
                foreach (var mirror in mirrors)
                {
                    if (mirror.Policy.Identity == preferred?.Policy.Identity) continue;
                    try
                    {
                        var mirrorFile = mirror.Release.Files.Single(item => item.Path == file.Path);
                        var resolved = await mirror.Policy.OpenSource(_http).ResolveAsync(mirrorFile.SourcePath, ct).ConfigureAwait(false);
                        await engine.DownloadAsync(new(resolved, destination, file.Size, file.Sha256), progress, ct).ConfigureAwait(false);
                        lock (_gate) preferredMirror = mirror;
                        return;
                    }
                    catch (Exception error) when (IsSourceTransferFailure(error, ct)) { }
                }
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstFailure).Throw();
            }
            var objects = UpdateFiles.Safe(_root, "objects"); Directory.CreateDirectory(objects);
            Prune(objects, release);
            var taskFileIndexes = _task!.Files.Select((file, index) => (file.Path, index))
                .ToDictionary(item => item.Path, item => item.index, StringComparer.OrdinalIgnoreCase);
            var speeds = new Dictionary<string, double>(StringComparer.Ordinal);
            var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 };
            // Equal hashes share a resume object and must never write it concurrently.
            var changedPaths = _task!.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            async Task StageIncrementalAsync()
            {
                await Parallel.ForEachAsync(release.Files.GroupBy(file => file.Sha256), options, async (group, token) =>
                {
                    token.ThrowIfCancellationRequested(); var file = group.First();
                    var visible = group.Where(item => taskFileIndexes.ContainsKey(item.Path)).ToArray();
                    void Report(long received, string stage, double speed = 0)
                    {
                        lock (_gate)
                        {
                            speeds[file.Sha256] = speed;
                            var files = _task!.Files.ToArray();
                            foreach (var item in visible)
                                files[taskFileIndexes[item.Path]] = new(item.Path, item.Size, received, stage);
                            _task = _task with { Stage = "downloading", Files = files, CurrentPath = file.Path,
                                BytesPerSecond = speeds.Values.Sum(), SpeedSampleCount = _task.SpeedSampleCount + (speed > 0 ? 1 : 0) };
                        }
                    }
                    var destination = UpdateFiles.Safe(objects, file.Sha256 + ".verified");
                    if (visible.Length == 0) return;
                    if (!await HttpDownloadEngine.MatchesAsync(destination, file.Size, file.Sha256, token).ConfigureAwait(false))
                    {
                        var installedFile = group.FirstOrDefault(item => !changedPaths.Contains(item.Path));
                        if (installedFile is not null)
                        {
                            var installed = UpdateFiles.Safe(_installation, installedFile.Path);
                            File.Copy(installed, destination, true);
                            if (!await HttpDownloadEngine.MatchesAsync(destination, file.Size, file.Sha256, token).ConfigureAwait(false))
                                throw new InvalidDataException("Staged update file failed verification.");
                        }
                        else
                        {
                            await DownloadFileAsync(file, destination, new InlineProgress(p => Report(p.Received, "downloading", p.BytesPerSecond))).ConfigureAwait(false);
                        }
                    }
                    Report(file.Size, "verified");
                }).ConfigureAwait(false);
            }
            try
            {
                try { await StageIncrementalAsync().ConfigureAwait(false); }
                catch (Exception error) when (IsIncrementalTransferFailure(error, ct))
                {
                    await Task.Delay(500, ct).ConfigureAwait(false);
                    await StageIncrementalAsync().ConfigureAwait(false);
                }
            }
            catch (Exception error) when (IsIncrementalTransferFailure(error, ct))
            {
                Change(task => task with { Stage = "downloading", CurrentPath = null, Files = [], BytesPerSecond = 0 });
                await LauncherArchiveFallback.StageAsync(policy, release, _root, _http,
                    (file, speed) => Change(task => task with { Stage = "downloading", CurrentPath = file.Path,
                        Files = task.Files.Any(item => item.Path == file.Path)
                            ? task.Files.Select(item => item.Path == file.Path ? file : item).ToArray() : [.. task.Files, file],
                        BytesPerSecond = speed, SpeedSampleCount = task.SpeedSampleCount + 1 }),
                    percent => Change(task => task with { Stage = "verifying", CurrentPath = null, BytesPerSecond = 0, StageProgress = percent }), ct).ConfigureAwait(false);
                Change(task => task with { Files = task.Files.Select(file => file with { Received = file.Size, Stage = "verified" }).ToArray(), StageProgress = null });
            }
            ct.ThrowIfCancellationRequested();
            lock (_gate) { ct.ThrowIfCancellationRequested(); SaveIntent("ready"); terminalStage = "ready"; }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { terminalStage = "interrupted"; }
        catch (Exception error) when (Expected(error))
        {
            failure = CheckFailure(error);
            terminalStage = "failed";
        }
        finally
        {
            lock (_gate)
            {
                // Published terminal states are actionable before Task completion bookkeeping runs.
                // No transfer, verification or persistence remains after this transition.
                _transferRunning = false;
                if (ct.IsCancellationRequested || terminalStage == "interrupted")
                    _task = _task! with { Stage = _interrupted, BytesPerSecond = 0 };
                else if (terminalStage == "ready")
                {
                    _task = _task! with { Stage = "ready", CurrentPath = null, BytesPerSecond = 0 };
                    _state = _state with { Status = "ready" };
                }
                else if (failure is not null)
                {
                    _task = _task! with { Stage = "failed", Failure = failure.Failure, BytesPerSecond = 0 };
                    _state = _state with { Status = "failed", Failure = failure.Failure, FailureReason = failure.FailureReason, HttpStatus = failure.HttpStatus };
                }
            }
        }
    }
    private void SaveIntent(string stage) => UpdateFiles.Write(UpdateFiles.Safe(_root, "transfer.json"), new Intent(_release!.ManifestSha256, stage));
    private async Task<Candidate[]> EquivalentSourcesAsync(LauncherUpdatePolicy selected, LauncherRelease release, CancellationToken ct)
    {
        var policies = LauncherUpdatePolicy.LoadAll(_installation).Where(policy => policy.Identity != selected.Identity).ToArray();
        var candidates = await Task.WhenAll(policies.Select(async (policy, position) =>
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                policy.ValidateRuntime();
                var source = policy.OpenSource(_http);
                var index = await SourceDocumentReader.ReadBytesAsync(_http, source, policy.FeedPath, timeout.Token).ConfigureAwait(false);
                if (policy.SignaturePath is not null)
                    policy.VerifySignature(index, await SourceDocumentReader.ReadBytesAsync(_http, source, policy.SignaturePath, timeout.Token).ConfigureAwait(false));
                var other = LauncherRelease.Parse(policy, index);
                if (other.Revision != release.Revision || other.Version != release.Version || other.Files.Count != release.Files.Count) return null;
                var files = other.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
                if (release.Files.Any(file => !files.TryGetValue(file.Path, out var equivalent) || file.Size != equivalent.Size || file.Sha256 != equivalent.Sha256)) return null;
                return new Candidate(policy, other, index);
            }
            catch (Exception error) when (Expected(error) && !ct.IsCancellationRequested) { return null; }
        })).ConfigureAwait(false);
        return candidates.OfType<Candidate>().ToArray();
    }
    private LauncherUpdateAttempt? ReadLastAttempt(LauncherUpdatePolicy policy, LauncherRelease release) =>
        LauncherUpdateAttempt.From(UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(UpdateFiles.Safe(_root, "outcome.json")),
            policy.Identity, release.ManifestSha256);

    private bool WasRolledBack(LauncherUpdatePolicy policy, LauncherRelease release)
    {
        // Offline repair replaces its outcome, but must not make a failed release eligible again.
        var rejected = UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(UpdateFiles.Safe(_root, "rejected.json"));
        if (rejected is { Status: "rolled-back" } && rejected.SourceIdentity == policy.Identity && rejected.ManifestSha256 == release.ManifestSha256) return true;
        var outcome = UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(UpdateFiles.Safe(_root, "outcome.json"));
        return outcome is { Status: "rolled-back", Failure: not "disk" } && outcome.SourceIdentity == policy.Identity && outcome.ManifestSha256 == release.ManifestSha256;
    }
    private LauncherUpdateState ReportRollback(LauncherUpdatePolicy policy, LauncherRelease release)
    {
        var paths = (_task?.Files.Select(file => file.Path) ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _task = NewTask(policy, release, paths) with { Stage = "failed", Failure = "startup" };
        return SetState(new("failed", release.Version, release.Revision, release.ManifestSha256, "startup"));
    }
    private ClientDownloadTask NewTask(LauncherUpdatePolicy policy, LauncherRelease release, IReadOnlySet<string> changedPaths) =>
        new("launcher-update", "live", new("launcher", "", "", "", "Fantasy Tale Launcher", "", release.Version, "Fantasy Tale", policy.Kind, _installation),
            "queued", release.Files.Where(file => changedPaths.Contains(file.Path)).Select(f => new ClientDownloadFile(f.Path, f.Size, 0, "queued")).ToArray(), null, 0, 0, null, null, "downloading");

    private async Task<IReadOnlySet<string>> FindChangedPathsAsync(LauncherRelease release, CancellationToken ct)
    {
        var changed = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8) };
        await Parallel.ForEachAsync(release.Files, options, async (file, token) =>
        {
            if (!await _fileCache.MatchesAsync(file, token).ConfigureAwait(false))
                changed.TryAdd(file.Path, 0);
        }).ConfigureAwait(false);
        _fileCache.Save();
        return changed.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    private static bool IsSourceTransferFailure(Exception error, CancellationToken ct) =>
        !ct.IsCancellationRequested && error is HttpRequestException or OperationCanceledException or InvalidDataException;
    private static bool IsIncrementalTransferFailure(Exception error, CancellationToken ct) =>
        IsSourceTransferFailure(error, ct);
    private static void Prune(string objects, LauncherRelease release)
    {
        var keep = release.Files.Select(f => f.Sha256).ToHashSet(StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(objects);
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            if (name.Length >= 73 && name[..64].All(char.IsAsciiHexDigit) && name[64..].StartsWith(".verified", StringComparison.Ordinal) && !keep.Contains(name[..64]))
                File.Delete(UpdateFiles.Safe(objects, name));
        }
    }
    private void Change(Func<ClientDownloadTask, ClientDownloadTask> change) { lock (_gate) _task = change(_task!); }
    private sealed class InlineProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress> { public void Report(DownloadProgress value) => report(value); }
    internal static bool Expected(Exception e) => e is IOException or InvalidDataException or DefinitionException or CryptographicException or FormatException or ArgumentException or HttpRequestException or OperationCanceledException or UnauthorizedAccessException or System.Text.Json.JsonException;
    private static string Failure(Exception e) => e is HttpRequestException or OperationCanceledException ? "network" : e is IOException or UnauthorizedAccessException ? "disk" : "integrity";
    internal static LauncherUpdateState CheckFailure(Exception error)
    {
        var status = error is HttpRequestException { StatusCode: { } code } ? (int?)code : null;
        var reason = error switch
        {
            OperationCanceledException => "timeout",
            HttpRequestException when status == 408 || status == 504 => "timeout",
            HttpRequestException when status is 401 or 403 => "serverAccess",
            HttpRequestException when status == 404 => "feedMissing",
            HttpRequestException when status == 429 => "serverBusy",
            HttpRequestException when status >= 500 => "serverError",
            HttpRequestException when status is not null => "serverReply",
            HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => "serverNotFound",
            HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => "secureConnection",
            HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } => "connection",
            HttpRequestException => "networkUnknown",
            InvalidDataException or DefinitionException or FormatException or ArgumentException or System.Text.Json.JsonException or CryptographicException => "invalidInformation",
            IOException or UnauthorizedAccessException => "localStorage",
            _ => "unknown"
        };
        return new("failed", Failure: Failure(error), FailureReason: reason, HttpStatus: status);
    }
    internal static ClientInstallReceipt Receipt(LauncherUpdatePolicy policy, LauncherRelease release) => new("launcher", "fantasytale-launcher", release.Version, release.ManifestSha256,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(policy.Identity))), release.Revision);

    /// <summary>Stops transfers and waits for discovery before releasing the caller's transport.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_gate) _closing = true;
        _stop.Cancel();
        if (_monitor is not null) await _monitor.ConfigureAwait(false);
        else if (_startupCheck is not null)
        {
            try { await _startupCheck.ConfigureAwait(false); }
            catch (Exception error) when (_stop.IsCancellationRequested && Expected(error)) { }
        }
        await _operations.WaitAsync().ConfigureAwait(false);
        try { await _worker.ConfigureAwait(false); _transfer?.Dispose(); } finally { _operations.Release(); }
    }
}
