using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>Resolves installed successor identities without making a download or changing a user's enabled state.</summary>
public static class PluginIdentityMigration
{
    /// <summary>Moves a former identity to a compatible installed successor; version precedence is local to each identity.</summary>
    public static InstancePlugin Resolve(InstancePlugin selection, BuildDefinition build, IReadOnlyList<PluginDefinition> packages)
    {
        var successor = InstalledSuccessor(selection.Id, build, packages);
        return successor is null ? selection : Apply(selection, successor);
    }

    private static InstancePlugin Apply(InstancePlugin selection, PluginDefinition successor)
    {
        var options = selection.Options.Where(pair => successor.Options.Any(option => option.Key == pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        _ = PluginCatalog.ResolveOptionValues(successor, options);
        return selection with { Id = successor.Id, Version = successor.Version, Options = options };
    }

    /// <summary>Resolves saved selections, retaining an explicit successor when a former selection maps to the same identity.</summary>
    public static IReadOnlyList<InstancePlugin> ResolveSelections(IReadOnlyList<InstancePlugin> selections,
        BuildDefinition build, IReadOnlyList<PluginDefinition> packages)
    {
        var explicitIds = selections.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var resolved = new List<InstancePlugin>(selections.Count);
        foreach (var original in selections)
        {
            var successor = InstalledSuccessor(original.Id, build, packages);
            if (successor is null) resolved.Add(original);
            else if (!explicitIds.Contains(successor.Id)) resolved.Add(Apply(original, successor));
        }
        return resolved;
    }

    /// <summary>Omits former packages from a view when their installed successor covers the same relevant clients.</summary>
    public static IReadOnlyList<PluginDefinition> VisiblePackages(IReadOnlyList<PluginDefinition> packages,
        IReadOnlyList<BuildDefinition> builds) => packages.Where(package =>
    {
        if (PluginCatalog.CanonicalId(packages, package.Id) == package.Id) return true;
        // A former identity matters only to clients that still need its package.
        // Unsupported successors remain visible through their canonical identity.
        return builds.Any(build => PluginCatalog.IsAvailable(package, build)
            && InstalledSuccessor(package.Id, build, packages) is null);
    }).ToArray();

    internal static PluginDefinition? InstalledSuccessor(string id, BuildDefinition build, IReadOnlyList<PluginDefinition> packages)
    {
        var canonical = PluginCatalog.CanonicalId(packages, id);
        if (canonical == id) return null;
        var constraints = ProfileClientPolicy.CanonicalRules(ProfileClientPolicy.Rules(build), packages)
            .Where(rule => rule.Id == canonical).ToArray();
        return packages.Where(package => package.Id == canonical && PluginCatalog.IsAvailable(package, build)
                && LauncherCompatibility.Supports(package.MinimumLauncherVersion)
                && constraints.All(rule => !rule.Disabled && rule.Allows(package.Version)))
            .OrderByDescending(package => PluginVersion.Parse(package.Version)).FirstOrDefault();
    }
}
