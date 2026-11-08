using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>Applies typed client-policy offers and selection-safe plugin activation.</summary>
public static class ClientPolicyActivation
{
    /// <summary>Whether the installation prompt must retain this build's checked component.</summary>
    public static bool IsMandatory(PluginDefinition plugin, BuildDefinition build) => PluginCatalog.IsRequired(plugin, build) || PluginCatalog.NeedsWarning(plugin, build);

    /// <summary>Retains required installation choices even before a separately distributed component has been downloaded.</summary>
    public static bool IsMandatory(GameDefinition game, BuildDefinition build, string pluginId) =>
        ProfileClientPolicy.Has(build, pluginId, "require");

    /// <summary>Records the recommendations shown during installation without requiring an executable to be present yet.</summary>
    public static void AcknowledgeInstallation(InstanceStore store, string id, IReadOnlyList<string> recommendations)
    {
        if (recommendations.Count == 0) return;
        using var lease = store.Acquire(id);
        _ = store.ClaimClientPolicyActivationLeased(id, recommendations);
    }
    /// <summary>Records server recommendation rules as shown for this client and returns the ones not shown before.</summary>
    /// <param name="store">The client store.</param>
    /// <param name="id">The client instance.</param>
    /// <param name="keys">One <c>plugin@rule</c> key per recommendation rule.</param>
    public static IReadOnlySet<string> ClaimServer(InstanceStore store, string id, IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return new HashSet<string>(StringComparer.Ordinal);
        using var lease = store.Acquire(id);
        return store.ClaimClientPolicyActivationLeased(id, keys);
    }

    /// <summary>Claims the client's one-time prompt, retaining mandatory items and omitting enabled optional packages; empty declarations create no marker.</summary>
    public static IReadOnlyList<string> Claim(InstanceStore store, string id, GameDefinition game, IReadOnlyList<PluginDefinition> packages)
    {
        var instance = store.Load(id);
        if (instance.Game != game.Id) throw new ArgumentException("The game does not own this client.", nameof(game));
        var build = game.Builds.Single(b => b.Id == instance.Build);
        if (!game.SupportsPlugins || ProfileClientPolicy.Offered(build).Count == 0 || ClientExecutable.FindInstalled(store.ClientFolder(id), build) is null)
            return [];
        var offers = ProfileClientPolicy.CanonicalRules(ProfileClientPolicy.Rules(build), packages).Where(rule => rule.Offered && !rule.Disabled).ToArray();
        var keys = offers.Where(rule => rule.Recommendation is not null).Select(rule => rule.Id + "@" + rule.Recommendation).ToArray();
        // Satisfied requirements need no acknowledgement or write lease; a running
        // client must remain readable while another operation owns its files.
        if (!offers.Any(rule => rule.Required && !ServerPluginPolicy.Enabled(instance, build, packages, rule.Id))
            && store.ClientPolicyActivationSeen(id, keys)) return [];
        using var lease = store.Acquire(id);
        instance = store.Load(id);
        var missing = offers.Where(rule => !ServerPluginPolicy.Enabled(instance, build, packages, rule.Id)
            || !rule.Required && packages.Any(p => p.Id == rule.Id && PluginCatalog.IsAvailable(p, build) && IsMandatory(p, build)))
            .Select(rule => rule.Id).ToArray();
        var unseen = store.ClaimClientPolicyActivationLeased(id, keys);
        return missing.Where(plugin => offers.Any(rule => rule.Id == plugin && (rule.Required || unseen.Contains(rule.Id + "@" + rule.Recommendation)))).ToArray();
    }

