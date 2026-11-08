using FantasyTale.Launcher.Core.Plugins;
using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Hooks;

/// <summary>A verified source and client destination included in the recoverable launch transaction.</summary>
/// <param name="Source">Absolute package payload path.</param>
/// <param name="Target">Validated relative client path.</param>
/// <param name="Size">Expected source length.</param>
/// <param name="Sha256">Expected lower-case content digest.</param>
public sealed record FilePluginPayload(string Source, string Target, long Size, string Sha256)
{
    /// <summary>Optional pristine input digest required by a settings-dependent resource transformation.</summary>
    public string? OriginalSha256 { get; init; }

    /// <summary>Requires a new destination to remain absent through transaction staging and commit.</summary>
    public bool ExpectedAbsent { get; init; }

    /// <summary>Archive edits or whole-file replacements attributed to their immutable plugin identities.</summary>
    public IReadOnlyList<GameResourceChange> Changes { get; init; } = [];

    /// <summary>Whether this replacement reused a verified archive composition.</summary>
    public bool CacheReused { get; init; }

    /// <summary>Whether the preceding destination is restored after process exit.</summary>
    public bool RestoreOnExit { get; init; } = true;
}

/// <summary>Describes an unresolved plugin selection removed while repairing an instance before launch.</summary>
/// <param name="InstanceId">Instance whose persisted selection was repaired.</param>
/// <param name="Game">Game owning the instance.</param>
/// <param name="PluginId">Removed plugin identifier.</param>
/// <param name="Version">Removed exact plugin version.</param>
/// <param name="Reason">Why the selection could not be resolved.</param>
public sealed record PluginRecoveryNotice(string InstanceId, string Game, string PluginId, string Version, string Reason);

