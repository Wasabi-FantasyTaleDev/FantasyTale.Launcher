using System.Diagnostics;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Instances;

/// <summary>The observed exit status of a client whose session file has been removed.</summary>
/// <param name="InstanceId">Instance that held the client process.</param>
/// <param name="ExitCode">Operating-system process exit code.</param>
public sealed record LaunchResult(string InstanceId, int ExitCode);

/// <summary>A launched client and its cleanup completion, without account or ticket data.</summary>
/// <param name="InstanceId">Instance protected by the running process lease.</param>
/// <param name="ProcessId">Operating-system process identifier.</param>
/// <param name="Completion">Completes after exit and session cleanup; cleanup failures fault the task.</param>
public sealed record RunningClient(string InstanceId, int ProcessId, Task<LaunchResult> Completion)
{
    /// <summary>Completes when this process reports native readiness, fails initialization, or exits before reporting.</summary>
    public Task<HookLaunchStatus> Readiness { get; init; } = Task.FromResult(new HookLaunchStatus("not-required", "", []));

    /// <summary>Latest readiness snapshot without session credentials.</summary>
    public HookLaunchStatus Hook => Readiness.IsCompletedSuccessfully ? Readiness.Result : new("starting", "", []);

    /// <summary>Authenticated live control channel for this native host, when present.</summary>
    public HookChannel? Control { get; init; }

    internal Process? Process { get; init; }
    internal Task<LaunchResult>? StopOperation { get; set; }
}

/// <summary>Starts clients, holds instance leases and removes per-launch secrets on exit or startup failure.</summary>
public sealed class LaunchService
{
    private readonly InstanceStore _store;
    private readonly int _maximum;
    private readonly TimeSpan _startupTimeout;
    private readonly object _gate = new();
    private readonly Dictionary<string, RunningClient> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource> _starting = new(StringComparer.Ordinal);
    private readonly Queue<PluginRecoveryNotice> _recoveryNotices = new();
    private bool _closing;
    private readonly ILaunchDiagnosticSink? _diagnosticSink;

    // Per-coordinator seams let lifecycle tests exercise a denied termination with a real child.
    internal Func<ProcessStartInfo, Process?> ProcessLauncher { get; init; } = Process.Start;
    internal Action<Process, bool> ProcessTerminator { get; init; } = (process, tree) => process.Kill(tree);

