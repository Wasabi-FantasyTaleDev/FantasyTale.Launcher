using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>Applies explicit mutually exclusive package selections without removing saved versions or options.</summary>
public static class PluginSelectionCompatibility
{
    /// <summary>Whether either immutable package declares that the pair cannot be enabled together.</summary>
    public static bool Conflicts(PluginDefinition first, PluginDefinition second) =>
        first.Game == second.Game && first.Id != second.Id &&
        (PluginCatalog.MatchesIdentity(first, second.Id) || PluginCatalog.MatchesIdentity(second, first.Id)
            || first.ExclusiveWith.Contains(second.Id, StringComparer.Ordinal) || second.ExclusiveWith.Contains(first.Id, StringComparer.Ordinal));

    /// <summary>Replaces one selection and disables optional conflicts atomically; required selections remain protected.</summary>
    public static InstanceDefinition Select(InstanceDefinition instance, InstancePlugin selected, BuildDefinition build,
        IReadOnlyList<PluginDefinition> packages)
    {
        var package = packages.Single(item => item.Id == selected.Id && item.Version == selected.Version);
        var entries = instance.Plugins.Where(item => item.Id != selected.Id).Select(item =>
        {
            if (!selected.Enabled || !item.Enabled) return item;
            var other = packages.SingleOrDefault(p => p.Id == item.Id && p.Version == item.Version);
            var conflict = package.ExclusiveWith.Contains(item.Id, StringComparer.Ordinal) || other is not null && Conflicts(package, other);
            if (!conflict) return item;
            if (item.Required || ProfileClientPolicy.Has(build, item.Id, "require"))
                throw new DefinitionException("instance.toml", "plugins", $"required plugin '{item.Id}' conflicts with '{selected.Id}'");
            return item with { Enabled = false };
        }).Append(selected).ToArray();
        return instance with { Plugins = entries };
    }
}
