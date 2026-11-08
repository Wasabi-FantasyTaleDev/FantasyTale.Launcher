using System.IO.Compression;
using System.Net;
using System.Text;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task RetryingFailedArchiveFallbackStagesTheOriginalChangedFiles(bool corruptArchive, bool repairIncremental, bool retainInstalledFile)
    {
        var policy = Policy();
        if (retainInstalledFile)
        {
            Directory.CreateDirectory(Path.Combine(Installation, "wwwroot"));
            File.WriteAllText(Path.Combine(Installation, "wwwroot/index.html"), "web");
        }
        using var handler = new ArchiveFallbackHandler("valid") { ArchiveUnavailable = !corruptArchive, CorruptArchiveResponse = corruptArchive };
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var selected = await service.CheckAsync();
        await service.DownloadAsync(selected.ManifestSha256!);
        await UntilAsync(() => service.List()[0].Stage == "failed");
        var firstIncrementalRequests = handler.IncrementalRequests;
        var firstArchiveRequests = handler.ArchiveRequests;
        Assert.True(firstArchiveRequests > 0);
        Assert.DoesNotContain(service.List()[0].Files, file => file.Path == LauncherRelease.Executable);
        handler.IncrementalAvailable = repairIncremental;
        handler.ArchiveUnavailable = false;
        handler.CorruptArchiveResponse = false;
        await Task.Delay(20);
        await service.ActionAsync("retry");
        await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
        Assert.Equal("ready", service.List()[0].Stage);
        Assert.True(handler.IncrementalRequests > firstIncrementalRequests);
        Assert.Equal(repairIncremental, handler.ArchiveRequests == firstArchiveRequests);
        var release = LauncherRelease.Parse(policy, IndexBytes(Manifest()));
        foreach (var file in release.Files)
        {
            var content = Path.Combine(service.ReadyDirectory(), "objects", file.Sha256 + ".verified");
            if (!File.Exists(content)) content = Path.Combine(Installation, file.Path);
            Assert.True(File.Exists(content), "Ready state requires the selected payload: " + file.Path);
            Assert.Equal(file.Sha256, Hash(File.ReadAllBytes(content)));
        }
        if (retainInstalledFile)
        {
            Assert.Equal(0, handler.IndexRequests);
            Assert.DoesNotContain(service.List()[0].Files, file => file.Path == "wwwroot/index.html");
        }
        Assert.False(File.Exists(Path.Combine(Installation, LauncherRelease.Executable)));
    }

    [Fact]
    public async Task RepeatedArchiveFailuresCannotProduceReadyWithoutPayloads()
    {
        Policy();
        using var handler = new ArchiveFallbackHandler("valid") { ArchiveUnavailable = true };
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        await service.DownloadAsync((await service.CheckAsync()).ManifestSha256!);
        await UntilAsync(() => service.List()[0].Stage == "failed");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var requests = handler.IncrementalRequests;
            await Task.Delay(20);
            await service.ActionAsync("retry");
            await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
            Assert.Equal("failed", service.List()[0].Stage);
            Assert.True(handler.IncrementalRequests > requests);
            Assert.Throws<InvalidOperationException>(() => service.ReadyDirectory());
        }
        handler.IncrementalAvailable = true;
        await Task.Delay(20);
        await service.ActionAsync("retry");
        await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
        Assert.Equal("ready", service.List()[0].Stage);
        foreach (var file in LauncherRelease.Parse(LauncherUpdatePolicy.Load(Installation)!, IndexBytes(Manifest())).Files)
            Assert.Equal(file.Sha256, Hash(File.ReadAllBytes(Path.Combine(service.ReadyDirectory(), "objects", file.Sha256 + ".verified"))));
    }

    [Fact]
    public async Task PausedArchiveFallbackResumesTheOriginalIncrementalPlan()
    {
        Policy();
        using var handler = new ArchiveFallbackHandler("valid") { HoldArchive = true };
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        await service.DownloadAsync((await service.CheckAsync()).ManifestSha256!);
        await handler.ArchiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.ActionAsync("pause");
        Assert.Equal("paused", service.List()[0].Stage);
        var incrementalRequests = handler.IncrementalRequests;
        var archiveRequests = handler.ArchiveRequests;
        handler.IncrementalAvailable = true;
        await service.ActionAsync("resume");
        await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
        Assert.Equal("ready", service.List()[0].Stage);
        Assert.True(handler.IncrementalRequests > incrementalRequests);
        Assert.Equal(archiveRequests, handler.ArchiveRequests);
        Assert.Equal(3, service.List()[0].Files.Count);
        foreach (var file in LauncherRelease.Parse(LauncherUpdatePolicy.Load(Installation)!, IndexBytes(Manifest())).Files)
            Assert.Equal(file.Sha256, Hash(File.ReadAllBytes(Path.Combine(service.ReadyDirectory(), "objects", file.Sha256 + ".verified"))));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("split-zip")]
    [InlineData("wrong-release")]
    [InlineData("corrupt-archive")]
    [InlineData("missing-file")]
    [InlineData("wrong-file")]
    [InlineData("unexpected-file")]
    [InlineData("duplicate-file")]
    [InlineData("traversal")]
    public async Task ArchiveFallbackRequiresFailedIncrementalAndVerifiesEverySelectedFile(string scenario)
    {
        Policy();
        using var handler = new ArchiveFallbackHandler(scenario);
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var selected = await service.CheckAsync();
        Assert.Equal(0, handler.ArchiveRequests);
        await service.DownloadAsync(selected.ManifestSha256!);
        await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
        Assert.True(handler.IncrementalRequests >= 2);
        Assert.Equal(scenario is "valid" or "split-zip" ? "ready" : "failed", service.List()[0].Stage);
        Assert.Equal(scenario == "wrong-release" ? 0 : scenario == "split-zip" ? 2 : 1, handler.ArchiveRequests);
        Assert.False(File.Exists(Path.Combine(Installation, LauncherRelease.Executable)));
        if (scenario is "valid" or "split-zip")
        {
            Assert.Equal(scenario == "split-zip" ? 2 : 1, service.List()[0].Files.Count);
            foreach (var file in LauncherRelease.Parse(LauncherUpdatePolicy.Load(Installation)!, IndexBytes(Manifest())).Files)
                Assert.Equal(file.Sha256, Hash(File.ReadAllBytes(Path.Combine(service.ReadyDirectory(), "objects", file.Sha256 + ".verified"))));
        }
    }

    private sealed class ArchiveFallbackHandler : HttpMessageHandler
    {
        private readonly byte[] _archive;
        private readonly string _metadata;
        private readonly bool _split;
        internal int ArchiveRequests;
        internal int IncrementalRequests;
        internal int IndexRequests;
        internal bool IncrementalAvailable;
        internal bool ArchiveUnavailable;
        internal bool CorruptArchiveResponse;
        internal bool HoldArchive;
        internal readonly TaskCompletionSource ArchiveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal ArchiveFallbackHandler(string scenario)
        {
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            {
                void Entry(string path, string value)
                {
                    using var stream = zip.CreateEntry(path).Open();
                    stream.Write(Encoding.UTF8.GetBytes(value));
                }
                Entry(LauncherRelease.Executable, scenario == "wrong-file" ? "bad" : "new");
                Entry("wwwroot/index.html", "web");
                if (scenario != "missing-file") Entry(LauncherRelease.UpdaterExecutable, "new");
                Entry(LauncherUpdatePolicy.FileName, "ignored portable sources");
                if (scenario == "unexpected-file") Entry("unexpected.dll", "bad");
                if (scenario == "duplicate-file") Entry(LauncherRelease.Executable, "new");
                if (scenario == "traversal") Entry("../outside", "bad");
            }
            _archive = scenario == "corrupt-archive" ? "invalid zip"u8.ToArray() : buffer.ToArray();
            _split = scenario == "split-zip";
            var first = _split ? _archive[..(_archive.Length / 2)] : _archive;
            _metadata = $$"""
                schema = 1
                manifest_sha256 = "{{(scenario == "wrong-release" ? new string('a', 64) : Hash(IndexBytes(Manifest())))}}"
                format = "zip"
                [[files]]
                path = "launcher.zip"
                volume = 1
                source_path = "portable/launcher.zip"
                size = {{first.Length}}
                sha256 = "{{Hash(first)}}"
                """;
            if (_split)
            {
                var second = _archive[(_archive.Length / 2)..];
                _metadata += $$"""

                    [[files]]
                    path = "launcher.zip.002"
                    volume = 2
                    source_path = "portable/launcher.zip.002"
                    size = {{second.Length}}
                    sha256 = "{{Hash(second)}}"
                    """;
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("stable.toml", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Manifest()) });
            if (path.EndsWith(".archive.toml", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_metadata) });
            if (path.Contains("portable/launcher.zip", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref ArchiveRequests);
                if (HoldArchive) return HoldArchiveAsync(cancellationToken);
                if (ArchiveUnavailable) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                var bytes = !_split ? _archive : path.EndsWith(".002", StringComparison.Ordinal) ? _archive[(_archive.Length / 2)..] : _archive[..(_archive.Length / 2)];
                if (CorruptArchiveResponse) bytes = Encoding.UTF8.GetBytes(new string('x', bytes.Length));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
            }
            Interlocked.Increment(ref IncrementalRequests);
            if (path.EndsWith("index", StringComparison.Ordinal)) Interlocked.Increment(ref IndexRequests);
            if (IncrementalAvailable)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(path.EndsWith("index", StringComparison.Ordinal) ? "web"u8.ToArray() : "new"u8.ToArray()) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        private async Task<HttpResponseMessage> HoldArchiveAsync(CancellationToken ct)
        {
            ArchiveStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("The held archive request must be canceled.");
        }
    }
}
