using System.Net;
using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    [Theory]
    [InlineData("zip")]
    [InlineData("absent")]
    [InlineData("gone")]
    [InlineData("not-advertised")]
    [InlineData("extra-member")]
    [InlineData("wrong-member")]
    [InlineData("corrupt-zip")]
    [InlineData("forbidden")]
    public async Task OptionalZipTransportPrefersArchiveAndFallsBackOnlyWhenAbsent(string scenario)
    {
        var game = Game(); var folder = Folder(); var release = StoreRelease(folder);
        var definition = File.ReadAllBytes(Path.Combine(folder, "plugin.toml"));
        var zipPath = scenario == "extra-member" ? Zip(("plugin.toml", Manifest, 0), ("extra.txt", "unlisted", 0))
            : Zip(("plugin.toml", scenario == "wrong-member" ? Manifest + "\n# changed" : Manifest, 0));
        var bytes = File.ReadAllBytes(zipPath);
        var offer = new StorePluginArchive("plugins/fixture.zip", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var package = release.Package with { PluginArchive = scenario == "not-advertised" ? null : offer };
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example/", "source.toml", true, false);
        using var handler = new PluginArchiveHandler(bytes, definition, scenario);
        using var http = new HttpClient(handler);
        await using var downloads = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"), _ => source,
            _ => new HttpSource(new Uri(source.BaseUrl!)), library, _ => game);
        var task = downloads.PreloadPlugin(source, package, release.Manifest);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (downloads.List().Single().Stage is not ("completed" or "failed")) await Task.Delay(20, deadline.Token);
        var failed = scenario is "extra-member" or "wrong-member" or "corrupt-zip" or "forbidden";
        Assert.Equal(failed ? "failed" : "completed", downloads.List().Single().Stage);
        Assert.Equal(scenario == "not-advertised" ? 0 : 1, handler.ZipRequests);
        Assert.Equal(scenario is "absent" or "gone" or "not-advertised" ? 1 : 0, handler.MemberRequests);
        if (failed) Assert.Empty(library.Packages(game));
        else
        {
            var installed = Assert.Single(library.Packages(game));
            Assert.Equal(release.Package.ManifestSha256, library.Origin(installed)!.ManifestSha256);
            Assert.Equal(definition, File.ReadAllBytes(Path.Combine(installed.Folder, "plugin.toml")));
        }
    }

    private sealed class PluginArchiveHandler(byte[] zip, byte[] member, string scenario) : HttpMessageHandler
    {
        internal int ZipRequests;
        internal int MemberRequests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var archived = request.RequestUri!.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal);
            if (archived) ZipRequests++; else MemberRequests++;
            var status = archived && scenario == "absent" ? HttpStatusCode.NotFound
                : archived && scenario == "gone" ? HttpStatusCode.Gone
                : archived && scenario == "forbidden" ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new ByteArrayContent(archived ? scenario == "corrupt-zip" ? new byte[zip.Length] : zip : member)
            });
        }
    }
}