    /// <summary>Enabled selections whose exact version is not installed, such as a download that never completed; the client cannot start until they are installed again.</summary>
    public static IReadOnlyList<string> MissingSelections(InstanceDefinition instance, IReadOnlyList<PluginDefinition> packages) =>
        instance.Plugins.Where(selection => selection.Enabled && !packages.Any(package => package.Id == selection.Id && package.Version == selection.Version))
            .Select(selection => selection.Id).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>Captures one plugin's identity, version, enablement and options, excluding the policy-derived required flag.</summary>
    public static string SelectionStamp(InstanceDefinition instance, string pluginId)
    {
        PluginCatalog.ValidateId("recommendation", "id", pluginId);
        var selection = new InstanceDefinition("selection", "Selection", instance.Game, instance.Build, null,
            instance.Plugins.Where(p => p.Id == pluginId).Select(p => p with { Required = false }).ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(InstanceFile.Render(selection)))).ToLowerInvariant();
    }

    /// <summary>Matches deferred user choices while accepting older stamps that included an enabled selection's required flag.</summary>
    public static bool SelectionMatches(InstanceDefinition instance, string pluginId, string expected)
    {
        if (SelectionStamp(instance, pluginId) == expected) return true;
        var selected = instance.Plugins.SingleOrDefault(p => p.Id == pluginId);
        if (selected is not { Enabled: true }) return false;
        var legacy = new InstanceDefinition("selection", "Selection", instance.Game, instance.Build, null,
            [selected with { Required = true }]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(InstanceFile.Render(legacy)))).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Finds the existing canonical or former selection whose settings an installed successor must retain.</summary>
    public static string SelectionId(InstanceDefinition instance, PluginDefinition plugin) =>
        instance.Plugins.Any(selection => selection.Id == plugin.Id) ? plugin.Id
            : plugin.Aliases.FirstOrDefault(alias => instance.Plugins.Any(selection => selection.Id == alias)) ?? plugin.Id;

    /// <summary>Enables an installed recommendation only while its original selection still matches, preserving shared option overrides.</summary>
    public static void Enable(InstanceStore store, string instanceId, GameDefinition game, IReadOnlyList<PluginDefinition> packages,
        string pluginId, string version, string expectedSelection, bool serverRecommended = false)
    {
        using var lease = store.Acquire(instanceId);
        var package = packages.Single(package => package.Id == pluginId && package.Version == version);
        EnableLeased(store, instanceId, game, packages, pluginId, version, expectedSelection, serverRecommended: serverRecommended,
            selectionPlugin: SelectionId(store.Load(instanceId), package));
    }

    /// <summary>Activates a selected package, preserving an existing upgrade's enabled state unless the caller explicitly requests enabling it.</summary>
    public static void EnableExplicit(InstanceStore store, string instanceId, GameDefinition game, IReadOnlyList<PluginDefinition> packages,
        string pluginId, string version, string? expectedSelection = null, bool forceEnable = false)
    {
        using var lease = store.Acquire(instanceId);
        var instance = store.Load(instanceId);
        var package = packages.Single(package => package.Id == pluginId && package.Version == version);
        var selectionId = SelectionId(instance, package);
        if (expectedSelection is not null && !SelectionMatches(instance, selectionId, expectedSelection)) return;
        EnableLeased(store, instanceId, game, packages, pluginId, version, SelectionStamp(instance, selectionId), explicitSelection: true,
            forceEnable: forceEnable, selectionPlugin: selectionId);
    }

    internal static void EnableLeased(InstanceStore store, string instanceId, GameDefinition game, IReadOnlyList<PluginDefinition> packages,
        string pluginId, string version, string expectedSelection, bool upgrade = false, bool serverRecommended = false, bool explicitSelection = false, bool nameRegistryHeld = false, bool forceEnable = false,
        string? selectionPlugin = null)
    {
        var instance = store.Load(instanceId);
        if (instance.Game != game.Id) throw new InvalidDataException("The recommendation target changed.");
        var build = game.Builds.Single(b => b.Id == instance.Build);
        var package = packages.Single(p => p.Id == pluginId && p.Version == version);
        var selectionId = selectionPlugin ?? pluginId;
        if (!PluginCatalog.MatchesIdentity(package, selectionId)) throw new InvalidDataException("The package does not own the former selection identity.");
        if (!explicitSelection && (!game.SupportsPlugins || !upgrade && !serverRecommended
            && !ProfileClientPolicy.Offered(build).Any(id => PluginCatalog.MatchesIdentity(package, id))
            && !ProfileClientPolicy.Has(build, package, "latest")))
            throw new InvalidOperationException("The build does not recommend this plugin.");
        if (upgrade && (!SelectionMatches(instance, selectionId, expectedSelection) ||
            instance.Plugins.SingleOrDefault(p => p.Id == selectionId) is not { } active ||
            selectionId == pluginId && PluginCatalog.CompareVersions(version, active.Version) <= 0)) return;
        if (instance.Plugins.Any(p => p.Id == pluginId && p.Version == version && p.Enabled)) return;
        if (!SelectionMatches(instance, selectionId, expectedSelection))
            throw new InvalidOperationException("The plugin selection changed before recommendation activation.");
        if (!PluginCatalog.IsAvailable(package, build)) throw new InvalidDataException("The recommended plugin is unavailable on this build.");
        var previous = instance.Plugins.SingleOrDefault(p => p.Id == selectionId);
        var options = previous?.Options.Where(pair => package.Options.Any(option => option.Key == pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal) ?? [];
        var retainEnabled = !forceEnable && (upgrade || explicitSelection && previous is not null
            && (selectionId != pluginId || PluginCatalog.CompareVersions(version, previous.Version) > 0));
        var required = previous?.Required == true || PluginCatalog.IsRequired(package, build) || ProfileClientPolicy.Has(build, package, "require");
        var selected = new InstancePlugin(pluginId, version, required || (retainEnabled ? previous?.Enabled ?? true : true), required, options);
        if (package.Resources is not null)
        {
            var module = GameModules.Load(game, AppContext.BaseDirectory) as IGameResourceModule
                ?? throw new InvalidOperationException("Update the game module to configure resource plugins.");
            module.ValidateResourceSettings(new LaunchContext(game, build, store.ClientFolder(instanceId), null, null),
                new(package, PluginCatalog.ResolveOptionValues(package, options)));
        }
        // Retire the former selection only after the verified successor is ready, under the same lease.
        var baseline = selectionId == pluginId ? instance : instance with { Plugins = instance.Plugins.Where(item => item.Id != selectionId).ToArray() };
        var changed = PluginSelectionCompatibility.Select(baseline, selected, build, packages);
        // Installing a package is not a launch: another missing or disabled requirement must not
        // block its activation. Retain those selections for recovery and the normal launch prompt.
        var validation = changed with { Plugins = changed.Plugins.Where(p => packages.Any(item => item.Id == p.Id && item.Version == p.Version)).ToArray() };
        _ = PluginResolver.Resolve(validation, build, packages, requireLibraries: false, requireRequiredPlugins: false);
        store.SaveLeased(changed, instance, nameRegistryHeld);
    }
}
