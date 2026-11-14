using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;

namespace FantasyTale.Launcher.Core.Stores;

/// <summary>Client update preferences; launcher self-update authority is deliberately separate.</summary>
/// <param name="AutoCheck">Check on startup and every thirty minutes while the launcher runs.</param>
/// <param name="AutoDownload">Download and install discovered updates when the client is idle.</param>
/// <param name="AutoCheckPlugins">Discover compatible plugin upgrades in the background.</param>
/// <param name="AutoUpdatePlugins">Download discovered plugin upgrades and defer activation until the client is idle.</param>
/// <param name="RemoveOldPluginVersions">Remove superseded shared plugin packages after safe activation.</param>
public sealed record ClientUpdatePreferences(bool AutoCheck = true, bool AutoDownload = false, bool AutoCheckPlugins = true, bool AutoUpdatePlugins = true, bool RemoveOldPluginVersions = true);

/// <summary>Atomically persists client-update choices outside web-view browser storage.</summary>
public sealed class ClientUpdateSettings
{
    private readonly string _file;
    private readonly object _gate = new();
    private ClientUpdatePreferences _value = new();

    /// <summary>Reads validated client-updates.toml; new installations check automatically and ask before downloading.</summary>
    public ClientUpdateSettings(string dataRoot)
    {
        _file = Path.Combine(dataRoot, "client-updates.toml"); InstanceStore.RejectLinks(_file); Directory.CreateDirectory(dataRoot);
        if (!File.Exists(_file)) return;
        var root = TomlReader.Load(_file); root.OnlyKeys("auto_check", "auto_download", "auto_check_plugins", "auto_update_plugins", "remove_old_plugin_versions");
        _value = new(root.Boolean("auto_check", true), root.Boolean("auto_download"), root.Boolean("auto_check_plugins", true), root.Boolean("auto_update_plugins", true), root.Boolean("remove_old_plugin_versions", true));
    }

    /// <summary>Returns an immutable snapshot of the saved choices.</summary>
    public ClientUpdatePreferences Value { get { lock (_gate) return _value; } }

    /// <summary>Flushes the complete next configuration before replacing the current file.</summary>
    public void Save(ClientUpdatePreferences value)
    {
        lock (_gate)
        {
            InstanceStore.RejectLinks(_file); var temporary = _file + ".tmp"; InstanceStore.RejectLinks(temporary);
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(Encoding.UTF8.GetBytes($"auto_check = {value.AutoCheck.ToString().ToLowerInvariant()}\nauto_download = {value.AutoDownload.ToString().ToLowerInvariant()}\nauto_check_plugins = {value.AutoCheckPlugins.ToString().ToLowerInvariant()}\nauto_update_plugins = {value.AutoUpdatePlugins.ToString().ToLowerInvariant()}\nremove_old_plugin_versions = {value.RemoveOldPluginVersions.ToString().ToLowerInvariant()}\n"));
                file.Flush(true);
            }
            AtomicFile.Replace(temporary, _file); _value = value;
        }
    }
}
