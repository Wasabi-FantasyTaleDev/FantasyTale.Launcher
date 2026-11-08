using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    private sealed record ClientComponent(string Id, IReadOnlyDictionary<string, string> Name,
        IReadOnlyDictionary<string, string>? Description, bool Required, bool DefaultSelected);

    private void RegisterClientComponents(BridgeRouter bridge)
    {
        bridge.Add("plugins.installationPlan", async (p, ct) =>
        {
            Keys(p, "game", "build", "serverId");
            var game = Game(Text(p, "game")); var build = game.Builds.Single(b => b.Id == Text(p, "build"));
            build.RequireEnabled();
            return await ClientComponentsAsync(game, build, p?["serverId"]?.GetValue<string>(), ct).ConfigureAwait(false);
        });
        bridge.Add("plugins.installationComponents", async (p, ct) =>
        {
            Keys(p, "instanceId", "ids", "languages", "serverId");
            var instance = _store.Load(Text(p, "instanceId")); var game = Game(instance.Game);
            var build = game.Builds.Single(b => b.Id == instance.Build); build.RequireEnabled();
            var choices = await ClientComponentsAsync(game, build, p?["serverId"]?.GetValue<string>(), ct).ConfigureAwait(false);
            var ids = p?["ids"]?.Deserialize<string[]>(BridgeRouter.Json) ?? [];
            var languages = p?["languages"]?.Deserialize<string[]>(BridgeRouter.Json) ?? [];
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length || ids.Any(id => !choices.Any(choice => choice.Id == id)))
                throw new ArgumentException("The selection contains undeclared recommendations.");
            var supported = ClientLanguagePolicy.Supported(game, build);
            if (languages.Distinct(StringComparer.Ordinal).Count() != languages.Length || languages.Any(language => !supported.Contains(language, StringComparer.Ordinal)))
                throw new ArgumentException("The selection contains undeclared languages.");
            var selected = ids.Concat(choices.Where(choice => choice.Required).Select(choice => choice.Id))
                .Concat(languages.Where(build.AdditionalLanguages.ContainsKey).Select(language => build.AdditionalLanguages[language]))
                .Distinct(StringComparer.Ordinal).ToArray();
            var rules = await ServerRulesAsync(p?["serverId"]?.GetValue<string>(), build, ct).ConfigureAwait(false);
            var local = new List<PluginDefinition>();
            var downloads = new List<(StoreSource Source, StorePackage Package, byte[] Manifest)>();
            foreach (var id in selected)
            {
                var rule = rules.SingleOrDefault(item => item.Id == id);
                if (rule is { Disabled: true }) throw new InvalidOperationException("client_policy_disabled:" + id);
                var package = Packages(game).Where(plugin => plugin.Id == id && PluginCatalog.IsAvailable(plugin, build)
                    && (rule?.Allows(plugin.Version) ?? true) && LauncherCompatibility.Supports(plugin.MinimumLauncherVersion))
                    .OrderByDescending(plugin => plugin.Version, Comparer<string>.Create(PluginCatalog.CompareVersions)).FirstOrDefault();
                if (package is not null)
                {
                    if (languages.Any(language => build.AdditionalLanguages.GetValueOrDefault(language) == id) && !ClientLanguagePolicy.IsTranslationPackage(build, package))
                        throw new InvalidDataException("The selected language package does not provide restorable client translations.");
                    local.Add(package); continue;
                }
                if (!await _catalogRequests.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("Too many catalog requests.");
                try
                {
                    var (sourceChoice, release) = await HighestAllowedAsync(game, build, id, rule, ct).ConfigureAwait(false);
                    if (sourceChoice is null || release is null) throw new InvalidOperationException($"The selected plugin is unavailable in configured stores: {id}");
                    var manifest = await SourceDocumentReader.ReadBytesAsync(_sourceHttp, OpenSource(sourceChoice), release.Manifest, ct).ConfigureAwait(false);
                    _ = ClientRelease.Parse(release, manifest);
                    downloads.Add((sourceChoice, release, manifest));
                }
                finally { _catalogRequests.Release(); }
            }
            // Resolve every transfer before changing selections, so an unavailable optional package leaves the form retryable.
            foreach (var package in local) ClientPolicyActivation.EnableExplicit(_store, instance.Id, game, Packages(game), package.Id, package.Version, forceEnable: true);
            ClientPolicyActivation.AcknowledgeInstallation(_store, instance.Id, rules.Where(rule => rule.Recommendation is not null)
                .Select(rule => rule.Id + "@" + rule.Recommendation).ToArray());
            var tasks = downloads.Select(download => _pluginUpgrades.Enqueue(download.Source, download.Package, download.Manifest,
                instance.Id, explicitSelection: true, forceEnable: true)).ToArray();
            bridge.Emit("instances.changed", new { id = instance.Id });
            return tasks;
        });
    }

    private async Task<ClientComponent[]> ClientComponentsAsync(GameDefinition game, BuildDefinition build, string? serverId, CancellationToken ct)
    {
        var capabilities = await CatalogForAsync(game.Id, serverId, ct).ConfigureAwait(false);
        var remote = await ServerRecommendationsAsync(serverId, build, ct).ConfigureAwait(false);
        var packages = Packages(game);
        var ids = ProfileClientPolicy.Offered(build).Concat(remote.Select(item => item.Id))
            .Concat(packages.Where(plugin => PluginCatalog.IsAvailable(plugin, build) && ClientPolicyActivation.IsMandatory(plugin, build)).Select(plugin => plugin.Id));
        return ids.Distinct(StringComparer.Ordinal).Select(id =>
        {
            var plugin = packages.Where(item => item.Id == id && PluginCatalog.IsAvailable(item, build))
                .OrderByDescending(item => item.Version, Comparer<string>.Create(PluginCatalog.CompareVersions)).FirstOrDefault();
            var metadata = CachedComponent(game, build, id);
            return new { Plugin = plugin ?? metadata?.PluginDefinition, Choice = new ClientComponent(id, plugin?.Name.Values ?? metadata?.Name ?? new Dictionary<string, string> { ["en"] = id },
                plugin?.Description?.Values ?? metadata?.Description, remote.Any(item => item.Id == id && item.Required) || ClientPolicyActivation.IsMandatory(game, build, id)
                    || plugin is not null && ClientPolicyActivation.IsMandatory(plugin, build), remote.Any(item => item.Id == id && item.DefaultSelected)) };
        }).Where(item => (capabilities?.SupportsPlugins ?? game.SupportsPlugins) || item.Plugin is { Kind: "files" } && item.Choice.Required)
            .Select(item => item.Choice).ToArray();
    }

    private StorePackage? CachedComponent(GameDefinition game, BuildDefinition build, string id) =>
        _sources.List().Where(source => source.Enabled && source.BaseUrl is not null)
            .SelectMany(source => _catalogs.Cached(source)?.Packages ?? [])
            .Where(package => package.Kind == "plugin" && package.Game == game.Id && PluginCatalog.MatchesIdentity(package, id)
                && (package.Builds.Count == 0 || package.Builds.Contains(build.Id, StringComparer.Ordinal)))
            .OrderByDescending(package => package.Version, Comparer<string>.Create(PluginCatalog.CompareVersions)).FirstOrDefault();
}
