using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FantasyTale.Launcher.Core.Hosting;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads.AList;
using FantasyTale.Launcher.Sdk;
using Xunit.Abstractions;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class PublicGitHubStoreTests(ITestOutputHelper output)
{
    private sealed class PublicDownloadFactAttribute : FactAttribute
    {
        public PublicDownloadFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_GITHUB_LIVE_ALIST_BASE")))
                Skip = "Set FT_TEST_GITHUB_LIVE_ALIST_BASE, FT_TEST_GITHUB_LIVE_CATALOG, FT_TEST_GITHUB_LIVE_PACKAGE and FT_TEST_GITHUB_LIVE_REFERENCE_ARCHIVE for public download acceptance.";
        }
    }

    [PublicDownloadFact]
    public async Task AnonymousCatalogDiscoversDownloadsAndRestoresAnIndependentlyVerifiedServerPackage()
    {
        var source = new StoreSource("public-acceptance", "Public acceptance", "alist", Required("ALIST_BASE"), Required("CATALOG"), true, false);
        var root = Directory.CreateTempSubdirectory("ft-public-github-").FullName;
        try
        {
            using var handler = new ObservedTransport(output);
            using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            await using var cache = new StoreCatalogCache(Path.Combine(root, "catalog"), http,
                item => new AListSource(http, new Uri(item.BaseUrl!)));
            var catalog = await cache.GetAsync(source);
            var metadataState = cache.State(source);
            Assert.True(metadataState.Status is "current" or "failed");
            if (metadataState.Status == "failed") Assert.Equal("network", metadataState.Failure);
            var package = Assert.Single(catalog.Packages, item => item.Id == Required("PACKAGE"));
            var pin = Assert.IsType<StoreGitHubAsset>(package.GitHubAsset);
            Assert.True(handler.ApiRequests > 0);
            Assert.Equal(0, handler.ArchiveRequests);

            using var reference = File.OpenRead(Required("REFERENCE_ARCHIVE"));
            Assert.Equal(package.Size, reference.Length);
            Assert.Equal(pin.Sha256, Convert.ToHexStringLower(await SHA256.HashDataAsync(reference)));
            reference.Position = 0;

            var servers = Path.Combine(root, "servers");
            var tasks = Path.Combine(root, "tasks");
            var library = new ServerPackageLibrary(servers);
            var instances = new InstanceStore(Path.Combine(root, "instances"));
            Assert.Empty(library.List());
            ClientDownloadTask task;
            await using (var downloads = new ClientDownloadService(instances, http, tasks, _ => source,
                _ => throw new InvalidOperationException("A GitHub subscription must not fetch its archive from the parent source."), serverPackages: library))
            {
                downloads.StartServer(source, package, GitHubStoreReleases.Manifest(package));
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                while ((task = Assert.Single(downloads.List())).Stage is not ("completed" or "failed"))
                    await Task.Delay(50, deadline.Token);
                Assert.True(task.Stage == "completed", $"Public transfer ended at {task.Stage}: {task.Failure}");
                Assert.All(task.Files, file => Assert.Equal(file.Size, file.Received));
            }
            Assert.True(handler.ArchiveRequests > 0);
            var installed = Assert.Single(library.List());
            Assert.Equal(source.Id, installed.SourceId);
            Assert.Equal(package.ManifestSha256, installed.Package.ManifestSha256);
            Assert.True(File.Exists(Path.Combine(installed.Directory, pin.Executable)));

            using var archive = new ZipArchive(reference, ZipArchiveMode.Read);
            var executableEntry = Assert.Single(archive.Entries, entry =>
                Path.GetFileName(entry.FullName).Equals(pin.Executable, StringComparison.OrdinalIgnoreCase));
            var prefix = executableEntry.FullName[..^pin.Executable.Length];
            var verifiedFiles = 0;
            foreach (var entry in archive.Entries.Where(entry => entry.Length > 0 || !entry.FullName.EndsWith('/')))
            {
                Assert.StartsWith(prefix, entry.FullName, StringComparison.Ordinal);
                var relative = entry.FullName[prefix.Length..];
                await using var expected = entry.Open();
                await using var actual = File.OpenRead(ClientInstallTransaction.Safe(installed.Directory, relative));
                Assert.Equal(entry.Length, actual.Length);
                Assert.Equal(await SHA256.HashDataAsync(expected), await SHA256.HashDataAsync(actual));
                verifiedFiles++;
            }
            Assert.True(verifiedFiles > 0);
            Assert.Empty(instances.List());

            var requests = handler.Requests;
            var restoredLibrary = new ServerPackageLibrary(servers);
            await using var restored = new ClientDownloadService(instances, http, tasks, _ => source,
                _ => throw new InvalidOperationException("A completed task must restore without a source request."), serverPackages: restoredLibrary);
            Assert.Equal("completed", Assert.Single(restored.List()).Stage);
            var restoredPackage = Assert.Single(restoredLibrary.List());
            Assert.Equal(installed.Id, restoredPackage.Id);
            Assert.Equal(installed.Package.ManifestSha256, restoredPackage.Package.ManifestSha256);
            Assert.Equal(installed.Package.GitHubAsset, restoredPackage.Package.GitHubAsset);
            Assert.Equal(requests, handler.Requests);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                checkedAtUtc = DateTimeOffset.UtcNow, package.Id, package.Version, package.ManifestSha256,
                pin.Repository, pin.Tag, pin.Asset, archiveSha256 = pin.Sha256, archiveBytes = package.Size,
                verifiedFiles, handler.ApiRequests, handler.ArchiveRequests, task.Stage,
                metadataRefresh = metadataState.Status, metadataFailure = metadataState.Failure,
                restoredWithoutNetwork = true, serverStarted = false,
            }));
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Required(string suffix) => Environment.GetEnvironmentVariable("FT_TEST_GITHUB_LIVE_" + suffix)
        ?? throw new InvalidOperationException("Missing public acceptance setting: " + suffix);

    private sealed class ObservedTransport(ITestOutputHelper output) : DelegatingHandler(new HttpClientHandler())
    {
        internal int Requests;
        internal int ApiRequests;
        internal int ArchiveRequests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            if (request.RequestUri!.Host == "api.github.com") Interlocked.Increment(ref ApiRequests);
            if (request.RequestUri.Host == "github.com" && request.RequestUri.AbsolutePath.Contains("/releases/download/", StringComparison.Ordinal))
                Interlocked.Increment(ref ArchiveRequests);
            var response = await base.SendAsync(request, cancellationToken);
            if (request.RequestUri.Host == "api.github.com")
            {
                output.WriteLine("GitHub metadata HTTP status: " + (int)response.StatusCode);
                foreach (var name in new[] { "Date", "X-RateLimit-Remaining", "X-RateLimit-Reset", "Retry-After" })
                    if (response.Headers.TryGetValues(name, out var values)) output.WriteLine(name + ": " + string.Join(", ", values));
            }
            return response;
        }
    }
}
