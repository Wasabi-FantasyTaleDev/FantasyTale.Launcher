using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>One enabled launcher service with validated per-client options.</summary>
public sealed record ResolvedLauncherService(PluginDefinition Plugin, IReadOnlyDictionary<string, object> Options);

/// <summary>Resolves launcher-only capabilities without putting them into the x86 hook session.</summary>
public static class LauncherServicePlan
{
    /// <summary>Resolves explicit native consumer grants from enabled exact-version packages before launch.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> NativeConsumers(InstanceDefinition instance,
        BuildDefinition build, IReadOnlyList<PluginDefinition> packages)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);
        foreach (var selection in instance.Plugins.Where(p => p.Enabled))
        {
            var matches = packages.Where(p => p.Id == selection.Id && p.Version == selection.Version).ToArray();
            if (matches.Length != 1) throw new DefinitionException("instance.toml", "plugins", "native consumers must resolve to exactly one package");
            var package = matches[0];
            if (package.CompanionMessages.Count == 0) continue;
            if (package.Kind != "native" || package.Game != instance.Game || instance.Build != build.Id)
                throw new DefinitionException("plugin.toml", "companion", "consumer must be a native plugin for this game and build");
            _ = PluginCatalog.ResolveOptions(package, build, selection.Options);
            if (!result.TryAdd(package.Id, package.CompanionMessages)) throw new DefinitionException("instance.toml", "plugins", "duplicate native companion owner");
        }
        return result;
    }

    /// <summary>Merges exact schema grants for one launch, rejecting incompatible consumers and excessive route budgets.</summary>
    public static IReadOnlyDictionary<string, int> MessageGrants(IReadOnlyList<ResolvedLauncherService> services,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> consumers)
    {
        var rows = services.Select(row => row.Plugin.Service!.Messages).Concat(consumers.Values).ToArray();
        if (consumers.Count > 64 || rows.Sum(row => row.Count) > 128)
            throw new DefinitionException("instance.toml", "plugins", "companion consumer route budget exceeded");
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
            foreach (var (type, version) in row)
            {
                if (result.TryGetValue(type, out var previous) && previous != version)
                    throw new DefinitionException("instance.toml", "plugins", "companion consumers request incompatible schemas");
                result[type] = version;
            }
        return result;
    }

    /// <summary>Returns enabled, exact-version service rows; missing optional dependencies are handled at activation.</summary>
    public static IReadOnlyList<ResolvedLauncherService> Resolve(InstanceDefinition instance, BuildDefinition build,
        IReadOnlyList<PluginDefinition> packages)
    {
        var result = new List<ResolvedLauncherService>();
        foreach (var selection in instance.Plugins.Where(p => p.Enabled))
        {
            var matches = packages.Where(p => p.Id == selection.Id && p.Version == selection.Version).ToArray();
            if (matches.Length != 1) throw new DefinitionException("instance.toml", "plugins", "service selections must resolve to exactly one installed package");
            var package = matches[0];
            if (package.Kind != "service") continue;
            if (package.Game != instance.Game || instance.Build != build.Id)
                throw new DefinitionException("instance.toml", "plugins", "launcher service belongs to another game or build");
            if (package.Service is null) throw new DefinitionException("plugin.toml", "service", "missing launcher service definition");
            var options = PluginCatalog.ResolveOptions(package, build, selection.Options);
            foreach (var (key, value) in options)
            {
                var valid = (package.Service.Provider, key) switch
                {
                    ("companion", "heartbeat_interval_ms") => value is long heartbeat && heartbeat is >= 1000 and <= 300000,
                    ("companion", "request_timeout_ms") => value is long timeout && timeout is >= 100 and <= 300000,
                    ("companion", "max_dialogs") => value is long notifications && notifications is >= 1 and <= 16,
                    ("overlay-notifications", "max_dialogs") => value is long dialogs && dialogs is >= 1 and <= 16,
                    _ => false,
                };
                if (!valid) throw new DefinitionException(Path.Combine(package.Folder, "plugin.toml"), "options." + key,
                    "not a supported value for this launcher service provider");
            }
            if (result.Any(row => row.Plugin.Service!.Provider == package.Service.Provider))
                throw new DefinitionException("instance.toml", "plugins", "only one enabled package may provide a launcher service");
            result.Add(new(package, options));
        }
        return result;
    }
}
