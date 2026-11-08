using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads.AList;
using FantasyTale.Launcher.Sdk;
using Xunit.Abstractions;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class PublicClientArchiveTests(ITestOutputHelper output)
{
    private sealed class PublicClientArchiveFactAttribute : FactAttribute
    {
        public PublicClientArchiveFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_CLIENT_ARCHIVE_CATALOG"))
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_CLIENT_ARCHIVE_URL"))
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_CLIENT_ARCHIVE_BUILD")))
                Skip = "Set FT_TEST_CLIENT_ARCHIVE_CATALOG, FT_TEST_CLIENT_ARCHIVE_URL and FT_TEST_CLIENT_ARCHIVE_BUILD for public archive installation acceptance.";
        }
    }

    private sealed class LocalClientArchiveFactAttribute : FactAttribute
    {
        public LocalClientArchiveFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_CLIENT_ARCHIVE_LOCAL"))
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_CLIENT_ARCHIVE_CATALOG"))
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_CLIENT_ARCHIVE_BUILD")))
                Skip = "Set the local archive, candidate catalog and build for native installation acceptance.";
        }
    }

    [LocalClientArchiveFact]
    public async Task LocalCandidateArchiveExtractsAndInstallsWithRestartableMetadata()
    {
        var catalogPath = Path.GetFullPath(Environment.GetEnvironmentVariable("FT_TEST_CLIENT_ARCHIVE_CATALOG")!);
        var catalog = StoreCatalogFile.Parse(catalogPath, await File.ReadAllTextAsync(catalogPath));
        var buildId = Environment.GetEnvironmentVariable("FT_TEST_CLIENT_ARCHIVE_BUILD")!;
        var package = Assert.Single(catalog.Packages, row => row.Kind == "client" && row.Builds.Contains(buildId));
        var manifest = await File.ReadAllBytesAsync(ClientInstallTransaction.Safe(Path.GetDirectoryName(catalogPath)!, package.Manifest));
        var archive = Assert.Single(ClientRelease.Parse(package, manifest));
        var root = Directory.CreateTempSubdirectory("ft-local-client-").FullName;
        try
        {
            Directory.CreateDirectory(ClientInstallTransaction.Workspace(root));
            File.Copy(Environment.GetEnvironmentVariable("FT_TEST_CLIENT_ARCHIVE_LOCAL")!, ClientInstallTransaction.Staged(root, archive.Sha256));
            var files = await ClientArchiveExtractor.ExtractAsync(root, [archive], null, _ => { }, default);
            output.WriteLine($"Extracted {files.Count} files for {buildId}.");
            await ClientInstallTransaction.ApplyAsync(root, files, _ => { }, default);
            var cache = new ClientFileCache(root);
            cache.RecordInstalled(files);
            var reopened = new ClientFileCache(root);
            Assert.True(await reopened.MatchesAsync(files[0], default));
            Assert.True(await reopened.MatchesAsync(files[^1], default));
        }
        finally { Directory.Delete(root, true); }
    }

    [PublicClientArchiveFact]
    public async Task CandidateArchiveInstallsFromAnonymousStorageAndRetainsItsReceiptAfterRestart()
    {
        var catalogPath = Path.GetFullPath(Environment.GetEnvironmentVariable("FT_TEST_CLIENT_ARCHIVE_CATALOG")!);
        var catalogRoot = Path.GetDirectoryName(catalogPath)!;
        var catalog = StoreCatalogFile.Parse(catalogPath, await File.ReadAllTextAsync(catalogPath));
        var buildId = Environment.GetEnvironmentVariable("FT_TEST_CLIENT_ARCHIVE_BUILD")!;
        var package = Assert.Single(catalog.Packages, row => row.Kind == "client" && row.Builds.Contains(buildId));
        var manifest = await File.ReadAllBytesAsync(ClientInstallTransaction.Safe(catalogRoot, package.Manifest));
        var files = ClientRelease.Parse(package, manifest);
        Assert.All(files, file => Assert.True(file.IsArchive));
        var game = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games"))
            .Single(row => row.Id == package.Game);
        var root = Directory.CreateTempSubdirectory("ft-public-client-").FullName;
        try
        {
            var source = new StoreSource("acceptance", "Archive acceptance", "alist",
                Environment.GetEnvironmentVariable("FT_TEST_CLIENT_ARCHIVE_URL"), "FantasyTale/source.toml", true, false);
            using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
            var instances = new InstanceStore(Path.Combine(root, "instances"));
            await instances.CreateAsync(new("acceptance", "Archive acceptance", package.Game, buildId, null, []) { CheckIntegrity = false });
            var transport = new AListSource(http, new Uri(source.BaseUrl!));
            var tasksRoot = Path.Combine(root, "tasks");
            string taskId;
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            await using (var downloads = new ClientDownloadService(instances, http, tasksRoot, _ => source, _ => transport, game: _ => game))
            {
                taskId = downloads.Start(source, package, manifest, "acceptance").Id;
                string? stage = null;
                while (true)
                {
                    var task = Assert.Single(downloads.List());
                    if (task.Stage != stage)
                    {
                        stage = task.Stage;
                        output.WriteLine("Archive task stage: " + stage);
                    }
                    Assert.True(task.Stage != "failed", "Archive installation failure: " + task.Failure);
                    if (task.Stage == "completed") break;
                    await Task.Delay(100, deadline.Token);
                }
            }
            var installed = instances.ClientFolder("acceptance");
            Assert.NotNull(ClientExecutable.FindInstalled(installed, game.Builds.Single(build => build.Id == buildId)));
            var receipt = ClientInstallTransaction.ReadReceipt(installed);
            Assert.NotNull(receipt);
            Assert.Equal(package.ManifestSha256, receipt.ManifestSha256);
            Assert.Equal(StoreSourceIdentity.Of(source), receipt.SourceIdentity);
            Assert.False(File.Exists(Path.Combine(installed, "session.toml")));
            Assert.False(File.Exists(Path.Combine(ClientInstallTransaction.Workspace(installed), "journal.json")));
            await using var restarted = new ClientDownloadService(instances, http, tasksRoot, _ => source, _ => transport, game: _ => game);
            var restored = Assert.Single(restarted.List());
            Assert.Equal(taskId, restored.Id);
            Assert.Equal("completed", restored.Stage);
            output.WriteLine($"Installed {buildId}: {files.Sum(file => file.Size)} archive bytes; receipt and restart accepted without executing the game.");
        }
        finally { Directory.Delete(root, true); }
    }
}
