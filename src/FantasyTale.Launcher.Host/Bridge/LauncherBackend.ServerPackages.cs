using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Bridge;

internal sealed partial class LauncherBackend
{
    private void RegisterServerPackages(BridgeRouter bridge)
    {
        bridge.Add("hosting.packages", _ => _serverPackages.List());
        bridge.Add("store.installServer", async (p, ct) =>
        {
            Keys(p, "sourceId", "packageId", "version", "manifestSha256");
            var source = _sources.List().SingleOrDefault(s => s.Id == Text(p, "sourceId")) ?? throw new ArgumentException("Unknown source.");
            if (!source.Enabled || source.BaseUrl is null) throw new InvalidOperationException("Configure and enable this source first.");
            if (!await _catalogRequests.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("Too many catalog requests.");
            try
            {
                var catalog = await _catalogs.GetAsync(source, force: true, cancellationToken: ct).ConfigureAwait(false);
                var package = catalog.Packages.SingleOrDefault(item => item.Kind == "server" && item.Id == Text(p, "packageId") &&
                    item.Version == Text(p, "version") && item.ManifestSha256 == Text(p, "manifestSha256"))
                    ?? throw new InvalidOperationException("The release changed. Refresh the store before installing.");
                var game = Game(package.Game);
                if (game.Status != GameStatus.Enabled || !game.Builds.Any(b => b.HostingAdapter.Length > 0 && package.Builds.Contains(b.Id, StringComparer.Ordinal)))
                    throw new InvalidOperationException("This server package has no supported local hosting build.");
                var manifest = package.GitHubAsset is not null ? GitHubStoreReleases.Manifest(package)
                    : await SourceDocumentReader.ReadBytesAsync(_sourceHttp, OpenSource(source), package.Manifest, ct).ConfigureAwait(false);
                return _downloads.StartServer(source, package, manifest);
            }
            finally { _catalogRequests.Release(); }
        });
    }
}
