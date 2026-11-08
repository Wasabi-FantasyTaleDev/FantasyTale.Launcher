using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>A completed logical removal, independent of any running game's retained payload files.</summary>
/// <param name="Game">Owning game identity.</param>
/// <param name="PluginId">Retired plugin identity, also used to remove saved scheme selections.</param>
/// <param name="Versions">Installed versions removed together.</param>
/// <param name="Name">Localized plugin title supplied by its authorized catalog.</param>
/// <param name="Reason">Localized retirement explanation.</param>
public sealed record PluginDeprecationNotice(string Game, string PluginId, IReadOnlyList<string> Versions,
    IReadOnlyDictionary<string, string> Name, IReadOnlyDictionary<string, string> Reason);

/// <summary>Applies enabled-store retirement metadata through the same lease-safe removal path as manual uninstallation.</summary>
public sealed class PluginDeprecationService(PluginLibrary library, PluginUpgradeActivationService upgrades,
    Func<string, GameDefinition?> game)
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Game, string Id), PluginDeprecationNotice> _notices = [];

    /// <summary>Removes matching writable packages regardless of their former download source; busy transfers defer removal.</summary>
    public void Apply(StoreSource source, StoreCatalog catalog)
    {
        if (!source.Enabled || source.BaseUrl is null || catalog.DeprecatedPlugins.Count == 0) return;
        lock (_gate)
        {
            foreach (var retired in catalog.DeprecatedPlugins)
            {
                var definition = game(retired.Game);
                if (definition is null) continue;
                var packages = library.Packages(definition).Where(plugin => plugin.Id == retired.Id).ToArray();
                var rejected = library.LoadIssues().Where(issue => issue.Game == retired.Game && issue.Id == retired.Id).ToArray();
                var folders = packages.Select(plugin => plugin.Folder).Concat(rejected.Select(issue => issue.Folder)).Distinct().ToArray();
                if (folders.Length == 0 || packages.Any(plugin => !library.CanUninstall(plugin))) continue;
                var versions = packages.Select(plugin => plugin.Version).Concat(rejected.Select(issue => issue.Version)
                    .OfType<string>()).Distinct(StringComparer.Ordinal).ToArray();
                if (!upgrades.TryUninstallDeprecated(definition, retired.Id)) continue;
                _notices[(retired.Game, retired.Id)] = new(retired.Game, retired.Id, versions, retired.Name, retired.Reason);
            }
        }
    }

    /// <summary>Returns each completed retirement once; repeats are coalesced until the UI consumes them.</summary>
    public IReadOnlyList<PluginDeprecationNotice> DrainNotices()
    {
        lock (_gate)
        {
            var notices = _notices.Values.ToArray(); _notices.Clear(); return notices;
        }
    }
}
