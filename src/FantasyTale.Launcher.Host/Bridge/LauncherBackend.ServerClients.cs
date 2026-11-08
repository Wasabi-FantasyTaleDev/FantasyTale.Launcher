using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    private void RegisterServerClients(BridgeRouter bridge)
    {
        bridge.Add("servers.clientRelease", async (p, ct) =>
        {
            Keys(p, "serverId", "presetId");
            if (!await _catalogRequests.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("Too many catalog requests.");
            try { return await ServerClientReleaseAsync(Text(p, "serverId"), Text(p, "presetId"), ct).ConfigureAwait(false); }
            finally { _catalogRequests.Release(); }
        });
        bridge.Add("servers.installClient", async (p, ct) =>
        {
            Keys(p, "serverId", "presetId", "sourceId", "manifestSha256", "instanceId", "archivePassword");
            if (!await _catalogRequests.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("Too many catalog requests.");
            try
            {
                var release = await ServerClientReleaseAsync(Text(p, "serverId"), Text(p, "presetId"), ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("This server no longer offers a client download.");
                if (release.Source.Id != Text(p, "sourceId") || release.Package.ManifestSha256 != Text(p, "manifestSha256"))
                    throw new InvalidOperationException("The client download changed. Review it again before installing.");
                var instance = _store.Load(Text(p, "instanceId"));
                if (instance.Game != release.Package.Game || instance.Build != release.Package.Builds.Single())
                    throw new InvalidOperationException("The client download does not match this installation.");
                var existing = _sources.List().SingleOrDefault(s => s.Id == release.Source.Id);
                if (existing is not null && (!existing.Enabled || existing.Kind != release.Source.Kind || existing.BaseUrl is null ||
                    SourceAddress.BaseUrl(existing.BaseUrl) != SourceAddress.BaseUrl(release.Source.BaseUrl!) || existing.CatalogPath != release.Source.CatalogPath))
                    throw new InvalidOperationException("This source was disabled or changed. Review its settings before installing.");
                // Discovery is read-only. Explicit installation enrolls the ordinary source
                // so task recovery and future updates retain their existing identity fences.
                if (existing is null) _sources.Save(release.Source);
                return await InstallVerifiedAsync(existing ?? release.Source, release.Package, instance.Id, ct,
                    archivePassword: p?["archivePassword"]?.GetValue<string>()).ConfigureAwait(false);
            }
            finally { _catalogRequests.Release(); }
        });
    }

    private async Task<ServerClientRelease?> ServerClientReleaseAsync(string serverId, string presetId, CancellationToken ct)
    {
        var server = await ConnectedServerAsync(serverId, ct).ConfigureAwait(false);
        if (server.ApiUrl is null) return null;
        var catalog = await ReadCatalogAsync(server, ct).ConfigureAwait(false);
        var preset = catalog.Instances.SingleOrDefault(p => p.Id == presetId) ?? throw new ArgumentException("Unknown server client preset.");
        Game(server.Game).Builds.SingleOrDefault(build => build.Id == preset.Build)?.RequireEnabled();
        if (preset.DownloadSource is not { } download) return null;
        var root = SourceAddress.BaseUrl(download.BaseUrl).AbsoluteUri;
        var path = SourceAddress.RelativePath(download.CatalogPath);
        var identity = ServerStore.ApiBase(server.ApiUrl).AbsoluteUri + "\n" + download.Kind + "\n" + root + "\n" + path;
        var id = "server-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        var source = new StoreSource(id, server.Name, download.Kind, root, path, true, false);
        var store = await _catalogs.GetAsync(source, force: true, cancellationToken: ct).ConfigureAwait(false);
        var choices = store.Packages.Where(p => p.Id == download.PackageId).ToArray();
        if (choices.Length == 0) throw new InvalidDataException("The server's client package is missing from its source.");
        if (choices.Any(p => p.Kind != "client" || p.Game != server.Game || p.Builds.Count != 1 || p.Builds[0] != preset.Build))
            throw new InvalidDataException("The server's client package has incompatible game or build metadata.");
        StorePackage selected;
        if (choices.Length == 1) selected = choices[0];
        else
        {
            var revision = choices.Max(p => p.Revision);
            if (choices.Any(p => p.UpdateMode != "release") || revision <= 0 || choices.Count(p => p.Revision == revision) != 1)
                throw new InvalidDataException("The server's client package has ambiguous releases.");
            selected = choices.Single(p => p.Revision == revision);
        }
        return new(source, selected);
    }
}
