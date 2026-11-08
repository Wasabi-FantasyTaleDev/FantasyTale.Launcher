using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    private sealed record LaunchComponentWarning(string Key, string Id, string Version, bool Installed,
        IReadOnlyDictionary<string, string> Name, IReadOnlyDictionary<string, string>? Description,
        IReadOnlyDictionary<string, string>? Warning);

    private LaunchComponentWarning[] LaunchComponentWarnings(string instanceId, string? serverId, BridgeRouter bridge)
    {
        var instance = _store.Load(instanceId); var game = Game(instance.Game);
        var build = game.Builds.Single(item => item.Id == instance.Build); var packages = Packages(game);
        instance = RecoverPluginSelections(instance, game, build, bridge);
        var required = ProfileClientPolicy.CanonicalRules(ProfileClientPolicy.ForDestination(build, string.IsNullOrEmpty(serverId)
            ? ClientPolicy.Empty : _api.ClientPolicy(Server(serverId))), packages).Where(rule => rule.Required && !rule.Disabled).Select(rule => rule.Id);
        var ids = FilePluginResolver.Warnings(instance, build, packages).Select(plugin => plugin.Id)
            .Concat(required).Concat(instance.Plugins.Where(plugin => plugin.Required).Select(plugin => plugin.Id)).Distinct(StringComparer.Ordinal);
        return ids.Select(id =>
        {
            var selection = instance.Plugins.SingleOrDefault(plugin => plugin.Id == id);
            var installed = packages.Where(plugin => plugin.Id == id && PluginCatalog.IsAvailable(plugin, build)).ToArray();
            if (selection is { Enabled: true } && installed.Any(plugin => plugin.Version == selection.Version)) return null;
            var plugin = installed.FirstOrDefault(item => item.Version == selection?.Version) ?? installed.FirstOrDefault();
            var metadata = CachedComponent(game, build, id);
            var version = plugin?.Version ?? metadata?.Version ?? selection?.Version ?? "";
            return new LaunchComponentWarning(id + "@" + version, id, version, plugin is not null,
                plugin?.Name.Values ?? metadata?.Name ?? new Dictionary<string, string> { ["en"] = id },
                plugin?.Description?.Values ?? metadata?.Description, plugin?.Warning?.Values ?? metadata?.PluginDefinition?.Warning?.Values);
        }).OfType<LaunchComponentWarning>().ToArray();
    }
}
