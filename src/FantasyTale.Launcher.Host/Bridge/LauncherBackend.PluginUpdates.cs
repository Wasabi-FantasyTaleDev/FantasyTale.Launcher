using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    internal sealed record RequiredPluginUpdate(string Key, string Id, IReadOnlyDictionary<string, string> Name,
        string InstalledVersion, string Version, ClientDownloadTask? Task);

    private RequiredPluginUpdate[] RequiredPluginUpdates(string instanceId, string? serverId)
    {
        var instance = _store.Load(instanceId); var game = Game(instance.Game); var build = game.Builds.Single(b => b.Id == instance.Build);
        var packages = Packages(game); var snapshot = _pluginMonitor.Read(instanceId);
        var rules = ProfileClientPolicy.ForDestination(build, string.IsNullOrEmpty(serverId) ? ClientPolicy.Empty : _api.ClientPolicy(Server(serverId)));
        var tasks = _downloads.List();
        return instance.Plugins.Select(selection =>
        {
            var plugin = packages.SingleOrDefault(p => p.Id == selection.Id && p.Version == selection.Version && PluginCatalog.IsAvailable(p, build));
            var rule = rules.FirstOrDefault(item => item.Id == selection.Id && !item.Disabled);
            var versioned = selection.Enabled && rule is not null && (rule.Latest || !rule.Allows(selection.Version));
            if (plugin is null || !(versioned || selection.Required || ClientPolicyActivation.IsMandatory(plugin, build) || ProfileClientPolicy.Has(build, plugin.Id, "latest") || rule is { Required: true })) return null;
            // The newest known version the server's bounds allow; a maximum below the installed version selects an older release.
            var known = snapshot.Updates.Where(item => item.Id == plugin.Id && item.Version is not null && (item.Status is "available" or "installed")).Select(item => item.Version!)
                .Concat(packages.Where(p => p.Id == plugin.Id && PluginCatalog.IsAvailable(p, build)).Select(p => p.Version))
                .Concat(tasks.Where(t => t.Target.Kind == "plugin" && t.Target.GameId == game.Id && t.Target.PackageId == plugin.Id && t.Stage != "canceled").Select(t => t.Target.ToVersion))
                .OfType<string>().Where(version => rule?.Allows(version) ?? true)
                .OrderByDescending(version => version, Comparer<string>.Create(PluginCatalog.CompareVersions)).FirstOrDefault();
            if (known is null || known == selection.Version || !versioned && PluginCatalog.CompareVersions(known, selection.Version) <= 0) return null;
            var task = tasks.LastOrDefault(t => t.Target.Kind == "plugin" && t.Target.GameId == game.Id && t.Target.PackageId == plugin.Id && t.Target.ToVersion == known);
            return new RequiredPluginUpdate(plugin.Id + "@" + selection.Version + "->" + known, plugin.Id, plugin.Name.Values, selection.Version, known, task);
        }).OfType<RequiredPluginUpdate>().ToArray();
    }

    /// <summary>
    /// Moves enabled plugins onto a version the server's version rules allow: an installed release is selected at once,
    /// otherwise the highest allowed release is downloaded from the player's stores and activated when it completes.
    /// </summary>
    private async Task EnsureServerVersionsAsync(string instanceId, string? serverId, CancellationToken ct)
    {
        var instance = _store.Load(instanceId); var game = Game(instance.Game); var build = game.Builds.Single(b => b.Id == instance.Build);
        foreach (var rule in (await ServerRulesAsync(serverId, build, ct).ConfigureAwait(false)).Where(item => !item.Disabled && (item.Latest || item.Minimum is not null || item.Maximum is not null)))
        {
            instance = _store.Load(instanceId);
            if (instance.Plugins.SingleOrDefault(selection => selection.Id == rule.Id && selection.Enabled) is not { } active) continue;
            var (source, package) = rule.Latest || !rule.Allows(active.Version) ? await HighestAllowedAsync(game, build, rule.Id, rule, ct).ConfigureAwait(false) : (null, null);
            var packages = Packages(game);
            var target = package?.Version ?? (rule.Allows(active.Version) ? null : packages.Where(p => p.Id == rule.Id && PluginCatalog.IsAvailable(p, build) && rule.Allows(p.Version))
                .OrderByDescending(p => p.Version, Comparer<string>.Create(PluginCatalog.CompareVersions)).FirstOrDefault()?.Version);
            if (target is null || target == active.Version || rule.Allows(active.Version) && PluginCatalog.CompareVersions(target, active.Version) <= 0) continue;
            var stamp = ClientPolicyActivation.SelectionStamp(instance, rule.Id);
            var targetId = package?.Id ?? PluginCatalog.CanonicalId(packages, rule.Id);
            if (packages.Any(p => p.Id == targetId && p.Version == target))
            {
                ClientPolicyActivation.Enable(_store, instanceId, game, packages, targetId, target, stamp, serverRecommended: true);
                continue;
            }
            if (_downloads.List().Any(t => t.Target.Kind == "plugin" && t.Target.InstanceId == instanceId && t.Target.PackageId == rule.Id && t.Target.ToVersion == target && t.Stage is not ("canceled" or "completed")))
                continue;
            var manifest = await SourceDocumentReader.ReadBytesAsync(_sourceHttp, OpenSource(source!), package!.Manifest, ct).ConfigureAwait(false);
            _pluginUpgrades.Enqueue(source!, package, manifest, instanceId, explicitSelection: true, expectedSelection: stamp);
        }
    }

    private void RegisterPluginUpdates(BridgeRouter bridge)
    {
        bridge.Add("plugins.launchUpdates", async (p, ct) =>
        {
            Keys(p, "instanceId", "serverId", "ensure");
            var id = Text(p, "instanceId");
            if (p?["ensure"]?.GetValue<bool>() == true) await EnsureServerVersionsAsync(id, p?["serverId"]?.GetValue<string>(), ct).ConfigureAwait(false);
            var snapshot = _pluginMonitor.Read(id);
            return new { checking = snapshot.Checking, items = RequiredPluginUpdates(id, p?["serverId"]?.GetValue<string>()) };
        });
        bridge.Add("plugins.ensureLatest", async (p, ct) =>
        {
            Keys(p, "instanceId"); var instance = _store.Load(Text(p, "instanceId")); var game = Game(instance.Game); var build = game.Builds.Single(b => b.Id == instance.Build);
            Core.Hooks.FilePluginResolver.EnsureInstalled(_store, instance.Id, game, build, Packages(game));
            await RefreshPluginCatalogsAsync(instance.Id, ct).ConfigureAwait(false);
            var tasks = await _pluginUpdates.EnsureLatestAsync(instance.Id, ct).ConfigureAwait(false);
            bridge.Emit("instances.changed", new { id = instance.Id }); return tasks;
        });
        bridge.Add("plugins.updateSnapshot", p =>
        {
            Keys(p, "instanceId"); var id = Text(p, "instanceId"); _store.Load(id);
            return _pluginMonitor.Read(id);
        });
        bridge.Add("plugins.launchTransfers", p =>
        {
            Keys(p, "instanceId"); return PendingPluginTransfers(Text(p, "instanceId"));
        });
        bridge.Add("plugins.checkUpdates", async (p, ct) =>
        {
            Keys(p, "instanceId", "serverId"); await EnsurePluginSupportAsync(p, ct).ConfigureAwait(false);
            var id = Text(p, "instanceId");
            var instance = _store.Load(id);
            var target = new PluginUpdateTarget(id, instance.Game + "/" + instance.Build);
            await RefreshPluginCatalogsAsync(id, ct).ConfigureAwait(false);
            var result = await _pluginUpdates.CheckAsync(id, ct).ConfigureAwait(false);
            _pluginMonitor.Remember(target, result); return result;
        });
        bridge.Add("plugins.installUpdate", async (p, ct) =>
        {
            Keys(p, "instanceId", "id", "installedVersion", "version", "manifestSha256", "serverId");
            await EnsurePluginSupportAsync(p, ct).ConfigureAwait(false);
            await RefreshPluginCatalogsAsync(Text(p, "instanceId"), ct).ConfigureAwait(false);
            return await _pluginUpdates.InstallAsync(Text(p, "instanceId"), Text(p, "id"), Text(p, "installedVersion"),
                Text(p, "version"), Text(p, "manifestSha256"), ct).ConfigureAwait(false);
        });
    }

    private Task<IReadOnlyList<StoreCatalogSyncState>> RefreshPluginCatalogsAsync(string instanceId, CancellationToken ct)
    {
        _store.Load(instanceId);
        return _catalogs.SynchronizeAsync(_sources.List(), ct);
    }

    private async Task EnsurePluginSupportAsync(JsonNode? p, CancellationToken ct)
    {
        var instance = _store.Load(Text(p, "instanceId")); var game = Game(instance.Game);
        var capabilities = await CatalogForAsync(game.Id, p?["serverId"]?.GetValue<string>(), ct).ConfigureAwait(false);
        if (!(capabilities?.SupportsPlugins ?? game.SupportsPlugins)) throw new InvalidOperationException("This server does not support plugins.");
    }

    private async Task<ClientDownloadTask> InstallPluginAsync(StoreSource source, StorePackage package, string instanceId, CancellationToken ct, IReadOnlyList<string>? activationTargets = null)
    {
        var selections = new Dictionary<string, string>(StringComparer.Ordinal);
        if (activationTargets is not null)
            foreach (var targetId in activationTargets)
            {
                var target = _store.Load(targetId);
                if (target.Game != package.Game || package.Builds.Count > 0 && !package.Builds.Contains(target.Build, StringComparer.Ordinal))
                    throw new InvalidDataException("The plugin does not support every selected client.");
                var selectionId = package.PluginDefinition is { } definition
                    ? ClientPolicyActivation.SelectionId(target, definition) : package.Id;
                selections.Add(targetId, ClientPolicyActivation.SelectionStamp(target, selectionId));
            }
        var manifest = await SourceDocumentReader.ReadBytesAsync(_sourceHttp, OpenSource(source), package.Manifest, ct).ConfigureAwait(false);
        if (activationTargets is null) return _pluginUpgrades.Enqueue(source, package, manifest, instanceId);
        ClientDownloadTask? task = null;
        foreach (var target in activationTargets)
            task = _pluginUpgrades.Enqueue(source, package, manifest, target, explicitSelection: true, forceEnable: true,
                expectedSelection: selections[target]);
        return task ?? throw new ArgumentException("Missing plugin targets.");
    }

    private ClientDownloadTask[] PendingPluginTransfers(string instanceId)
    {
        _store.Load(instanceId);
        var shared = _pluginUpgrades.TasksFor(instanceId).ToHashSet(StringComparer.Ordinal);
        return _downloads.List().Where(task => task.Target.Kind == "plugin" && task.Stage is not ("completed" or "canceled")
            && (task.Target.InstanceId == instanceId || shared.Contains(task.Id))).ToArray();
    }
}