    /// <summary>Creates a launch coordinator with an explicit concurrent process limit.</summary>
    public LaunchService(InstanceStore store, int maximumConcurrent = 8, TimeSpan? startupTimeout = null, ILaunchDiagnosticSink? diagnosticSink = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumConcurrent, 1);
        _store = store; _maximum = maximumConcurrent;
        _startupTimeout = startupTimeout ?? TimeSpan.FromMinutes(1);
        _diagnosticSink = diagnosticSink;
        if (_startupTimeout < TimeSpan.FromSeconds(1) || _startupTimeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(startupTimeout));
    }

    /// <summary>Returns a snapshot of currently monitored processes.</summary>
    public IReadOnlyList<RunningClient> Running { get { lock (_gate) return _running.Values.ToArray(); } }

    /// <summary>Whether launch preparation or system confirmation still owns a client lease without a returned process.</summary>
    public bool HasPendingStarts { get { lock (_gate) return _starting.Count != 0; } }

    /// <summary>Whether the named client is reserved by startup or monitored as a running process.</summary>
    public bool IsActive(string id) { lock (_gate) return _starting.ContainsKey(id) || _running.ContainsKey(id); }

    /// <summary>Returns and clears bounded notices produced by automatic plugin selection repair.</summary>
    public IReadOnlyList<PluginRecoveryNotice> DrainRecoveryNotices()
    {
        lock (_gate)
        {
            var notices = _recoveryNotices.ToArray();
            _recoveryNotices.Clear();
            return notices;
        }
    }

    /// <summary>
    /// The messenger section for a launch: only a build whose messenger calls the launcher's stand-in answers can use it,
    /// over http, in a schema 2 session read by runtime 3.5.0 or later (older runtimes refuse the unknown section).
    /// Other builds keep their own messenger and ignore the server's sign-in.
    /// </summary>
    internal static SessionMessenger? SessionMessengerFor(BuildDefinition build, string? launchId, string? runtimeVersion, LaunchMessenger? messenger) =>
        messenger is not null && launchId is not null && build.Messenger == "standin" && runtimeVersion is not null
            && PluginVersion.Parse(runtimeVersion).CompareTo(PluginVersion.Parse("3.5.0")) >= 0
            ? new SessionMessenger("http", messenger.Url, messenger.Ticket) : null;

    /// <summary>Starts on a worker so native shell confirmation cannot block the frontend callback; pending starts remain owned until resolved.</summary>
    public Task<RunningClient> StartAsync(string id, GameDefinition game, IGameModule module, IReadOnlyList<PluginDefinition> packages,
        string? account = null, string? ticket = null, ServerEndpoint? server = null, bool useServerOverride = false, IReadOnlyList<string>? confirmedWarnings = null,
        IReadOnlyList<string>? requiredPlugins = null, bool enableOverlayBroker = false, bool enableCompanionBroker = false, IReadOnlyList<string>? disabledPlugins = null,
        LaunchMessenger? messenger = null, bool nameLogin = false, SessionGameLogin? gameLogin = null, ServerEndpoint? gameEndpoint = null,
        IReadOnlyList<GameServerTarget>? gameTargets = null, IProgress<GameResourceProgress>? resourceProgress = null,
        IReadOnlyList<GameServerChoice>? gameChoices = null, IAutomaticServerSelection? automaticSelection = null,
        IReadOnlyList<GameResourceConflictChoice>? resourceConflictChoices = null,
        TimeSpan? startupTimeout = null, string? launcherLanguage = null, CancellationToken preparationCancellation = default) =>
        Task.Run(() => Start(id, game, module, packages, account, ticket, server, useServerOverride, confirmedWarnings, requiredPlugins, enableOverlayBroker, enableCompanionBroker, disabledPlugins, messenger, nameLogin, gameLogin, gameEndpoint, gameTargets, resourceProgress, gameChoices, automaticSelection, resourceConflictChoices, startupTimeout, launcherLanguage, preparationCancellation));

    /// <summary>
    /// Validates and starts one instance. Credentials remain local to the session-writing call. When resource plugins
    /// conflict, <see cref="GameResourceConflictException"/> is raised before client files change; retry with the user's
    /// <paramref name="resourceConflictChoices"/> bound to that report's snapshot.
    /// </summary>
    public RunningClient Start(string id, GameDefinition game, IGameModule module, IReadOnlyList<PluginDefinition> packages,
        string? account = null, string? ticket = null, ServerEndpoint? server = null, bool useServerOverride = false, IReadOnlyList<string>? confirmedWarnings = null,
        IReadOnlyList<string>? requiredPlugins = null, bool enableOverlayBroker = false, bool enableCompanionBroker = false, IReadOnlyList<string>? disabledPlugins = null,
        LaunchMessenger? messenger = null, bool nameLogin = false, SessionGameLogin? gameLogin = null, ServerEndpoint? gameEndpoint = null,
        IReadOnlyList<GameServerTarget>? gameTargets = null, IProgress<GameResourceProgress>? resourceProgress = null,
        IReadOnlyList<GameServerChoice>? gameChoices = null, IAutomaticServerSelection? automaticSelection = null,
        IReadOnlyList<GameResourceConflictChoice>? resourceConflictChoices = null,
        TimeSpan? startupTimeout = null, string? launcherLanguage = null, CancellationToken preparationCancellation = default)
    {
        var startupClock = Stopwatch.StartNew();
        var startupBudget = startupTimeout ?? _startupTimeout;
        preparationCancellation.ThrowIfCancellationRequested();
        if (startupBudget <= TimeSpan.Zero) throw new OperationCanceledException(preparationCancellation);
        using var preparationDeadline = CancellationTokenSource.CreateLinkedTokenSource(preparationCancellation);
        var preparationBudget = startupBudget;
        if (automaticSelection is not null)
        {
            var remaining = automaticSelection.LocalExpiresAtUtc - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new InvalidOperationException("Native server admission expired before launch preparation.");
            if (remaining < preparationBudget) preparationBudget = remaining;
        }
        preparationDeadline.CancelAfter(preparationBudget);
        preparationCancellation = preparationDeadline.Token;
        if (nameLogin && (ticket is not null || string.IsNullOrEmpty(account) || account.Length > 32
            || account.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.' or '@'))))
            throw new ArgumentException("local_player_name_invalid");
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(module);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_closing) throw new InvalidOperationException("The launcher is shutting down.");
            if (_running.Count + _starting.Count >= _maximum) throw new InvalidOperationException("The concurrent client limit has been reached.");
            if (_running.ContainsKey(id) || _starting.ContainsKey(id)) throw new InvalidOperationException($"Instance '{id}' is already running or starting.");
            _starting.Add(id, pending);
        }
        try
        {
            var lease = _store.Acquire(id);
            string? ownedSession = null;
            RuntimeDeployment? deployment = null;
            HookChannel? control = null;
            Process? startedProcess = null;
            SessionSpec? startedSpec = null;
            ILaunchBootstrapLease? bootstrap = null;
            try
            {
                var instance = _store.Load(id);
                if (game.Status != GameStatus.Enabled || instance.Game != game.Id || module.GameId != game.Id)
                    throw new InvalidOperationException("The instance requires an enabled game and its matching module.");
                var build = game.Builds.SingleOrDefault(b => b.Id == instance.Build)
                    ?? throw new DefinitionException("instance.toml", "instance.build", "build is not installed in the game catalog");
                build.RequireEnabled();
                if (InstanceRecovery.Required(_store.ClientFolder(id))) InstanceRecovery.RecoverLeased(_store, id, game);
                var updated = FilePluginResolver.EnsureComponents(instance, game, build, packages, out var recoveries);
                if (recoveries.Count > 0)
                {
                    lock (_gate)
                    {
                        foreach (var notice in recoveries)
                        {
                            if (_recoveryNotices.Any(existing => existing.InstanceId == notice.InstanceId && existing.PluginId == notice.PluginId && existing.Version == notice.Version)) continue;
                            if (_recoveryNotices.Count == 32) _recoveryNotices.Dequeue();
                            _recoveryNotices.Enqueue(notice);
                        }
                    }
                }
                if (!ReferenceEquals(updated, instance)) { _store.SaveLeased(updated, instance); instance = updated; }
                var localPolicy = Plugins.ProfileClientPolicy.Rules(build);
                Servers.ServerPluginPolicy.RequireEnabled(instance, build, packages, localPolicy);
                instance = Servers.ServerPluginPolicy.ForLaunch(instance, build, packages, localPolicy);
                if (requiredPlugins is { Count: > 0 })
                {
                    Servers.ServerPluginPolicy.RequireEnabled(instance, build, packages,
                        requiredPlugins.Select(plugin => new Servers.ServerPluginRule(plugin) { Required = true }));
                    if (!game.SupportsPlugins && instance.Plugins.Any(selection => requiredPlugins.Contains(selection.Id, StringComparer.Ordinal) &&
                        packages.Any(package => package.Id == selection.Id && package.Version == selection.Version && package.Kind != "files")))
                        throw new InvalidOperationException("The game module does not support the server's required hook plugins.");
                    // Runtime failure is fatal only for this destination; do not persist the server's lock.
                    instance = instance with { Plugins = instance.Plugins.Select(plugin => requiredPlugins.Contains(plugin.Id, StringComparer.Ordinal)
                        ? plugin with { Required = true } : plugin).ToArray() };
                }
                instance = Servers.ServerPluginPolicy.ForLaunch(instance, build, packages,
                    (disabledPlugins ?? []).Select(plugin => new Servers.ServerPluginRule(plugin) { Disabled = true }));
                if (FilePluginResolver.Warnings(instance, build, packages).Any(p => !(disabledPlugins ?? []).Contains(p.Id, StringComparer.Ordinal) && !(confirmedWarnings ?? []).Contains(p.Id + "@" + p.Version, StringComparer.Ordinal)))
                    throw new InvalidOperationException("Disabled startup components require explicit confirmation for this launch.");
                ClientLanguagePolicy.RequireAvailable(game, build, instance, packages);
                instance = ClientLanguagePolicy.ForLaunch(game, build, instance);
                var endpoint = useServerOverride ? server : instance.Endpoint;
                var plugins = game.SupportsPlugins ? PluginResolver.Resolve(instance, build, packages) : [];
                // Shared data a package declared with `requires` must be installed and compatible before any client file is touched.
                if (game.SupportsPlugins) _ = Plugins.PluginRequirements.RequireSatisfied(instance, build, packages, launcherLanguage);
                var filePlugins = FilePluginResolver.Resolve(instance, build, packages);
                var client = _store.ClientFolder(id);
                if (File.Exists(Path.Combine(client, Stores.ClientInstallTransaction.DirectoryName, "journal.json")))
                    throw new IOException("An interrupted client installation must be recovered before launching. Retry the installation from Downloads.");
                var log = _store.LogFolder(id);
                var preparationId = Guid.NewGuid().ToString("N");
                var context = new LaunchContext(game, build, client, endpoint, account) { ResourceCacheFolder = _store.ResourceCacheFolder(id),
                    ResourceProgress = resourceProgress, PreparationCancellation = preparationCancellation, NameLogin = nameLogin, ClientLanguage = instance.Language,
                    ResourceConflictChoices = resourceConflictChoices ?? [],
                    Diagnostics = _diagnosticSink, LaunchId = preparationId,
                    GameEndpoint = gameEndpoint, GameTargets = gameTargets ?? [], GameChoices = gameChoices ?? [], AutomaticSelection = automaticSelection,
                    FilePluginTargets = packages.Where(p => p.Kind == "files" && p.Game == game.Id && PluginCatalog.IsAvailable(p, build) &&
                        instance.Plugins.Any(s => s.Id == p.Id && s.Version == p.Version && (s.Enabled || PluginCatalog.NeedsWarning(p, build))))
                        .SelectMany(p => p.Files.Where(f => f.Builds.Count == 0 || PluginCatalog.Matches(f.Builds, build))).Select(f => f.Target).ToHashSet(StringComparer.OrdinalIgnoreCase) };
                if ((context.GameTargets.Count > 0 || context.GameChoices.Count > 0) && module is not ILaunchBootstrapProvider)
                    throw new InvalidOperationException("The game module does not support an in-game server directory.");
                if (module is ILaunchBootstrapProvider bootstrapProvider)
                {
                    bootstrap = bootstrapProvider.PrepareBootstrapAsync(context, preparationCancellation).AsTask().GetAwaiter().GetResult();
                    if (bootstrap is not null) context = context with { GameEndpoint = bootstrap.GameEndpoint };
                }
                var plan = module.CreateLaunchPlan(context);
                preparationCancellation.ThrowIfCancellationRequested();
                var runtimeEndpoint = context.GameEndpoint ?? endpoint;
                foreach (var (pluginId, minimum) in plan.MinimumPluginVersions)
                    if (!instance.Plugins.Any(selection => selection.Id == pluginId && selection.Enabled
                        && PluginVersion.Parse(selection.Version).CompareTo(PluginVersion.Parse(minimum)) >= 0))
                        throw new InvalidOperationException($"Update the enabled authentication component '{pluginId}' to {minimum} or later before launch.");
                if (nameLogin && (plan.Runtime is null || PluginVersion.Parse(RuntimePackage.Load(plan.Runtime.PackageFolder, game.Id).Version)
                    .CompareTo(PluginVersion.Parse("3.6.0")) < 0))
                    throw new InvalidOperationException("Name-only login requires runtime 3.6.0 or later.");
                // The stand-in signs in from the game's login screen only from runtime 3.7.0 on.
                if (gameLogin is not null && (plan.Runtime is null || build.Messenger != "standin"
                    || PluginVersion.Parse(RuntimePackage.Load(plan.Runtime.PackageFolder, game.Id).Version).CompareTo(PluginVersion.Parse("3.7.0")) < 0))
                    throw new InvalidOperationException("In-game sign-in requires the messenger stand-in of runtime 3.7.0 or later. Update the launcher's runtime.");
                if (plan.AuthenticationPlugin is { } authenticationPlugin)
                {
                    if ((!nameLogin && string.IsNullOrEmpty(ticket)) || runtimeEndpoint is null)
                        throw new InvalidOperationException("The module's authentication grant requires a launch ticket and endpoint.");
                    if (!plugins.Any(plugin => plugin.Id == authenticationPlugin)
                        || !packages.Any(package => package.Id == authenticationPlugin && package.Kind == "native"
                            && plugins.Any(plugin => plugin.Id == package.Id && plugin.Version == package.Version)))
                        throw new InvalidOperationException("The module's authentication owner must be a selected native plugin.");
                    if (plan.Runtime is null || PluginVersion.Parse(RuntimePackage.Load(plan.Runtime.PackageFolder, game.Id).Version)
                        .CompareTo(PluginVersion.Parse(nameLogin ? "3.6.0" : "3.4.0")) < 0)
                        throw new InvalidOperationException("The selected runtime does not support this authentication mode.");
                }
                if (!InstanceStore.IsWithin(client, plan.ExecutablePath) || !Path.GetFullPath(plan.WorkingDirectory).Equals(client, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The game module must start an executable inside the instance client directory.");
                InstanceStore.RejectLinks(plan.ExecutablePath);
                if (!File.Exists(plan.ExecutablePath)) throw new FileNotFoundException("The client executable is not installed.", plan.ExecutablePath);
                module.ValidateInstallation(context);
                Directory.CreateDirectory(log);
                InstanceStore.RejectLinks(Path.Combine(log, "fthook.log"));
                InstanceStore.RejectLinks(Path.Combine(log, "crash.log"));
                var sessionPath = Path.Combine(client, SessionWriter.FileName);
                InstanceStore.RejectLinks(sessionPath);
                if (File.Exists(sessionPath)) throw new IOException("A previous session.toml is present. Remove it only after confirming that this client is stopped.");
                if (plugins.Count > 0 && plan.Runtime is null) throw new InvalidOperationException("The game module did not supply a verified host for its selected plugins.");
                var resourceSelections = instance.Plugins.Where(s => s.Enabled).Select(s =>
                {
                    var package = packages.Single(p => p.Id == s.Id && p.Version == s.Version);
                    return package.Kind == "resources" ? new GameResourceSelection(package, PluginCatalog.ResolveOptions(package, build, s.Options)) : null;
                }).OfType<GameResourceSelection>().ToArray();
                if (resourceSelections.Length > 0 && module is not IGameResourceModule)
                    throw new InvalidOperationException("The game module does not support selected resource plugins. Update the Launcher.");
                using var resources = module is IGameResourceModule resourceModule ? resourceModule.PrepareResources(context, resourceSelections) : null;
                if (resources is not null)
                    filePlugins = filePlugins.Concat(resources.Files.Select(f => new FilePluginPayload(f.Source,
                        FilePluginResolver.RelativePath(f.Target), f.Size, f.Sha256)
                        { OriginalSha256 = f.OriginalSha256, ExpectedAbsent = f.ExpectedAbsent,
                            Changes = f.Changes, CacheReused = f.CacheReused })).ToArray();
                var collision = filePlugins.GroupBy(f => f.Target, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
                if (collision is not null)
                    throw new IOException("Resource replacements conflict at " + collision.Key + ": "
                        + string.Join(", ", collision.SelectMany(f => f.Changes).Select(c => c.PluginId).Distinct())
                        + ". Disable the overlapping replacement before retrying.");
                ResourceOverrideDiagnostics.Capture(_store, id, filePlugins);
                preparationCancellation.ThrowIfCancellationRequested();
                module.PrepareLaunch(context);
                var launchId = plan.Runtime is null ? null : preparationId;
                if (launchId is not null) log = LaunchLogs.Create(log, skipped: (path, error) =>
                    RecordDiagnostic("launch-log-retention-skipped", path + " " + error.GetType().Name + " os-" + (error.HResult & 0xffff)));
                if (launchId is not null) LaunchLogs.RecordExecutable(log, launchId, game.Id, build.Id, plan.ExecutablePath);
                if (launchId is not null) control = new HookChannel(launchId, enableOverlayBroker, enableCompanionBroker);
                var selectedClientLanguage = instance.Language ?? ClientLanguagePolicy.Native(game, build);
                var spec = new SessionSpec(game.Id, build, id, Path.Combine(log, "fthook.log"), account, ticket, runtimeEndpoint, plugins)
                { LaunchId = launchId, StartupTimeoutMs = (int)_startupTimeout.TotalMilliseconds, OverlayPipe = control?.OverlayPipeName,
                    ClientLanguage = selectedClientLanguage.Length > 0 && plan.Runtime is not null && PluginVersion.Parse(RuntimePackage.Load(plan.Runtime.PackageFolder, game.Id).Version)
                        .CompareTo(PluginVersion.Parse("3.7.2")) >= 0 ? selectedClientLanguage : null,
                    NameLogin = nameLogin, AuthenticationPlugin = plan.AuthenticationPlugin, Messenger = SessionMessengerFor(build, launchId,
                        plan.Runtime is null || messenger is null ? null : RuntimePackage.Load(plan.Runtime.PackageFolder, game.Id).Version, messenger),
                    GameLogin = launchId is null ? null : gameLogin, ModuleData = bootstrap?.Metadata };
                startedSpec = spec;
                preparationCancellation.ThrowIfCancellationRequested();
                if (plan.Runtime is not null || filePlugins.Count != 0) deployment = RuntimeDeployment.Apply(client, game.Id, plan.Runtime, filePlugins);
                preparationCancellation.ThrowIfCancellationRequested();
                ownedSession = SessionWriter.Create(client, spec);
                var start = new ProcessStartInfo(plan.ExecutablePath)
                {
                    WorkingDirectory = client,
                    // Elevation belongs to the module's launch plan; forcing it for
                    // every executable also elevates unrelated clients and helpers.
                    UseShellExecute = OperatingSystem.IsWindows(),
                    Verb = OperatingSystem.IsWindows() && plan.RequiresElevation ? "runas" : string.Empty,
                };
                foreach (var argument in plan.Arguments) start.ArgumentList.Add(argument);
                preparationCancellation.ThrowIfCancellationRequested();
                Process process;
                try
                {
                    process = ProcessLauncher(start) ?? throw new InvalidOperationException("The client did not return a monitorable process.");
                }
                catch (System.ComponentModel.Win32Exception error) when (OperatingSystem.IsWindows() && error.NativeErrorCode == 1223)
                {
                    throw new InvalidOperationException(
                        "Windows cancelled the administrator permission request. Approve the Windows prompt to start this client, then try again.", error);
                }
                startedProcess = process;
                control?.BindProcess(process.Id);
                control?.BindOverlayProcess(process.Id);
                preparationCancellation.ThrowIfCancellationRequested();
                var running = MonitorStarted(process, lease, id, ownedSession, spec, deployment, control, bootstrap, startupBudget - startupClock.Elapsed);
                bootstrap = null;
                return running;
            }
            catch (Exception startFailure)
            {
                if (startedProcess is not null)
                {
                    // Even a canceled shell start can have created an elevated child that refuses Kill.
                    // Transfer the process and recovery barrier to the normal monitor before reporting failure.
                    RecordDiagnostic("launch-start-failed", startFailure.GetType().Name);
                    _ = MonitorStarted(startedProcess, lease, id, ownedSession!, startedSpec!, deployment, control, bootstrap,
                        TimeSpan.Zero, failedStartup: true);
                    bootstrap = null;
                    throw;
                }
                control?.Dispose();
                Exception? cleanupFailure = null;
                try
                {
                    // Keep the session as a recovery barrier if restoration fails.
                    deployment?.Restore();
                    if (ownedSession is not null) File.Delete(ownedSession);
                }
                catch (Exception error)
                { cleanupFailure = error; RecordDiagnostic("launch-start-cleanup-failed", "client-" + error.GetType().Name); }
                try
                {
                    if (bootstrap is not null) bootstrap.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception error)
                { cleanupFailure ??= error; RecordDiagnostic("launch-start-cleanup-failed", "bootstrap-" + error.GetType().Name); }
                lease.Dispose();
                if (cleanupFailure is not null) throw cleanupFailure;
                throw;
            }
        }
        finally
        {
            lock (_gate) { _starting.Remove(id); pending.TrySetResult(); }
        }
    }

    private RunningClient MonitorStarted(Process process, FileStream lease, string id, string session, SessionSpec spec,
        RuntimeDeployment? deployment, HookChannel? control, ILaunchBootstrapLease? bootstrap, TimeSpan startupTimeout, bool failedStartup = false)
    {
        var completion = new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readiness = new TaskCompletionSource<HookLaunchStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = new RunningClient(id, process.Id, completion.Task) { Readiness = readiness.Task, Control = control, Process = process };
        lock (_gate) _running.Add(id, running);
        _ = MonitorAsync(process, lease, id, session, spec, deployment, control, bootstrap, readiness, completion, startupTimeout, failedStartup);
        return running;
    }

    /// <summary>Rejects new starts and waits for every existing client and its secret-file cleanup.</summary>
    public async Task DrainAsync()
    {
        Task starting;
        lock (_gate) { _closing = true; starting = Task.WhenAll(_starting.Values.Select(p => p.Task)); }
        await starting.ConfigureAwait(false);
        Task running;
        lock (_gate) running = Task.WhenAll(_running.Values.Select(p => p.Completion));
        await running.ConfigureAwait(false);
    }

    /// <summary>Ends only the process owned by this launch coordinator, then waits for session and native-file cleanup.</summary>
    public Task<LaunchResult> StopAsync(string id)
    {
        lock (_gate)
        {
            var running = _running.GetValueOrDefault(id) ?? throw new InvalidOperationException("This client is not running.");
            return StopOwnedAsync(running);
        }
    }

    /// <summary>Stops the confirmed process only; an exited or replaced target is a harmless no-op.</summary>
    public Task<LaunchResult?> StopAsync(string id, int expectedProcessId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedProcessId);
        lock (_gate)
        {
            if (!_running.TryGetValue(id, out var running) || running.ProcessId != expectedProcessId)
                return Task.FromResult<LaunchResult?>(null);
            return StopConfirmedAsync(running);
        }
    }

    private async Task<LaunchResult?> StopConfirmedAsync(RunningClient running)
        => await StopOwnedAsync(running).ConfigureAwait(false);

    private Task<LaunchResult> StopOwnedAsync(RunningClient running)
    {
        lock (_gate)
        {
            // Repeated UI/exit requests share the same IPC stop and cleanup instead of racing an OS kill.
            if (running.StopOperation is null or { IsFaulted: true } or { IsCanceled: true })
                running.StopOperation = StopOwnedCoreAsync(running);
            return running.StopOperation;
        }
    }

    /// <summary>Waits for an owned pending start to finish publishing or canceling, then stops any process it created.</summary>
    public async Task CancelStartingAsync(string id)
    {
        Task? pending;
        lock (_gate) pending = _starting.GetValueOrDefault(id)?.Task;
        if (pending is not null) await pending.ConfigureAwait(false);
        RunningClient? running;
        lock (_gate) running = _running.GetValueOrDefault(id);
        if (running is not null) await StopOwnedAsync(running).ConfigureAwait(false);
    }

    private async Task<LaunchResult> StopOwnedCoreAsync(RunningClient running)
    {
        if (running.Completion.IsCompleted) return await running.Completion.ConfigureAwait(false);
        RecordDiagnostic("launch-stop-start");
        var acknowledged = false;
        // Self-termination also works for a client started through UAC by a non-elevated launcher.
        if (running.Control is { } control)
        {
            try { acknowledged = await control.RequestAsync("stop").ConfigureAwait(false) == "stopping"; }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or DefinitionException or System.Text.DecoderFallbackException) { }
        }
        RecordDiagnostic("launch-stop-control", acknowledged ? "acknowledged" : "unavailable");
        // The reply precedes self-termination; avoid racing an OS kill against an elevated client.
        if (acknowledged && await Task.WhenAny(running.Completion, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false) == running.Completion)
            return await running.Completion.ConfigureAwait(false);
        lock (_gate)
        {
            if (_running.TryGetValue(running.InstanceId, out var current) && ReferenceEquals(current, running) && !running.Process!.HasExited)
            {
                RecordDiagnostic("launch-stop-kill");
                ProcessTerminator(running.Process, false);
            }
        }
        return await running.Completion.ConfigureAwait(false);
    }

    /// <summary>Stops all owned clients for an explicitly confirmed application exit, preserving each cleanup monitor.</summary>
    public Task StopAllAsync() => StopAllAsync(null);

    /// <summary>Stops owned processes for a confirmed update while letting the caller retain its restart gate; failure leaves ordinary launching available.</summary>
    public async Task StopForUpdateAsync(Func<Task>? stopRemainingProcesses)
    {
        try { await StopAllAsync(stopRemainingProcesses).ConfigureAwait(false); }
        finally { lock (_gate) _closing = false; }
    }

    /// <summary>Stops owned clients and remaining processes for a confirmed exit; failed cleanup reopens the launch gate.</summary>
    public async Task StopAllAsync(Func<Task>? stopRemainingProcesses)
    {
        Task starting;
        lock (_gate) { _closing = true; starting = Task.WhenAll(_starting.Values.Select(p => p.Task)); }
        try
        {
            await starting.ConfigureAwait(false);
            Task stopping;
            lock (_gate) stopping = Task.WhenAll(_running.Values.Select(StopOwnedAsync));
            await stopping.ConfigureAwait(false);
            if (stopRemainingProcesses is not null) await stopRemainingProcesses().ConfigureAwait(false);
        }
        catch { lock (_gate) _closing = false; throw; }
    }

    private async Task MonitorAsync(Process process, FileStream lease, string id, string session, SessionSpec spec,
        RuntimeDeployment? deployment, HookChannel? control, ILaunchBootstrapLease? bootstrap,
        TaskCompletionSource<HookLaunchStatus> readiness, TaskCompletionSource<LaunchResult> completion, TimeSpan startupTimeout, bool failedStartup)
    {
        // Yield until Start has published the running entry, including for an immediately exiting child.
        await Task.Yield();
        Exception? failure = null;
        var exitCode = 0;
        var phase = "observe-exit";
        var failedPhase = phase;
        var bootstrapDisposeAttempted = false;
        var exitConfirmed = false;
        async ValueTask CompleteBootstrapAsync(CancellationToken cancellationToken)
        {
            if (bootstrap is null || bootstrapDisposeAttempted) return;
            RecordDiagnostic("launch-bootstrap-handoff-start");
            await bootstrap.CompleteHandoffAsync(cancellationToken).ConfigureAwait(false);
            RecordDiagnostic("launch-bootstrap-handoff-complete");
        }
        async ValueTask DisposeBootstrapAsync()
        {
            if (bootstrap is null || bootstrapDisposeAttempted) return;
            bootstrapDisposeAttempted = true;
            RecordDiagnostic("launch-bootstrap-dispose-start");
            try
            {
                await bootstrap.DisposeAsync().ConfigureAwait(false);
                RecordDiagnostic("launch-bootstrap-dispose-complete");
            }
            catch (Exception error)
            {
                failure = error;
                failedPhase = "bootstrap-dispose";
                RecordDiagnostic("launch-cleanup-failed", "bootstrap-dispose-" + error.GetType().Name);
            }
        }
        try
        {
            RecordDiagnostic("launch-readiness-start");
            var hookStatus = failedStartup ? new HookLaunchStatus("failed", "monitor-failed", [])
                : await ObserveHookAsync(process, session, spec, deployment, startupTimeout).ConfigureAwait(false);
            readiness.TrySetResult(hookStatus);
            if (failedStartup || hookStatus.State == "failed" && hookStatus.Error == "startup-timeout")
            {
                RecordDiagnostic(failedStartup ? "launch-start-failure-stop" : "launch-startup-timeout");
                if (!process.HasExited)
                {
                    var stopping = false;
                    if (control is not null)
                    {
                        try { stopping = await control.RequestAsync("stop").ConfigureAwait(false) == "stopping"; }
                        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or DefinitionException or System.Text.DecoderFallbackException) { }
                    }
                    var exitedAfterTimeout = process.WaitForExitAsync();
                    if (!stopping || await Task.WhenAny(exitedAfterTimeout, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false) != exitedAfterTimeout)
                        if (!process.HasExited) ProcessTerminator(process, true);
                    await exitedAfterTimeout.ConfigureAwait(false);
                }
            }
            RecordDiagnostic("launch-readiness-complete");
            var exited = process.WaitForExitAsync();
            if (bootstrap is not null && control is not null && deployment is not null && spec.LaunchId is not null)
            {
                var started = process.StartTime.ToUniversalTime().ToFileTimeUtc();
                await BootstrapHandoff.WaitForExitAsync(exited, async () => HookStatusReader.Parse("hook-control",
                    await control.RequestAsync("status").ConfigureAwait(false), spec, process.Id, started, deployment.Version),
                    CompleteBootstrapAsync, detail => RecordDiagnostic("launch-bootstrap-observation-failed", detail)).ConfigureAwait(false);
            }
            else await exited.ConfigureAwait(false);
            exitCode = process.ExitCode;
            exitConfirmed = true;
            RecordDiagnostic("launch-client-exited", "code-" + exitCode);
        }
        catch (Exception e)
        {
            // An observation or tree-termination failure does not prove process exit. Keep ownership
            // and the recovery barrier until the original process handle confirms termination.
            failure = e is AggregateException { InnerExceptions.Count: 1 } single ? single.InnerExceptions[0] : e;
            RecordDiagnostic("launch-observe-failed", failure.GetType().Name);
            readiness.TrySetResult(new HookLaunchStatus("failed", "monitor-failed", []));
            while (!exitConfirmed)
            {
                try
                {
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    exitCode = process.ExitCode;
                    exitConfirmed = true;
                }
                catch (Exception)
                {
                    // Retain the bounded running slot and lease if Windows cannot confirm exit.
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                }
            }
        }
        try
        {
            if (!exitConfirmed) throw new IOException("The client exit could not be established; native backups and session are retained.", failure);
            phase = "bootstrap-dispose";
            await DisposeBootstrapAsync().ConfigureAwait(false);
            phase = "runtime-restore";
            RecordDiagnostic("launch-runtime-restore-start");
            deployment?.Restore();
            RecordDiagnostic("launch-runtime-restore-complete");
            phase = "readiness-archive";
            if (spec.LaunchId is { } launchId)
            {
                var status = Path.Combine(Path.GetDirectoryName(session)!, HookStatusReader.FileName(launchId));
                InstanceStore.RejectLinks(status); InstanceStore.RejectLinks(status + ".tmp");
                if (File.Exists(status) && new FileInfo(status).Length <= 64 * 1024)
                    File.Copy(status, Path.Combine(Path.GetDirectoryName(spec.LogPath)!, "readiness.toml"), false);
                File.Delete(status); File.Delete(status + ".tmp");
            }
            phase = "session-delete";
            File.Delete(session);
            RecordDiagnostic("launch-cleanup-complete");
        }
        catch (Exception e)
        {
            // The completion must not report a clean exit while recovery evidence remains.
            failure = e; failedPhase = phase; RecordDiagnostic("launch-cleanup-failed", phase + "-" + e.GetType().Name);
        }
        finally
        {
            if (bootstrap is not null && !bootstrapDisposeAttempted)
            {
                bootstrapDisposeAttempted = true;
                try { await bootstrap.DisposeAsync().ConfigureAwait(false); }
                catch (Exception e)
                { if (failure is null) { failure = e; failedPhase = "bootstrap-dispose"; } RecordDiagnostic("launch-final-bootstrap-dispose-failed", e.GetType().Name); }
            }
            void Release(Action action, string step)
            {
                try { action(); }
                catch (Exception error)
                {
                    if (failure is null) { failure = error; failedPhase = step; }
                    RecordDiagnostic("launch-final-release-failed", step + "-" + error.GetType().Name);
                }
            }
            Release(() => control?.Dispose(), "control-dispose");
            readiness.TrySetResult(new HookLaunchStatus("failed", "monitor-failed", []));
            Release(lease.Dispose, "lease-dispose");
            lock (_gate)
            {
                // StopOwnedCoreAsync reads Process.HasExited under this lock; removing the entry before
                // disposing keeps a concurrent stop from touching a disposed Process.
                _running.Remove(id);
                Release(process.Dispose, "process-dispose");
                if (failure is null) completion.SetResult(new LaunchResult(id, exitCode));
                else completion.SetException(new LaunchCleanupException(failedPhase, exitConfirmed, failure, exitConfirmed ? exitCode : null));
            }
        }
    }

    private void RecordDiagnostic(string name, string? detail = null)
    {
        try { _diagnosticSink?.Record(name, detail); }
        catch (Exception) { /* Recording must not interrupt process and session cleanup. */ }
    }

    private static async Task<HookLaunchStatus> ObserveHookAsync(Process process, string session, SessionSpec spec, RuntimeDeployment? deployment, TimeSpan startupTimeout)
    {
        if (spec.LaunchId is null || deployment is null) return new HookLaunchStatus("not-required", "", []);
        var file = Path.Combine(Path.GetDirectoryName(session)!, HookStatusReader.FileName(spec.LaunchId));
        var started = process.StartTime.ToUniversalTime().ToFileTimeUtc();
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                if (File.Exists(file))
                {
                    var status = HookStatusReader.Read(file, spec, process.Id, started, deployment.Version);
                    if (status.State != "starting") return status;
                }
            }
            catch (FileNotFoundException) { /* Atomic replacement may race the existence check. */ }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or DefinitionException or System.Text.DecoderFallbackException)
            { return new HookLaunchStatus("failed", "invalid-report", []); }
            if (process.HasExited) return new HookLaunchStatus("failed", "exited-before-ready", []);
            if (elapsed.Elapsed >= startupTimeout) return new HookLaunchStatus("failed", "startup-timeout", []);
            await Task.Delay(50).ConfigureAwait(false);
        }
    }
}
