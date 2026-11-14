using System.Text;
using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Instances;

/// <summary>A validated installation destination and whether existing files require confirmation.</summary>
/// <param name="Path">Canonical absolute destination for the client files.</param>
/// <param name="ContainsFiles">Whether the destination already contains filesystem entries.</param>
public sealed record InstallationPathInfo(string Path, bool ContainsFiles);

/// <summary>Manages owned instance directories with atomic creation and cross-process mutation leases.</summary>
public sealed partial class InstanceStore
{
    private readonly string _root;

    /// <summary>Opens the instance root and rejects linked directories.</summary>
    public InstanceStore(string root)
    {
        _root = Path.GetFullPath(root);
        RejectLinks(_root);
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, ".locks"));
        RejectLinks(Path.Combine(_root, ".locks"));
    }

    /// <summary>Lists installed definitions in stable identifier order; incomplete staging directories are excluded.</summary>
    public IReadOnlyList<InstanceDefinition> List() => Directory.EnumerateDirectories(_root)
        .Where(p => !Path.GetFileName(p).StartsWith('.') && File.Exists(Path.Combine(p, "instance.toml"))).Order(StringComparer.Ordinal)
        .Select(p => Load(Path.GetFileName(p))).ToArray();

    /// <summary>Persistent prepared resource cache outside the registered client directory.</summary>
    public string ResourceCacheFolder(string id) => ChildFolder(id, "resource-cache");

    /// <summary>Archives duplicate external registrations for the same installation, preserving the preferred settings and all archived metadata.</summary>
    public void ConsolidateRegistrations(string? preferredId = null, Func<string, bool>? isActive = null)
    {
        var registryLock = Path.Combine(_root, ".locks", ".installation-paths.lock");
        RejectLinks(registryLock);
        using var registration = OpenLease(registryLock);
        var groups = List().Where(instance => instance.InstallationPath is not null)
            .GroupBy(instance => ClientFolder(instance.Id), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1);
        foreach (var group in groups)
        {
            var items = group.OrderBy(instance => instance.Id, StringComparer.Ordinal).ToArray();
            if (items.Any(instance => instance.Game != items[0].Game || instance.Build != items[0].Build || isActive?.Invoke(instance.Id) == true)) continue;
            var canonical = items.FirstOrDefault(instance => instance.Id == preferredId) ?? items[0];
            FileStream lease;
            try { lease = Acquire(canonical.Id); }
            catch (InstanceBusyException) { continue; }
            using (lease)
            {
                foreach (var duplicate in items.Where(instance => instance.Id != canonical.Id))
                {
                    var archive = Path.Combine(_root, ".duplicates", Guid.NewGuid().ToString("N"));
                    RejectLinks(archive);
                    Directory.CreateDirectory(archive);
                    Directory.Move(Folder(duplicate.Id), Path.Combine(archive, duplicate.Id));
                }
            }
        }
    }

    /// <summary>Reads one instance and verifies that its identity matches its managed directory.</summary>
    public InstanceDefinition Load(string id)
    {
        var file = Path.Combine(Folder(id), "instance.toml");
        RejectLinks(file);
        var instance = InstanceFile.Load(file);
        if (instance.Id != id) throw new DefinitionException(file, "instance.id", "must match the directory name");
        return instance;
    }

    /// <summary>Returns the validated installation directory recorded for this client.</summary>
    public string ClientFolder(string id) => Load(id).InstallationPath is { } path ? NormalizeInstallationPath(id, path) : ChildFolder(id, "client");

    /// <summary>Checks a destination without creating directories or modifying any existing files.</summary>
    public InstallationPathInfo InspectInstallationPath(string id, string? path)
    {
        var destination = NormalizeInstallationPath(id, path);
        foreach (var installed in List())
        {
            var existing = ClientFolder(installed.Id);
            if (existing.Equals(destination, StringComparison.OrdinalIgnoreCase) || IsWithin(existing, destination) || IsWithin(destination, existing))
                throw new IOException("client_installation_overlap");
        }
        return new InstallationPathInfo(destination, Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any());
    }

    /// <summary>Registers an installation destination after explicit confirmation of existing files; package replacement is performed by the updater.</summary>
    public async Task<InstanceDefinition> CreateAtAsync(InstanceDefinition definition, string? installationPath, bool allowExistingFiles = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        var registryLock = Path.Combine(_root, ".locks", ".installation-paths.lock");
        RejectLinks(registryLock);
        using var registration = new FileStream(registryLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var destination = InspectInstallationPath(definition.Id, installationPath);
        if (destination.ContainsFiles && !allowExistingFiles) throw new IOException("The installation path contains files and requires confirmation.");
        var path = destination.Path.Equals(ChildFolder(definition.Id, "client"), StringComparison.OrdinalIgnoreCase) ? null : destination.Path;
        return await CreateCoreAsync(definition with { InstallationPath = path }, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Registers a verified existing client, reuses its registration on retry, and repairs a previously selected enclosing folder without moving game files.</summary>
    public async Task<InstanceDefinition> RegisterDetectedAsync(InstanceDefinition definition, string folder, BuildDefinition build, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(build);
        if (definition.Build != build.Id) throw new ArgumentException("Client build does not match the registration.", nameof(build));
        var destination = NormalizeInstallationPath(definition.Id, folder);
        if (ClientExecutable.FindInstalled(destination, build) is null) throw new IOException("client_executable_missing");
        var registryLock = Path.Combine(_root, ".locks", ".installation-paths.lock"); RejectLinks(registryLock);
        using var registration = new FileStream(registryLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (RetiredInstallation(destination) is { } retired)
        {
            if (retired.Game != definition.Game || retired.Build != definition.Build) throw new IOException("client_installation_overlap");
            foreach (var active in List())
            {
                var existing = ClientFolder(active.Id);
                if (existing.Equals(destination, StringComparison.OrdinalIgnoreCase) || IsWithin(existing, destination) || IsWithin(destination, existing))
                    throw new IOException("client_installation_overlap");
            }
            using var retiredLease = AcquireRetired(retired);
            using var names = AcquireNameRegistry();
            var restored = retired with { Name = AvailableName(retired.Name) };
            var temporary = Path.Combine(Folder(retired.Id), ".reactivate-" + Guid.NewGuid().ToString("N"));
            try
            {
                await File.WriteAllTextAsync(temporary, InstanceFile.Render(restored), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                File.Move(temporary, Path.Combine(Folder(retired.Id), "instance.toml"));
                File.Delete(Path.Combine(Folder(retired.Id), RemovedDefinition));
            }
            finally { File.Delete(temporary); }
            return Load(retired.Id);
        }
        foreach (var installed in List())
        {
            var existing = ClientFolder(installed.Id);
            if (installed.Game != definition.Game || installed.Build != definition.Build) continue;
            if (existing.Equals(destination, StringComparison.OrdinalIgnoreCase)) return installed;
            if (installed.InstallationPath is null || !IsWithin(existing, destination) || ClientExecutable.FindInstalled(existing, build) is not null) continue;
            using var lease = Acquire(installed.Id);
            foreach (var other in List().Where(item => item.Id != installed.Id))
            {
                var path = ClientFolder(other.Id);
                if (path.Equals(destination, StringComparison.OrdinalIgnoreCase) || IsWithin(path, destination) || IsWithin(destination, path))
                    throw new IOException("client_installation_overlap");
            }
            var repaired = installed with { InstallationPath = destination };
            var file = Path.Combine(Folder(installed.Id), "instance.toml");
            var temporary = file + ".repair-" + Guid.NewGuid().ToString("N");
            try
            {
                await File.WriteAllTextAsync(temporary, InstanceFile.Render(repaired), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                AtomicFile.Replace(temporary, file);
            }
            finally { File.Delete(temporary); }
            return repaired;
        }
        _ = InspectInstallationPath(definition.Id, destination);
        return await CreateCoreAsync(definition with { InstallationPath = destination }, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns the directory for bounded runtime diagnostics.</summary>
    public string LogFolder(string id) => ChildFolder(id, "logs");

    /// <summary>Creates an empty instance or copies an existing client without modifying the source.</summary>
    public Task<InstanceDefinition> CreateAsync(InstanceDefinition definition, string? sourceClient = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.InstallationPath is not null) throw new ArgumentException("Use CreateAtAsync to register an installation path.", nameof(definition));
        return CreateCoreAsync(definition, sourceClient, cancellationToken);
    }

    private async Task<InstanceDefinition> CreateCoreAsync(InstanceDefinition definition, string? sourceClient, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _ = InstanceFile.Render(definition);
        using var lease = Acquire(definition.Id);
        var destination = Folder(definition.Id);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException($"Instance '{definition.Id}' already exists.");
        if (sourceClient is not null)
        {
            sourceClient = Path.GetFullPath(sourceClient);
            RejectLinks(sourceClient);
            if (!Directory.Exists(sourceClient)) throw new DirectoryNotFoundException(sourceClient);
            // Staging must never become part of the source enumeration.
            if (IsWithin(sourceClient, _root) || sourceClient.Equals(_root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The instance root cannot be inside the imported client directory.");
        }
        var stage = Path.Combine(_root, ".creating-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var client = Directory.CreateDirectory(definition.InstallationPath ?? Path.Combine(stage, "client")).FullName;
            Directory.CreateDirectory(Path.Combine(stage, "logs"));
            if (sourceClient is not null) await CopyAsync(sourceClient, client, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            using var names = AcquireNameRegistry();
            definition = definition with { Name = AvailableName(definition.Name) };
            var content = InstanceFile.Render(definition);
            await File.WriteAllTextAsync(Path.Combine(stage, "instance.toml"), content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            Directory.Move(stage, destination);
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
        }
        return Load(definition.Id);
    }

    /// <summary>Clones client files and settings into a new identity, excluding transient session credentials and logs.</summary>
    public async Task<InstanceDefinition> CloneAsync(string sourceId, string id, string name, CancellationToken cancellationToken = default)
    {
        if (sourceId == id) throw new ArgumentException("The clone needs a new instance id.", nameof(id));
        using var sourceLease = Acquire(sourceId);
        var source = Load(sourceId);
        return await CreateAsync(source with { Id = id, Name = name, InstallationPath = null }, ClientFolder(sourceId), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Atomically saves settings while rejecting edits to a running instance.</summary>
    public void Save(InstanceDefinition definition) => SaveCore(definition, null);

    /// <summary>Validates and saves an optimistic settings edit while the same lease protects its referenced packages.</summary>
    public void SaveValidated(InstanceDefinition definition, InstanceDefinition expected, Action validate)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(validate);
        if (definition.Id != expected.Id) throw new ArgumentException("The expected settings belong to another instance.");
        using var lease = Acquire(definition.Id);
        validate();
        SaveLeased(definition, expected);
    }

    /// <summary>Saves only plugin selections for the next launch, independently of the immutable running session and deployed files.</summary>
    public void SavePluginSettingsValidated(InstanceDefinition definition, InstanceDefinition expected, Action validate)
    {
        if (InstanceFile.Render(definition with { Plugins = expected.Plugins }) != InstanceFile.Render(expected))
            throw new InvalidOperationException("Only plugin settings can change during a running session.");
        validate();
        using var registry = AcquireNameRegistry();
        SaveLeased(definition, expected, nameRegistryHeld: true);
    }

    /// <summary>Saves only when the persisted settings still match the snapshot used to prepare this edit.</summary>
    public void Save(InstanceDefinition definition, InstanceDefinition expected)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(expected);
        if (definition.Id != expected.Id) throw new ArgumentException("The expected settings belong to another instance.", nameof(expected));
        SaveCore(definition, expected);
    }

    private void SaveCore(InstanceDefinition definition, InstanceDefinition? expected)
    {
        ArgumentNullException.ThrowIfNull(definition);
        using var lease = Acquire(definition.Id);
        SaveLeased(definition, expected);
    }

    // Download workers already own the same cross-process instance lease.
    internal void SaveLeased(InstanceDefinition definition, InstanceDefinition? expected, bool nameRegistryHeld = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var content = InstanceFile.Render(definition);
        using var names = nameRegistryHeld ? null : AcquireNameRegistry();
        var original = Load(definition.Id);
        if (expected is not null && InstanceFile.Render(original) != InstanceFile.Render(expected))
            throw new InvalidOperationException("The client settings changed while this request was pending. Refresh and try again.");
        if (original.Game != definition.Game || original.Build != definition.Build) throw new InvalidOperationException("An installed instance cannot change its game or build.");
        if (original.InstallationPath != definition.InstallationPath) throw new InvalidOperationException("Moving an installation requires a separate file operation.");
        var file = Path.Combine(Folder(definition.Id), "instance.toml");
        var temporary = Path.Combine(Folder(definition.Id), ".instance-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllText(temporary, content, new UTF8Encoding(false)); AtomicFile.Replace(temporary, file); }
        finally { File.Delete(temporary); }
    }

    internal bool ClientPolicyActivationSeen(string id, IReadOnlyList<string> recommendations)
    {
        var seen = ReadClientPolicyActivation(id);
        foreach (var recommendation in recommendations) ValidateSeen("recommendation", recommendation);
        return recommendations.All(seen.Contains);
    }

    private HashSet<string> ReadClientPolicyActivation(string id)
    {
        var instance = Load(id);
        var path = Path.Combine(Folder(id), "client-policy.seen");
        RejectLinks(path);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (File.Exists(path))
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var lines = new StreamReader(input).ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2 || lines[0] != instance.Game || lines[1] != instance.Build || lines.Skip(2).Any(item => !seen.Add(item)))
                throw new InvalidDataException("Invalid plugin recommendation acknowledgement.");
            foreach (var item in seen) ValidateSeen(path, item);
        }
        return seen;
    }

    // Both profile and server offers use plugin@rule, so a changed rule is offered once for this client.
    private static void ValidateSeen(string file, string key)
    {
        var parts = key.Split('@');
        if (parts.Length != 2 || parts[1].Split('+').Any(rule => rule.Length == 0 || !rule.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new DefinitionException(file, "id", "invalid recommendation key");
        PluginCatalog.ValidateId(file, "id", parts[0]);
    }

    internal IReadOnlySet<string> ClaimClientPolicyActivationLeased(string id, IReadOnlyList<string> recommendations)
    {
        var instance = Load(id);
        var path = Path.Combine(Folder(id), "client-policy.seen");
        var declared = recommendations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var recommendation in declared) ValidateSeen("recommendation", recommendation);
        var seen = ReadClientPolicyActivation(id);
        var missing = declared.Where(item => seen.Add(item)).ToArray();
        if (missing.Length == 0) return new HashSet<string>(StringComparer.Ordinal);
        var temporary = path + ".tmp"; RejectLinks(temporary);
        try
        {
            using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
                writer.NewLine = "\n";
                writer.WriteLine(instance.Game); writer.WriteLine(instance.Build);
                foreach (var item in seen.Order(StringComparer.Ordinal)) writer.WriteLine(item);
                writer.Flush(); output.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
        return missing.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Removes a client registration while preserving its files and a declaration for later reactivation.</summary>
    public void Delete(string id)
    {
        using var lease = Acquire(id);
        _ = Load(id);
        var folder = Folder(id);
        File.Move(Path.Combine(folder, "instance.toml"), Path.Combine(folder, RemovedDefinition));
    }

    internal FileStream Acquire(string id)
    {
        var folder = Folder(id);
        // Registrations pointing at one physical client must share the launch/download/recovery lease.
        var key = File.Exists(Path.Combine(folder, "instance.toml"))
            ? "installation-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(ClientFolder(id)).ToUpperInvariant())))
            : id;
        var path = Path.Combine(_root, ".locks", key + ".lock");
        RejectLinks(path);
        return OpenLease(path, id, File.Exists(Path.Combine(folder, "instance.toml")) ? ClientFolder(id) : null);
    }

    private static FileStream OpenLease(string path, string? instanceId = null, string? clientPath = null)
    {
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) when ((e.HResult & 0xffff) is 32 or 33) { throw new InstanceBusyException(e, path, instanceId, clientPath); }
        catch (IOException e) { throw new IOException("instance_lock_unavailable", e); }
        catch (UnauthorizedAccessException e) { throw new IOException("instance_lock_unavailable", e); }
    }

    internal FileStream AcquireNameRegistry()
    {
        var file = Path.Combine(_root, ".locks", ".client-names.lock");
        RejectLinks(file);
        return OpenLease(file);
    }

    private string AvailableName(string requested)
    {
        var taken = List().Select(instance => instance.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(requested)) return requested;
        for (var number = 1; number <= taken.Count + 1; number++)
        {
            var suffix = " (" + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
            var length = Math.Min(requested.Length, 128 - suffix.Length);
            if (length > 0 && char.IsHighSurrogate(requested[length - 1])) length--;
            var candidate = requested[..length].TrimEnd() + suffix;
            if (!taken.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException("Unable to allocate a client display name.");
    }

    internal static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Linked paths are not supported: {current}");
    }

    internal static bool IsWithin(string parent, string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private string Folder(string id)
    {
        InstanceFile.ValidateId("instance.toml", id);
        var path = Path.Combine(_root, id);
        RejectLinks(path);
        return path;
    }

    private string ChildFolder(string id, string child)
    {
        var path = Path.Combine(Folder(id), child);
        RejectLinks(path);
        return path;
    }

    private string NormalizeInstallationPath(string id, string? path)
    {
        var managed = ChildFolder(id, "client");
        if (string.IsNullOrWhiteSpace(path)) return managed;
        if (!Path.IsPathFullyQualified(path) || path.Any(char.IsControl)) throw new ArgumentException("The installation path must be absolute.", nameof(path));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        RejectLinks(full);
        if (File.Exists(full)) throw new IOException("The installation path is a file.");
        if (full.Equals(managed, StringComparison.OrdinalIgnoreCase)) return managed;
        if (RetiredInstallation(full) is not null) return full;
        if (full.Equals(Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase) || full.Equals(_root, StringComparison.OrdinalIgnoreCase) || IsWithin(_root, full) || IsWithin(full, _root))
            throw new IOException("Choose a dedicated client folder outside the launcher metadata directory.");
        return full;
    }

    private static async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        if (Path.Exists(Path.Combine(source, ".fantasytale-runtime")))
            throw new IOException("Recover the interrupted launch before copying this client.");
        if (File.Exists(Path.Combine(source, Stores.ClientInstallTransaction.DirectoryName, "journal.json")))
            throw new IOException("Recover the interrupted installation before copying this client.");
        var pending = new Stack<(string Source, string Target, bool Root)>();
        pending.Push((source, target, true));
        while (pending.TryPop(out var folder))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder.Source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectLinks(entry);
                var name = Path.GetFileName(entry);
                if (folder.Root && name.Equals(Stores.ClientInstallTransaction.DirectoryName, StringComparison.OrdinalIgnoreCase))
                { Stores.ClientInstallTransaction.CopyReceipt(source, target); continue; }
                if (folder.Root && (name.Equals(SessionWriter.FileName, StringComparison.OrdinalIgnoreCase) || name.StartsWith("session.toml.", StringComparison.OrdinalIgnoreCase))) continue;
                var destination = Path.Combine(folder.Target, name);
                if (Directory.Exists(entry))
                {
                    Directory.CreateDirectory(destination);
                    pending.Push((entry, destination, false));
                    continue;
                }
                await using var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }
    }

}
