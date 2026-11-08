using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;
using FantasyTale.Launcher.Downloads;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    private void RegisterClientPolicyActivation(BridgeRouter bridge)
    {
        RegisterClientComponents(bridge);
        bridge.Add("clientPolicy.claimPlugins", async (p, ct) =>
        {
            Keys(p, "instanceId", "serverId");
            var instanceId = Text(p, "instanceId");
            await Task.Run(() => _pluginUpgrades.ApplyReady(instanceId, ct), ct).ConfigureAwait(false);
            var instance = _store.Load(instanceId); var game = Game(instance.Game); var packages = Packages(game);
            var selectedBuild = game.Builds.Single(b => b.Id == instance.Build);
            instance = RecoverPluginSelections(instance, game, selectedBuild, bridge);
            var capabilities = await CatalogForAsync(game.Id, p?["serverId"]?.GetValue<string>(), ct).ConfigureAwait(false);
            var remote = ProfileClientPolicy.CanonicalRules(await ServerRecommendationsAsync(p?["serverId"]?.GetValue<string>(), selectedBuild, ct).ConfigureAwait(false), packages);
            // A selection whose package is gone would fail the launch, so it is offered again whether or not it was seen.
            var missing = ClientPolicyActivation.MissingSelections(instance, packages);
            IEnumerable<string> claimed = [];
            var supported = capabilities?.SupportsPlugins ?? game.SupportsPlugins;
            if (game.SupportsPlugins && remote.Count > 0)
            {
                var build = game.Builds.Single(b => b.Id == instance.Build);
                var absent = remote.Where(item => !ServerPluginPolicy.Enabled(instance, build, packages, item.Id) && (supported || packages.Any(plugin => plugin.Id == item.Id && plugin.Kind == "files" && ClientPolicyActivation.IsMandatory(plugin, build)))).ToArray();
                var unseen = ClientPolicyActivation.ClaimServer(_store, instance.Id,
                    absent.Where(item => !item.Required).Select(item => item.Id + "@" + item.Recommendation).ToArray());
                claimed = claimed.Concat(absent.Where(item => item.Required || unseen.Contains(item.Id + "@" + item.Recommendation)).Select(item => item.Id));
            }
            return missing.Concat(claimed).Distinct(StringComparer.Ordinal).Select(id =>
            {
                var package = packages.Where(item => item.Id == id && PluginCatalog.IsAvailable(item, game.Builds.Single(b => b.Id == instance.Build)))
                    .OrderByDescending(item => item.Version, Comparer<string>.Create(PluginCatalog.CompareVersions)).FirstOrDefault();
                var metadata = CachedComponent(game, game.Builds.Single(b => b.Id == instance.Build), id);
                return (object)new { id, name = package?.Name.Values ?? metadata?.Name ?? new Dictionary<string, string> { ["en"] = id },
                    description = package?.Description?.Values ?? metadata?.Description,
                    defaultSelected = remote.Any(item => item.Id == id && item.DefaultSelected), required = remote.Any(item => item.Id == id && item.Required)
                        || instance.Plugins.Any(selection => selection.Id == id && selection.Enabled && selection.Required)
                        || ClientPolicyActivation.IsMandatory(game, game.Builds.Single(b => b.Id == instance.Build), id)
                        || package is not null && ClientPolicyActivation.IsMandatory(package, game.Builds.Single(b => b.Id == instance.Build)) };
            }).ToArray();
        });
        bridge.Add("clientPolicy.acceptPlugins", async (p, ct) =>
        {
            Keys(p, "instanceId", "ids", "serverId");
            var instance = _store.Load(Text(p, "instanceId")); var game = Game(instance.Game); var build = game.Builds.Single(b => b.Id == instance.Build);
            _ = await CatalogForAsync(game.Id, p?["serverId"]?.GetValue<string>(), ct).ConfigureAwait(false);
            var remote = await ServerRecommendationsAsync(p?["serverId"]?.GetValue<string>(), game.Builds.Single(b => b.Id == instance.Build), ct).ConfigureAwait(false);
            var offered = ProfileClientPolicy.Offered(build).Concat(remote.Select(item => item.Id)).Distinct(StringComparer.Ordinal).ToArray();
            var ids = p?["ids"]?.Deserialize<string[]>(BridgeRouter.Json) ?? throw new ArgumentException("Missing recommendation identities.");
            var missing = ClientPolicyActivation.MissingSelections(instance, Packages(game));
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length
                || ids.Any(id => !offered.Contains(id, StringComparer.Ordinal) && !missing.Contains(id, StringComparer.Ordinal)))
                throw new ArgumentException("The selection contains undeclared recommendations.");
            var tasks = new List<ClientDownloadTask>();
            var mandatory = Packages(game).Where(plugin => offered.Contains(plugin.Id, StringComparer.Ordinal)
                && PluginCatalog.IsAvailable(plugin, build) && ClientPolicyActivation.IsMandatory(plugin, build)).Select(plugin => plugin.Id)
                .Concat(offered.Where(id => ClientPolicyActivation.IsMandatory(game, build, id)))
                .Concat(remote.Where(item => item.Required).Select(item => item.Id));
            if (ids.Concat(mandatory).Any(id => !Packages(game).Any(plugin => plugin.Id == id && plugin.Kind == "files"
                && PluginCatalog.IsAvailable(plugin, build) && ClientPolicyActivation.IsMandatory(plugin, build))))
                await EnsurePluginSupportAsync(p, ct).ConfigureAwait(false);
            foreach (var id in ids.Concat(mandatory).Distinct(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                instance = _store.Load(instance.Id);
                var packages = Packages(game);
                if (instance.Plugins.Any(selection => selection.Id == id && selection.Enabled && packages.Any(package => package.Id == id
                    && package.Version == selection.Version && PluginCatalog.IsAvailable(package, build)))) continue;
                var stamp = ClientPolicyActivation.SelectionStamp(instance, id);
                var rule = remote.FirstOrDefault(item => item.Id == id);
                var installed = packages.Where(item => item.Id == id && PluginCatalog.IsAvailable(item, build) && (rule?.Allows(item.Version) ?? true))
                    .OrderByDescending(item => item.Version, Comparer<string>.Create(PluginCatalog.CompareVersions)).FirstOrDefault();
                // A missing selection is the user's own choice, so it is restored explicitly rather than as a recommendation.
                var restore = missing.Contains(id, StringComparer.Ordinal) && !offered.Contains(id, StringComparer.Ordinal);
                if (installed is not null)
                {
                    stamp = ClientPolicyActivation.SelectionStamp(instance, ClientPolicyActivation.SelectionId(instance, installed));
                    if (restore) ClientPolicyActivation.EnableExplicit(_store, instance.Id, game, packages, id, installed.Version, stamp);
                    else ClientPolicyActivation.Enable(_store, instance.Id, game, packages, id, installed.Version, stamp,
                        serverRecommended: remote.Any(item => item.Id == id));
                    continue;
                }
                if (!await _catalogRequests.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("Too many catalog requests.");
                try
                {
                    var (selectedSource, selectedPackage) = await HighestAllowedAsync(game, build, id, rule, ct).ConfigureAwait(false);
                    if (selectedSource is null || selectedPackage is null) throw new InvalidOperationException("The recommended plugin is unavailable in configured stores.");
                    var manifest = await SourceDocumentReader.ReadBytesAsync(_sourceHttp, OpenSource(selectedSource), selectedPackage.Manifest, ct).ConfigureAwait(false);
                    var selectedId = selectedPackage.PluginDefinition is { } metadata ? ClientPolicyActivation.SelectionId(instance, metadata) : id;
                    stamp = ClientPolicyActivation.SelectionStamp(instance, selectedId);
                    tasks.Add(_pluginUpgrades.Enqueue(selectedSource, selectedPackage, manifest, instance.Id,
                        explicitSelection: true, forceEnable: true, expectedSelection: stamp));
                }
                finally { _catalogRequests.Release(); }
            }
            bridge.Emit("instances.changed", new { id = instance.Id });
            return tasks;
        });
    }

    /// <summary>Every rule the selected server's client policy applies to this build.</summary>
    private async Task<IReadOnlyList<ServerPluginRule>> ServerRulesAsync(string? serverId, BuildDefinition build, CancellationToken ct)
    {
        var policy = string.IsNullOrEmpty(serverId) ? ClientPolicy.Empty : _api.ClientPolicy(await ConnectedServerAsync(serverId, ct).ConfigureAwait(false));
        return ProfileClientPolicy.ForDestination(build, policy);
    }

    /// <summary>The plugins the server requires or recommends on this build.</summary>
    private async Task<IReadOnlyList<ServerPluginRule>> ServerRecommendationsAsync(string? serverId, BuildDefinition build, CancellationToken ct) =>
        (await ServerRulesAsync(serverId, build, ct).ConfigureAwait(false)).Where(item => item.Offered && !item.Disabled).ToArray();

    /// <summary>
    /// The player's enabled stores a rule may install from: those matching the rule's named sources, or every
    /// enabled store when it names none. A server never adds a store; the player adds it from the server page.
    /// </summary>
    private IEnumerable<StoreSource> PolicySources(ServerPluginRule? rule) => _sources.List().Where(source => source.Enabled && source.BaseUrl is not null &&
        (rule?.Sources is not { } named || named.Any(item => item.Kind == source.Kind && item.CatalogPath == source.CatalogPath &&
            item.BaseUrl.TrimEnd('/').Equals(source.BaseUrl!.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))));

    /// <summary>The highest release the rule's version bounds allow across the stores it may use.</summary>
    private async Task<(StoreSource? Source, StorePackage? Package)> HighestAllowedAsync(GameDefinition game, BuildDefinition build, string id, ServerPluginRule? rule, CancellationToken ct)
    {
        StoreSource? selectedSource = null; StorePackage? selectedPackage = null;
        foreach (var source in PolicySources(rule))
        {
            var catalog = await _catalogs.GetAsync(source, cancellationToken: ct).ConfigureAwait(false);
            var candidate = catalog.Packages.Where(package => package.Kind == "plugin" && PluginCatalog.MatchesIdentity(package, id) && package.Game == game.Id
                && (package.Builds.Count == 0 || package.Builds.Contains(build.Id, StringComparer.Ordinal)) && (rule?.Allows(package.Version) ?? true)
                && LauncherCompatibility.Supports(package.MinimumLauncherVersion))
                .OrderByDescending(package => package.Version, Comparer<string>.Create(PluginCatalog.CompareVersions)).FirstOrDefault();
            if (candidate is not null && (selectedPackage is null || PluginCatalog.CompareVersions(candidate.Version, selectedPackage.Version) > 0))
            { selectedSource = source; selectedPackage = candidate; }
        }
        return (selectedSource, selectedPackage);
    }
}
