using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>
/// Keeps the last server directory read for each server profile and build, and which section's account is in use, so
/// the Launcher still knows every section and server while the profile's main API cannot be reached.
/// </summary>
public sealed class ServerSectionCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _file;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries;

    /// <summary>Reads server-directories.json from the data root; an unreadable cache starts empty.</summary>
    public ServerSectionCache(string dataRoot)
    {
        _file = Path.Combine(dataRoot, "server-directories.json"); InstanceStore.RejectLinks(_file); System.IO.Directory.CreateDirectory(dataRoot);
        try { _entries = File.Exists(_file) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllBytes(_file), Json) ?? [] : []; }
        catch (JsonException) { _entries = []; }
        _entries = new(_entries, StringComparer.Ordinal);
    }

    /// <summary>Records a directory as read from the profile's API.</summary>
    public void Store(string serverId, ServerDirectory directory)
    {
        lock (_gate)
        {
            var entry = _entries.GetValueOrDefault(serverId) ?? new Entry(null, new(StringComparer.Ordinal));
            entry.Directories[directory.Build] = directory;
            _entries[serverId] = entry with { Sections = directory.Sections ?? [] };
            Save();
        }
    }

    /// <summary>Returns the last directory read for a build, or null.</summary>
    public ServerDirectory? Directory(string serverId, string build)
    {
        lock (_gate) return _entries.GetValueOrDefault(serverId)?.Directories.GetValueOrDefault(build);
    }

    /// <summary>Returns the profile's last known sections; empty when it has none or none was read yet.</summary>
    public IReadOnlyList<ServerDirectorySection> Sections(string serverId)
    {
        lock (_gate) return _entries.GetValueOrDefault(serverId)?.Sections ?? [];
    }

    /// <summary>Gets the section whose account is in use for a profile; null means the default section.</summary>
    public string? ActiveSection(string serverId)
    {
        lock (_gate) return _entries.GetValueOrDefault(serverId)?.ActiveSection;
    }

    /// <summary>Sets the section whose account is in use for a profile.</summary>
    public void SetActiveSection(string serverId, string? section)
    {
        lock (_gate)
        {
            var entry = _entries.GetValueOrDefault(serverId) ?? new Entry(null, new(StringComparer.Ordinal));
            if (entry.ActiveSection == section) return;
            _entries[serverId] = entry with { ActiveSection = section };
            Save();
        }
    }

    /// <summary>Forgets a removed server profile.</summary>
    public void Forget(string serverId)
    {
        lock (_gate) if (_entries.Remove(serverId)) Save();
    }

    private void Save()
    {
        InstanceStore.RejectLinks(_file); var temporary = _file + ".tmp"; InstanceStore.RejectLinks(temporary);
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, _entries, Json);
            file.Flush(true);
        }
        AtomicFile.Replace(temporary, _file);
    }

    private sealed record Entry(string? ActiveSection, Dictionary<string, ServerDirectory> Directories)
    {
        public IReadOnlyList<ServerDirectorySection> Sections { get; init; } = [];
    }
}
