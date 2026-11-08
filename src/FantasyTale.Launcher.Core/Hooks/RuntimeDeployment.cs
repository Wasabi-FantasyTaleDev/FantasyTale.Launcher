using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Hooks;

/// <summary>Applies verified file transactions, restoring runtime files while retaining completed permanent components.</summary>
public sealed class RuntimeDeployment
{
    private const string DirectoryName = ".fantasytale-runtime";
    private readonly string _client;
    private readonly string _staging;
    private readonly List<Entry> _entries;
    private sealed record Entry(string Target, string Hash, string? OriginalHash, int Slot, bool KeepInstalled = false);

    private RuntimeDeployment(string client, string staging, List<Entry> entries, string version)
    { _client = client; _staging = staging; _entries = entries; Version = version; }

    /// <summary>Version of the verified native package, matched against the host's readiness report.</summary>
    public string Version { get; }

    /// <summary>Verifies and stages every source and original before changing any client file. Caller holds the instance lease.</summary>
    public static RuntimeDeployment Apply(string clientFolder, string game, RuntimeDeploymentPlan? plan, IReadOnlyList<FilePluginPayload>? filePlugins = null)
    {
        var client = Path.GetFullPath(clientFolder);
        var staging = Path.Combine(client, DirectoryName);
        InstanceStore.RejectLinks(staging);
        if (Path.Exists(staging)) throw new IOException("An unfinished native deployment exists. Confirm the client is stopped before recovering it.");
        var package = plan is null ? null : RuntimePackage.Load(plan.PackageFolder, game);
        filePlugins ??= [];
        if (plan is not null && plan.Files.Count < 1) throw new ArgumentException("Native deployment requires at least one binding.", nameof(plan));
        if (plan is null && filePlugins.Count == 0) throw new IOException("File deployment requires a nonempty selection.");
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in plan?.Files ?? [])
        {
            if (!targets.Add(RuntimePackage.FileName(binding.Target))) throw new IOException("Native deployment contains colliding targets.");
            if (binding.OriginalAlias is { } alias && !targets.Add(RuntimePackage.FileName(alias))) throw new IOException("Native deployment contains colliding original aliases.");
            if (!package!.Files.ContainsKey(RuntimePackage.FileName(binding.Payload))) throw new IOException("A native payload is missing from runtime.toml.");
        }
        foreach (var file in filePlugins)
            if (!targets.Add(FilePluginResolver.RelativePath(file.Target))) throw new IOException("File plugins collide with another plugin or native runtime destination.");
        if (targets.Any(path => targets.Any(other => other.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase))))
            throw new IOException("File deployment destinations overlap a parent file.");
        var entries = new List<Entry>();
        var deployment = new RuntimeDeployment(client, staging, entries, package?.Version ?? "");
        Directory.CreateDirectory(staging);
        try
        {
            using (var preparing = new FileStream(Path.Combine(staging, "preparing"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                preparing.Flush(true);
            foreach (var binding in plan?.Files ?? [])
            {
                var payload = package!.Files[binding.Payload];
                if (binding.OriginalAlias is { } alias)
                {
                    var aliasPath = Path.Combine(client, alias);
                    InstanceStore.RejectLinks(aliasPath);
                    if (!File.Exists(aliasPath)) deployment.Stage(Path.Combine(client, binding.Target), alias, null, null);
                }
                deployment.Stage(payload.Path, binding.Target, payload.Size, payload.Sha256);
            }
            foreach (var file in filePlugins)
            {
                if (file.ExpectedAbsent && (file.OriginalSha256 is not null || Path.Exists(Path.Combine(client, file.Target))))
                    throw new IOException("A new client resource must still be absent. Retry launch.");
                if (file.OriginalSha256 is { } original && HashOrNull(Path.Combine(client, file.Target)) != original)
                    throw new IOException("A client resource changed after composition. Retry launch.");
                deployment.Stage(file.Source, file.Target, file.Size, file.Sha256, !file.RestoreOnExit);
                if (file.ExpectedAbsent && entries[^1].OriginalHash is not null)
                    throw new IOException("A new client resource appeared while staging its transaction. Retry launch.");
                if (file.OriginalSha256 is { } expected && entries[^1].OriginalHash != expected)
                    throw new IOException("A client resource changed while backing up its original. Retry launch.");
            }
            // This immutable journal precedes the first replacement, so interrupted application can be undone.
            using (var journal = new FileStream(Path.Combine(staging, "journal.pending"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(journal, entries); journal.Flush(true); }
            File.Move(Path.Combine(staging, "journal.pending"), Path.Combine(staging, "journal.json"));
        }
        catch { deployment.DeleteStaging(); throw; }
        try
        {
            foreach (var entry in entries)
            {
                var target = Path.Combine(client, entry.Target);
                InstanceStore.RejectLinks(target);
                if (HashOrNull(target) != entry.OriginalHash) throw new IOException("A client file changed while preparing its native overlay.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(deployment.NewFile(entry.Slot), target, entry.OriginalHash is not null);
            }
            // Retain permanent components only after the whole replacement set has succeeded.
            if (entries.Any(entry => entry.KeepInstalled))
            {
                using var committed = new FileStream(Path.Combine(staging, "applied"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                committed.Flush(true);
            }
            return deployment;
        }
        catch (Exception applyError)
        {
            try { deployment.Restore(); }
            catch (Exception restoreError) { throw new AggregateException("Native deployment failed and recovery is required.", applyError, restoreError); }
            throw;
        }
    }

    private void Stage(string source, string name, long? size, string? expectedHash, bool keepInstalled = false)
    {
        var target = Path.Combine(_client, name);
        InstanceStore.RejectLinks(target);
        if (Directory.Exists(target)) throw new IOException("A native deployment target is a directory.");
        var slot = _entries.Count;
        var hash = CopyVerified(source, NewFile(slot), size, expectedHash);
        if (keepInstalled && File.Exists(target) && new FileInfo(target).Length == size && HashOrNull(target) == hash)
        {
            File.Delete(NewFile(slot));
            return;
        }
        string? original = null;
        if (File.Exists(target)) original = CopyVerified(target, OldFile(slot), null, null);
        _entries.Add(new Entry(name, hash, original, slot, keepInstalled));
    }

    private static string CopyVerified(string source, string destination, long? size, string? hash)
    {
        InstanceStore.RejectLinks(source);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (size is not null && size != input.Length) throw new IOException("A native file differs from its declared size.");
        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { input.CopyTo(output); output.Flush(true); }
        var actual = HashOrNull(destination)!;
        if (hash is not null && hash != actual) throw new IOException("A native payload failed SHA-256 verification.");
        return actual;
    }

    /// <summary>Restores temporary originals and retains committed permanent files after validating ownership; conflicts preserve backups.</summary>
    public void Restore()
    {
        InstanceStore.RejectLinks(_staging);
        var committed = Path.Combine(_staging, "applied");
        InstanceStore.RejectLinks(committed);
        var keepInstalled = File.Exists(committed);
        var deadline = Stopwatch.StartNew();
        foreach (var entry in _entries)
        {
            RetryReleasedFile(() => ValidateRestoreEntry(entry, keepInstalled && entry.KeepInstalled), deadline);
        }
        foreach (var entry in _entries.AsEnumerable().Reverse())
        {
            if (keepInstalled && entry.KeepInstalled) continue;
            RetryReleasedFile(() =>
            {
                // A reader may outlive process exit; every retry rechecks ownership before replacing bytes.
                ValidateRestoreEntry(entry);
                var target = Path.Combine(_client, entry.Target);
                if (HashOrNull(target) == entry.OriginalHash) return;
                if (entry.OriginalHash is null) File.Delete(target);
                else
                {
                    // The verified durable backup is on the same volume. Atomic replacement avoids
                    // another full copy and flush; recovery already accepts the restored original.
                    File.Move(OldFile(entry.Slot), target, true);
                }
            }, deadline);
        }
        DeleteStaging();
    }

    private void ValidateRestoreEntry(Entry entry, bool retained = false)
    {
        InstanceStore.RejectLinks(Path.Combine(_client, entry.Target));
        var current = HashOrNull(Path.Combine(_client, entry.Target));
        if (current != entry.Hash && current != entry.OriginalHash) throw new IOException("A deployed native file changed externally; its backup has been preserved.");
        // Recovery can be interrupted after originals were restored and some backups deleted.
        if (!retained && current != entry.OriginalHash && entry.OriginalHash is not null && HashOrNull(OldFile(entry.Slot)) != entry.OriginalHash)
            throw new IOException("A native backup failed integrity verification; recovery was stopped.");
    }

    private static void RetryReleasedFile(Action operation, Stopwatch deadline)
    {
        while (true)
        {
            try { operation(); return; }
            catch (Exception error) when (OperatingSystem.IsWindows() && (error is IOException or UnauthorizedAccessException) &&
                (error.HResult & 0xffff) is (5 or 32 or 33) && deadline.Elapsed < TimeSpan.FromSeconds(2))
            { Thread.Sleep(25); }
        }
    }

    /// <summary>Recovers an interrupted overlay after the caller has independently established that the client is stopped.</summary>
    public static void RecoverStoppedClient(string clientFolder)
    {
        var client = Path.GetFullPath(clientFolder);
        var staging = Path.Combine(client, DirectoryName);
        var journal = Path.Combine(staging, "journal.json");
        InstanceStore.RejectLinks(journal);
        if (!File.Exists(journal) && Directory.Exists(staging) && !Directory.EnumerateFileSystemEntries(staging).Any())
        {
            InstanceStore.RejectLinks(staging);
            Directory.Delete(staging);
            return;
        }
        if (!File.Exists(journal) && File.Exists(Path.Combine(staging, "preparing")))
        {
            // No replacement can precede the atomic commit of the complete journal.
            InstanceStore.RejectLinks(staging);
            var files = Directory.GetFileSystemEntries(staging);
            foreach (var path in files)
            {
                InstanceStore.RejectLinks(path);
                var name = Path.GetFileName(path);
                var slot = name.StartsWith("old-", StringComparison.Ordinal) || name.StartsWith("new-", StringComparison.Ordinal);
                if (Directory.Exists(path) || name is not ("preparing" or "journal.pending")
                    && !(slot && int.TryParse(name.AsSpan(4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number >= 0))
                    throw new IOException("Unexpected file in an interrupted resource preparation; originals are preserved.");
            }
            foreach (var path in files) File.Delete(path);
            Directory.Delete(staging);
            return;
        }
        using var input = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.Read);
        var entries = JsonSerializer.Deserialize<List<Entry>>(input) ?? throw new IOException("Native recovery journal is empty.");
        input.Dispose();
        if (entries.Select(e => e.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new IOException("Duplicate native recovery target.");
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            _ = FilePluginResolver.RelativePath(entry.Target);
            if (entry.Slot != i || !ValidHash(entry.Hash) || (entry.OriginalHash is not null && !ValidHash(entry.OriginalHash))) throw new IOException("Invalid native recovery entry.");
        }
        new RuntimeDeployment(client, staging, entries, "").Restore();
    }

    private static bool ValidHash(string hash) => hash.Length == 64 && hash.All(char.IsAsciiHexDigitLower);
    private string NewFile(int slot) => Path.Combine(_staging, $"new-{slot}");
    private string OldFile(int slot) => Path.Combine(_staging, $"old-{slot}");
    private static string? HashOrNull(string path)
    {
        InstanceStore.RejectLinks(path);
        if (Directory.Exists(path)) throw new IOException("A native file was replaced with a directory.");
        if (!File.Exists(path)) return null;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(file));
    }

    private void DeleteStaging()
    {
        // Never traverse a linked or foreign directory during transaction cleanup.
        InstanceStore.RejectLinks(_staging);
        var files = Directory.GetFileSystemEntries(_staging);
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "journal.json", "journal.pending", "preparing", "applied" };
        // A failed Stage can leave its current slot before the entry reaches the journal.
        foreach (var slot in _entries.Select(entry => entry.Slot).Append(_entries.Count))
        {
            allowed.Add($"new-{slot}"); allowed.Add($"old-{slot}"); allowed.Add($"restore-{slot}");
        }
        foreach (var file in files)
        {
            InstanceStore.RejectLinks(file);
            var name = Path.GetFileName(file);
            if (Directory.Exists(file) || !allowed.Contains(name))
                throw new IOException("Unexpected entry in native transaction directory.");
        }
        // Keep the journal until every backup is gone, and the preparation marker until the journal is gone.
        foreach (var file in files.Where(file => Path.GetFileName(file) is not ("journal.json" or "preparing"))) File.Delete(file);
        File.Delete(Path.Combine(_staging, "journal.json"));
        File.Delete(Path.Combine(_staging, "preparing"));
        Directory.Delete(_staging);
    }
}
