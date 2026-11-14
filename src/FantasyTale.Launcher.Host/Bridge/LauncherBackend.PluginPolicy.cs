using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    private BridgeRouter? _policyEvents;

    /// <summary>Records clients an installed upgrade newly supports, so the page can ask whether to enable it there.</summary>
    private void OnPluginInstalled(PluginDefinition installed, IReadOnlyList<PluginDefinition> previous)
    {
        try
        {
            var game = Game(installed.Game);
            // A bundled copy counts as an earlier version: a store or local upgrade of it is still an upgrade.
            var bundled = PluginCatalog.Load(Path.Combine(game.Folder, "plugins")).Where(plugin => plugin.Id == installed.Id && plugin.Version != installed.Version);
            if (_supportOffers.Record(installed, previous.Concat(bundled).ToArray(), game, _store.List()))
                _policyEvents?.Emit("pluginPolicy.supportOffers", null);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or DefinitionException or InvalidOperationException or UnauthorizedAccessException)
        { /* Offers are a convenience; installation already succeeded and the plugin page still lists the package. */ }
    }

    private void RegisterPluginPolicy(BridgeRouter bridge)
    {
        _policyEvents = bridge;
        // Language-scoped client policy rules follow the launcher's one UI language, which the page reports.
        bridge.Add("settings.launcherLanguage", p => { Keys(p, "language"); LauncherLanguage.Current = p?["language"]?.GetValue<string>(); return null; });
        bridge.Add("pluginPolicy.check", p =>
        {
            Keys(p, "instanceId", "language", "enabling", "disabling", "launching");
            var instance = _store.Load(Text(p, "instanceId")); var game = Game(instance.Game);
            if (!game.SupportsPlugins) return Array.Empty<object>();
            var build = game.Builds.Single(b => b.Id == instance.Build); var packages = Packages(game);
            var language = p?["language"]?.GetValue<string>() ?? LauncherLanguage.Current;
            PluginDefinition? enabling = null;
            if (p?["enabling"] is JsonObject target)
                enabling = packages.SingleOrDefault(item => item.Id == Text(target, "id") && item.Version == Text(target, "version"))
                    ?? throw new InvalidOperationException("The requested plugin version is not installed.");
            PluginDefinition? disabling = null;
            if (p?["disabling"] is JsonObject off)
            {
                var offId = Text(off, "id");
                var offVersion = off["version"]?.GetValue<string>();
                var active = instance.Plugins.FirstOrDefault(item => item.Enabled && (item.Id == offId
                    || packages.Any(pkg => pkg.Id == item.Id && pkg.Version == item.Version && PluginCatalog.MatchesIdentity(pkg, offId))));
                disabling = (offVersion is not null
                    ? packages.SingleOrDefault(item => item.Id == offId && item.Version == offVersion)
                    : active is not null
                        ? packages.SingleOrDefault(item => item.Id == active.Id && item.Version == active.Version)
                        : packages.FirstOrDefault(item => PluginCatalog.MatchesIdentity(item, offId)))
                    ?? throw new InvalidOperationException("The requested plugin version is not installed.");
            }
            var findings = PluginPolicy.Evaluate(instance, build, language, packages, enabling, disabling).Where(finding => finding.Status != "satisfied").ToArray();
            // Enabling a package shows its own rules (plus transitive disabled requirements) and the conflicts it would start;
            // disabling shows active plugins that depend on it; launch shows every blocking rule and each recommendation once per client.
            if (disabling is not null)
                findings = findings.Where(finding => finding.Blocks && finding.Rule.Type == "require"
                    && PluginCatalog.MatchesIdentity(disabling, finding.Rule.Plugin)).ToArray();
            else if (enabling is not null)
            {
                var owners = new HashSet<string>(StringComparer.Ordinal) { enabling.Id };
                while (true)
                {
                    var added = false;
                    foreach (var finding in findings)
                    {
                        if (owners.Contains(finding.Plugin) && finding.Rule.Type == "require" && finding.Target is { } dep && owners.Add(dep.Id))
                            added = true;
                    }
                    if (!added) break;
                }
                findings = findings.Where(finding => owners.Contains(finding.Plugin)
                    || finding.Rule.Type == "conflict" && finding.Target?.Id == enabling.Id).ToArray();
            }
            else if (p?["launching"]?.GetValue<bool>() == true)
            {
                var recommendations = findings.Where(finding => finding.Rule.Type == "recommend").ToArray();
                var unseen = ClientPolicyActivation.ClaimServer(_store, instance.Id, recommendations.Select(PluginPolicy.RecommendationKey).Distinct(StringComparer.Ordinal).ToArray());
                findings = findings.Where(finding => finding.Rule.Type != "recommend" || unseen.Contains(PluginPolicy.RecommendationKey(finding))).ToArray();
            }
            return findings.Select(finding =>
            {
                var owner = packages.Single(item => item.Id == finding.Plugin && item.Version == finding.Version);
                return (object)new
                {
                    plugin = finding.Plugin, pluginVersion = finding.Version, pluginName = owner.Name.Values,
                    pluginDescription = owner.Description?.Values,
                    pluginKind = owner.Kind,
                    // A plugin the build or the client requires cannot be switched off to get past its own rule.
                    pluginRequired = PluginCatalog.IsRequired(owner, build) || instance.Plugins.Any(item => item.Id == owner.Id && item.Required),
                    pluginActive = instance.Plugins.Any(item => item.Enabled && item.Id == owner.Id && item.Version == owner.Version)
                        || enabling is not null && enabling.Id == owner.Id && enabling.Version == owner.Version,
                    type = finding.Rule.Type, other = finding.Rule.Plugin, range = finding.Rule.Range, finding.Status, finding.Blocks,
                    defaultSelected = finding.Rule.DefaultSelected,
                    target = finding.Target is { } other ? new { other.Id, other.Version, name = other.Name.Values, description = other.Description?.Values, other.Kind } : null,
                };
            }).ToArray();
        });
        bridge.Add("pluginPolicy.supportOffers", _ =>
        {
            var instances = _store.List();
            return _supportOffers.List().Select(offer =>
            {
                var game = Game(offer.Game);
                var package = Packages(game).SingleOrDefault(item => item.Id == offer.Plugin && item.Version == offer.Version);
                var clients = instances.Where(instance => offer.Instances.Contains(instance.Id, StringComparer.Ordinal)
                    && !instance.Plugins.Any(selection => selection.Id == offer.Plugin)).ToArray();
                return package is null || clients.Length == 0 ? null : new
                {
                    offer.Game, offer.Plugin, offer.Version, name = package.Name.Values, description = package.Description?.Values,
                    clients = clients.Select(instance => new { instance.Id, instance.Name, instance.Build }).ToArray(),
                };
            }).Where(item => item is not null).ToArray();
        });
        bridge.Add("pluginPolicy.answerSupportOffer", p =>
        {
            Keys(p, "game", "plugin", "version", "enable", "dismiss");
            var game = Game(Text(p, "game")); var plugin = Text(p, "plugin");
            var enable = p?["enable"]?.Deserialize<string[]>(BridgeRouter.Json) ?? [];
            var dismiss = p?["dismiss"]?.Deserialize<string[]>(BridgeRouter.Json) ?? [];
            var package = Packages(game).SingleOrDefault(item => item.Id == plugin && item.Version == Text(p, "version"))
                ?? throw new InvalidOperationException("The offered plugin version is no longer installed.");
            var skipped = enable.Where(id => !EnableEverywhereSelection(id, package)).ToArray();
            _supportOffers.Resolve(game.Id, plugin, enable.Concat(dismiss).ToArray(), _store.List().Select(instance => instance.Id).ToArray());
            bridge.Emit("instances.changed", null);
            return new { skipped };
        });
    }

    /// <summary>
    /// Adds an enabled selection of <paramref name="package"/> to a client that supports it and has none yet. An existing
    /// selection keeps its state, and a client where it would turn off another enabled plugin is skipped, never changed.
    /// </summary>
    private bool EnableEverywhereSelection(string instanceId, PluginDefinition package)
    {
        var instance = _store.Load(instanceId); var game = Game(instance.Game);
        var build = game.Builds.SingleOrDefault(b => b.Id == instance.Build);
        if (build is null || !game.SupportsPlugins || !PluginCatalog.IsAvailable(package, build)) return false;
        if (instance.Plugins.Any(selection => PluginCatalog.MatchesIdentity(package, selection.Id))) return true;
        var packages = Packages(game);
        var enabled = instance.Plugins.Where(selection => selection.Enabled)
            .Select(selection => packages.SingleOrDefault(item => item.Id == selection.Id && item.Version == selection.Version)).OfType<PluginDefinition>();
        if (enabled.Any(other => PluginSelectionCompatibility.Conflicts(package, other) || package.ExclusiveWith.Contains(other.Id, StringComparer.Ordinal))) return false;
        var selected = new InstancePlugin(package.Id, package.Version, true, PluginCatalog.IsRequired(package, build), new Dictionary<string, object>());
        var changed = PluginSelectionCompatibility.Select(instance, selected, build, packages);
        void Validate()
        {
            PluginResolver.Resolve(changed, build, packages, requireLibraries: false, requireRequiredPlugins: false);
            PluginPolicy.RequireConfigured(package, true, changed, build, LauncherLanguage.Current, packages);
        }
        if (_launches.IsActive(instance.Id)) _store.SavePluginSettingsValidated(changed, instance, Validate);
        else _store.SaveValidated(changed, instance, Validate);
        return true;
    }

    private IEnumerable<object> SerializePolicyRules(PluginDefinition plugin, InstanceDefinition? instance, BuildDefinition? build, string? language)
    {
        if (plugin.Policy.Count == 0) return [];
        var rules = plugin.Policy.Where(rule => build is null || PluginPolicy.Applies(rule, build, language)).ToArray();
        if (rules.Length == 0) return [];
        IReadOnlyList<PluginDefinition>? gamePackages = null;
        IReadOnlyList<PluginDefinition>? enabled = null;
        if (instance is not null && build is not null && plugin.Game == instance.Game)
        {
            gamePackages = Packages(Game(plugin.Game));
            enabled = PluginPolicy.Enabled(instance, build, gamePackages);
        }
        return rules.Select(rule => (object)new
        {
            rule.Plugin, rule.Type, rule.Range, rule.Builds, rule.Languages, rule.DefaultSelected,
            satisfied = enabled is null || gamePackages is null ? (bool?)null : PluginPolicy.IsRuleSatisfied(rule, plugin, build!, enabled, gamePackages),
        });
    }
}
