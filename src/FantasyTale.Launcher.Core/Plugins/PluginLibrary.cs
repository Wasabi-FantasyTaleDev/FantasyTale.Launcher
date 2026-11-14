using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>A validated, temporary package snapshot awaiting explicit installation.</summary>
/// <param name="Token">Opaque handle valid only in this launcher session.</param>
/// <param name="Plugin">Manifest parsed from the staged copy, without executing any code.</param>
/// <param name="FileCount">Number of regular files included in the package.</param>
/// <param name="Size">Total uncompressed byte count.</param>
public sealed record PluginImportPreview(string Token, PluginDefinition Plugin, int FileCount, long Size);

/// <summary>Measured phase completion for one local plugin import; null means the phase total is not known yet.</summary>
/// <param name="Stage">Checking, copying, verifying, installing or completed phase.</param>
/// <param name="Percent">Actual completed work in the current phase, from zero to one hundred.</param>
public sealed record PluginImportProgress(string Stage, double? Percent);

/// <summary>An installed package rejected by the current manifest reader, retained without loading its code.</summary>
/// <param name="Folder">Original package folder, retained for diagnosis or replacement.</param>
/// <param name="Game">Validated game identity when its header can be read.</param>
/// <param name="Id">Validated plugin identity when its header can be read.</param>
/// <param name="Version">Validated package version when its header can be read.</param>
/// <param name="Message">Definition failure including its file and key.</param>
public sealed record PluginLoadIssue(string Folder, string? Game, string? Id, string? Version, string Message)
{
    /// <summary>The validated original store receipt identity, absent for unmanaged or untrusted packages.</summary>
    public string? SourceId { get; init; }
}

internal sealed record PluginStoreOrigin(string SourceId, string SourceIdentity, string ManifestSha256);

/// <summary>Imports immutable plugin versions into the per-user library without changing client selections.</summary>
public sealed class PluginLibrary : IDisposable
{
    private readonly string _root, _packages, _staging;
    private readonly object _gate = new();
    private Pending? _pending;
    private IReadOnlyList<PluginLoadIssue> _loadIssues = [];
    private bool _disposed;
    private sealed record FileStamp(string Path, long Size, string Hash);
    private sealed record Pending(PluginImportPreview Preview, string Folder, FileStamp[] Files);
    private const string SourceReceiptName = ".fantasytale-source.json";
    private const string UninstalledMarker = ".fantasytale-uninstalled";
    private sealed record SourceReceipt(string SourceId, string SourceIdentity, string ManifestSha256)
    {
        public FileStamp[]? Files { get; init; }
    }

    internal PluginStoreOrigin? Origin(PluginDefinition plugin)
        => Origin(plugin.Folder);

