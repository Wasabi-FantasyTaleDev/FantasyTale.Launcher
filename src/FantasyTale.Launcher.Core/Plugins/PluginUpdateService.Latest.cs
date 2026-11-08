using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

public sealed partial class PluginUpdateService
{
    /// <summary>Verifies latest-only releases across enabled sources and queues selection-safe upgrades before launch.</summary>
    public async Task<IReadOnlyList<ClientDownloadTask>> EnsureLatestAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token); var ct = linked.Token;
        await _operation.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_running(instanceId)) throw new InvalidOperationException("End the running game before updating its plugins.");
            var instance = _instances.Load(instanceId); var game = _game(instance.Game); var build = game.Builds.Single(b => b.Id == instance.Build);
            var packages = _library.Packages(game);
            FilePluginResolver.EnsureInstalled(_instances, instanceId, game, build, packages);
            instance = _instances.Load(instanceId);
            packages = _library.Packages(game);
            var tasks = new List<ClientDownloadTask>();
            var cache = new Dictionary<string, StoreCatalog>(StringComparer.Ordinal);
            foreach (var selection in instance.Plugins)
            {
                var peers = packages.Where(p => p.Id == selection.Id && PluginCatalog.IsAvailable(p, build)).ToArray();
                var policy = peers.Where(p => ProfileClientPolicy.Has(build, p, "latest")).OrderByDescending(p => PluginVersion.Parse(p.Version)).FirstOrDefault();
                if (policy is null || !selection.Enabled && !ClientPolicyActivation.IsMandatory(policy, build)) continue;
                var sources = _sources.List().Where(source => source.Enabled && source.BaseUrl is not null).ToArray();
                var releases = new List<(StoreSource Source, StorePackage Package)>();
                var successfulCatalogs = 0;
                var retired = false;
                foreach (var candidateSource in sources)
                {
                    StoreCatalog catalog;
                    try
                    {
                        if (!cache.TryGetValue(candidateSource.Id, out catalog!))
                        {
                            catalog = await ReadCatalogAsync(candidateSource, ct).ConfigureAwait(false);
                            cache.Add(candidateSource.Id, catalog);
                        }
                        successfulCatalogs++;
                        if (catalog.DeprecatedPlugins.Any(item => item.Id == selection.Id && item.Game == game.Id)) retired = true;
                    }
                    catch (Exception error) when (!ct.IsCancellationRequested && error is HttpRequestException or OperationCanceledException
                        or IOException or DefinitionException or InvalidDataException or System.Text.Json.JsonException)
                    { continue; }
                    var matching = catalog.Packages.Where(package => MatchesIdentity(package, selection.Id)).ToArray();
                    if (matching.Any(package => package.Kind != "plugin" || package.Game != game.Id)) throw new InvalidDataException("plugin_latest_integrity");
                    releases.AddRange(matching.Select(package => (candidateSource, package)));
                }
                if (retired) continue;
                if (sources.Length > 0 && successfulCatalogs == 0) throw new InvalidOperationException("plugin_latest_network");
                var baseline = PluginVersion.Parse(selection.Version);
                var eligible = releases.Where(offer => offer.Package.Builds.Count == 0 || offer.Package.Builds.Contains(build.Id, StringComparer.Ordinal))
                    .Where(offer => baseline.Pre.Length > 0 || PluginVersion.Parse(offer.Package.Version).Pre.Length == 0)
                    .ToArray();
                if (eligible.Length == 0) throw new InvalidDataException("plugin_latest_unpublished");
                string canonical;
                try { canonical = PreferredIdentity(selection.Id, eligible.Select(offer => offer.Package)); }
                catch (DefinitionException) { throw new InvalidDataException("plugin_latest_integrity"); }
                eligible = eligible.Where(offer => offer.Package.Id == canonical)
                    .OrderByDescending(offer => PluginVersion.Parse(offer.Package.Version)).ToArray();
                var (source, latest) = eligible[0];
                if ((latest.Id == selection.Id && PluginVersion.Parse(latest.Version).CompareTo(baseline) < 0) || eligible.Skip(1).Any(offer =>
                    PluginVersion.Parse(offer.Package.Version).CompareTo(PluginVersion.Parse(latest.Version)) == 0 &&
                    !offer.Package.ManifestSha256.Equals(latest.ManifestSha256, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("plugin_latest_integrity");
                instance = _instances.Load(instanceId);
                var activationId = latest.Id;
                var selectionStamp = ClientPolicyActivation.SelectionStamp(instance, selection.Id);
                LauncherCompatibility.Require(latest.MinimumLauncherVersion, latest.Id);
                var installed = packages.SingleOrDefault(p => p.Id == activationId && p.Version == latest.Version);
                if (installed is not null)
                {
                    var installedOrigin = _library.Origin(installed);
                    // Bundled definitions can change with launcher releases or checkout line endings.
                    // Install the catalog-pinned snapshot before treating them as a store release.
                    if (installedOrigin is null && _installRequired is not null)
                    {
                        tasks.Add(await _installRequired(source, latest, instanceId,
                            selectionStamp, ct).ConfigureAwait(false));
                        continue;
                    }
                    if (installedOrigin is not null && installedOrigin.ManifestSha256 != latest.ManifestSha256)
                        throw new InvalidDataException("plugin_latest_integrity");
                    var manifest = await SourceDocumentReader.ReadBytesAsync(_http, _openSource(source), latest.Manifest, ct).ConfigureAwait(false);
                    var files = ClientRelease.Parse(latest, manifest);
                    if (files.Any(f => f.PluginArchive))
                    {
                        if (installedOrigin is null || !_library.MatchesInstalledSnapshot(installed, ct)) throw new InvalidDataException("plugin_latest_integrity");
                    }
                    else foreach (var file in files)
                    {
                        var path = ClientInstallTransaction.Safe(installed.Folder, file.Path);
                        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        if (input.Length != file.Size || !Convert.ToHexString(SHA256.HashData(input)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("plugin_latest_integrity");
                    }
                    if (selection.Id != activationId || selection.Version != latest.Version)
                    {
                        using var lease = _instances.Acquire(instanceId);
                        ClientPolicyActivation.EnableLeased(_instances, instanceId, game, packages, activationId, latest.Version,
                            selectionStamp, upgrade: true, selectionPlugin: selection.Id);
                    }
                    continue;
                }
                if (_installRequired is null) throw new InvalidOperationException("plugin_latest_source");
                tasks.Add(await _installRequired(source, latest, instanceId, selectionStamp, ct).ConfigureAwait(false));
            }
            return tasks;
        }
        catch (Exception error) when (!ct.IsCancellationRequested && error is HttpRequestException or OperationCanceledException)
        { throw new InvalidOperationException("plugin_latest_network", error); }
        finally { _operation.Release(); }
    }
}
