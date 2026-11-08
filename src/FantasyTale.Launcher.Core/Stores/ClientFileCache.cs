using System.Text.Json;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Downloads;

namespace FantasyTale.Launcher.Core.Stores;

// This is a fast change detector, not a tamper-proof integrity assertion. Full checks bypass it.
internal sealed class ClientFileCache
{
    private sealed record Stamp(string Path, long Size, string Sha256, long Written, long Created);
    private readonly string _client;
    private readonly object _gate = new();
    private readonly Dictionary<string, Stamp> _entries = new(StringComparer.OrdinalIgnoreCase);
    internal ClientFileCache(string client)
    {
        _client = client;
        var path = ClientInstallTransaction.Safe(ClientInstallTransaction.Workspace(client), "file-cache.json");
        if (!File.Exists(path)) return;
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var entries = JsonSerializer.Deserialize<Stamp[]>(file) ?? throw new InvalidDataException("Invalid file check cache.");
        foreach (var entry in entries)
        {
            if (entry is null || entry.Sha256 is not { Length: 64 } || !entry.Sha256.All(char.IsAsciiHexDigit) || entry.Size < 0 ||
                !_entries.TryAdd(ClientRelease.ValidatePath(entry.Path), entry)) throw new InvalidDataException("Invalid file check cache entry.");
        }
    }

    internal async Task<bool> MatchesAsync(ClientReleaseFile file, CancellationToken ct)
    {
        var path = ClientInstallTransaction.Safe(_client, file.Path); var before = Read(file);
        if (before is null || before.Size != file.Size) return false;
        lock (_gate) if (_entries.TryGetValue(file.Path, out var known) && known == before) return true;
        if (!await HttpDownloadEngine.MatchesAsync(path, file.Size, file.Sha256, ct).ConfigureAwait(false)) return false;
        if (before != Read(file)) throw new IOException("A client file changed while it was being checked.");
        lock (_gate) _entries[file.Path] = before;
        return true;
    }

    internal IReadOnlyList<ClientReleaseFile> InstalledFiles()
    {
        lock (_gate) return _entries.Values.Select(entry => new ClientReleaseFile(entry.Path, entry.Path, entry.Size, entry.Sha256)).ToArray();
    }

    internal void RecordInstalled(IEnumerable<ClientReleaseFile> files)
    {
        foreach (var file in files)
        {
            var stamp = Read(file);
            if (stamp is null || stamp.Size != file.Size) throw new IOException("An installed file changed before its check result could be saved.");
            lock (_gate) _entries[file.Path] = stamp;
        }
        // Older packages may have left unmanaged files; retain only this snapshot's check cache.
        var paths = files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
            foreach (var path in _entries.Keys.Where(p => !paths.Contains(p)).ToArray()) _entries.Remove(path);
        Save();
    }

    internal void Save()
    {
        var workspace = ClientInstallTransaction.Workspace(_client); Directory.CreateDirectory(workspace);
        var temp = ClientInstallTransaction.Safe(workspace, "file-cache.tmp");
        Stamp[] snapshot;
        lock (_gate) snapshot = _entries.Values.ToArray();
        using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(output, snapshot);
            output.Flush(true);
        }
        File.Move(temp, ClientInstallTransaction.Safe(workspace, "file-cache.json"), true);
    }

    private Stamp? Read(ClientReleaseFile file)
    {
        var path = ClientInstallTransaction.Safe(_client, file.Path); InstanceStore.RejectLinks(path);
        var info = new FileInfo(path);
        return info.Exists ? new(file.Path, info.Length, file.Sha256, info.LastWriteTimeUtc.Ticks, info.CreationTimeUtc.Ticks) : null;
    }
}
