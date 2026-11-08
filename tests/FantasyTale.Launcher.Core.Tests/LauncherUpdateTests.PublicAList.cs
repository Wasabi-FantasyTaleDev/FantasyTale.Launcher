using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FantasyTale.Launcher.Core.Updates;
using FantasyTale.Launcher.Downloads;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    private sealed class PublicLauncherFactAttribute : FactAttribute
    {
        public PublicLauncherFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_LAUNCHER_ALIST_URL")))
                Skip = "Set FT_TEST_LAUNCHER_ALIST_URL on Windows for anonymous real-source launcher staging acceptance.";
        }
    }

    [PublicLauncherFact]
    public async Task PublicAListLauncherDownloadsVerifiedObjectsAndRestoresReadyWithoutNetwork()
    {
        var source = new Uri(Environment.GetEnvironmentVariable("FT_TEST_LAUNCHER_ALIST_URL")!);
        var feed = Environment.GetEnvironmentVariable("FT_TEST_LAUNCHER_FEED") ?? $"launcher/{Platform}/current.toml";
        Directory.CreateDirectory(Installation);
        File.WriteAllText(Path.Combine(Installation, LauncherUpdatePolicy.FileName),
            $"kind = \"alist\"\nbase_url = {JsonSerializer.Serialize(source.AbsoluteUri)}\nfeed_path = {JsonSerializer.Serialize(feed)}\n");
        var policy = LauncherUpdatePolicy.Load(Installation)!;
        using var handler = new SocketsHttpHandler { UseProxy = false };
        var address = Environment.GetEnvironmentVariable("FT_TEST_LAUNCHER_ADDRESS");
        if (!string.IsNullOrWhiteSpace(address))
        {
            var pinned = IPAddress.Parse(address);
            handler.ConnectCallback = async (context, ct) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    if (context.DnsEndPoint.Host.Equals(source.Host, StringComparison.OrdinalIgnoreCase))
                        await socket.ConnectAsync(new IPEndPoint(pinned, context.DnsEndPoint.Port), ct);
                    else await socket.ConnectAsync(context.DnsEndPoint, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            };
        }
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        var publicIndex = await SourceDocumentReader.ReadBytesAsync(http, policy.OpenSource(http), policy.FeedPath, CancellationToken.None);
        Console.WriteLine($"Public launcher acceptance runtime: {policy.Platform}.");
        var publicRelease = LauncherRelease.Parse(policy, publicIndex);
        Assert.NotEmpty(publicRelease.Files);
        string selection;
        await using (var service = new LauncherUpdateService(Installation, Data, http))
        {
            service.SavePreferences(new(false, false));
            var available = await service.CheckAsync();
            Assert.True(available.Status == "available", $"Public launcher discovery returned {available.Status} ({available.Failure ?? "no failure code"}).");
            selection = available.ManifestSha256!;
            await service.DownloadAsync(selection);
            await UntilAsync(() => service.List()[0].Stage is "ready" or "failed", 900000);
            Assert.True(service.List()[0].Stage == "ready", $"Public launcher staging returned {service.List()[0].Stage} ({service.List()[0].Failure ?? "no failure code"}).");
            var stage = service.ReadyDirectory();
            var release = LauncherRelease.Parse(policy, File.ReadAllBytes(Path.Combine(stage, "release.toml")));
            Assert.Equal(selection, release.ManifestSha256);
            Assert.NotEmpty(release.Files);
            foreach (var file in release.Files)
                Assert.True(await HttpDownloadEngine.MatchesAsync(Path.Combine(stage, "objects", file.Sha256 + ".verified"), file.Size, file.Sha256, CancellationToken.None), file.Path);
            Assert.False(File.Exists(Path.Combine(Installation, LauncherRelease.Executable)));
            Console.WriteLine($"Public AList staging verified {release.Files.Count} files, {release.Files.Sum(file => file.Size)} bytes, revision {release.Revision}.");
        }
        using var offlineHandler = new FeedHandler([]) { Unavailable = true };
        using var offline = new HttpClient(offlineHandler);
        await using var restored = new LauncherUpdateService(Installation, Data, offline);
        restored.StartMonitoring();
        await UntilAsync(() => restored.List().Count > 0 && restored.List()[0].Stage is "ready" or "failed", 120000);
        Assert.Equal("ready", restored.List()[0].Stage);
        Assert.Equal(selection, restored.State.ManifestSha256);
        Assert.Equal(Path.Combine(Data, "launcher-update"), restored.ReadyDirectory());
    }
}
