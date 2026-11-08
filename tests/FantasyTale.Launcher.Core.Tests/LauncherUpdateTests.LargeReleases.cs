using System.Net;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Fact]
    public async Task LargePublishedIndexDownloadsAndRestoresReadyState()
    {
        var policy = Policy();
        var input = Path.Combine(_root, "release-input");
        Directory.CreateDirectory(Path.Combine(input, "wwwroot"));
        Directory.CreateDirectory(Path.Combine(input, "updater"));
        File.WriteAllText(Path.Combine(input, LauncherRelease.Executable), "new");
        File.WriteAllText(Path.Combine(input, "wwwroot/index.html"), "web");
        File.WriteAllText(Path.Combine(input, LauncherRelease.UpdaterExecutable), "new");
        const int assets = 1100;
        for (var index = 0; index < assets; index++)
            File.WriteAllText(Path.Combine(input, $"wwwroot/asset-{index:D4}-{new string('x', 80)}.js"), "web");
        var configuration = Path.Combine(_root, "large-publication.toml");
        File.WriteAllText(configuration, $"input = 'release-input'\noutput = 'large-public'\nplatform = '{Platform}'\nfeed_path = 'launcher/stable.toml'\nsource_prefix = 'launcher'\nrevision = 2\n");
        var indexPath = await LauncherReleasePublisher.PublishAsync(configuration);
        Assert.True(new FileInfo(indexPath).Length > 256 * 1024);
        var release = LauncherRelease.Parse(policy, await File.ReadAllBytesAsync(indexPath));
        Assert.Equal(assets + 3, release.Files.Count);
        using var handler = new PublishedFilesHandler(Path.Combine(_root, "large-public"));
        using var http = new HttpClient(handler);
        await using (var service = new LauncherUpdateService(Installation, Data, http))
        {
            var discovered = await service.CheckAsync();
            Assert.Equal("available", discovered.Status);
            var queued = await service.DownloadAsync(discovered.ManifestSha256!);
            await UntilAsync(() => service.List()[0].Stage is "ready" or "failed", 30000);
            Assert.Equal("ready", service.List()[0].Stage);
            Assert.Equal(assets + 3, service.List()[0].Files.Count);
            Assert.All(service.List()[0].Files, file =>
            {
                Assert.Equal("verified", file.Stage);
                Assert.Equal(file.Size, file.Received);
            });
            Assert.All(queued.Files, file => Assert.Equal("queued", file.Stage));
            Assert.All(queued.Files, file => Assert.Equal(0, file.Received));
            Assert.Equal(2, handler.ObjectRequests);
        }
        await using var restarted = new LauncherUpdateService(Installation, Data, http);
        restarted.StartMonitoring();
        await UntilAsync(() => restarted.List().Count > 0 && restarted.List()[0].Stage is "ready" or "failed", 30000);
        Assert.Equal("ready", restarted.List()[0].Stage);
        Assert.Equal(assets + 3, restarted.List()[0].Files.Count);
        Assert.Equal(Path.Combine(Data, "launcher-update"), restarted.ReadyDirectory());
    }

    [Fact]
    public void ReleaseSizesAreNotRestrictedByFormerTransferBudgets()
    {
        var release = LauncherRelease.Parse(Policy(), IndexBytes(Manifest(mainHash: new string('a', 64), mainSize: 101L * 1024 * 1024 * 1024)));
        Assert.Equal(101L * 1024 * 1024 * 1024, release.Files[0].Size);
    }

    private sealed class PublishedFilesHandler(string root) : HttpMessageHandler
    {
        private int _objectRequests;
        internal int ObjectRequests => Volatile.Read(ref _objectRequests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri!.AbsolutePath.Contains("/objects/", StringComparison.Ordinal)) Interlocked.Increment(ref _objectRequests);
            var path = Path.Combine(root, request.RequestUri!.AbsolutePath.TrimStart('/'));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(path)) });
        }
    }
}
