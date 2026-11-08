using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>Pending configuration changes for the requested client, independent of completed package downloads.</summary>
/// <param name="Count">Number of plugin selections still waiting to be applied.</param>
/// <param name="WaitingForExit">Selections held by a game that is actually running.</param>
/// <param name="Failure">Safe failure category for these selections, if a previous attempt failed.</param>
public sealed record PluginActivationStatus(int Count, int WaitingForExit, string? Failure);

/// <summary>Downloads shared plugin releases independently of game leases and persists selection-safe activation until each client exits.</summary>
public sealed class PluginUpgradeActivationService
{
    private sealed record Intent(string Instance, string Game, string Build, string Plugin, string Version,
        string Selection, string Task, string Source, string Identity, string Hash, bool Explicit = false, bool LocalImport = false, bool ForceEnable = false,
        string? SelectionPlugin = null);
    private readonly string _path;
    private readonly InstanceStore _instances;
    private readonly PluginLibrary _library;
    private readonly ClientDownloadService _downloads;
    private readonly Func<string, GameDefinition> _game;
    private readonly Func<string, bool> _running;
    private readonly Func<bool> _removeOldVersions;
    private readonly object _gate = new();
    private readonly object _applyGate = new();
    private readonly ConcurrentDictionary<Intent, string> _failures = new();
    private string? _maintenanceFailure;
    private readonly string? _restoreFailure;
    private List<Intent> _pending = [];
    private long _revision;
    private static readonly JsonSerializerOptions Json = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true, MaxDepth = 8 };

    /// <summary>Restores pending activations; malformed records fail before any client selection can change.</summary>
    public PluginUpgradeActivationService(string root, InstanceStore instances, PluginLibrary library, ClientDownloadService downloads,
        Func<string, GameDefinition> game, Func<string, bool> running, Func<string, StoreSource?> source, Func<bool>? removeOldVersions = null)
    {
        Directory.CreateDirectory(root); _path = Path.Combine(root, "plugin-upgrades.json"); InstanceStore.RejectLinks(_path);
        _instances = instances; _library = library; _downloads = downloads; _game = game; _running = running; _removeOldVersions = removeOldVersions ?? (() => true);
        if (!File.Exists(_path)) return;
        try
        {
            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var restored = JsonSerializer.Deserialize<List<Intent>>(file, Json) ?? throw new InvalidDataException("Invalid plugin upgrade intents.");
            Validate(restored); _pending = restored;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or DefinitionException or ArgumentException or FormatException)
        { _restoreFailure = error is JsonException or DefinitionException or InvalidDataException or ArgumentException ? "integrity" : "disk"; Failure = _restoreFailure; }
    }

    /// <summary>Safe processing failure category; pending records remain available for retry.</summary>
    public string? Failure { get; private set; }

    /// <summary>Changes when activation, removal or cleanup modifies the shared plugin inventory or client selections.</summary>
    public long Revision => Interlocked.Read(ref _revision);

    /// <summary>Returns the number of outstanding client activations without starting network or file work.</summary>
    public int PendingCount => Volatile.Read(ref _pending).Count;

    /// <summary>Returns shared transfer identities whose saved selection still belongs to this client's current launch configuration.</summary>
    public IReadOnlyList<string> TasksFor(string instanceId)
    {
        var instance = _instances.Load(instanceId);
        return Volatile.Read(ref _pending).Where(intent => !intent.LocalImport && intent.Instance == instanceId && intent.Game == instance.Game && intent.Build == instance.Build
            && ClientPolicyActivation.SelectionMatches(instance, intent.SelectionPlugin ?? intent.Plugin, intent.Selection)).Select(intent => intent.Task).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Scopes pending configuration and failures to one client; null returns the shared-library overview.</summary>
    public PluginActivationStatus Status(string? instanceId = null)
    {
        if (instanceId is not null) _ = _instances.Load(instanceId);
        var pending = Volatile.Read(ref _pending).Where(intent => instanceId is null || intent.Instance == instanceId).ToArray();
        return new(pending.Length, pending.Count(intent => _running(intent.Instance)),
            _restoreFailure ?? Volatile.Read(ref _maintenanceFailure)
                ?? pending.Select(intent => _failures.GetValueOrDefault(intent)).FirstOrDefault(failure => failure is not null));
    }

    /// <summary>Reuses one source-pinned library transfer and records only the client's unchanged enabled upgrade selection.</summary>
    public ClientDownloadTask Enqueue(StoreSource source, StorePackage package, byte[] manifest, string instanceId, bool explicitSelection = false, bool forceEnable = false,
        string? expectedSelection = null)
    {
        lock (_gate)
        {
            if (_restoreFailure is not null) throw new IOException("Saved plugin upgrade activations could not be read. The original records were retained.");
            if (forceEnable && !explicitSelection) throw new ArgumentException("Enabling requires an explicit component selection.", nameof(forceEnable));
            var instance = _instances.Load(instanceId);
            if (instance.Game != package.Game || package.Builds.Count > 0 && !package.Builds.Contains(instance.Build, StringComparer.Ordinal))
                throw new InvalidDataException("The plugin update does not support this client.");
            var selected = instance.Plugins.SingleOrDefault(p => p.Id == package.Id);
            selected ??= package.PluginDefinition is { } definition
                ? instance.Plugins.SingleOrDefault(selection => selection.Id == ClientPolicyActivation.SelectionId(instance, definition)) : null;
            var intentRequired = explicitSelection || selected is not null
                && (selected.Id != package.Id || PluginCatalog.CompareVersions(package.Version, selected.Version) > 0);
            var task = _downloads.PreloadPlugin(source, package, manifest);
            if (expectedSelection is not null && !ClientPolicyActivation.SelectionMatches(instance, selected?.Id ?? package.Id, expectedSelection)) return task;
            if (!intentRequired) return task;
            var next = _pending.Where(i => i.Instance != instanceId || i.Plugin != package.Id).ToList();
            next.Add(new(instanceId, instance.Game, instance.Build, package.Id, package.Version,
                ClientPolicyActivation.SelectionStamp(instance, selected?.Id ?? package.Id), task.Id, source.Id, StoreSourceIdentity.Of(source), package.ManifestSha256,
                explicitSelection, ForceEnable: forceEnable, SelectionPlugin: selected?.Id));
            Save(next); _pending = next;
            foreach (var previous in _failures.Keys.Where(i => i.Instance == instanceId && i.Plugin == package.Id)) _failures.TryRemove(previous, out _);
            return task;
        }
    }

    /// <summary>Retains a verified local import upgrade for each existing compatible selection, including running clients.</summary>
    public void EnqueueImported(PluginDefinition imported, GameDefinition game)
    {
        lock (_gate)
        {
            if (_restoreFailure is not null) throw new IOException("Saved plugin upgrade activations could not be read.");
            if (imported.Game != game.Id) throw new ArgumentException("The game does not own the imported package.");
            var snapshot = _library.ImportedSnapshot(imported);
            var next = new List<Intent>(_pending);
            foreach (var instance in _instances.List().Where(item => item.Game == game.Id))
            {
                var selectedId = ClientPolicyActivation.SelectionId(instance, imported);
                var selected = instance.Plugins.SingleOrDefault(item => item.Id == selectedId);
                var build = game.Builds.SingleOrDefault(item => item.Id == instance.Build);
                if (selected is null || build is null || selectedId == imported.Id && PluginCatalog.CompareVersions(imported.Version, selected.Version) <= 0
                    || !PluginCatalog.IsAvailable(imported, build)) continue;
                var prior = next.SingleOrDefault(item => item.Instance == instance.Id && item.Plugin == imported.Id);
                if (prior is not null && PluginCatalog.CompareVersions(prior.Version, imported.Version) > 0) continue;
                next.RemoveAll(item => item.Instance == instance.Id && item.Plugin == imported.Id);
                next.Add(new(instance.Id, game.Id, instance.Build, imported.Id, imported.Version,
                    ClientPolicyActivation.SelectionStamp(instance, selected.Id), Guid.NewGuid().ToString("N"),
                    "local-import", snapshot, snapshot, LocalImport: true, SelectionPlugin: selected.Id));
            }
            Save(next); _pending = next;
        }
    }

    /// <summary>Uninstalls a plugin while excluding upgrade application and transfer admission, then retires its pending activation records.</summary>
    public void Uninstall(GameDefinition game, string pluginId)
    {
        if (!TryUninstallDeprecated(game, pluginId))
            throw new InvalidOperationException("Wait for active downloads to finish before uninstalling plugins.");
    }

    /// <summary>Removes a retired plugin when no transfer can reinstall it concurrently; false requests a later retry.</summary>
    public bool TryUninstallDeprecated(GameDefinition game, string pluginId)
    {
        lock (_gate)
        {
            if (_restoreFailure is not null) throw new IOException("Saved plugin upgrade activations could not be read.");
            return _downloads.TryRunWhileIdle(() =>
            {
                _library.Uninstall(game, pluginId, _instances, _running);
                var next = _pending.Where(intent => intent.Game != game.Id || intent.Plugin != pluginId && intent.SelectionPlugin != pluginId).ToList();
                Save(next); _pending = next; _revision++; return true;
            });
        }
    }

    /// <summary>Applies completed shared packages; a client-specific pass excludes unrelated activations and library retirement.</summary>
    public void ApplyReady(string? instanceId = null, CancellationToken cancellationToken = default)
    {
        if (_restoreFailure is not null) { Failure = _restoreFailure; return; }
        lock (_applyGate)
        {
            Volatile.Write(ref _maintenanceFailure, null);
            try { ApplyReadyCore(instanceId, cancellationToken); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or DefinitionException or InvalidOperationException or ArgumentException or JsonException or FormatException)
            { Volatile.Write(ref _maintenanceFailure, Failure = error is IOException or UnauthorizedAccessException ? "disk" : "integrity"); }
        }
    }

    private void ApplyReadyCore(string? instanceId, CancellationToken cancellationToken)
    {
        Failure = null;
        if (instanceId is null) _library.CleanupUninstalled(_instances, _running);
        var verified = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var imported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool Verify(PluginDefinition plugin)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!verified.TryGetValue(plugin.Folder, out var valid))
                verified.Add(plugin.Folder, valid = _library.MatchesInstalledSnapshot(plugin, cancellationToken));
            return valid;
        }
        var games = new Dictionary<string, (GameDefinition Game, IReadOnlyList<PluginDefinition> Packages)>(StringComparer.Ordinal);
        (GameDefinition Game, IReadOnlyList<PluginDefinition> Packages) Packages(string id)
        {
            if (!games.TryGetValue(id, out var value))
            {
                var game = _game(id); value = (game, _library.Packages(game)); games.Add(id, value);
            }
            return value;
        }
        var tasks = _downloads.List().ToDictionary(task => task.Id, StringComparer.Ordinal);
        // Pending lists are replaced atomically. File verification must not hold the selection or catalog locks.
        foreach (var intent in Volatile.Read(ref _pending).Where(intent => instanceId is null || intent.Instance == instanceId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _failures.TryRemove(intent, out _);
            try
            {
                if (_running(intent.Instance)) continue;
                var instance = _instances.Load(intent.Instance);
                if (instance.Game != intent.Game || instance.Build != intent.Build || !ClientPolicyActivation.SelectionMatches(instance, intent.SelectionPlugin ?? intent.Plugin, intent.Selection))
                { Remove(intent); continue; }
                var (game, packages) = Packages(intent.Game);
                var plugin = packages.SingleOrDefault(p => p.Id == intent.Plugin && p.Version == intent.Version);
                if (intent.LocalImport)
                {
                    if (plugin is null) { Failed(intent, "integrity"); continue; }
                    if (!imported.TryGetValue(plugin.Folder, out var snapshot))
                        imported.Add(plugin.Folder, snapshot = _library.ImportedSnapshot(plugin, cancellationToken));
                    if (snapshot != intent.Hash) { Failed(intent, "integrity"); continue; }
                }
                else
                {
                    tasks.TryGetValue(intent.Task, out var task);
                    if (task is { Stage: "canceled" }) { Remove(intent); continue; }
                    if (task is not null && task.Stage != "completed") continue;
                    if (plugin is null || _library.Origin(plugin)?.ManifestSha256 != intent.Hash || !Verify(plugin))
                    { Failed(intent, "integrity"); continue; }
                }
                lock (_gate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_pending.Contains(intent) || _running(intent.Instance)) continue;
                    using var lease = _instances.Acquire(intent.Instance);
                    ClientPolicyActivation.EnableLeased(_instances, intent.Instance, game, packages, intent.Plugin, intent.Version, intent.Selection,
                        upgrade: intent.LocalImport || !intent.Explicit, explicitSelection: intent.Explicit, forceEnable: intent.ForceEnable,
                        selectionPlugin: intent.SelectionPlugin);
                    Remove(intent); Interlocked.Increment(ref _revision);
                }
            }
            catch (InstanceBusyException) { }
            catch (FileNotFoundException) when (!_instances.List().Any(i => i.Id == intent.Instance)) { Remove(intent); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or DefinitionException or InvalidOperationException or ArgumentException or JsonException or FormatException)
            { Failed(intent, error is IOException or UnauthorizedAccessException ? "disk" : "integrity"); }
        }
        if (instanceId is not null || PendingCount != 0 || !_removeOldVersions()) return;
        // Verify replacements once before taking download and client leases for retirement.
        var replacements = _library.SupersedingStorePackages();
        var folders = replacements.Where(Verify).Select(plugin => plugin.Folder).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (folders.Count == 0) return;
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_pending.Count == 0) _downloads.TryRunWhileIdle(() => RetireSupersededIdle(folders, cancellationToken));
        }
    }

    private void Remove(Intent intent)
    {
        lock (_gate)
        {
            if (!_pending.Contains(intent)) return;
            var next = _pending.Where(item => item != intent).ToList();
            Save(next); Volatile.Write(ref _pending, next);
            _failures.TryRemove(intent, out _);
        }
    }

    private void Failed(Intent intent, string failure)
    {
        lock (_gate)
        {
            if (!_pending.Contains(intent)) return;
            Failure = failure;
            _failures[intent] = failure;
        }
    }

    private bool RetireSupersededIdle(HashSet<string> verifiedFolders, CancellationToken cancellationToken)
    {
        var leases = new List<IDisposable>();
        try
        {
            var instances = _instances.List();
            foreach (var instance in instances)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_running(instance.Id)) return false;
                leases.Add(_instances.Acquire(instance.Id));
            }
            using var registry = _instances.AcquireNameRegistry();
            var current = _instances.List();
            if (!instances.Select(instance => instance.Id).SequenceEqual(current.Select(instance => instance.Id))) return false;
            foreach (var instance in current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var game = _game(instance.Game); var packages = _library.Packages(game);
                var build = game.Builds.Single(value => value.Id == instance.Build);
                foreach (var selection in instance.Plugins)
                {
                    var replacement = packages.FirstOrDefault(package => package.Id == selection.Id
                        && PluginCatalog.CompareVersions(package.Version, selection.Version) > 0
                        && PluginCatalog.IsAvailable(package, build) && verifiedFolders.Contains(package.Folder));
                    if (replacement is null) continue;
                    var latest = _instances.Load(instance.Id);
                    ClientPolicyActivation.EnableLeased(_instances, instance.Id, game, packages, selection.Id, replacement.Version,
                        ClientPolicyActivation.SelectionStamp(latest, selection.Id), upgrade: true, nameRegistryHeld: true);
                    Interlocked.Increment(ref _revision);
                }
            }
            var references = _instances.List().SelectMany(instance => instance.Plugins.Select(plugin => (instance.Game, plugin.Id, plugin.Version))).ToHashSet();
            if (_library.RetireSuperseded(references, verifiedFolders)) Interlocked.Increment(ref _revision);
            return true;
        }
        catch (InstanceBusyException) { return false; }
        finally { foreach (var lease in leases.AsEnumerable().Reverse()) lease.Dispose(); }
    }

    private void Save(List<Intent> next)
    {
        Validate(next);
        var temporary = _path + ".tmp"; InstanceStore.RejectLinks(_path); InstanceStore.RejectLinks(temporary);
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(file, next, Json); file.Flush(true); }
        File.Move(temporary, _path, true);
        foreach (var intent in _failures.Keys.Where(intent => !next.Contains(intent))) _failures.TryRemove(intent, out _);
    }

    private static void Validate(IReadOnlyList<Intent> intents)
    {
        if (intents.Any(i => i is null) || intents.Select(i => (i.Instance, i.Plugin)).Distinct().Count() != intents.Count)
            throw new InvalidDataException("Plugin upgrade activations contain null or duplicate records.");
        foreach (var intent in intents)
        {
            InstanceFile.ValidateId("plugin upgrade", intent.Instance); InstanceFile.ValidateId("plugin upgrade", intent.Source);
            PluginCatalog.ValidateId("plugin upgrade", "id", intent.Plugin); _ = PluginVersion.Parse(intent.Version);
            if (intent.SelectionPlugin is not null) PluginCatalog.ValidateId("plugin upgrade", "selection", intent.SelectionPlugin);
            if (intent.ForceEnable && (!intent.Explicit || intent.LocalImport))
                throw new InvalidDataException("Only explicit component selections can request enabling.");
            if (intent.Game.Length is < 1 or > 128 || intent.Build.Length is < 1 or > 128 || !Guid.TryParseExact(intent.Task, "N", out _)
                || new[] { intent.Selection, intent.Identity, intent.Hash }.Any(value => value.Length != 64 || !value.All(char.IsAsciiHexDigit)))
                throw new InvalidDataException("Invalid plugin upgrade activation identity.");
        }
    }
}
