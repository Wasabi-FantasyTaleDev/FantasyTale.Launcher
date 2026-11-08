using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Hooks;

/// <summary>Resolves pinned packages, required locks and option values before writing a native session.</summary>
public static class PluginResolver
{
    /// <summary>Selects defaults and mandatory packages for a new instance, preserving the build's declared order.</summary>
    public static IReadOnlyList<InstancePlugin> Defaults(GameDefinition game, BuildDefinition build, IReadOnlyList<PluginDefinition> packages)
    {
        if (!game.SupportsPlugins) return [];
        var candidates = packages.Where(p => p.Game == game.Id).ToArray();
        var rules = ProfileClientPolicy.CanonicalRules(ProfileClientPolicy.Rules(build), candidates);
        var ids = game.DefaultPlugins.Where(id => candidates.Any(p => PluginCatalog.MatchesIdentity(p, id) && PluginCatalog.IsAvailable(p, build)))
            .Concat(build.DefaultPlugins.Concat(ProfileClientPolicy.Rules(build).Where(rule => rule.Required).Select(rule => rule.Id)).Where(id => !game.StoreManagedPlugins.Contains(id, StringComparer.Ordinal)
                || candidates.Any(p => p.Id == id && PluginCatalog.IsAvailable(p, build))))
            .Concat(candidates.Where(p => PluginCatalog.IsRequired(p, build) || PluginCatalog.NeedsWarning(p, build)).Select(p => p.Id).Order(StringComparer.Ordinal))
            .Select(id => PluginCatalog.CanonicalId(candidates, id)).Distinct(StringComparer.Ordinal);
        return ids.Select(id =>
        {
            var package = candidates.Where(p => p.Id == id && PluginCatalog.IsAvailable(p, build))
                .OrderByDescending(p => PluginVersion.Parse(p.Version)).FirstOrDefault()
                ?? throw new DefinitionException("instance.toml", "plugins", $"default or required plugin '{id}' is unavailable");
            return new InstancePlugin(id, package.Version, true, rules.Any(rule => rule.Id == id && rule.Required) || PluginCatalog.IsRequired(package, build), new Dictionary<string, object>());
        }).ToArray();
    }

    /// <summary>Validates selections and returns session entries in activation order; registration may defer missing required packages until launch.</summary>
    public static IReadOnlyList<SessionPluginSpec> Resolve(InstanceDefinition instance, BuildDefinition build,
        IReadOnlyList<PluginDefinition> packages, bool requireLibraries = true, bool requireRequiredPlugins = true)
    {
        var rules = ProfileClientPolicy.CanonicalRules(ProfileClientPolicy.Rules(build), packages);
        instance = Servers.ServerPluginPolicy.ForLaunch(instance, build, packages, rules);
        _ = InstanceFile.Render(instance);
        if (instance.Build != build.Id) throw new DefinitionException("instance.toml", "instance.build", "does not match the selected build");
        if (requireRequiredPlugins)
            foreach (var required in rules.Where(rule => rule.Required).Select(rule => rule.Id).Concat(packages.Where(p => p.Game == instance.Game && PluginCatalog.IsRequired(p, build)).Select(p => p.Id)).Distinct(StringComparer.Ordinal))
                if (!instance.Plugins.Any(p => p.Id == required && p.Enabled)) throw new DefinitionException("instance.toml", "plugins", $"required plugin '{required}' must be enabled");
        Servers.ServerPluginPolicy.RequireEnabled(instance, build, packages, rules.Select(rule =>
            requireRequiredPlugins ? rule : rule with { Required = false }));
        var result = new List<SessionPluginSpec>();
        var enabledPackages = new List<PluginDefinition>();
        foreach (var selection in instance.Plugins)
        {
            var matching = packages.Where(p => p.Id == selection.Id && p.Version == selection.Version).ToArray();
            if (matching.Length != 1) throw new DefinitionException("instance.toml", "plugins", $"'{selection.Id}' version {selection.Version} must resolve to exactly one package");
            var package = matching[0];
            if ((ProfileClientPolicy.Has(build, package.Id, "latest") || packages.Any(p => p.Id == selection.Id && ProfileClientPolicy.Has(build, p.Id, "latest") && PluginCatalog.IsAvailable(p, build)))
                && (selection.Enabled || PluginCatalog.NeedsWarning(package, build) || PluginCatalog.IsRequired(package, build))
                && packages.Any(p => p.Id == selection.Id && PluginCatalog.IsAvailable(p, build) && PluginCatalog.CompareVersions(p.Version, selection.Version) > 0))
                throw new DefinitionException("instance.toml", "plugins", "plugin_latest_pending");
            if (package.Game != instance.Game) throw new DefinitionException("instance.toml", "plugins", $"'{selection.Id}' belongs to another game");
            if (!selection.Enabled)
            {
                _ = PluginCatalog.ResolveOptionValues(package, selection.Options);
                if (requireRequiredPlugins && (selection.Required || PluginCatalog.IsRequired(package, build)))
                    throw new DefinitionException("instance.toml", "plugins", $"required plugin '{selection.Id}' cannot be disabled");
                continue;
            }
            var options = PluginCatalog.ResolveOptions(package, build, selection.Options);
            if (enabledPackages.FirstOrDefault(other => PluginSelectionCompatibility.Conflicts(package, other)) is { } conflict)
                throw new DefinitionException("instance.toml", "plugins", $"'{package.Id}' cannot be enabled with '{conflict.Id}'");
            enabledPackages.Add(package);
            if (requireLibraries && package.Library is { } library && !File.Exists(Path.Combine(package.Folder, library)))
                throw new DefinitionException(Path.Combine(package.Folder, "plugin.toml"), "native.library", "native library is not installed");
            // The launcher owns file deployment; only matching in-memory patches need native activation.
            var filePatches = package.Kind == "files" && package.Patches.Any(patch =>
                patch.Builds.Count == 0 || PluginCatalog.Matches(patch.Builds, build));
            if (filePatches || package.Kind is not ("files" or "overlay" or "service" or "resources"))
                result.Add(new SessionPluginSpec(package.Id, package.Version, package.Folder, selection.Required || PluginCatalog.IsRequired(package, build), options));
        }
        return result;
    }
}