/// <summary>Resolves launcher-side file plugins independently of the native hook runtime.</summary>
public static class FilePluginResolver
{
    /// <summary>Validates relative payload paths and protects launcher session and recovery files.</summary>
    public static string RelativePath(string value)
    {
        if (value.Length is < 1 or > 240 || value.Contains('\\')) throw new IOException("File plugin paths must be relative slash-separated paths.");
        try { _ = Stores.ClientRelease.ValidatePath(value); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException)
        { throw new IOException("File plugin path is unsafe or reserved.", error); }
        var parts = value.Split('/');
        if (parts.Any(p => p.StartsWith(".fantasytale", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("File plugins cannot replace launcher recovery files.");
        return value;
    }

    /// <summary>Adds warning components once and migrates missing retired bundled store versions while retaining enabled state and compatible options.</summary>
    public static InstanceDefinition EnsureComponents(InstanceDefinition instance, GameDefinition game, BuildDefinition build, IReadOnlyList<PluginDefinition> packages) =>
        EnsureComponents(instance, game, build, packages, false, out _);

    /// <summary>Repairs stale selections before applying launch policy; missing mandatory packages can then be requested normally.</summary>
    public static InstanceDefinition EnsureComponents(InstanceDefinition instance, GameDefinition game, BuildDefinition build,
        IReadOnlyList<PluginDefinition> packages, out IReadOnlyList<PluginRecoveryNotice> recoveries) =>
        EnsureComponents(instance, game, build, packages, true, out recoveries);

    private static InstanceDefinition EnsureComponents(InstanceDefinition instance, GameDefinition game, BuildDefinition build,
        IReadOnlyList<PluginDefinition> packages, bool repairMissing, out IReadOnlyList<PluginRecoveryNotice> recoveries)
    {
        var bundled = PluginCatalog.Load(Path.Combine(game.Folder, "plugins"));
        var rules = ProfileClientPolicy.CanonicalRules(ProfileClientPolicy.Rules(build), packages);
        var notices = new List<PluginRecoveryNotice>();
        var retained = new List<InstancePlugin>(instance.Plugins.Count);
        foreach (var original in instance.Plugins)
        {
            var selection = PluginIdentityMigration.Resolve(original, build, packages);
            // A separately selected successor is the user's explicit choice; do not duplicate it.
            if (selection.Id != original.Id && (instance.Plugins.Any(item => item.Id == selection.Id)
                || retained.Any(item => item.Id == selection.Id))) continue;
            var selectedPackage = packages.SingleOrDefault(p => p.Id == selection.Id && p.Version == selection.Version);
            // Library cleanup or an interrupted activation can leave a selection
            // pointing at an absent predecessor while its installed successor is ready.
            if (selectedPackage is null)
            {
                var constraints = rules.Where(rule => rule.Id == selection.Id).ToArray();
                var replacement = packages.Where(package => package.Id == selection.Id && package.Game == instance.Game
                    && PluginCatalog.IsAvailable(package, build) && LauncherCompatibility.Supports(package.MinimumLauncherVersion)
                    && PluginCatalog.CompareVersions(package.Version, selection.Version) > 0
                    && constraints.All(rule => !rule.Disabled && rule.Allows(package.Version)))
                    .OrderByDescending(package => PluginVersion.Parse(package.Version)).FirstOrDefault();
                if (replacement is not null)
                {
                    var options = selection.Options.Where(pair => replacement.Options.Any(option => option.Key == pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                    _ = PluginCatalog.ResolveOptionValues(replacement, options);
                    selection = selection with { Version = replacement.Version, Options = options };
                    selectedPackage = replacement;
                }
            }
            if (repairMissing && selectedPackage is null)
            {
                notices.Add(new(instance.Id, instance.Game, original.Id, original.Version, "missing"));
                // A stale version of a bundled default must not suppress its healthy bundled fallback.
                var fallback = game.DefaultPlugins.Contains(selection.Id, StringComparer.Ordinal)
                    ? packages.Where(package => package.Id == selection.Id && package.Game == game.Id
                        && PluginCatalog.IsAvailable(package, build) && LauncherCompatibility.Supports(package.MinimumLauncherVersion)
                        && bundled.Any(candidate => candidate.Id == package.Id && candidate.Version == package.Version)
                        && rules.Where(rule => rule.Id == package.Id).All(rule => !rule.Disabled && rule.Allows(package.Version)))
                        .OrderByDescending(package => PluginVersion.Parse(package.Version)).FirstOrDefault()
                    : null;
                if (fallback is not null)
                {
                    var options = selection.Options.Where(pair => fallback.Options.Any(option => option.Key == pair.Key))
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                    retained.Add(selection with { Version = fallback.Version, Options = options,
                        Required = rules.Any(rule => rule.Id == fallback.Id && rule.Required) || PluginCatalog.IsRequired(fallback, build),
                        Enabled = selection.Enabled || rules.Any(rule => rule.Id == fallback.Id && rule.Required) || PluginCatalog.IsRequired(fallback, build) });
                }
                continue;
            }

            if (selectedPackage is not null)
            {
                var required = rules.Any(rule => rule.Id == selection.Id && rule.Required)
                    || PluginCatalog.IsRequired(selectedPackage, build);
                if (required != selection.Required || required && !selection.Enabled)
                    selection = selection with { Required = required, Enabled = required || selection.Enabled };
            }
            if (!game.StoreManagedPlugins.Contains(selection.Id, StringComparer.Ordinal))
            {
                retained.Add(selection);
                continue;
            }
            var successor = bundled.Where(p => p.Id == selection.Id && PluginCatalog.IsAvailable(p, build) && PluginCatalog.CompareVersions(p.Version, selection.Version) > 0)
                .OrderByDescending(p => PluginVersion.Parse(p.Version)).FirstOrDefault();
            if (successor is not null)
            {
                var options = selection.Options.Where(pair => successor.Options.Any(option => option.Key == pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                _ = PluginCatalog.ResolveOptionValues(successor, options);
                selection = selection with { Version = successor.Version, Options = options };
            }
            retained.Add(selection);
        }
        var retainedArray = retained.ToArray();
        if (!retainedArray.SequenceEqual(instance.Plugins)) instance = instance with { Plugins = retainedArray };
        var defaults = PluginResolver.Defaults(game with { SupportsPlugins = true }, build, packages);
        var additions = defaults.Where(p => !instance.Plugins.Any(s => s.Id == p.Id) &&
            (game.DefaultPlugins.Contains(p.Id, StringComparer.Ordinal)
                || packages.Any(package => package.Id == p.Id && (PluginCatalog.IsRequired(package, build) || PluginCatalog.NeedsWarning(package, build))))).ToArray();
        var result = additions.Length == 0 ? instance : instance with { Plugins = instance.Plugins.Concat(additions).ToArray() };
        recoveries = notices;
        return result;
    }

    /// <summary>Installs new bundled component selections under the client lease, retaining explicit disable choices.</summary>
    public static InstanceDefinition EnsureInstalled(InstanceStore store, string id, GameDefinition game, BuildDefinition build, IReadOnlyList<PluginDefinition> packages)
    {
        var snapshot = store.Load(id);
        if (ReferenceEquals(EnsureComponents(snapshot, game, build, packages), snapshot)) return snapshot;
        IDisposable lease;
        try { lease = store.Acquire(id); }
        catch (InstanceBusyException) { return snapshot; }
        using var held = lease;
        var instance = store.Load(id);
        var updated = EnsureComponents(instance, game, build, packages);
        if (!ReferenceEquals(updated, instance)) store.SaveLeased(updated, instance);
        return updated;
    }

    /// <summary>Repairs plugin selections from the latest persisted settings under the client and registry leases; running sessions retain their deployed payloads.</summary>
    public static InstanceDefinition RecoverInstalled(InstanceStore store, string id, GameDefinition game, BuildDefinition build,
        IReadOnlyList<PluginDefinition> packages, bool running, out IReadOnlyList<PluginRecoveryNotice> recoveries)
    {
        var snapshot = store.Load(id);
        if (snapshot.Game != game.Id || snapshot.Build != build.Id)
            throw new InvalidOperationException("The recovery definitions do not match the client.");
        if (ReferenceEquals(EnsureComponents(snapshot, game, build, packages, out recoveries), snapshot)) return snapshot;
        using var lease = running ? null : store.Acquire(id);
        using var registry = store.AcquireNameRegistry();
        var instance = store.Load(id);
        if (instance.Game != game.Id || instance.Build != build.Id)
            throw new InvalidOperationException("The recovery definitions do not match the client.");
        // A background upgrade may have changed the selection since the caller read it.
        var updated = EnsureComponents(instance, game, build, packages, out recoveries);
        if (!ReferenceEquals(updated, instance)) store.SaveLeased(updated, instance, nameRegistryHeld: true);
        return updated;
    }

    /// <summary>Lists disabled warning components using the selected immutable package identity.</summary>
    public static IReadOnlyList<PluginDefinition> Warnings(InstanceDefinition instance, BuildDefinition build, IReadOnlyList<PluginDefinition> packages) =>
        packages.Where(p => p.Game == instance.Game && PluginCatalog.IsAvailable(p, build) && PluginCatalog.NeedsWarning(p, build))
            .GroupBy(p => p.Id).Select(group => group.FirstOrDefault(p => instance.Plugins.Any(s => s.Id == p.Id && s.Version == p.Version))
                ?? group.OrderByDescending(p => PluginVersion.Parse(p.Version)).First())
            .Where(p => !instance.Plugins.Any(s => s.Id == p.Id && s.Enabled)).ToArray();

    /// <summary>Validates selected file payloads and rejects overlapping destinations before any client file changes.</summary>
    public static IReadOnlyList<FilePluginPayload> Resolve(InstanceDefinition instance, BuildDefinition build, IReadOnlyList<PluginDefinition> packages)
    {
        var result = new List<FilePluginPayload>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var selection in instance.Plugins.Where(s => s.Enabled))
        {
            var package = packages.Single(p => p.Id == selection.Id && p.Version == selection.Version);
            if (package.Kind != "files") continue;
            _ = PluginCatalog.ResolveOptions(package, build, selection.Options);
            foreach (var file in package.Files.Where(f => (f.Builds.Count == 0 || PluginCatalog.Matches(f.Builds, build))
                && PluginCatalog.MatchesClientLanguage(f.ClientLanguages, f.ExcludeClientLanguages, instance.Language ?? build.NativeLanguage)))
            {
                if (!targets.Add(file.Target)) throw new IOException("File plugins replace the same destination.");
                var source = Path.GetFullPath(Path.Combine(package.Folder, RelativePath(file.Source)));
                InstanceStore.RejectLinks(source);
                if (!InstanceStore.IsWithin(package.Folder, source)) throw new IOException("File plugin payload escapes its package.");
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length != file.Size || !Convert.ToHexString(SHA256.HashData(input)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("File plugin payload failed size or SHA-256 verification.");
                result.Add(new(source, RelativePath(file.Target), file.Size, file.Sha256)
                { RestoreOnExit = package.RestoreFiles, Changes = [new(package.Id, package.Version, package.Name, "", "file", null, file.Sha256)] });
            }
        }
        return result;
    }
}
