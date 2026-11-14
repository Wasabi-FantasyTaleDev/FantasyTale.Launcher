using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>
/// Resolves <c>requires</c> declarations. A required assets package is shared data: one installed copy serves every
/// dependent and is active whenever an enabled package needs it. Any other required package must be enabled itself.
/// </summary>
public static class PluginRequirements
{
    /// <summary>
    /// Checks every requirement of every enabled, build-compatible package that applies to this build and launcher
    /// language, and every applicable conflict, and returns the assets packages they need, dependencies after their
    /// dependents. Throws the launch-requirement error naming the first unmet rule.
    /// </summary>
    public static IReadOnlyList<PluginDefinition> RequireSatisfied(InstanceDefinition instance, BuildDefinition build,
        IReadOnlyList<PluginDefinition> packages, string? language = null)
    {
        var enabled = EnabledPackages(instance, build, packages);
        if (PluginPolicy.Evaluate(instance, build, language, packages).FirstOrDefault(finding => finding.Rule.Type == "conflict" && finding.Blocks) is { } conflict)
            throw new DefinitionException("instance.toml", "plugins", $"'{conflict.Plugin}' conflicts with '{conflict.Target!.Id}', which must be disabled");
        var assets = new Dictionary<string, PluginDefinition>(StringComparer.Ordinal);
        foreach (var package in enabled) Walk(package, [package.Id], enabled, build, packages, assets, strict: true, language);
        return assets.Values.ToArray();
    }

    /// <summary>
    /// The folders an overlay page may read: every enabled overlay package's own folder, and the data folder of every
    /// assets package that one of them requires (transitively). Keys are package ids. Unmet requirements are skipped;
    /// <see cref="RequireSatisfied"/> reports them at launch.
    /// </summary>
    public static IReadOnlyDictionary<string, string> OverlayPackageFolders(InstanceDefinition instance, BuildDefinition build,
        IReadOnlyList<PluginDefinition> packages, string? language = null)
    {
        var enabled = EnabledPackages(instance, build, packages);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var assets = new Dictionary<string, PluginDefinition>(StringComparer.Ordinal);
        foreach (var overlay in enabled.Where(package => package.Kind == "overlay"))
        {
            result[overlay.Id] = overlay.Folder;
            Walk(overlay, [overlay.Id], enabled, build, packages, assets, strict: false, language);
        }
        foreach (var package in assets.Values)
        {
            var folder = AssetsFolder(package);
            if (Directory.Exists(folder)) result[package.Id] = folder;
        }
        return result;
    }

