using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Sdk;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Stores;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    [Fact]
    public async Task NextLaunchPluginEditsPreserveRunningSnapshotAndRejectUnrelatedOrStaleChanges()
    {
        var game = Game(); var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, game.Builds[0].Id, null,
            [new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>())]));
        var launched = store.Load("first");
        using var runningLease = store.Acquire("first");
        var next = launched with { Plugins = [launched.Plugins[0] with { Enabled = false, Options = new Dictionary<string, object> { ["speed"] = 2L } }] };
        store.SavePluginSettingsValidated(next, launched, () => { });
        Assert.True(Assert.Single(launched.Plugins).Enabled);
        Assert.Empty(Assert.Single(launched.Plugins).Options);
        Assert.False(Assert.Single(store.Load("first").Plugins).Enabled);
        Assert.Equal(2L, Assert.Single(store.Load("first").Plugins).Options["speed"]);
        Assert.Throws<InvalidOperationException>(() => store.SavePluginSettingsValidated(next with { Name = "Changed" }, next, () => { }));
        Assert.Throws<InvalidOperationException>(() => store.SavePluginSettingsValidated(launched, launched, () => { }));
        Assert.Throws<IOException>(() => store.SavePluginSettingsValidated(launched, next, () => throw new IOException("Invalid settings")));
        Assert.False(Assert.Single(store.Load("first").Plugins).Enabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UninstallRetainsLivePayloadAcrossRestartAndCanRestoreTheSameVerifiedRelease(bool fromStore)
    {
        var game = Game(); var root = Path.Combine(_root, "library");
        using var library = new PluginLibrary(root);
        var plugin = library.Install(library.Prepare(Folder(), game).Token, game);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, game.Builds[0].Id, null,
            [new(plugin.Id, plugin.Version, true, false, new Dictionary<string, object>())]));
        using (store.Acquire("first"))
        {
            library.Uninstall(game, plugin.Id, store, _ => true);
            using var restored = new PluginLibrary(root);
            Assert.Empty(restored.Packages(game));
            Assert.True(File.Exists(Path.Combine(plugin.Folder, "plugin.toml")));
            var source = Folder(); var release = StoreRelease(source);
            var reinstalled = fromStore
                ? restored.InstallFromStore(source, game, game.Builds[0], release.Package, release.Manifest, "fixture", new string('a', 64))
                : restored.Install(restored.Prepare(source, game).Token, game);
            if (fromStore) Assert.Equal(release.Package.ManifestSha256, restored.Origin(reinstalled)?.ManifestSha256);
            Assert.Equal(plugin.Folder, reinstalled.Folder);
            Assert.Single(restored.Packages(game));
        }
        library.CleanupUninstalled(store, _ => false);
        Assert.True(Directory.Exists(plugin.Folder));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitEnableIntentSurvivesRestartWhileOrdinaryUpgradesRetainDisabledSelections(bool forceEnable)
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
        var task = upgrades.Enqueue(source, package, manifest, "first", explicitSelection: true, forceEnable: forceEnable);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (downloads.List().Single(item => item.Id == task.Id).Stage is not ("completed" or "failed")) await Task.Delay(20, deadline.Token);
        Assert.Equal("completed", downloads.List().Single(item => item.Id == task.Id).Stage);
        var restored = new PluginUpgradeActivationService(root, store, library, downloads, _ => game, _ => false, _ => source);
        restored.ApplyReady();
        Assert.Null(restored.Failure);
        Assert.Equal(0, restored.PendingCount);
        var selected = Assert.Single(store.Load("first").Plugins);
        Assert.Equal("2.0.0", selected.Version);
        Assert.Equal(forceEnable, selected.Enabled);
    }
}