    internal PluginStoreOrigin? Origin(string folder)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!InstanceStore.IsWithin(_packages, folder)) return null;
            var path = Safe(folder, SourceReceiptName);
            if (!File.Exists(path)) return null;
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var receipt = JsonSerializer.Deserialize<SourceReceipt>(input) ?? throw new InvalidDataException("Invalid plugin source receipt.");
            if (string.IsNullOrWhiteSpace(receipt.SourceId) || receipt.SourceIdentity is not { Length: 64 } ||
                !receipt.SourceIdentity.All(char.IsAsciiHexDigit) || receipt.ManifestSha256 is not { Length: 64 } || !receipt.ManifestSha256.All(char.IsAsciiHexDigit))
                throw new InvalidDataException("Invalid plugin source receipt.");
            PluginCatalog.ValidateId(path, "SourceId", receipt.SourceId);
            return new(receipt.SourceId, receipt.SourceIdentity, receipt.ManifestSha256);
        }
    }

    internal bool MatchesInstalledSnapshot(PluginDefinition plugin, CancellationToken cancellationToken = default)
    {
        FileStream input;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!InstanceStore.IsWithin(_packages, plugin.Folder)) return false;
            input = new FileStream(Safe(plugin.Folder, SourceReceiptName), FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        // Keep the receipt open against replacement without holding the catalog lock during payload hashing.
        using (input)
        {
            var receipt = JsonSerializer.Deserialize<SourceReceipt>(input);
            return receipt?.Files is { Length: > 0 } files && files.SequenceEqual(Snapshot(plugin.Folder, cancellationToken, trusted: true));
        }
    }

    internal string ImportedSnapshot(PluginDefinition plugin, CancellationToken cancellationToken = default)
    {
        FileStream manifest;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!InstanceStore.IsWithin(_packages, plugin.Folder) || Origin(plugin) is not null)
                throw new InvalidDataException("The package is not an installed local import.");
            manifest = new FileStream(Safe(plugin.Folder, "plugin.toml"), FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        using (manifest)
            return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Snapshot(plugin.Folder, cancellationToken))));
    }

    /// <summary>
    /// Raised after a new package version is committed, outside the library lock, with the other installed versions of
    /// the same package as they were before it. Plugin policy uses it to offer clients a build the upgrade newly supports.
    /// </summary>
    public event Action<PluginDefinition, IReadOnlyList<PluginDefinition>>? PackageInstalled;

    private void Announce(PluginDefinition installed, IReadOnlyList<PluginDefinition>? previous)
    {
        if (previous is not null) PackageInstalled?.Invoke(installed, previous);
    }

    /// <summary>Installs catalog-pinned plugin bytes without enabling them; an exact completed installation is safe to retry after process loss.</summary>
    public PluginDefinition InstallFromStore(string source, GameDefinition game, BuildDefinition build, StorePackage package,
        byte[] manifest, string sourceId, string sourceIdentity, CancellationToken cancellationToken = default)
    {
        LauncherCompatibility.Require(package.MinimumLauncherVersion, package.Id);
        IReadOnlyList<PluginDefinition>? previousVersions = null;
        PluginDefinition result;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (package.Kind != "plugin" || package.Game != game.Id || package.Builds.Count > 0 && !package.Builds.Contains(build.Id, StringComparer.Ordinal))
                throw new InvalidDataException("The store plugin does not match this client.");
            var receipt = new SourceReceipt(sourceId, sourceIdentity, package.ManifestSha256);
            var files = ClientRelease.Parse(package, manifest);
            var snapshot = Snapshot(source, cancellationToken);
            if (snapshot.Length != files.Count || snapshot.Any(s => !files.Any(f => f.Path == s.Path && f.Size == s.Size && f.Sha256.Equals(s.Hash, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("Plugin contents do not match the verified store manifest.");

            var previous = _pending;
            var token = Guid.NewGuid().ToString("N"); var folder = Safe(_staging, token);
            var transport = Safe(_staging, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                using (var lease = Acquire())
                {
                    if (files[0].PluginArchive)
                    {
                        Directory.CreateDirectory(transport);
                        CopyFolder(source, transport, null, cancellationToken);
                        if (!snapshot.SequenceEqual(Snapshot(transport, cancellationToken))) throw new InvalidDataException("The store archive changed while copying.");
                        ExtractZip(Safe(transport, files[0].Path), folder, null, cancellationToken);
                        if (!snapshot.SequenceEqual(Snapshot(transport, cancellationToken))) throw new InvalidDataException("The store archive changed while extracting.");
                    }
                    else CopyFolder(source, folder, null, cancellationToken);
                }
                var plugin = ValidatePackage(folder, game, build, fromStore: true, sourceId);
                if (plugin.Id != package.Id || plugin.Version != package.Version)
                    throw new InvalidDataException("The plugin manifest identity does not match the selected store release.");
                if ((package.PluginDefinition is not null || package.MinimumLauncherVersion.Length > 0)
                    && (plugin.MinimumLauncherVersion ?? "") != package.MinimumLauncherVersion)
                    throw new InvalidDataException("The plugin minimum launcher version does not match its catalog metadata.");
                var copied = Snapshot(folder, cancellationToken);
                if (!files[0].PluginArchive && !snapshot.SequenceEqual(copied)) throw new InvalidDataException("The store package changed while copying.");
                var existing = Installed().SingleOrDefault(p => p.Game == game.Id && p.Id == package.Id && p.Version == package.Version);
                if (existing is not null)
                {
                    using var lease = Acquire();
                    var actual = Snapshot(existing.Folder, cancellationToken, trusted: true);
                    if (!copied.SequenceEqual(actual)) throw new InvalidDataException("The installed plugin bytes changed.");
                    _ = ValidatePackage(existing.Folder, game, build, fromStore: true, sourceId);
                    return existing;
                }
                _pending = new(new(token, plugin, copied.Length, copied.Sum(f => f.Size)), folder, copied);
                result = InstallCore(token, game, build, receipt, null, cancellationToken, out previousVersions);
            }
            finally { _pending = previous; DeleteStaging(folder); DeleteStaging(transport); }
        }
        Announce(result, previousVersions);
        return result;
    }

    /// <summary>Opens the library and removes abandoned import staging after acquiring its exclusive lease.</summary>
    public PluginLibrary(string root)
    {
        _root = Path.GetFullPath(root);
        _packages = Safe(_root, "packages"); _staging = Safe(_root, "staging");
        Directory.CreateDirectory(_root);
        using var lease = Acquire();
        Directory.CreateDirectory(_packages); Directory.CreateDirectory(_staging);
        foreach (var folder in Directory.EnumerateDirectories(_staging)) DeleteStaging(folder);
        if (Directory.EnumerateFileSystemEntries(_staging).Any()) throw new IOException("Plugin staging contains unexpected entries.");
    }

    /// <summary>Combines bundled versions with installed packages, allowing only declared store-managed optional identities.</summary>
    public IReadOnlyList<PluginDefinition> Packages(GameDefinition game)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!game.SupportsPlugins) return [];
            var bundled = PluginCatalog.Load(Path.Combine(game.Folder, "plugins"));
            var imported = new List<PluginDefinition>();
            var installed = Installed(); var issues = _loadIssues.ToList();
            foreach (var package in installed.Where(p => p.Game == game.Id))
            {
                try
                {
                    var origin = Origin(package);
                    ValidateIdentity(package, game, bundled, origin is not null, origin?.SourceId);
                    imported.Add(package);
                }
                catch (Exception error) when (IsPackageFailure(error)) { issues.Add(ReadIssue(package.Folder, error)); }
            }
            _loadIssues = issues;
            var packages = imported.Concat(bundled).GroupBy(p => (p.Id, p.Version)).Select(group => group.First()).ToArray();
            // Installed predecessors remain valid while a verified newer identity is downloaded and activated.
            PluginCatalog.ValidateIdentityConflicts(packages, _packages, allowInstalledPredecessors: true);
            return packages.OrderBy(p => p.Id, StringComparer.Ordinal)
                .ThenByDescending(p => PluginVersion.Parse(p.Version)).ToArray();
        }
    }

    /// <summary>Returns individual installed-package errors without turning a rejected package into a loadable definition.</summary>
    public IReadOnlyList<PluginLoadIssue> LoadIssues()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _loadIssues;
        }
    }

    /// <summary>Finds only a rejected library directory belonging to the exact selected release.</summary>
    public PluginLoadIssue? RejectedSelection(string game, string id, string version)
    {
        lock (_gate)
        {
            var path = Safe(_packages, PackageKey(game, id, version));
            return _loadIssues.FirstOrDefault(issue => issue.Folder.Equals(path, StringComparison.OrdinalIgnoreCase)
                && (issue.Game is null || issue.Game == game) && (issue.Id is null || issue.Id == id)
                && (issue.Version is null || issue.Version == version));
        }
    }

    /// <summary>Identifies packages owned by the writable shared library; bundled application files are immutable.</summary>
    public bool CanUninstall(PluginDefinition plugin)
        => string.Equals(Path.GetDirectoryName(Path.GetFullPath(plugin.Folder)), _packages, StringComparison.OrdinalIgnoreCase);

    /// <summary>Detaches managed releases for future launches, retaining their immutable payloads until running sessions release them.</summary>
    public void Uninstall(GameDefinition game, string id, InstanceStore instances, Func<string, bool> running)
    {
        PluginCatalog.ValidateId("uninstall", "id", id);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var libraryLease = Acquire();
            var inventory = Packages(game);
            // Shared data stays while any installed package still declares a requirement on it.
            var others = inventory.Where(package => package.Id != id).ToArray();
            var dependents = inventory.Where(package => package.Id == id)
                .SelectMany(package => PluginRequirements.Dependents(package, others))
                .Select(dependent => dependent.Id).Distinct(StringComparer.Ordinal).ToArray();
            if (dependents.Length > 0)
                throw new InvalidOperationException($"'{id}' is required by {string.Join(", ", dependents.Select(dependent => $"'{dependent}'"))}. Uninstall those first.");
            var folders = Installed().Where(package => package.Game == game.Id && package.Id == id).Select(package => package.Folder)
                .Concat(_loadIssues.Where(issue => issue.Game == game.Id && issue.Id == id).Select(issue => issue.Folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (folders.Length == 0) throw new InvalidOperationException("This plugin is bundled with the Launcher or is no longer installed.");
            foreach (var folder in folders)
            {
                if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(folder)), _packages, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The plugin is outside the managed library.");
                InstanceStore.RejectLinks(folder); _ = FileTree(folder, trusted: true);
            }
            var affected = instances.List().Where(instance => instance.Game == game.Id && instance.Plugins.Any(plugin => plugin.Id == id)).ToArray();
            var leases = new List<IDisposable>();
            try
            {
                foreach (var instance in affected)
                {
                    if (!running(instance.Id)) leases.Add(instances.Acquire(instance.Id));
                }
                using var registry = instances.AcquireNameRegistry();
                if (instances.List().Any(instance => instance.Game == game.Id && instance.Plugins.Any(plugin => plugin.Id == id)
                    && !affected.Any(original => original.Id == instance.Id))) throw new InvalidOperationException("The client list changed. Try uninstalling again.");
                foreach (var instance in affected)
                {
                    var current = instances.Load(instance.Id);
                    instances.SaveLeased(current with { Plugins = current.Plugins.Where(plugin => plugin.Id != id).ToArray() }, current, nameRegistryHeld: true);
                }
                foreach (var folder in folders)
                {
                    File.WriteAllText(Safe(folder, UninstalledMarker), "");
                }
                _loadIssues = _loadIssues.Where(issue => issue.Game != game.Id || issue.Id != id).ToArray();
            }
            finally { foreach (var lease in leases.AsEnumerable().Reverse()) lease.Dispose(); }
        }
        CleanupUninstalled(instances, running);
    }

    /// <summary>Reclaims logically uninstalled packages only while every registered client is idle and exclusively leased.</summary>
    public void CleanupUninstalled(InstanceStore instances, Func<string, bool> running)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var folders = Directory.EnumerateDirectories(_packages).Where(folder => File.Exists(Safe(folder, UninstalledMarker))).ToArray();
            if (folders.Length == 0) return;
            using var libraryLease = Acquire();
            folders = Directory.EnumerateDirectories(_packages).Where(folder => File.Exists(Safe(folder, UninstalledMarker))).ToArray();
            var leases = new List<IDisposable>();
            try
            {
                foreach (var instance in instances.List())
                {
                    if (running(instance.Id)) return;
                    leases.Add(instances.Acquire(instance.Id));
                }
                using var registry = instances.AcquireNameRegistry();
                foreach (var folder in folders)
                {
                    InstanceStore.RejectLinks(folder); _ = FileTree(folder, trusted: true);
                    var removed = Safe(_staging, Guid.NewGuid().ToString("N"));
                    Directory.Move(folder, removed); DeleteStaging(removed);
                }
            }
            catch (InstanceBusyException) { }
            finally { foreach (var lease in leases.AsEnumerable().Reverse()) lease.Dispose(); }
        }
    }

    /// <summary>Detaches a rejected package for future launches and retains its payload until every client is idle.</summary>
    public void UninstallRejected(GameDefinition game, string folder, InstanceStore instances, Func<string, bool> running)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var libraryLease = Acquire();
            var packages = Packages(game);
            var full = Path.GetFullPath(folder);
            var issue = _loadIssues.SingleOrDefault(item => item.Folder.Equals(full, StringComparison.OrdinalIgnoreCase)
                && (item.Game is null || item.Game == game.Id))
                ?? throw new InvalidOperationException("This rejected package changed. Refresh the plugin library.");
            if (!Path.GetDirectoryName(full)!.Equals(_packages, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Only rejected packages directly inside the plugin library can be uninstalled.");
            InstanceStore.RejectLinks(full);
            _ = FileTree(full, trusted: true);
            var detach = issue.Id is not null && issue.Version is not null
                && !packages.Any(package => package.Id == issue.Id && package.Version == issue.Version);
            var affected = instances.List().Where(instance => instance.Game == game.Id && detach
                && instance.Plugins.Any(selection => selection.Id == issue.Id && selection.Version == issue.Version)).ToArray();
            var leases = new List<IDisposable>();
            try
            {
                foreach (var instance in affected)
                {
                    if (!running(instance.Id)) leases.Add(instances.Acquire(instance.Id));
                }
                foreach (var instance in affected)
                {
                    var current = instances.Load(instance.Id);
                    instances.SaveLeased(current with { Plugins = current.Plugins.Where(selection => selection.Id != issue.Id || selection.Version != issue.Version).ToArray() }, current);
                }
                File.WriteAllText(Safe(full, UninstalledMarker), "");
                _loadIssues = _loadIssues.Where(item => item.Folder != issue.Folder).ToArray();
            }
            finally { foreach (var lease in leases.AsEnumerable().Reverse()) lease.Dispose(); }
        }
        CleanupUninstalled(instances, running);
    }

    /// <summary>Copies a folder or ZIP to staging, validates it for a client build and returns an immutable preview.</summary>
    public PluginImportPreview Prepare(string source, GameDefinition game, BuildDefinition build, CancellationToken cancellationToken = default)
        => Prepare(source, game, build, null, cancellationToken);

    /// <summary>Verifies one package for the shared game library without requiring or selecting a client instance.</summary>
    public PluginImportPreview Prepare(string source, GameDefinition game, Action<PluginImportProgress>? progress = null, CancellationToken cancellationToken = default)
        => Prepare(source, game, null, progress, cancellationToken);

    /// <summary>Stages and verifies an import while reporting measured copy, extraction and hash progress.</summary>
    public PluginImportPreview Prepare(string source, GameDefinition game, BuildDefinition? build, Action<PluginImportProgress>? progress, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var lease = Acquire();
            ClearPending();
            if (!Path.IsPathFullyQualified(source)) throw new ArgumentException("Select an absolute plugin package path.");
            source = Path.GetFullPath(source); InstanceStore.RejectLinks(source);
            if (source.Equals(_root, StringComparison.OrdinalIgnoreCase) || InstanceStore.IsWithin(_root, source) || InstanceStore.IsWithin(source, _root))
                throw new IOException("The import source overlaps the plugin library.");
            var token = Guid.NewGuid().ToString("N"); var folder = Safe(_staging, token);
            Directory.CreateDirectory(folder);
            try
            {
                if (Directory.Exists(source)) CopyFolder(source, folder, progress, cancellationToken);
                else ExtractZip(source, folder, progress, cancellationToken);
                progress?.Invoke(new("checking", null));
                var plugin = ValidatePackage(folder, game, build);
                var installed = Installed();
                if (installed.Any(p => p.Game == plugin.Game && p.Id == plugin.Id && p.Version == plugin.Version))
                    throw new InvalidOperationException("This plugin version is already installed. Publish changed contents with a new version.");
                var files = Snapshot(folder, progress, false, cancellationToken);
                var preview = new PluginImportPreview(token, plugin, files.Length, files.Sum(f => f.Size));
                _pending = new(preview, folder, files);
                progress?.Invoke(new("completed", 100));
                return preview;
            }
            catch { DeleteStaging(folder); throw; }
        }
    }

    /// <summary>Rechecks the previewed bytes and atomically installs a new version; existing versions are never overwritten.</summary>
    public PluginDefinition Install(string token, GameDefinition game, BuildDefinition build, CancellationToken cancellationToken = default)
        => Install(token, game, build, null, null, cancellationToken);

    /// <summary>Commits a verified package to the shared library without changing any client's selection or settings.</summary>
    public PluginDefinition Install(string token, GameDefinition game, Action<PluginImportProgress>? progress = null, CancellationToken cancellationToken = default)
        => Install(token, game, null, null, progress, cancellationToken);

    /// <summary>Installs a preview while reporting revalidation and atomic commit progress.</summary>
    public PluginDefinition Install(string token, GameDefinition game, BuildDefinition build, Action<PluginImportProgress> progress, CancellationToken cancellationToken = default)
        => Install(token, game, build, null, progress, cancellationToken);

    private PluginDefinition Install(string token, GameDefinition game, BuildDefinition? build, SourceReceipt? receipt, Action<PluginImportProgress>? progress, CancellationToken cancellationToken)
    {
        var installed = InstallCore(token, game, build, receipt, progress, cancellationToken, out var previous);
        Announce(installed, previous);
        return installed;
    }

    private PluginDefinition InstallCore(string token, GameDefinition game, BuildDefinition? build, SourceReceipt? receipt, Action<PluginImportProgress>? progress,
        CancellationToken cancellationToken, out IReadOnlyList<PluginDefinition>? previousVersions)
    {
        previousVersions = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var lease = Acquire();
            var pending = _pending is { } p && p.Preview.Token == token ? p : throw new InvalidOperationException("Select the plugin package again; its preview is no longer available.");
            var current = Snapshot(pending.Folder, progress, false, cancellationToken);
            if (!current.SequenceEqual(pending.Files)) throw new IOException("The staged package changed after preview. Select it again.");
            var plugin = ValidatePackage(pending.Folder, game, build, receipt is not null, receipt?.SourceId);
            var installed = Installed();
            if (installed.Any(p => p.Game == plugin.Game && p.Id == plugin.Id && p.Version == plugin.Version)) throw new IOException("This plugin version is already installed.");
            previousVersions = installed.Where(p => p.Game == plugin.Game && p.Id == plugin.Id).ToArray();
            var key = PackageKey(plugin.Game, plugin.Id, plugin.Version);
            var destination = Safe(_packages, key);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(Safe(destination, UninstalledMarker)))
            {
                if (!Snapshot(destination, default, trusted: true).SequenceEqual(current))
                    throw new IOException("The retained plugin payload differs from the requested release. Close running games before reinstalling it.");
                if (receipt is not null)
                {
                    using var output = new FileStream(Safe(destination, SourceReceiptName), FileMode.Create, FileAccess.Write, FileShare.None);
                    output.Write(JsonSerializer.SerializeToUtf8Bytes(receipt with { Files = current })); output.Flush(true);
                }
                File.Delete(Safe(destination, UninstalledMarker));
                ClearPending();
                progress?.Invoke(new("completed", 100));
                return plugin with { Folder = destination };
            }
            if (receipt is not null)
            {
                using var output = new FileStream(Safe(pending.Folder, SourceReceiptName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                output.Write(JsonSerializer.SerializeToUtf8Bytes(receipt with { Files = current })); output.Flush(true);
            }
            progress?.Invoke(new("installing", null));
            Directory.Move(pending.Folder, destination);
            _pending = null;
            progress?.Invoke(new("completed", 100));
            return plugin with { Folder = destination };
        }
    }

    /// <summary>Discards a matching preview, leaving installed versions and all client selections untouched.</summary>
    public void Discard(string token)
    {
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); using var lease = Acquire(); if (_pending?.Preview.Token == token) ClearPending(); }
    }

    /// <summary>Deletes this session's pending import without removing installed packages.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_pending is not null) { using var lease = Acquire(); ClearPending(); }
            _disposed = true;
        }
    }

    private List<PluginDefinition> Installed()
    {
        var result = new List<PluginDefinition>();
        var issues = new List<PluginLoadIssue>();
        foreach (var folder in Directory.EnumerateDirectories(_packages))
        {
            if (File.Exists(Safe(folder, UninstalledMarker))) continue;
            try
            {
                InstanceStore.RejectLinks(folder); InstanceStore.RejectLinks(Path.Combine(folder, "plugin.toml"));
                var package = PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml"));
                _ = PluginConfigurationUi.Load(package);
                if (package.Library is { } library && !File.Exists(Safe(folder, library)))
                    throw new InvalidDataException("The installed native plugin library is missing.");
                foreach (var payload in package.Files)
                {
                    var file = new FileInfo(Safe(folder, payload.Source));
                    if (!file.Exists || file.Length != payload.Size)
                        throw new InvalidDataException("An installed file-plugin payload is missing or has an invalid size.");
                }
                result.Add(package);
            }
            catch (Exception error) when (IsPackageFailure(error))
            {
                // Header identity is sufficient for source-bound repair; rejected rules never become runtime policy.
                issues.Add(ReadIssue(folder, error));
            }
        }
        _loadIssues = issues;
        if (result.GroupBy(p => (p.Game, p.Id, p.Version)).Any(group => group.Count() != 1)) throw new IOException("Duplicate plugin identities in the library.");
        return result;
    }

    private static string PackageKey(string game, string id, string version) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{game}\n{id}\n{version}"))).ToLowerInvariant();

    private static bool IsPackageFailure(Exception error) => error is DefinitionException or IOException or UnauthorizedAccessException or
        InvalidDataException or JsonException or FormatException or OverflowException;

    private PluginLoadIssue ReadIssue(string folder, Exception error)
    {
        try
        {
            InstanceStore.RejectLinks(folder);
            var file = Safe(folder, "plugin.toml");
            var header = TomlReader.Load(file).Table("plugin", true)!;
            var game = header.String("game", true); var id = header.String("id", true); var version = header.String("version", true);
            PluginCatalog.ValidateId(file, "plugin.game", game); PluginCatalog.ValidateId(file, "plugin.id", id);
            _ = PluginVersion.Parse(version);
            string? sourceId = null;
            try { sourceId = Origin(folder)?.SourceId; }
            catch (Exception invalid) when (IsPackageFailure(invalid)) { }
            return new(folder, game, id, version, error.Message) { SourceId = sourceId };
        }
        catch (Exception invalid) when (IsPackageFailure(invalid))
        { return new(folder, null, null, null, error.Message); }
    }

    internal bool HasSupersededStorePackages()
        => SupersedingStorePackages().Any(plugin => MatchesInstalledSnapshot(plugin));

    internal IReadOnlyList<PluginDefinition> SupersedingStorePackages()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var installed = Installed();
            return installed.Where(candidate => Origin(candidate) is not null &&
                (installed.Any(previous => previous.Game == candidate.Game && previous.Id == candidate.Id && PluginCatalog.CompareVersions(candidate.Version, previous.Version) > 0)
                || _loadIssues.Any(issue => issue.Game == candidate.Game && issue.Id == candidate.Id && issue.Version is not null && PluginCatalog.CompareVersions(candidate.Version, issue.Version) > 0))).ToArray();
        }
    }

    private bool HasStoreReplacement(PluginDefinition plugin, IReadOnlyList<PluginDefinition> installed, Func<PluginDefinition, bool> verified)
    {
        // Client references protect versions still needed by an unsupported build.
        return installed.Any(other => other.Game == plugin.Game && other.Id == plugin.Id
            && PluginCatalog.CompareVersions(other.Version, plugin.Version) > 0
            && Origin(other) is not null
            && verified(other));
    }

    private bool HasRejectedReplacement(PluginLoadIssue issue, IReadOnlyList<PluginDefinition> installed, Func<PluginDefinition, bool> verified)
    {
        if (issue.Game is null || issue.Id is null || issue.Version is null) return false;
        try
        {
            return installed.Any(other => other.Game == issue.Game && other.Id == issue.Id
                && PluginCatalog.CompareVersions(other.Version, issue.Version) > 0
                && Origin(other) is not null
                && verified(other));
        }
        catch (Exception error) when (IsPackageFailure(error)) { return false; }
    }

    internal bool RetireSuperseded(IReadOnlySet<(string Game, string Plugin, string Version)> references)
        => RetireSuperseded(references, SupersedingStorePackages().Where(plugin => MatchesInstalledSnapshot(plugin)).Select(plugin => plugin.Folder).ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Removes local imports that a newer installed version of the same package supersedes and no client selects; store
    /// releases are retired separately after their replacement is verified. Returns whether a package was removed.
    /// </summary>
    internal bool RetireSupersededImports(IReadOnlySet<(string Game, string Plugin, string Version)> references)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var lease = Acquire();
            var installed = Installed(); var changed = false;
            foreach (var plugin in installed)
            {
                if (Origin(plugin) is not null || references.Contains((plugin.Game, plugin.Id, plugin.Version))) continue;
                if (!installed.Any(other => other.Game == plugin.Game && other.Id == plugin.Id
                    && PluginCatalog.CompareVersions(other.Version, plugin.Version) > 0)) continue;
                _ = FileTree(plugin.Folder, trusted: true);
                var quarantine = Safe(_staging, Guid.NewGuid().ToString("N"));
                Directory.Move(plugin.Folder, quarantine);
                DeleteStaging(quarantine); changed = true;
            }
            return changed;
        }
    }

    internal bool RetireSuperseded(IReadOnlySet<(string Game, string Plugin, string Version)> references, IReadOnlySet<string> verifiedFolders)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var lease = Acquire();
            var installed = Installed(); var changed = false;
            foreach (var plugin in installed)
            {
                if (references.Contains((plugin.Game, plugin.Id, plugin.Version))) continue;
                // References retain releases that cannot be replaced on an installed build.
                if (!HasStoreReplacement(plugin, installed, other => verifiedFolders.Contains(other.Folder))) continue;
                _ = FileTree(plugin.Folder, trusted: true);
                var quarantine = Safe(_staging, Guid.NewGuid().ToString("N"));
                Directory.Move(plugin.Folder, quarantine);
                DeleteStaging(quarantine); changed = true;
            }
            foreach (var issue in _loadIssues)
            {
                if (references.Contains((issue.Game!, issue.Id!, issue.Version!)) || !HasRejectedReplacement(issue, installed, other => verifiedFolders.Contains(other.Folder))) continue;
                // Preserve rejected bytes for diagnosis outside the active package index; never infer their former build scope.
                var retained = Safe(_root, "rejected"); Directory.CreateDirectory(retained);
                Directory.Move(issue.Folder, Safe(retained, Guid.NewGuid().ToString("N"))); changed = true;
            }
            return changed;
        }
    }

    private static PluginDefinition ValidatePackage(string folder, GameDefinition game, BuildDefinition? build, bool fromStore = false, string? sourceId = null)
    {
        var plugin = PluginCatalog.LoadFile(Safe(folder, "plugin.toml"));
        _ = PluginConfigurationUi.Load(plugin);
        ValidateIdentity(plugin, game, PluginCatalog.Load(Path.Combine(game.Folder, "plugins")), fromStore, sourceId);
        foreach (var target in build is null ? game.Builds.Where(b => PluginCatalog.IsAvailable(plugin, b)) : [build])
        {
            _ = PluginCatalog.ResolveOptions(plugin, target, new Dictionary<string, object>());
            if (plugin.Kind == "files")
                _ = Hooks.FilePluginResolver.Resolve(new InstanceDefinition("validation", "Validation", game.Id, target.Id, null,
                    [new(plugin.Id, plugin.Version, true, false, new Dictionary<string, object>())]), target, [plugin]);
        }
        if (plugin.Assets is { } assets)
        {
            // An assets package without its data folder or without any file in it is a packaging error, not an empty pack.
            var data = Safe(folder, ClientRelease.ValidatePath(assets.Root));
            if (!Directory.Exists(data) || FileTree(data).Count == 0) throw new InvalidDataException("The assets package does not contain files under its data folder.");
        }
        if (plugin.Library is { } library)
        {
            using var input = File.OpenRead(Safe(folder, ClientRelease.ValidatePath(library)));
            using var pe = new PEReader(input);
            if (pe.PEHeaders.CoffHeader.Machine != Machine.I386 || pe.PEHeaders.PEHeader?.Magic != PEMagic.PE32 || pe.PEHeaders.CorHeader is not null ||
                (pe.PEHeaders.CoffHeader.Characteristics & Characteristics.Dll) == 0)
                throw new InvalidDataException("Native plugins must contain a valid x86 Windows DLL.");
        }
        return plugin;
    }

    private static void ValidateIdentity(PluginDefinition plugin, GameDefinition game, IReadOnlyList<PluginDefinition> bundled, bool fromStore = false, string? sourceId = null)
    {
        if (!game.SupportsPlugins || plugin.Game != game.Id) throw new InvalidDataException("The plugin does not belong to this game or the game does not support plugins.");
        var reservedId = PluginCatalog.CanonicalId(bundled, plugin.Id);
        var localComponent = !fromStore && game.StoreManagedPlugins.Contains(reservedId, StringComparer.Ordinal);
        if (game.PluginSources.TryGetValue(reservedId, out var declared))
        {
            if ((!fromStore && !localComponent) || plugin.UpdateSource != declared)
                throw new InvalidDataException("The component update source does not match its game definition.");
            return;
        }
        // Only identities explicitly declared as store-managed are reserved. The
        // game catalog also carries optional plugin manifests so they can be
        // discovered and validated; those manifests must remain replaceable by a
        // local test package without making client creation fail.
        var allPeers = bundled.Where(p => PluginCatalog.MatchesIdentity(p, plugin.Id)).ToArray();
        var peers = allPeers.Where(p => game.StoreManagedPlugins.Contains(p.Id, StringComparer.Ordinal)).ToArray();
        var policy = peers.Where(p => p.UpdateSource.Length > 0)
            .OrderByDescending(p => PluginVersion.Parse(p.Version)).FirstOrDefault();
        var controlled = fromStore && policy is not null;
        if (allPeers.Length > 0 && fromStore && peers.Length == 0)
            throw new InvalidDataException("The store package targets a bundled plugin identity that is not store-managed.");
        if (peers.Length > 0)
        {
            if (!game.StoreManagedPlugins.Contains(reservedId, StringComparer.Ordinal) || !fromStore && (!localComponent || policy is null))
                throw new InvalidDataException("Bundled plugin identities are reserved and cannot be replaced by imported packages.");
            if (policy is not null && (plugin.UpdateSource != policy.UpdateSource
                // Historical releases keep their installation policy until the latest-version gate upgrades them.
                || PluginCatalog.CompareVersions(plugin.Version, policy.Version) >= 0 && plugin.RestoreFiles != policy.RestoreFiles))
                throw new InvalidDataException("The component update source or required policy does not match its bundled definition.");
            var sameVersion = peers.SingleOrDefault(p => p.Version == plugin.Version);
            // A confirmed local component import retains its declared update channel.
            // Reusing a bundled version still requires its exact bytes and restoration policy.
            if (sameVersion is not null && !controlled && !Snapshot(sameVersion.Folder, default).SequenceEqual(Snapshot(plugin.Folder, default, trusted: true)))
                throw new InvalidDataException("This bundled plugin version has different bytes. Publish a new version instead.");
        }
    }

    private static void CopyFolder(string source, string destination, Action<PluginImportProgress>? progress, CancellationToken ct)
    {
        var files = FileTree(source, cancellationToken: ct);
        var total = files.Sum(file => new FileInfo(file.Full).Length);
        long complete = 0;
        progress?.Invoke(new("copying", total == 0 ? 100 : 0));
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var target = Safe(destination, file.Relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = new FileStream(file.Full, FileMode.Open, FileAccess.Read, FileShare.Read);
            CopyFile(input, target, input.Length, count => progress?.Invoke(new("copying", total == 0 ? 100 : Math.Min(100, (double)(complete + count) * 100 / total))), ct);
            complete += input.Length;
        }
    }

    private static void ExtractZip(string source, string destination, Action<PluginImportProgress>? progress, CancellationToken ct)
    {
        if (!Path.GetExtension(source).Equals(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Select a plugin folder or a ZIP archive.");
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        CheckZipDirectory(input);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Plugin archive import requires the packaged Windows 7-Zip decoder.");
        using var archive = new NativeArchiveDecoder("zip", [Path.GetFileName(source)], [input], null, ct);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(NativeArchiveEntry Entry, string Path)>();
        foreach (var entry in archive.Entries)
        {
            var directory = entry.Directory;
            var path = entry.Path;
            if (!paths.Add(path)) throw new InvalidDataException("Duplicate or case-aliased ZIP paths.");
            if (directory) { if (entry.Size != 0) throw new InvalidDataException("ZIP directory contains data."); continue; }
            files.Add((entry, path));
        }
        // Accept either the package root or one enclosing folder, never a recursive manifest search.
        var roots = files.Where(f => f.Path.Equals("plugin.toml", StringComparison.OrdinalIgnoreCase) ||
            f.Path.Count(c => c == '/') == 1 && f.Path.EndsWith("/plugin.toml", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (roots.Length != 1) throw new InvalidDataException("The ZIP must contain one plugin.toml at its root or inside one enclosing folder.");
        var prefix = roots[0].Path[..^"plugin.toml".Length];
        var expanded = new Dictionary<string, (string Path, bool Directory)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!file.Path.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("ZIP contains files outside the plugin folder.");
            var relative = file.Path[prefix.Length..];
            AddPath(relative, false);
            for (var at = relative.IndexOf('/'); at >= 0; at = relative.IndexOf('/', at + 1)) AddPath(relative[..at], true);
        }
        void AddPath(string path, bool directory)
        {
            if (expanded.TryGetValue(path, out var existing) && (existing.Path != path || existing.Directory != directory))
                throw new InvalidDataException("ZIP paths conflict or alias another entry.");
            expanded[path] = (path, directory);
        }
        archive.Extract(entry =>
        {
            var target = Safe(destination, entry.Path[prefix.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            return target;
        }, (_, _) => { }, percent => progress?.Invoke(new("copying", percent)));
    }

    private static void CheckZipDirectory(FileStream input)
    {
        // The EOCD search window is defined by ZIP's 16-bit comment length.
        var tail = new byte[(int)Math.Min(input.Length, 65557)]; input.Position = input.Length - tail.Length; input.ReadExactly(tail);
        var end = -1;
        for (var i = tail.Length - 22; i >= 0; i--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == 0x06054b50 &&
                i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20)) == tail.Length) { end = i; break; }
        if (end < 0) throw new InvalidDataException("Invalid plugin ZIP directory.");
        var record = tail.AsSpan(end);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
        // ZIP64 sizes and counts are validated by the same native decoder as client archives.
        if (count == ushort.MaxValue || length == uint.MaxValue || offset == uint.MaxValue)
        { input.Position = 0; return; }
        if (BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(record[8..]) != count ||
            count == 0 || (long)offset + length != input.Length - tail.Length + end)
            throw new InvalidDataException("Plugin ZIP requires a complete single-volume directory.");
        input.Position = offset;
        Span<byte> entry = stackalloc byte[46];
        for (var index = 0; index < count; index++)
        {
            if (input.Position > (long)offset + length - entry.Length) throw new InvalidDataException("Invalid plugin ZIP entry directory.");
            input.ReadExactly(entry);
            if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != 0x02014b50)
                throw new InvalidDataException("Invalid plugin ZIP entry directory.");
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[28..]);
            var name = new byte[nameLength]; input.ReadExactly(name);
            if (name.AsSpan().Contains((byte)'\\')) throw new InvalidDataException("Plugin ZIP paths must use forward slashes.");
            input.Position += BinaryPrimitives.ReadUInt16LittleEndian(entry[30..]) + BinaryPrimitives.ReadUInt16LittleEndian(entry[32..]);
        }
        if (input.Position != (long)offset + length) throw new InvalidDataException("Plugin ZIP entry count does not match its directory.");
        input.Position = 0;
    }

    private static List<(string Full, string Relative)> FileTree(string root, bool trusted = false, CancellationToken cancellationToken = default)
    {
        var files = new List<(string Full, string Relative)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var queue = new Queue<string>(); queue.Enqueue(root);
        while (queue.TryDequeue(out var folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstanceStore.RejectLinks(folder);
            foreach (var path in Directory.EnumerateFileSystemEntries(folder))
            {
                cancellationToken.ThrowIfCancellationRequested();
                InstanceStore.RejectLinks(path);
                if (trusted && folder == root && Path.GetFileName(path) is SourceReceiptName or UninstalledMarker && File.Exists(path)) continue;
                var relative = ClientRelease.ValidatePath(Path.GetRelativePath(root, path).Replace('\\', '/'));
                if (!names.Add(relative)) throw new IOException("Case-aliased plugin paths.");
                if (Directory.Exists(path)) { queue.Enqueue(path); continue; }
                files.Add((path, relative));
            }
        }
        return files;
    }

    private static void CopyFile(Stream source, string target, long expected, Action<long> progress, CancellationToken ct)
    {
        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[64 * 1024]; long count = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = source.Read(buffer); if (read == 0) break;
            count += read; if (count > expected) throw new IOException("Plugin file grew or its ZIP length is invalid.");
            output.Write(buffer, 0, read); progress(count);
        }
        if (count != expected) throw new IOException("Plugin file was truncated.");
        output.Flush(true);
    }

    private static FileStamp[] Snapshot(string root, CancellationToken ct, bool trusted = false)
        => Snapshot(root, null, trusted, ct);

    private static FileStamp[] Snapshot(string root, Action<PluginImportProgress>? progress, bool trusted, CancellationToken ct)
    {
        var files = FileTree(root, trusted, ct).OrderBy(file => file.Relative, StringComparer.Ordinal).ToArray();
        var total = files.Sum(file => new FileInfo(file.Full).Length);
        long complete = 0;
        progress?.Invoke(new("verifying", total == 0 ? 100 : 0));
        var buffer = new byte[64 * 1024];
        return files.Select(file =>
        {
            ct.ThrowIfCancellationRequested();
            using var stream = new FileStream(file.Full, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            int read;
            while ((read = stream.Read(buffer)) != 0)
            {
                ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); complete += read;
                progress?.Invoke(new("verifying", total == 0 ? 100 : Math.Min(100, (double)complete * 100 / total)));
            }
            return new FileStamp(file.Relative, stream.Length, Convert.ToHexString(hash.GetHashAndReset()));
        }).ToArray();
    }

    private FileStream Acquire() => new(Safe(_root, ".library.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private void ClearPending() { if (_pending is { } pending) { DeleteStaging(pending.Folder); _pending = null; } }
    private void DeleteStaging(string folder)
    {
        if (!InstanceStore.IsWithin(_staging, folder) || !Guid.TryParseExact(Path.GetFileName(folder), "N", out _)) throw new IOException("Unexpected plugin staging directory.");
        if (!Directory.Exists(folder)) return;
        var queue = new Queue<string>(); queue.Enqueue(folder);
        while (queue.TryDequeue(out var directory))
        {
            InstanceStore.RejectLinks(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                InstanceStore.RejectLinks(entry);
                if (Directory.Exists(entry)) queue.Enqueue(entry);
            }
        }
        Directory.Delete(folder, recursive: true);
    }
    private static string Safe(string root, string path)
    {
        var full = Path.GetFullPath(Path.Combine(root, path));
        if (!InstanceStore.IsWithin(root, full)) throw new IOException("Plugin path escapes its directory.");
        InstanceStore.RejectLinks(full); return full;
    }
}
