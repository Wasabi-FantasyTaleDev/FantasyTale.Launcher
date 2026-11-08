using System.Net;
using System.Net.Sockets;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Downloads.AList;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class PublicStorePluginTests
{
    private sealed class PublicPluginFactAttribute : FactAttribute
    {
        public PublicPluginFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_PLUGIN_ALIST_URL")))
                Skip = "Set FT_TEST_PLUGIN_ALIST_URL for anonymous published plugin installation acceptance.";
        }
    }

    [PublicPluginFact]
    public async Task PublishedPluginsInstallWithTheirOptionsWithoutExecutingNativeCode()
    {
        var root = Directory.CreateTempSubdirectory("ft-public-plugins-").FullName;
        try
        {
            var repository = TestRepository.StartDirectory();
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "AGENTS.md"))) repository = repository.Parent;
            Assert.NotNull(repository);
            var game = GameCatalog.Load(Path.Combine(repository.FullName, "games")).Single(g => g.Id == "bubblefighter");
            var source = new StoreSourceStore(Path.Combine(root, "sources")).List().Single(s => s.BuiltIn) with
            { BaseUrl = Environment.GetEnvironmentVariable("FT_TEST_PLUGIN_ALIST_URL") };
            var address = new Uri(source.BaseUrl!);
            using var handler = new SocketsHttpHandler { UseProxy = false };
            var pinned = Environment.GetEnvironmentVariable("FT_TEST_PLUGIN_ADDRESS");
            if (!string.IsNullOrWhiteSpace(pinned))
            {
                var ip = IPAddress.Parse(pinned);
                handler.ConnectCallback = async (context, ct) =>
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        if (context.DnsEndPoint.Host.Equals(address.Host, StringComparison.OrdinalIgnoreCase))
                            await socket.ConnectAsync(new IPEndPoint(ip, context.DnsEndPoint.Port), ct);
                        else await socket.ConnectAsync(context.DnsEndPoint, ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch { socket.Dispose(); throw; }
                };
            }
            using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var ct = deadline.Token;
            var transport = new AListSource(http, address);
            var catalog = StoreCatalogFile.Parse("public-plugin-acceptance",
                await SourceDocumentReader.ReadAsync(http, transport, source.CatalogPath, ct));
            using var library = new PluginLibrary(Path.Combine(root, "library"));
            foreach (var id in new[] { "bubblefighter.fantasy.numeric-health", "bubblefighter.fantasy.mid-match-exit", "bubblefighter.fantasy.login-fix-kr" })
            {
                var build = game.Builds.Single(b => b.Id == (id == "bubblefighter.fantasy.login-fix-kr" ? "kr-1256" : "kr-812"));
                var package = catalog.Packages.Where(p => p.Kind == "plugin" && p.Id == id &&
                    (p.Builds.Count == 0 || p.Builds.Contains(build.Id))).OrderByDescending(p => PluginVersion.Parse(p.Version)).First();
                var manifest = await SourceDocumentReader.ReadBytesAsync(http, transport, package.Manifest, ct);
                var files = ClientRelease.Parse(package, manifest);
                var folder = Directory.CreateDirectory(Path.Combine(root, id)).FullName;
                foreach (var file in files)
                {
                    var destination = Path.Combine(folder, file.Path);
                    var resolved = await transport.ResolveAsync(file.SourcePath, ct);
                    await new HttpDownloadEngine(http).DownloadAsync(new(resolved, destination, file.Size, file.Sha256), null, ct);
                    Assert.True(await HttpDownloadEngine.MatchesAsync(destination, file.Size, file.Sha256, ct));
                }
                var plugin = library.InstallFromStore(folder, game, build, package, manifest, source.Id, StoreSourceIdentity.Of(source), ct);
                Assert.Equal(package.Version, plugin.Version);
                Assert.Equal(package.ManifestSha256, library.Origin(plugin)!.ManifestSha256);
                var defaults = PluginCatalog.ResolveOptionValues(plugin, new Dictionary<string, object>());
                if (id.EndsWith("numeric-health", StringComparison.Ordinal))
                {
                    Assert.Equal("native", defaults["placement"]);
                    Assert.Equal("yellow", defaults["font_color"]);
                    Assert.Equal(false, defaults["raw_health"]);
                    Assert.Equal(true, defaults["show_percent"]);
                    Assert.Equal("yellow", PluginCatalog.ResolveOptionValues(plugin,
                        new Dictionary<string, object> { ["font_color"] = "yellow" })["font_color"]);
                }
                else if (id.EndsWith("mid-match-exit", StringComparison.Ordinal))
                {
                    Assert.Equal(false, defaults["all_modes"]);
                    Assert.Equal(true, PluginCatalog.ResolveOptionValues(plugin,
                        new Dictionary<string, object> { ["all_modes"] = true })["all_modes"]);
                }
                else
                {
                    Assert.True(FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Has(build, plugin.Id, "require"));
                    Assert.True(FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Has(build, plugin.Id, "latest"));
                    Assert.Equal(source.Id, plugin.UpdateSource);
                    Assert.True(PluginCatalog.IsRequired(plugin, build));
                    Assert.All(game.Builds.Where(b => b.Id != build.Id), b => Assert.False(PluginCatalog.IsAvailable(plugin, b)));
                }
                Assert.Equal(plugin.Folder, library.InstallFromStore(folder, game, build, package, manifest,
                    source.Id, StoreSourceIdentity.Of(source), ct).Folder);
                Console.WriteLine($"Installed published {plugin.Id} {plugin.Version}: {files.Count} verified files; options and source receipt accepted.");
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
