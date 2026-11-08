using System.Net;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RetiredBundledUpdateEndpointMigratesWithoutReplacingTheSavedPolicy(int schema)
    {
        Directory.CreateDirectory(Installation);
        var path = Path.Combine(Installation, LauncherUpdatePolicy.FileName);
        var text = $"schema = {schema}\n" + (schema == 2 ? "[[sources]]\n" : "") + $"""
            name = "Saved archive"
            kind = "alist"
            base_url = "http://wanwanlocksmith.pp.ua:5244/"
            feed_path = "FantasyTale/launcher/{Platform}/current.toml"
            minimum_revision = 1234
            """;
        File.WriteAllText(path, text);
        var policy = LauncherUpdatePolicy.Load(Installation)!;
        Assert.Equal("archive-cn.600819.xyz", policy.BaseUrl.Host);
        Assert.True(policy.PreferIp);
        Assert.Equal(1234, policy.MinimumRevision);
        Assert.Equal("Saved archive", policy.Name);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"alist\nhttp://wanwanlocksmith.pp.ua:5244/\nFantasyTale/launcher/{Platform}/current.toml"))), policy.Identity);
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Theory]
    [InlineData("https://wanwanlocksmith.pp.ua:5244/", "")]
    [InlineData("http://wanwanlocksmith.pp.ua:5244/custom/", "")]
    [InlineData("http://custom.example:5244/", "")]
    [InlineData("http://wanwanlocksmith.pp.ua:5244/", "signature_path = \"current.sig\"\nsignature_public_key = \"retained-key\"")]
    public void CustomAndSignedUpdateSourcesRetainTheirAuthority(string address, string extra)
    {
        Directory.CreateDirectory(Installation);
        File.WriteAllText(Path.Combine(Installation, LauncherUpdatePolicy.FileName), $"""
            schema = 1
            kind = "alist"
            base_url = "{address}"
            feed_path = "FantasyTale/launcher/{Platform}/current.toml"
            {extra}
            """);
        Assert.Equal(address, LauncherUpdatePolicy.Load(Installation)!.BaseUrl.AbsoluteUri);
    }

    [Fact]
    public async Task AutomaticDownloadIsEnabledForNewInstallationsButSavedOptOutIsPreserved()
    {
        Policy();
        using var http = new HttpClient();
        await using (var service = new LauncherUpdateService(Installation, Data, http))
            Assert.True(service.Preferences.AutoDownload);

        Directory.CreateDirectory(Path.Combine(Data, "launcher-update"));
        File.WriteAllText(Path.Combine(Data, "launcher-update", "preferences.toml"), "auto_check = true\nauto_download = false\n");
        await using var reopened = new LauncherUpdateService(Installation, Data, http);
        Assert.False(reopened.Preferences.AutoDownload);
    }

    [Fact]
    public async Task EditingSourcesPersistsTheirOrderAndCannotLowerTheInstallationBaseline()
    {
        MultiplePolicies();
        using var http = new HttpClient();
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var original = service.Sources();
        var saved = await service.SaveSourcesAsync([
            original[1] with { Name = "Backup", MinimumRevision = 0, PreferIp = true },
            original[0] with { Name = "Primary", MinimumRevision = 0 }
        ]);
        Assert.Equal("second.example", new Uri(saved[0].BaseUrl).Host);
        Assert.All(saved, source => Assert.Equal(1, source.MinimumRevision));
        Assert.True(saved[0].PreferIp);
        Assert.False(saved[1].PreferIp);
        await using var reopened = new LauncherUpdateService(Installation, Data, http);
        Assert.Equal(saved, reopened.Sources());
    }

    [Fact]
    public async Task InvalidSourceEditsKeepThePreviousConfigurationIntact()
    {
        MultiplePolicies();
        using var http = new HttpClient();
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var original = service.Sources();
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveSourcesAsync([original[0], original[0]]));
        Assert.Equal(original, service.Sources());
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveSourcesAsync([]));
        Assert.Equal(original, service.Sources());
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveSourcesAsync([original[0] with { MinimumRevision = -1 }]));
        Assert.Equal(original, service.Sources());
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveSourcesAsync([original[0] with { BaseUrl = null! }]));
        Assert.Equal(original, service.Sources());
    }

    [Fact]
    public async Task SourceEditingRequiresDetachedSignatureAndTrustedKeyTogether()
    {
        MultiplePolicies();
        using var http = new HttpClient();
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var original = service.Sources();

        await Assert.ThrowsAsync<DefinitionException>(() => service.SaveSourcesAsync([
            original[0] with { SignaturePath = "current.toml.sig" }
        ]));
        Assert.Equal(original, service.Sources());

        await Assert.ThrowsAsync<DefinitionException>(() => service.SaveSourcesAsync([
            original[0] with { SignaturePublicKey = "not-base64" }
        ]));
        Assert.Equal(original, service.Sources());
    }

    private void MultiplePolicies()
    {
        Directory.CreateDirectory(Installation);
        File.WriteAllText(Path.Combine(Installation, LauncherUpdatePolicy.FileName), """
            schema = 2
            [[sources]]
            kind = "http"
            base_url = "https://first.example/"
            feed_path = "current.toml"
            minimum_revision = 1
            [[sources]]
            kind = "http"
            base_url = "https://second.example/"
            feed_path = "current.toml"
            minimum_revision = 1
            """);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedPayloadUsesOnlyAnEquivalentSource(bool equivalent)
    {
        MultiplePolicies();
        using var handler = new MirrorPayloadHandler(equivalent);
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var selected = await service.CheckAsync();
        Assert.Equal(2, selected.Revision);
        await service.DownloadAsync(selected.ManifestSha256!);
        await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
        Assert.Equal(equivalent ? "ready" : "failed", service.List()[0].Stage);
        Assert.Equal(equivalent, handler.MirrorPayloadRequests > 0);
        Assert.Equal(selected.ManifestSha256, service.State.ManifestSha256);
    }

    [Fact]
    public async Task CorruptPrimaryPayloadUsesOnlyAnEquivalentSource()
    {
        MultiplePolicies();
        using var handler = new MirrorPayloadHandler(equivalent: true, corruptPrimary: true);
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var selected = await service.CheckAsync();
        await service.DownloadAsync(selected.ManifestSha256!);
        await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
        Assert.Equal("ready", service.List()[0].Stage);
        Assert.True(handler.MirrorPayloadRequests > 0);
    }

    private sealed class MirrorPayloadHandler(bool equivalent, bool corruptPrimary = false) : HttpMessageHandler
    {
        internal int MirrorPayloadRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.EndsWith("current.toml", StringComparison.Ordinal))
            {
                var index = uri.Host == "first.example" || equivalent ? Manifest() : Manifest(mainHash: Hash("bad"u8.ToArray()));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(index) });
            }
            if (uri.Host == "first.example")
            {
                if (!corruptPrimary) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("corrupt"u8.ToArray()) });
            }
            Interlocked.Increment(ref MirrorPayloadRequests);
            var bytes = uri.AbsolutePath.EndsWith("index", StringComparison.Ordinal) ? "web"u8.ToArray() : "new"u8.ToArray();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }

    [Fact]
    public async Task DiscoveryDoesNotOpenInstalledPayloadsAndStagingListsOnlyChangedFiles()
    {
        Policy();
        var executable = Path.Combine(Installation, LauncherRelease.Executable);
        File.WriteAllText(executable, "old");
        Directory.CreateDirectory(Path.Combine(Installation, "wwwroot"));
        File.WriteAllText(Path.Combine(Installation, "wwwroot", "index.html"), "web");
        Directory.CreateDirectory(Path.Combine(Installation, "updater"));
        File.WriteAllText(Path.Combine(Installation, "updater", "FantasyTale.Launcher.Updater.exe"), "new");
        using var handler = new FeedHandler(IndexBytes(Manifest()));
        using var http = new HttpClient(handler);
        await using (var service = new LauncherUpdateService(Installation, Data, http))
        {
            LauncherUpdateState selected;
            using (var held = new FileStream(executable, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                selected = await service.CheckAsync();
            Assert.Equal("available", selected.Status);
            var task = await service.DownloadAsync(selected.ManifestSha256!);
            Assert.Equal(LauncherRelease.Executable, Assert.Single(task.Files).Path);
            await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
            Assert.Equal("ready", service.List()[0].Stage);
            Assert.Equal(LauncherRelease.Executable, Assert.Single(service.List()[0].Files).Path);
            Assert.Equal(1, handler.Requests);
        }
        handler.Unavailable = true;
        await using var reopened = new LauncherUpdateService(Installation, Data, http);
        reopened.StartMonitoring();
        await UntilAsync(() => reopened.List().Count > 0 && reopened.List()[0].Stage is "ready" or "failed");
        Assert.Equal("ready", reopened.List()[0].Stage);
        Assert.Equal(LauncherRelease.Executable, Assert.Single(reopened.List()[0].Files).Path);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public void SharedObjectsCannotDeclareConflictingSizes()
    {
        var policy = Policy();
        var conflicting = $$"""
            [[files]]
            path = "conflicting.dll"
            source_path = "objects/app"
            size = 4
            sha256 = "{{Hash("new"u8.ToArray())}}"
            """;
        Assert.Throws<InvalidDataException>(() => LauncherRelease.Parse(policy, IndexBytes(Manifest(extra: conflicting))));
    }

    [Fact]
    public async Task StagingDownloadsDistinctObjectsConcurrentlyAndSharesDuplicateHashes()
    {
        Policy();
        using var handler = new ConcurrentObjectHandler();
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var selected = await service.CheckAsync();
        await service.DownloadAsync(selected.ManifestSha256!);
        try
        {
            await handler.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, Volatile.Read(ref handler.PayloadRequests));
            Assert.NotEqual("ready", service.State.Status);
        }
        finally { handler.Release.TrySetResult(); }
        await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
        Assert.Equal("ready", service.List()[0].Stage);
        Assert.Equal(2, handler.PayloadRequests);
        Assert.Equal(3, service.List()[0].Files.Count);
        Assert.All(service.List()[0].Files, file => Assert.Equal("verified", file.Stage));
    }

    private sealed class ConcurrentObjectHandler : HttpMessageHandler
    {
        internal readonly TaskCompletionSource BothStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int PayloadRequests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("stable.toml", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new StringContent(Manifest()) };
            if (Interlocked.Increment(ref PayloadRequests) == 2) BothStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri.AbsolutePath.EndsWith("index", StringComparison.Ordinal) ? "web"u8.ToArray() : "new"u8.ToArray()) };
        }
    }

    [Fact]
    public async Task FirstValidAvailableSourceIsSelectedWithoutRequestingBackup()
    {
        MultiplePolicies();
        using var handler = new MultipleFeedHandler(Manifest(2), Manifest(3));
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        var result = await service.CheckAsync();
        Assert.Equal("available", result.Status);
        Assert.Equal(2, result.Revision);
        Assert.Equal("first.example", LauncherUpdatePolicy.LoadSelected(Installation, Path.Combine(Data, "launcher-update"))!.BaseUrl.Host);
        Assert.Equal("first.example", Assert.Single(handler.RequestedHosts));
        Assert.Single(service.SourceResults);
    }

    [Fact]
    public async Task CurrentPrimaryStopsDiscoveryWithoutRequestingNewerBackup()
    {
        MultiplePolicies();
        using var handler = new MultipleFeedHandler(Manifest(1), Manifest(3));
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);

        var result = await service.CheckAsync();

        Assert.Equal("current", result.Status);
        Assert.Equal(1, result.Revision);
        Assert.Equal("first.example", LauncherUpdatePolicy.LoadSelected(Installation, Path.Combine(Data, "launcher-update"))!.BaseUrl.Host);
        Assert.Equal("first.example", Assert.Single(handler.RequestedHosts));
    }

    [Fact]
    public async Task StartupDiscoveryBeginsImmediatelyAndMonitorReusesItsResult()
    {
        MultiplePolicies();
        using var handler = new MultipleFeedHandler(Manifest(1), Manifest(3)) { BlockFirst = true };
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);

        var startup = service.BeginStartupCheck();
        await handler.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("checking", service.State.Status);
        handler.ReleaseFirst.SetResult();
        Assert.Equal("current", (await startup).Status);

        service.StartMonitoring();
        await Task.Delay(100);
        Assert.Equal("first.example", Assert.Single(handler.RequestedHosts));
    }

    [Fact]
    public async Task FailedPrimaryFallsBackToNextConfiguredSource()
    {
        MultiplePolicies();
        using var handler = new MultipleFeedHandler(null, Manifest(3));
        handler.ReleaseSecond.SetResult();
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);

        var result = await service.CheckAsync();

        Assert.Equal("available", result.Status);
        Assert.Equal(3, result.Revision);
        Assert.Equal("second.example", LauncherUpdatePolicy.LoadSelected(Installation, Path.Combine(Data, "launcher-update"))!.BaseUrl.Host);
        Assert.Collection(handler.RequestedHosts,
            host => Assert.Equal("first.example", host),
            host => Assert.Equal("second.example", host));
    }

    [Fact]
    public async Task ChangedPreviouslySeenSourceCannotHideAValidOtherSource()
    {
        MultiplePolicies();
        using (var first = new MultipleFeedHandler(Manifest(4), Manifest(3)))
        using (var http = new HttpClient(first))
        {
            await using var service = new LauncherUpdateService(Installation, Data, http);
            Assert.Equal(4, (await service.CheckAsync()).Revision);
            Assert.Equal("first.example", Assert.Single(first.RequestedHosts));
        }
        using var changed = new MultipleFeedHandler(Manifest(4) + "\n", Manifest(3));
        using var nextHttp = new HttpClient(changed);
        changed.ReleaseSecond.SetResult();
        await using var reopened = new LauncherUpdateService(Installation, Data, nextHttp);
        var result = await reopened.CheckAsync();
        Assert.Equal("available", result.Status);
        Assert.Equal(3, result.Revision);
        Assert.Equal("second.example", LauncherUpdatePolicy.LoadSelected(Installation, Path.Combine(Data, "launcher-update"))!.BaseUrl.Host);
        Assert.Collection(changed.RequestedHosts,
            host => Assert.Equal("first.example", host),
            host => Assert.Equal("second.example", host));
    }

    [Fact]
    public async Task FailureInTheFirstSourceDoesNotHideAValidSecondSource()
    {
        MultiplePolicies();
        using var handler = new MultipleFeedHandler(null, Manifest(3));
        handler.ReleaseSecond.SetResult();
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        Assert.Equal("available", (await service.CheckAsync()).Status);
        Assert.Equal("failed", service.SourceResults[0].State.Status);
        Assert.Equal("feedMissing", service.SourceResults[0].State.FailureReason);
        Assert.Equal("available", service.SourceResults[1].State.Status);
        Assert.Collection(handler.RequestedHosts,
            host => Assert.Equal("first.example", host),
            host => Assert.Equal("second.example", host));
    }

    [Theory]
    [InlineData("outdated", "feedOutdated")]
    [InlineData("changed", "feedChanged")]
    [InlineData("unchanged", null)]
    public async Task EachSourceRetainsItsRevisionHistoryAcrossFallbackAndRestart(string scenario, string? rejection)
    {
        MultiplePolicies();
        using (var handler = new MultipleFeedHandler(Manifest(4), Manifest(3)))
        using (var http = new HttpClient(handler))
        {
            await using var first = new LauncherUpdateService(Installation, Data, http);
            Assert.Equal(4, (await first.CheckAsync()).Revision);
        }
        using (var handler = new MultipleFeedHandler(null, Manifest(3)))
        using (var http = new HttpClient(handler))
        {
            handler.ReleaseSecond.SetResult();
            await using var fallback = new LauncherUpdateService(Installation, Data, http);
            Assert.Equal(3, (await fallback.CheckAsync()).Revision);
            Assert.Equal("second.example", LauncherUpdatePolicy.LoadSelected(Installation, Path.Combine(Data, "launcher-update"))!.BaseUrl.Host);
        }
        var returnedIndex = scenario switch
        {
            "outdated" => Manifest(2),
            "changed" => Manifest(4) + "\n",
            _ => Manifest(4),
        };
        using var returned = new MultipleFeedHandler(returnedIndex, Manifest(3));
        returned.ReleaseSecond.SetResult();
        using var nextHttp = new HttpClient(returned);
        await using var restored = new LauncherUpdateService(Installation, Data, nextHttp);
        var result = await restored.CheckAsync();
        Assert.Equal("available", result.Status);
        Assert.Equal(rejection is null ? 4 : 3, result.Revision);
        Assert.Equal(rejection, restored.SourceResults[0].State.FailureReason);
        Assert.Equal(rejection is null ? "first.example" : "second.example",
            LauncherUpdatePolicy.LoadSelected(Installation, Path.Combine(Data, "launcher-update"))!.BaseUrl.Host);
        Assert.Equal(rejection is null ? 1 : 2, returned.RequestedHosts.Count);
        Assert.Empty(restored.List());
    }

    [Fact]
    public async Task CancelingDiscoveryRemovesTheUncommittedPreview()
    {
        MultiplePolicies();
        using var handler = new MultipleFeedHandler(Manifest(3), Manifest(4)) { BlockFirst = true };
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        using var cancel = new CancellationTokenSource();
        var check = service.CheckAsync(cancel.Token);
        await handler.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("checking", service.State.Status);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        Assert.Equal("unconfigured", service.State.Status);
        Assert.False(File.Exists(Path.Combine(Data, "launcher-update", "source.json")));
    }

    private sealed class MultipleFeedHandler(string? first, string second) : HttpMessageHandler
    {
        internal readonly TaskCompletionSource FirstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource SecondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseSecond = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<string> RequestedHosts = [];
        internal bool BlockFirst;
        internal int Requests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            RequestedHosts.Add(request.RequestUri!.Host);
            if (request.RequestUri!.Host == "first.example")
            {
                FirstStarted.TrySetResult();
                if (BlockFirst) await ReleaseFirst.Task.WaitAsync(cancellationToken);
                return new(first is null ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new StringContent(first ?? "missing") };
            }
            SecondStarted.TrySetResult();
            await ReleaseSecond.Task.WaitAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(second) };
        }
    }
}
