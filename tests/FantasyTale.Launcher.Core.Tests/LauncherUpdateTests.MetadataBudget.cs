using System.Net;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Fact]
    public async Task DiscoveryAllowsMetadataTransfersBeyondTheConnectionTimeout()
    {
        Policy();
        using var handler = new SlowIndexHandler();
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));

        var selected = await service.CheckAsync(deadline.Token);

        Assert.Equal("available", selected.Status);
        Assert.Equal(2, selected.Revision);
        Assert.Equal(1, handler.Requests);
    }

    private sealed class SlowIndexHandler : HttpMessageHandler
    {
        internal int Requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            // Index transfer has its own budget after a connection has been established.
            await Task.Delay(TimeSpan.FromSeconds(16), cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(Manifest()) };
        }
    }

    [Fact]
    public async Task ArchiveFallbackAllowsMetadataTransfersBeyondTheConnectionTimeout()
    {
        Policy();
        using var handler = new SlowArchiveIndexHandler();
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var selected = await service.CheckAsync();
        await service.DownloadAsync(selected.ManifestSha256!);
        await UntilAsync(() => service.List()[0].Stage is "ready" or "failed", 35000);

        Assert.Equal("ready", service.List()[0].Stage);
        Assert.Equal(1, handler.Payload.ArchiveRequests);
    }

    private sealed class SlowArchiveIndexHandler : DelegatingHandler
    {
        internal ArchiveFallbackHandler Payload { get; } = new("valid");

        internal SlowArchiveIndexHandler() { InnerHandler = Payload; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".archive.toml", StringComparison.Ordinal))
                await Task.Delay(TimeSpan.FromSeconds(16), cancellationToken);
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
