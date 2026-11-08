using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    [Fact]
    public async Task UninstallCancelsCompletedButUnappliedUpgradeIntents()
    {
        var game = Game(); var build = game.Builds[0];
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        library.Install(library.Prepare(Folder(), game).Token, game);
        var folder = Folder(Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var release = StoreRelease(folder);
        var manifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(release.Manifest).Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var package = release.Package with { Version = "2.0.0", ManifestSha256 = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant() };
        var bytes = File.ReadAllBytes(Path.Combine(folder, "plugin.toml"));
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example", "source.toml", true, false);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null,
            [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())]));
        using var http = new HttpClient(new RecommendationHandler(bytes));
        await using var downloads = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"), _ => source,
            _ => new RecommendationSource(bytes.Length), library, _ => game);
        var root = Path.Combine(_root, "activations");
        var upgrades = new PluginUpgradeActivationService(root, store, library, downloads, _ => game, _ => false, _ => source);
        var task = upgrades.Enqueue(source, package, manifest, "first");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (downloads.List().Single(item => item.Id == task.Id).Stage is not ("completed" or "failed")) await Task.Delay(20, deadline.Token);
        Assert.Equal("completed", downloads.List().Single(item => item.Id == task.Id).Stage);
        Assert.Equal(1, upgrades.PendingCount);
        upgrades.Uninstall(game, "fixture.optional");
        upgrades.ApplyReady();
        Assert.Null(upgrades.Failure); Assert.Equal(0, upgrades.PendingCount);
        Assert.Empty(library.Packages(game)); Assert.Empty(store.Load("first").Plugins);
        Assert.Equal(0, new PluginUpgradeActivationService(root, store, library, downloads, _ => game, _ => false, _ => source).PendingCount);
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("running")]
    [InlineData("leased")]
    public async Task WholePluginUninstallChecksEveryClientAndRemovesAllVersionsOnly(string state)
    {
        var game = Game(); using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var old = library.Install(library.Prepare(Folder(), game).Token, game);
        var next = library.Install(library.Prepare(Folder(Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal)), game).Token, game);
        var other = library.Install(library.Prepare(Folder(Manifest.Replace("fixture.optional", "fixture.other", StringComparison.Ordinal)), game).Token, game);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        foreach (var id in new[] { "first", "second" })
            await store.CreateAsync(new(id, id, game.Id, game.Builds[0].Id, null,
                [new("fixture.optional", id == "first" ? "1.0.0" : "2.0.0", true, false, new Dictionary<string, object>())]));
        var personal = Path.Combine(_root, "keep.txt"); File.WriteAllText(personal, "keep");
        using var held = state == "leased" ? store.Acquire("second") : null;
        if (state == "running")
        {
            using (store.Acquire("second"))
            {
                library.Uninstall(game, old.Id, store, id => id == "second");
                Assert.Empty(store.Load("first").Plugins); Assert.Empty(store.Load("second").Plugins);
                Assert.Equal(other.Id, Assert.Single(library.Packages(game)).Id);
                Assert.True(Directory.Exists(old.Folder)); Assert.True(Directory.Exists(next.Folder));
            }
            library.CleanupUninstalled(store, _ => false);
            Assert.False(Directory.Exists(old.Folder)); Assert.False(Directory.Exists(next.Folder));
        }
        else if (state == "leased")
        {
            Assert.Throws<InstanceBusyException>(() => library.Uninstall(game, old.Id, store, _ => false));
            Assert.True(Directory.Exists(old.Folder)); Assert.True(Directory.Exists(next.Folder));
            Assert.Single(store.Load("first").Plugins); Assert.Single(store.Load("second").Plugins);
        }
        else
        {
            library.Uninstall(game, old.Id, store, _ => false);
            Assert.False(Directory.Exists(old.Folder)); Assert.False(Directory.Exists(next.Folder));
            Assert.Empty(store.Load("first").Plugins); Assert.Empty(store.Load("second").Plugins);
            Assert.Equal(other.Id, Assert.Single(library.Packages(game)).Id);
        }
        Assert.True(Directory.Exists(other.Folder)); Assert.Equal("keep", File.ReadAllText(personal));
    }

    [Theory]
    [InlineData("obsolete")]
    [InlineData("malformed")]
    [InlineData("missing")]
    [InlineData("receipt")]
    public void InvalidInstalledPackagesDoNotHideHealthySiblings(string failure)
    {
        var game = Game(); using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var preview = library.Prepare(Folder(), game);
        var healthy = library.Install(preview.Token, game);
        var rejected = Directory.CreateDirectory(Path.Combine(_root, "library", "packages", "rejected")).FullName;
        var manifest = Path.Combine(rejected, "plugin.toml");
        var original = failure == "malformed" ? "[plugin\n" : Manifest.Replace("fixture.optional", "fixture.rejected", StringComparison.Ordinal);
        if (failure == "obsolete") original = original.Replace("[plugin]", "[plugin]\nrequire_latest = true", StringComparison.Ordinal);
        if (failure != "missing") File.WriteAllText(manifest, original);
        if (failure == "receipt") File.WriteAllText(Path.Combine(rejected, ".fantasytale-source.json"), "{}");
        Assert.Equal(healthy.Id, Assert.Single(library.Packages(game)).Id);
        var issue = Assert.Single(library.LoadIssues());
        Assert.Equal(rejected, issue.Folder); Assert.NotEmpty(issue.Message);
        Assert.Null(issue.SourceId);
        if (failure != "missing") Assert.Equal(original, File.ReadAllText(manifest));
        if (failure == "obsolete")
        {
            Assert.Equal("fixture.rejected", issue.Id);
            Assert.Throws<DefinitionException>(() => PluginCatalog.LoadFile(manifest));
            Assert.Throws<DefinitionException>(() => library.Prepare(Folder(original), game));
        }
        else if (failure is "malformed" or "missing") Assert.Null(issue.Id);
    }

    [Fact]
    public async Task UninstallRejectedDetachesOnlyThatVersionAndPreservesHealthyPackagesAndClientFiles()
    {
        var game = Game(); using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var healthy = library.Install(library.Prepare(Folder(Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal)), game).Token, game);
        var rejected = Directory.CreateDirectory(Path.Combine(_root, "library", "packages", "obsolete")).FullName;
        File.WriteAllText(Path.Combine(rejected, "plugin.toml"), Manifest.Replace("[plugin]", "[plugin]\nrequire_latest = true", StringComparison.Ordinal));
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, game.Builds[0].Id, null,
            [new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>())]));
        await store.CreateAsync(new("second", "Second", game.Id, game.Builds[0].Id, null,
            [new("fixture.optional", "2.0.0", false, false, new Dictionary<string, object>())]));
        library.UninstallRejected(game, rejected, store, _ => false);
        Assert.False(Directory.Exists(rejected)); Assert.True(Directory.Exists(healthy.Folder));
        Assert.Empty(store.Load("first").Plugins); Assert.Equal("2.0.0", Assert.Single(store.Load("second").Plugins).Version);
        Assert.Empty(library.LoadIssues()); Assert.Single(library.Packages(game));
        Assert.Throws<InvalidOperationException>(() => library.UninstallRejected(game, healthy.Folder, store, _ => false));
        Assert.Throws<InvalidOperationException>(() => library.UninstallRejected(game, _root, store, _ => false));
    }

    [Fact]
    public async Task UninstallRejectedChecksAllAffectedClientLeasesBeforeChangingSelections()
    {
        var game = Game(); using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var rejected = Directory.CreateDirectory(Path.Combine(_root, "library", "packages", "obsolete")).FullName;
        File.WriteAllText(Path.Combine(rejected, "plugin.toml"), Manifest.Replace("[plugin]", "[plugin]\nrequire_latest = true", StringComparison.Ordinal));
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        foreach (var id in new[] { "first", "second" })
            await store.CreateAsync(new(id, id, game.Id, game.Builds[0].Id, null,
                [new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>())]));
        using (store.Acquire("second")) Assert.Throws<InstanceBusyException>(() => library.UninstallRejected(game, rejected, store, _ => false));
        Assert.True(Directory.Exists(rejected)); Assert.Single(store.Load("first").Plugins);
        using (store.Acquire("second"))
            library.UninstallRejected(game, rejected, store, id => id == "second");
        Assert.Empty(store.Load("first").Plugins); Assert.Empty(store.Load("second").Plugins);
        Assert.Empty(library.LoadIssues()); Assert.True(Directory.Exists(rejected));
        library.CleanupUninstalled(store, _ => false);
        Assert.False(Directory.Exists(rejected));
    }

    [Fact]
    public void LibraryImportDoesNotRequireCompatibilityWithAnUnrelatedClient()
    {
        var game = Game(); using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var source = Folder(Manifest.Replace("test-1", "future-build", StringComparison.Ordinal));
        var preview = library.Prepare(source, game);
        var installed = library.Install(preview.Token, game);
        Assert.False(PluginCatalog.IsAvailable(installed, game.Builds[0]));
        Assert.Equal(installed.Id, Assert.Single(library.Packages(game)).Id);
    }

    [Theory]
    [InlineData("enabled")]
    [InlineData("disabled")]
    [InlineData("source-disabled")]
    [InlineData("source-changed")]
    [InlineData("source-disabled-after-download")]
    [InlineData("mirror-conflict")]
    [InlineData("no-receipt")]
    public async Task RejectedPackagesUseVerifiedSourceUpdatesWithoutLoadingOldPolicy(string scenario)
    {
        var game = Game(); var build = game.Builds[0];
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var sources = new StoreSourceStore(Path.Combine(_root, "sources"));
        foreach (var configured in sources.List()) sources.Save(configured with { Enabled = false });
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example", "source.toml", true, false);
        sources.Save(source);
        var oldFolder = Folder(); var old = StoreRelease(oldFolder);
        var installed = library.InstallFromStore(oldFolder, game, build, old.Package, old.Manifest, source.Id, StoreSourceIdentity.Of(source));
        var rejectedFile = Path.Combine(installed.Folder, "plugin.toml");
        var rejectedBytes = Manifest.Replace("[plugin]", "[plugin]\nrequire_latest = true", StringComparison.Ordinal);
        File.WriteAllText(rejectedFile, rejectedBytes);
        if (scenario == "no-receipt") File.Delete(Path.Combine(installed.Folder, ".fantasytale-source.json"));
        if (scenario == "source-disabled") sources.Save(source with { Enabled = false });
        if (scenario == "source-changed") sources.Save(source with { BaseUrl = "https://changed.example" });
        var serving = scenario == "source-disabled" ? source with { Id = "mirror" }
            : scenario == "source-changed" ? source with { BaseUrl = "https://changed.example" } : source;
        sources.Save(serving);
        if (scenario == "mirror-conflict") sources.Save(source with { Id = "mirror", BaseUrl = "https://mirror.example" });
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null,
            [new("fixture.optional", "1.0.0", scenario != "disabled", false, new Dictionary<string, object>()),
             new("fixture.other", "1.0.0", true, false, new Dictionary<string, object>())]));
        var nextFolder = Folder(Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var next = StoreRelease(nextFolder);
        var manifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(next.Manifest).Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
        var package = next.Package with { Version = "2.0.0", ManifestSha256 = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant() };
        var catalog = new StoreCatalog("fixture", new Dictionary<string, string> { ["en"] = "Fixture" }, [old.Package, package]);
        var bytes = File.ReadAllBytes(Path.Combine(nextFolder, "plugin.toml"));
        using var http = new HttpClient(new RecommendationHandler(bytes));
        await using var downloads = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"), _ => serving,
            _ => new RecommendationSource(bytes.Length), library, _ => game);
        var activation = new PluginUpgradeActivationService(Path.Combine(_root, "activations"), store, library, downloads,
            _ => game, _ => false, id => sources.List().Single(item => item.Id == id));
        var catalogRequests = 0;
        await using var updates = new PluginUpdateService(library, store, sources, http, _ => new RecommendationSource(bytes.Length), _ => game,
            _ => false, (origin, release, id, _) => Task.FromResult(activation.Enqueue(origin, release, manifest, id)),
            readCatalog: (requested, _) =>
            {
                catalogRequests++;
                return Task.FromResult(scenario == "mirror-conflict" && requested.Id == "mirror"
                    ? catalog with { Packages = [old.Package, package with { ManifestSha256 = new string('f', 64) }] } : catalog);
            });
        var result = Assert.Single(await updates.CheckAsync("first"));
        Assert.Equal(scenario == "no-receipt" ? null : source.Id, Assert.Single(library.LoadIssues()).SourceId);
        Assert.Equal(rejectedBytes, File.ReadAllText(rejectedFile));
        if (scenario == "mirror-conflict")
        {
            Assert.Equal(2, catalogRequests);
            Assert.Equal("integrity", result.Status);
            Assert.Empty(downloads.List());
            return;
        }
        Assert.Equal(1, catalogRequests);
        Assert.Equal("available", result.Status); Assert.Equal("2.0.0", result.Version);
        await using var monitor = new PluginUpdateMonitor(() => [new("first", "fixture/test-1")], updates.CheckAsync, () => true,
            autoUpdate: () => true, install: async (target, releases, ct) =>
            {
                foreach (var release in releases.Where(item => item.Status == "available"))
                    await updates.InstallAsync(target.InstanceId, release.Id, release.InstalledVersion, release.Version!, release.ManifestSha256!, ct);
            });
        await monitor.CheckAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Assert.Single(downloads.List()).Stage is not ("completed" or "failed")) await Task.Delay(20, deadline.Token);
        Assert.Equal("completed", Assert.Single(downloads.List()).Stage);
        var verified = Assert.Single(library.Packages(game));
        Assert.Equal(new PluginStoreOrigin(serving.Id, StoreSourceIdentity.Of(serving), package.ManifestSha256), library.Origin(verified));
        Assert.True(library.MatchesInstalledSnapshot(verified));
        if (scenario == "source-disabled-after-download") sources.Save(serving with { Enabled = false });
        activation.ApplyReady(); Assert.Null(activation.Failure); Assert.Equal(0, activation.PendingCount);
        var selection = store.Load("first").Plugins.Single(p => p.Id == "fixture.optional");
        Assert.Equal(scenario != "disabled", selection.Enabled);
        Assert.Equal("2.0.0", selection.Version);
        Assert.Equal("2.0.0", Assert.Single(library.Packages(game)).Version);
        Assert.Equal("1.0.0", store.Load("first").Plugins.Single(p => p.Id == "fixture.other").Version);
        Assert.False(File.Exists(rejectedFile));
        Assert.Equal(rejectedBytes, File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_root, "library", "rejected"), "plugin.toml", SearchOption.AllDirectories))));
    }
}
