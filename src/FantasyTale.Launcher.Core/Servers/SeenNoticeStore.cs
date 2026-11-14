using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>
/// Remembers which operator notices the player has already been shown, per server profile, so each notice opens on
/// its own only once. Identities the server no longer publishes are dropped whenever the server's list is read.
/// </summary>
public sealed class SeenNoticeStore
{
    private readonly string _file;
    private readonly object _gate = new();
    private readonly Dictionary<string, HashSet<string>> _seen;

    /// <summary>Reads seen-notices.json from the data root; a missing file means nothing was seen yet.</summary>
    public SeenNoticeStore(string dataRoot)
    {
        _file = Path.Combine(dataRoot, "seen-notices.json"); InstanceStore.RejectLinks(_file); Directory.CreateDirectory(dataRoot);
        _seen = File.Exists(_file)
            ? (JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllBytes(_file)) ?? [])
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal)
            : new(StringComparer.Ordinal);
    }

    /// <summary>Returns the published notice identities of a server the player has not been shown, keeping only published ones on record.</summary>
    public IReadOnlySet<string> Unseen(string serverId, IReadOnlyCollection<string> published)
    {
        lock (_gate)
        {
            if (!_seen.TryGetValue(serverId, out var seen)) return published.ToHashSet(StringComparer.Ordinal);
            if (seen.RemoveWhere(id => !published.Contains(id)) > 0) Save();
            return published.Where(id => !seen.Contains(id)).ToHashSet(StringComparer.Ordinal);
        }
    }

    /// <summary>Records notices as shown to the player.</summary>
    public void MarkSeen(string serverId, IEnumerable<string> ids)
    {
        lock (_gate)
        {
            if (!_seen.TryGetValue(serverId, out var seen)) _seen[serverId] = seen = new(StringComparer.Ordinal);
            var changed = false;
            foreach (var id in ids) changed |= seen.Add(id);
            if (changed) Save();
        }
    }

    /// <summary>Forgets a removed server profile's notices.</summary>
    public void Forget(string serverId)
    {
        lock (_gate) if (_seen.Remove(serverId)) Save();
    }

    private void Save()
    {
        InstanceStore.RejectLinks(_file); var temporary = _file + ".tmp"; InstanceStore.RejectLinks(temporary);
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, _seen.ToDictionary(pair => pair.Key, pair => pair.Value.Order(StringComparer.Ordinal).ToArray()));
            file.Flush(true);
        }
        AtomicFile.Replace(temporary, _file);
    }
}
