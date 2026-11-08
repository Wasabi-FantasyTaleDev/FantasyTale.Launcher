using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using FantasyTale.Launcher.Sdk;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Downloads;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class GitHubMirrorTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private static GitHubMirrors Create(HttpMessageHandler? handler = null, Func<CancellationToken, Task<IReadOnlyList<GitHubMirror>>>? refresh = null) => new(
        [new("slow", "Slow", new("https://slow.invalid/")), new("fast", "Fast", new("https://fast.invalid/")), new("broken", "Broken", new("https://broken.invalid/"))],
        new("https://github.com/team/repo/releases/download/v1/test.zip"), [0x50, 0x4b], 4, TimeSpan.FromSeconds(2), 3, handler, refresh);

    [Fact]
    public async Task ParallelMeasurementsRankValidDownloadsAndRetainFailedNodes()
    {
        var active = 0; var concurrent = 0;
        using var mirrors = Create(new Handler(async (request, ct) =>
        {
            var count = Interlocked.Increment(ref active);
            if (count > 1) Interlocked.Exchange(ref concurrent, 1);
            Assert.Equal("bytes=0-3", request.Headers.Range!.ToString());
            try
            {
                await Task.Delay(request.RequestUri!.Host == "slow.invalid" ? 100 : 10, ct);
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri.Host == "broken.invalid" ? [1, 2, 3, 4] : [0x50, 0x4b, 3, 4]) };
            }
            finally { Interlocked.Decrement(ref active); }
        }));
        var routes = await mirrors.RoutesAsync(new("https://github.com/team/repo/releases/download/v1/test.zip"), "auto", CancellationToken.None);
        Assert.Equal(1, concurrent);
        Assert.Equal(["fast.invalid", "slow.invalid"], routes.Select(uri => uri.Host));
        Assert.Equal("failed", mirrors.Results.Single(result => result.Id == "broken").State);
        Assert.Equal(3, mirrors.Providers.Count);
    }

    [Fact]
    public async Task MirrorFailurePreservesRangesAndFallsThroughToAnotherMirrorThenDirect()
    {
        using var mirrors = Create();
        var visited = new List<string>();
        using var http = new HttpClient(new GitHubRoutingHandler(() => true, () => "slow", mirrors, new Handler((request, _) =>
        {
            visited.Add(request.RequestUri!.Host);
            Assert.Equal("bytes=4-9", request.Headers.Range!.ToString());
            Assert.Equal("\"etag\"", request.Headers.IfRange!.ToString());
            return Task.FromResult(new HttpResponseMessage(request.RequestUri.Host == "github.com" ? HttpStatusCode.PartialContent : HttpStatusCode.BadGateway) { Content = new ByteArrayContent([1, 2]) });
        })));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://github.com/team/repo/releases/download/v1/test.zip");
        request.Headers.Range = new(4, 9); request.Headers.IfRange = new(new EntityTagHeaderValue("\"etag\""));
        using var response = await http.SendAsync(request, CancellationToken.None);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(["slow.invalid", "fast.invalid", "broken.invalid", "github.com"], visited);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task CredentialsAndDisabledRoutingStayOnTheOriginalOrigin(bool enabled, bool credentials)
    {
        using var mirrors = Create();
        using var http = new HttpClient(new GitHubRoutingHandler(() => enabled, () => "auto", mirrors, new Handler((request, _) =>
        {
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        })));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/team/repo");
        if (credentials) request.Headers.Authorization = new("Bearer", "fixture");
        using var response = await http.SendAsync(request, CancellationToken.None);
        Assert.All(mirrors.Results, row => Assert.Equal("pending", row.State));
    }

    [Fact]
    public async Task DirectoryRefreshKeepsUnavailableNodesAndAcceptsNewNodes()
    {
        using var mirrors = Create(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))),
            _ => Task.FromResult<IReadOnlyList<GitHubMirror>>([new("new", "New", new("https://new.invalid/")), new("offline", "Offline", new("https://offline.invalid/"))]));
        await mirrors.MeasureAsync(CancellationToken.None);
        Assert.Equal(["new", "offline"], mirrors.Providers.Select(provider => provider.Id));
        Assert.All(mirrors.Results, result => Assert.Equal("failed", result.State));
    }

    [Fact]
    public async Task SavedNodeSurvivesDirectoryRemovalAndRemainsSelectable()
    {
        using var mirrors = Create(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))),
            _ => Task.FromResult<IReadOnlyList<GitHubMirror>>([new("new", "New", new("https://new.invalid/"))]));
        mirrors.RestoreSelection("saved.invalid");
        await mirrors.MeasureAsync(CancellationToken.None);
        Assert.Equal("saved.invalid", mirrors.ValidateSelection("saved.invalid"));
        var routes = await mirrors.RoutesAsync(new("https://github.com/team/repo/releases/download/v1/test.zip"), "saved.invalid", CancellationToken.None);
        Assert.Equal("saved.invalid", routes[0].Host);
        Assert.Equal(["new", "saved.invalid"], mirrors.Providers.Select(provider => provider.Id));
    }

    [Theory]
    [InlineData("disconnect")]
    [InlineData("short")]
    [InlineData("hash")]
    [InlineData("range")]
    public async Task ResponseBodyFailuresResumeOrRestartOnTheNextMirror(string failure)
    {
        var root = Directory.CreateTempSubdirectory("ft-mirror-body-").FullName;
        try
        {
            byte[] data = [1, 2, 3, 4, 5, 6, 7];
            var digest = Convert.ToHexString(SHA256.HashData(data));
            var destination = Path.Combine(root, "download.zip");
            await File.WriteAllTextAsync(destination, "existing");
            if (failure == "range") await File.WriteAllBytesAsync(destination + "." + digest.ToLowerInvariant() + ".part", data[..3]);
            using var mirrors = Create();
            var visited = new List<string>();
            using var http = new HttpClient(new GitHubRoutingHandler(() => true, () => "slow", mirrors, new Handler((request, _) =>
            {
                visited.Add(request.RequestUri!.Host);
                var offset = checked((int)(request.Headers.Range?.Ranges.Single().From ?? 0));
                HttpContent content;
                if (request.RequestUri.Host == "slow.invalid")
                {
                    content = failure switch
                    {
                        "disconnect" => new StreamContent(new InterruptedStream(data[..3])),
                        "short" => new ByteArrayContent(data[..3]),
                        "hash" => new ByteArrayContent(new byte[data.Length]),
                        _ => new ByteArrayContent(data[3..])
                    };
                    content.Headers.ContentLength = data.Length - offset;
                    if (failure == "range") content.Headers.ContentRange = new(2, data.Length - 1, data.Length);
                }
                else
                {
                    Assert.Equal(failure is "disconnect" or "short" ? 3 : 0, offset);
                    content = new ByteArrayContent(data[offset..]);
                    if (offset > 0) content.Headers.ContentRange = new(offset, data.Length - 1, data.Length);
                }
                return Task.FromResult(new HttpResponseMessage(offset > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = content });
            })));
            var source = new ResolvedDownload(new("https://github.com/team/repo/releases/download/v1/test.zip"), new Dictionary<string, string>(), data.Length);
            await new HttpDownloadEngine(http).DownloadAsync(new(source, destination, data.Length, digest), null, CancellationToken.None);
            Assert.Equal(["slow.invalid", "fast.invalid"], visited);
            Assert.Equal(data, await File.ReadAllBytesAsync(destination));
            Assert.Empty(Directory.GetFiles(root, "*.part"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExhaustedCorruptMirrorsNeverReplaceTheDestination()
    {
        var root = Directory.CreateTempSubdirectory("ft-mirror-exhaust-").FullName;
        try
        {
            var destination = Path.Combine(root, "download.zip");
            await File.WriteAllTextAsync(destination, "existing");
            using var mirrors = Create();
            var visited = new List<string>();
            using var http = new HttpClient(new GitHubRoutingHandler(() => true, () => "slow", mirrors, new Handler((request, _) =>
            {
                visited.Add(request.RequestUri!.Host);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0, 0, 0]) });
            })));
            var source = new ResolvedDownload(new("https://github.com/team/repo/releases/download/v1/test.zip"), new Dictionary<string, string>(), 3);
            await Assert.ThrowsAsync<InvalidDataException>(() => new HttpDownloadEngine(http).DownloadAsync(
                new(source, destination, 3, Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 }))), null, CancellationToken.None));
            Assert.Equal(["slow.invalid", "fast.invalid", "broken.invalid", "github.com"], visited);
            Assert.Equal("existing", await File.ReadAllTextAsync(destination));
            Assert.Empty(Directory.GetFiles(root, "*.part"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UserCancellationNeverStartsAnotherMirror()
    {
        var root = Directory.CreateTempSubdirectory("ft-mirror-cancel-").FullName;
        try
        {
            using var stop = new CancellationTokenSource();
            using var mirrors = Create();
            var visits = 0;
            using var http = new HttpClient(new GitHubRoutingHandler(() => true, () => "slow", mirrors, new Handler((_, ct) =>
            {
                visits++; stop.Cancel(); ct.ThrowIfCancellationRequested();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            })));
            var source = new ResolvedDownload(new("https://github.com/team/repo/releases/download/v1/test.zip"), new Dictionary<string, string>(), 3);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new HttpDownloadEngine(http).DownloadAsync(
                new(source, Path.Combine(root, "download.zip"), 3, new string('0', 64)), null, stop.Token));
            Assert.Equal(1, visits);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class InterruptedStream(byte[] prefix) : MemoryStream(prefix)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Position < Length ? base.ReadAsync(buffer, cancellationToken) : ValueTask.FromException<int>(new IOException("Disconnected fixture."));
    }

    [Theory]
    [InlineData("CN", true)]
    [InlineData("US", false)]
    [InlineData("HK", false)]
    [InlineData("TW", false)]
    public void CountryDetectionSuggestsWithoutEnablingAndNeverOverridesAnExplicitChoice(string country, bool suggest)
    {
        var root = Directory.CreateTempSubdirectory("ft-mirror-default-").FullName;
        try
        {
            var settings = new NetworkSettings(root);
            Assert.False(settings.MirrorDetectionComplete);
            settings.ObserveMirrorCountry(country);
            Assert.False(settings.GitHubMirror);
            Assert.Equal(suggest, settings.MirrorSuggestionPending);
            Assert.True(settings.MirrorDetectionComplete);
            settings.Save("all", true, "auto");
            settings.ObserveMirrorCountry(country);
            Assert.True(new NetworkSettings(root).GitHubMirror);
            Assert.True(new NetworkSettings(root).MirrorDetectionComplete);
            Assert.False(settings.MirrorSuggestionPending);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ManualChoiceDuringCountryLookupWins()
    {
        var root = Directory.CreateTempSubdirectory("ft-mirror-race-").FullName;
        try
        {
            var settings = new NetworkSettings(root);
            settings.Save("china", false);
            settings.ObserveMirrorCountry("CN");
            Assert.False(settings.GitHubMirror);
            Assert.False(settings.MirrorSuggestionPending);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DecliningTheSuggestionSurvivesRestartAndUnrelatedSettingsDoNotConsumeIt()
    {
        var root = Directory.CreateTempSubdirectory("ft-mirror-decline-").FullName;
        try
        {
            var settings = new NetworkSettings(root);
            settings.ObserveMirrorCountry("CN");
            settings.Save("all");
            Assert.True(settings.MirrorSuggestionPending);
            Assert.True(settings.MirrorDetectionComplete);
            settings.Save("all", false);
            var restarted = new NetworkSettings(root);
            restarted.ObserveMirrorCountry("CN");
            Assert.False(restarted.GitHubMirror);
            Assert.False(restarted.MirrorSuggestionPending);
            Assert.True(restarted.MirrorDetectionComplete);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExistingMirrorPreferencesWithoutDetectionMarkersStillReceiveOneDetection(bool enabled)
    {
        var root = Directory.CreateTempSubdirectory("ft-mirror-existing-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "network.toml"), $"ip_preference = \"china\"\ngithub_mirror = {(enabled ? "true" : "false")}\n");
            var settings = new NetworkSettings(root);
            Assert.False(settings.MirrorDetectionComplete);
            settings.ObserveMirrorCountry("CN");
            Assert.True(settings.MirrorSuggestionPending);
            Assert.Equal(enabled, settings.GitHubMirror);
            var beforeDisplay = new NetworkSettings(root);
            Assert.True(beforeDisplay.MirrorDetectionComplete);
            Assert.True(beforeDisplay.MirrorSuggestionPending);
            beforeDisplay.MarkMirrorSuggestionShown();
            var afterDisplay = new NetworkSettings(root);
            afterDisplay.ObserveMirrorCountry("CN");
            Assert.False(afterDisplay.MirrorSuggestionPending);
            Assert.Equal(enabled, afterDisplay.GitHubMirror);
            Assert.Contains("github_mirror_prompted = true", File.ReadAllText(Path.Combine(root, "network.toml")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void FailedDetectionIsRecordedOnceWithoutChangingThePreference()
    {
        var root = Directory.CreateTempSubdirectory("ft-mirror-detection-failure-").FullName;
        try
        {
            var settings = new NetworkSettings(root);
            settings.ObserveMirrorCountry(null);
            var restarted = new NetworkSettings(root);
            Assert.True(restarted.MirrorDetectionComplete);
            restarted.ObserveMirrorCountry("CN");
            Assert.False(restarted.MirrorSuggestionPending);
            Assert.False(restarted.GitHubMirror);
        }
        finally { Directory.Delete(root, true); }
    }
}
