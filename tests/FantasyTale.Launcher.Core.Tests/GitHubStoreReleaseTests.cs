using System.Net;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hosting;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class GitHubStoreReleaseTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-github-store-").FullName;
    private static readonly StoreSource Source = new("fixture", "Archive", "alist", "https://archive.invalid/", "source.toml", true, false);
    private const string Catalog = """
        schema = 2
        [catalog]
        id = "fixture"
        name = { en = "Archive" }
        [[github_releases]]
        id = "fixture.server"
        game = "fixture"
        builds = ["test-1"]
        repository = "fixture/server"
        asset = "server-win-x64.zip"
        executable = "server.exe"
        name = { en = "Server" }
        publisher = "Fixture"
        """;

    [Fact]
    public async Task ParentCatalogFollowsStableReleaseWithoutFetchingPayloadAndPersistsOfflineSnapshot()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        await using (var cache = new StoreCatalogCache(_root, http, _ => new HttpSource(new Uri("https://archive.invalid/"))))
        {
            var first = await cache.GetAsync(Source);
            var package = Assert.Single(first.Packages);
            Assert.Equal("1.0.0", package.Version); Assert.Equal(4, package.Size);
            Assert.Equal("v1.0.0", package.GitHubAsset!.Tag);
            Assert.Single(first.GitHubReleases);
            Assert.Equal(0, handler.Payloads);
            Assert.Equal(1, handler.ApiCalls);
            Assert.Same(first, await cache.GetAsync(Source));
            var manifest = GitHubStoreReleases.Manifest(package);
            var file = Assert.Single(ClientRelease.Parse(package, manifest));
            Assert.True(file.IsArchive); Assert.Equal(handler.Digest, file.Sha256);
            var resolved = await GitHubStoreReleases.Open(package.GitHubAsset).ResolveAsync(file.SourcePath, default);
            Assert.Equal("https://github.com/fixture/server/releases/download/v1.0.0/server-win-x64.zip", resolved.Url.AbsoluteUri);
            handler.Version = "1.1.0";
            var updated = await cache.GetAsync(Source, true);
            Assert.Equal("1.1.0", Assert.Single(updated.Packages).Version);
            Assert.Equal(0, handler.Payloads);
            handler.Failure = "offline";
            var offline = await cache.GetAsync(Source, true);
            Assert.Equal("1.1.0", Assert.Single(offline.Packages).Version);
            Assert.Equal("network", cache.State(Source).Failure);
        }
        await using var restored = new StoreCatalogCache(_root, http, _ => throw new InvalidOperationException("Offline"));
        Assert.Equal("1.1.0", Assert.Single(restored.Cached(Source)!.Packages).Version);
        Assert.Equal("v1.1.0", Assert.Single(restored.Cached(Source)!.Packages).GitHubAsset!.Tag);
    }

    [Theory]
    [InlineData("digest")]
    [InlineData("asset")]
    [InlineData("url")]
    [InlineData("draft")]
    [InlineData("prerelease")]
    [InlineData("version")]
    public async Task InvalidReleaseRetainsLastKnownGoodMetadata(string failure)
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        await using var cache = new StoreCatalogCache(_root, http, _ => new HttpSource(new Uri("https://archive.invalid/")));
        var valid = await cache.GetAsync(Source);
        handler.Failure = failure;
        var error = await Record.ExceptionAsync(() => cache.GetAsync(Source, true));
        Assert.True(error is InvalidDataException or DefinitionException, error?.ToString());
        Assert.Same(valid, cache.Cached(Source));
        Assert.Equal("failed", cache.State(Source).Status);
        Assert.Equal(0, handler.Payloads);
    }

    [Theory]
    [InlineData("repository = \"fixture/server/path\"")]
    [InlineData("repository = \"https://github.com/fixture/server\"")]
    [InlineData("asset = \"../server.zip\"")]
    [InlineData("asset = \"server.exe\"")]
    [InlineData("builds = []")]
    public void InvalidSubscriptionsFailBeforeNetwork(string replacement)
    {
        var key = replacement[..replacement.IndexOf('=')].Trim();
        var lines = Catalog.Split('\n').Select(line => line.StartsWith(key + " =", StringComparison.Ordinal) ? replacement : line);
        Assert.Throws<DefinitionException>(() => StoreCatalogFile.Parse("fixture", string.Join('\n', lines)));
    }

    [Fact]
    public async Task ColdGitHubFailureKeepsParentCatalogAndReportsTheFailedSubscription()
    {
        using var handler = new Handler { Failure = "offline" }; using var http = new HttpClient(handler);
        await using var cache = new StoreCatalogCache(_root, http, _ => new HttpSource(new Uri("https://archive.invalid/")));
        var catalog = await cache.GetAsync(Source);
        Assert.Empty(catalog.Packages);
        Assert.Single(catalog.GitHubReleases);
        Assert.Equal("network", cache.State(Source).Failure);
        handler.Failure = "";
        Assert.Single((await cache.GetAsync(Source, true)).Packages);
    }

    [Theory]
    [InlineData("quota")]
    [InlineData("retry")]
    public async Task RateLimitedGitHubRefreshPreservesPinnedPackagesAndSharesTheServerDeadlineAcrossSources(string failure)
    {
        using var handler = new Handler { CatalogText = Catalog + """

            [[packages]]
            id = "fixture.plugin"
            kind = "plugin"
            game = "fixture"
            builds = ["test-1"]
            version = "1.0.0"
            name = { en = "Plugin" }
            publisher = "Fixture"
            size = 4
            manifest = "plugin.toml"
            manifest_sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            """ }; using var http = new HttpClient(handler);
        await using var cache = new StoreCatalogCache(_root, http, _ => new HttpSource(new Uri("https://archive.invalid/")));
        var original = Assert.Single((await cache.GetAsync(Source)).Packages, item => item.Kind == "server");
        handler.Failure = failure;
        var retained = Assert.Single((await cache.GetAsync(Source, true)).Packages, item => item.Kind == "server");
        Assert.Equal(original.GitHubAsset, retained.GitHubAsset);
        Assert.Equal(original.ManifestSha256, retained.ManifestSha256);
        Assert.Equal("network", cache.State(Source).Failure);
        Assert.Equal(2, handler.ApiCalls);
        handler.Failure = ""; handler.Version = "2.0.0";
        handler.CatalogText = handler.CatalogText.Replace("version = \"1.0.0\"", "version = \"1.1.0\"", StringComparison.Ordinal);
        var refreshed = (await cache.GetAsync(Source, true)).Packages;
        Assert.Equal("1.0.0", Assert.Single(refreshed, item => item.Kind == "server").Version);
        Assert.Equal("1.1.0", Assert.Single(refreshed, item => item.Kind == "plugin").Version);
        var other = Source with { Id = "other", BaseUrl = "https://other.invalid/" };
        Assert.Single((await cache.GetAsync(other, true)).GitHubReleases);
        Assert.Equal("plugin", Assert.Single(cache.Cached(other)!.Packages).Kind);
        Assert.Equal("network", cache.State(other).Failure);
        Assert.Equal(2, handler.ApiCalls);
        Assert.Equal(0, handler.Payloads);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Retry-After", "120")]
    [InlineData(HttpStatusCode.TooManyRequests, "Retry-After", "Mon, 26 Oct 2026 00:02:00 GMT")]
    [InlineData(HttpStatusCode.Forbidden, "X-RateLimit-Reset", "1792972920")]
    public void ServerSpecifiedGitHubCooldownExpiresWithoutExtendingOnShorterResponses(HttpStatusCode status, string header, string value)
    {
        var clock = new GitHubClock();
        var limit = new GitHubRateLimit(clock);
        using var response = new HttpResponseMessage(status);
        response.Headers.Add(header, value);
        response.Headers.Add("X-RateLimit-Remaining", "0");
        limit.Observe(response);
        Assert.True(limit.Active);
        clock.Now = clock.Now.AddSeconds(30);
        using var shorter = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        shorter.Headers.RetryAfter = new(TimeSpan.FromSeconds(5));
        limit.Observe(shorter);
        clock.Now = clock.Now.AddSeconds(89);
        Assert.True(limit.Active);
        clock.Now = clock.Now.AddSeconds(1);
        Assert.False(limit.Active);
    }

    private sealed class GitHubClock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("past-quota")]
    [InlineData("bad-quota")]
    public async Task OrdinaryForbiddenOrInvalidResetDoesNotSuppressTheNextGitHubRefresh(string failure)
    {
        using var handler = new Handler { Failure = failure }; using var http = new HttpClient(handler);
        await using var cache = new StoreCatalogCache(_root, http, _ => new HttpSource(new Uri("https://archive.invalid/")));
        Assert.Empty((await cache.GetAsync(Source)).Packages);
        handler.Failure = "";
        Assert.Single((await cache.GetAsync(Source, true)).Packages);
        Assert.Equal(2, handler.ApiCalls);
    }

    [Fact]
    public async Task DownloadRemainsPinnedAfterNewReleaseAndRestoresWithParentStoreIdentity()
    {
        using var handler = new Handler(); using var http = new HttpClient(handler);
        await using var cache = new StoreCatalogCache(Path.Combine(_root, "catalog"), http, _ => new HttpSource(new Uri("https://archive.invalid/")));
        using (var buffer = new MemoryStream())
        {
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var output = archive.CreateEntry("server.exe").Open(); output.Write([1, 2, 3, 4]);
            }
            handler.Payload = buffer.ToArray();
        }
        var package = Assert.Single((await cache.GetAsync(Source)).Packages, item => item.Kind == "server");
        var manifest = GitHubStoreReleases.Manifest(package);
        Assert.Throws<InvalidDataException>(() => ClientRelease.Parse(package with { GitHubAsset = package.GitHubAsset! with { Tag = "v9.0.0" } }, manifest));
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        var library = new ServerPackageLibrary(Path.Combine(_root, "servers"));
        var tasks = Path.Combine(_root, "tasks");
        handler.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        string taskId;
        await using (var downloads = new ClientDownloadService(store, http, tasks, _ => Source,
            _ => throw new InvalidOperationException("The parent AList must not serve GitHub payloads."), serverPackages: library))
        {
            taskId = downloads.StartServer(Source, package, manifest).Id;
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await downloads.ActionAsync(taskId, "pause");
            Assert.Equal("paused", Assert.Single(downloads.List()).Stage);
        }
        handler.Version = "2.0.0";
        await cache.GetAsync(Source, true);
        handler.Gate = null;
        await using var reopened = new ClientDownloadService(store, http, tasks, _ => Source,
            _ => throw new InvalidOperationException("Unexpected AList payload request."), serverPackages: library);
        await reopened.ActionAsync(taskId, "resume");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        ClientDownloadTask task;
        while ((task = Assert.Single(reopened.List())).Stage is not ("completed" or "failed")) await Task.Delay(20, deadline.Token);
        Assert.Equal("completed", task.Stage);
        Assert.All(handler.PayloadTags, tag => Assert.Equal("v1.0.0", tag));
        Assert.Equal("Archive", task.Target.SourceName);
        var installed = Assert.Single(library.List());
        Assert.Equal(Source.Id, installed.SourceId);
        Assert.Equal("1.0.0", installed.Package.Version);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(installed.Directory, "server.exe")));
        Assert.Empty(store.List());
    }

    private sealed class PublicArchiveFactAttribute : FactAttribute
    {
        public PublicArchiveFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_GITHUB_SERVER_ARCHIVE")))
                Skip = "Set FT_TEST_GITHUB_SERVER_ARCHIVE, FT_TEST_GITHUB_SERVER_REPOSITORY and FT_TEST_GITHUB_SERVER_ASSET for public release acceptance.";
        }
    }

    [PublicArchiveFact]
    public async Task PublicReleaseMetadataInstallsPreviouslyDownloadedRealArchive()
    {
        var repository = Environment.GetEnvironmentVariable("FT_TEST_GITHUB_SERVER_REPOSITORY")!;
        var asset = Environment.GetEnvironmentVariable("FT_TEST_GITHUB_SERVER_ASSET")!;
        var executable = Environment.GetEnvironmentVariable("FT_TEST_GITHUB_SERVER_EXECUTABLE")!;
        var catalog = Catalog.Replace("fixture/server", repository, StringComparison.Ordinal)
            .Replace("server-win-x64.zip", asset, StringComparison.Ordinal)
            .Replace("server.exe", executable, StringComparison.Ordinal);
        if (Environment.GetEnvironmentVariable("FT_TEST_GITHUB_SERVER_CATALOG") is { } snapshot)
            catalog = await File.ReadAllTextAsync(snapshot);
        using var handler = new PublicParentHandler(catalog);
        using var http = new HttpClient(handler);
        await using var cache = new StoreCatalogCache(Path.Combine(_root, "catalog"), http, _ => new HttpSource(new Uri("https://archive.invalid/")));
        var package = Assert.Single((await cache.GetAsync(Source)).Packages, item => item.Kind == "server");
        var file = Assert.Single(ClientRelease.Parse(package, GitHubStoreReleases.Manifest(package)));
        var library = new ServerPackageLibrary(Path.Combine(_root, "servers"));
        var destination = library.Folder(Source.Id, StoreSourceIdentity.Of(Source), package);
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(destination));
        File.Copy(Environment.GetEnvironmentVariable("FT_TEST_GITHUB_SERVER_ARCHIVE")!, ClientInstallTransaction.Staged(destination, file.Sha256));
        var instances = new InstanceStore(Path.Combine(_root, "instances"));
        await using var downloads = new ClientDownloadService(instances, http, Path.Combine(_root, "tasks"), _ => Source,
            _ => throw new InvalidOperationException("The parent catalog cannot serve GitHub archives."), serverPackages: library);
        downloads.StartServer(Source, package, GitHubStoreReleases.Manifest(package));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        ClientDownloadTask task;
        while ((task = Assert.Single(downloads.List())).Stage is not ("completed" or "failed")) await Task.Delay(50, deadline.Token);
        Assert.Equal("completed", task.Stage);
        var installed = Assert.Single(library.List());
        Assert.Equal(package.ManifestSha256, installed.Package.ManifestSha256);
        Assert.True(File.Exists(Path.Combine(installed.Directory, "BubbleFighter.Fantasy.Host.exe")));
        Assert.True(File.Exists(Path.Combine(installed.Directory, "profiles", "cn-406", "profile.yaml")));
        Assert.True(File.Exists(Path.Combine(installed.Directory, "packets", "builds.yaml")));
        Assert.False(Directory.Exists(Path.Combine(installed.Directory, "bubblefighter-fantasy")));
        Assert.Empty(instances.List());
    }

    private sealed class PublicParentHandler(string catalog) : DelegatingHandler(new HttpClientHandler())
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "archive.invalid")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(catalog) });
            Assert.Equal("api.github.com", request.RequestUri.Host);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        internal string Version = "1.0.0", Failure = "";
        internal string CatalogText = Catalog;
        internal int ApiCalls, Payloads;
        internal List<string> PayloadTags { get; } = [];
        internal byte[] Payload = [1, 2, 3, 4];
        internal string Digest => Convert.ToHexStringLower(SHA256.HashData(Payload));
        internal TaskCompletionSource? Gate;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.Host == "archive.invalid") return new(HttpStatusCode.OK) { Content = new StringContent(CatalogText) };
            if (request.RequestUri.Host == "api.github.com")
            {
                ApiCalls++;
                Assert.NotEmpty(request.Headers.UserAgent);
                if (Failure == "offline") return new(HttpStatusCode.NotFound);
                if (Failure is "quota" or "retry" or "denied" or "past-quota" or "bad-quota")
                {
                    var response = new HttpResponseMessage(Failure == "retry" ? HttpStatusCode.TooManyRequests : HttpStatusCode.Forbidden);
                    if (Failure == "retry") response.Headers.RetryAfter = new(TimeSpan.FromMinutes(1));
                    else if (Failure != "denied")
                    {
                        response.Headers.Add("X-RateLimit-Remaining", "0");
                        response.Headers.Add("X-RateLimit-Reset", Failure == "bad-quota" ? "invalid" :
                            DateTimeOffset.UtcNow.AddMinutes(Failure == "past-quota" ? -1 : 1).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    return response;
                }
                var tag = "v" + Version;
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                {
                    tag_name = Failure == "version" ? "not-a-version" : tag,
                    draft = Failure == "draft", prerelease = Failure == "prerelease", published_at = "2026-10-24T00:00:00Z",
                    assets = new[] { new { name = Failure == "asset" ? "other.zip" : "server-win-x64.zip", size = Payload.Length,
                        digest = Failure == "digest" ? null : "sha256:" + Digest,
                        browser_download_url = Failure == "url" ? "https://unrelated.invalid/server.zip" : $"https://github.com/fixture/server/releases/download/{tag}/server-win-x64.zip" } },
                })) };
            }
            Assert.Equal("github.com", request.RequestUri.Host);
            Payloads++; PayloadTags.Add(request.RequestUri.AbsolutePath.Split('/')[5]); Started.TrySetResult();
            if (Gate is { } gate) await gate.Task.WaitAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