    /// <summary>
    /// Identifiers of every package the client's enabled packages require, directly or through installed packages, whether
    /// or not those are installed yet. Used to hold a launch until their transfers finish.
    /// </summary>
    public static IReadOnlySet<string> RequiredIds(InstanceDefinition instance, IReadOnlyList<PluginDefinition> packages)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<PluginDefinition>(instance.Plugins.Where(selection => selection.Enabled)
            .Select(selection => packages.FirstOrDefault(package => package.Id == selection.Id && package.Version == selection.Version)).OfType<PluginDefinition>());
        while (pending.TryDequeue(out var package))
            foreach (var requirement in PluginPolicy.PlanRequirements(package, [instance.Build]))
            {
                var canonical = PluginCatalog.CanonicalId(packages, requirement.Id);
                if (!result.Add(canonical)) continue;
                foreach (var next in packages.Where(item => item.Id == canonical)) pending.Enqueue(next);
            }
        return result;
    }

    /// <summary>Installed packages of the same game that declare a requirement on <paramref name="target"/>.</summary>
    public static IReadOnlyList<PluginDefinition> Dependents(PluginDefinition target, IReadOnlyList<PluginDefinition> packages) =>
        packages.Where(package => package.Game == target.Game && package.Id != target.Id
            && PluginPolicy.PlanRequirements(package).Any(requirement => PluginCatalog.MatchesIdentity(target, requirement.Id))).ToArray();

    /// <summary>The absolute data folder of an assets package.</summary>
    public static string AssetsFolder(PluginDefinition package) =>
        Path.GetFullPath(Path.Combine(package.Folder, (package.Assets ?? throw new ArgumentException("Not an assets package.", nameof(package))).Root));

    /// <summary>
    /// Lists the store releases that must be installed together with <paramref name="package"/>: the newest release of each
    /// unmet requirement from the same catalog, dependencies first. Requirements already satisfied by an installed package
    /// are not listed. Throws when a requirement cannot be met from this catalog.
    /// </summary>
    public static IReadOnlyList<StorePackage> PlanStoreInstall(StoreCatalog catalog, StorePackage package,
        IReadOnlyList<PluginDefinition> installed)
    {
        var plan = new List<StorePackage>();
        Plan(catalog, package, installed, plan, [package.Id]);
        return plan;
    }

    private static void Plan(StoreCatalog catalog, StorePackage package, IReadOnlyList<PluginDefinition> installed,
        List<StorePackage> plan, List<string> path)
    {
        foreach (var requirement in package.PluginDefinition is null ? [] : PluginPolicy.PlanRequirements(package.PluginDefinition, package.Builds))
        {
            if (installed.Any(item => item.Game == package.Game && PluginCatalog.MatchesIdentity(item, requirement.Id) && Satisfies(item.Version, requirement))
                || plan.Any(item => PluginCatalog.MatchesIdentity(item, requirement.Id) && Satisfies(item.Version, requirement))) continue;
            var offer = catalog.Packages.Where(item => item.Kind == "plugin" && item.Game == package.Game
                    && PluginCatalog.MatchesIdentity(item, requirement.Id) && Satisfies(item.Version, requirement)
                    && LauncherCompatibility.Supports(item.MinimumLauncherVersion)
                    && (item.Builds.Count == 0 || package.Builds.Count == 0 || item.Builds.Intersect(package.Builds, StringComparer.Ordinal).Any()))
                .OrderByDescending(item => PluginVersion.Parse(item.Version)).FirstOrDefault()
                ?? throw new InvalidOperationException($"'{package.Id}' requires '{requirement}', which this store does not offer for this client.");
            if (path.Contains(offer.Id, StringComparer.Ordinal))
                throw new InvalidOperationException($"'{package.Id}' and '{offer.Id}' require each other.");
            Plan(catalog, offer, installed, plan, [.. path, offer.Id]);
            plan.Add(offer);
        }
    }

    private static PluginDefinition[] EnabledPackages(InstanceDefinition instance, BuildDefinition build, IReadOnlyList<PluginDefinition> packages) =>
        instance.Plugins.Where(selection => selection.Enabled)
            .Select(selection => packages.SingleOrDefault(package => package.Id == selection.Id && package.Version == selection.Version))
            .OfType<PluginDefinition>().Where(package => package.Game == instance.Game && PluginCatalog.IsAvailable(package, build)).ToArray();

    private static bool Satisfies(string version, PluginRequirement requirement) =>
        requirement.Range.Length == 0 || PluginCatalog.Satisfies(version, requirement.Range);

    private static void Walk(PluginDefinition package, List<string> path, IReadOnlyList<PluginDefinition> enabled, BuildDefinition build,
        IReadOnlyList<PluginDefinition> packages, Dictionary<string, PluginDefinition> assets, bool strict, string? language)
    {
        foreach (var requirement in PluginPolicy.Requirements(package, build, language))
        {
            var candidates = packages.Where(item => item.Game == package.Game && PluginCatalog.MatchesIdentity(item, requirement.Id)).ToArray();
            var found = candidates.Where(item => Satisfies(item.Version, requirement) && PluginCatalog.IsAvailable(item, build))
                .Where(item => item.Kind == "assets" || enabled.Contains(item))
                .OrderByDescending(item => PluginVersion.Parse(item.Version)).FirstOrDefault();
            if (found is null)
            {
                if (!strict) continue;
                var because = candidates.Length == 0 ? "which is not installed"
                    : !candidates.Any(item => Satisfies(item.Version, requirement)) ? "but only version " + string.Join(", ", candidates.Select(item => item.Version)) + " is installed"
                    : !candidates.Any(item => PluginCatalog.IsAvailable(item, build)) ? $"which is not available for {build.Id}"
                    : "which must also be enabled";
                throw new DefinitionException("instance.toml", "plugins", $"'{package.Id}' requires '{requirement}', {because}");
            }
            if (found.Kind != "assets") continue;
            if (path.Contains(found.Id, StringComparer.Ordinal))
            {
                if (!strict) continue;
                throw new DefinitionException(Path.Combine(package.Folder, "plugin.toml"), "plugin.requires", $"'{package.Id}' and '{found.Id}' require each other");
            }
            if (strict) RequireData(found);
            if (assets.TryAdd(found.Id, found)) Walk(found, [.. path, found.Id], enabled, build, packages, assets, strict, language);
        }
    }

    private static void RequireData(PluginDefinition assets)
    {
        var folder = AssetsFolder(assets);
        try
        {
            if (!Directory.Exists(folder)) throw new IOException("missing");
            InstanceStore.RejectLinks(folder);
        }
        catch (IOException)
        {
            throw new DefinitionException(Path.Combine(assets.Folder, "plugin.toml"), "assets.root", "the data folder is not installed or is linked");
        }
    }
}
