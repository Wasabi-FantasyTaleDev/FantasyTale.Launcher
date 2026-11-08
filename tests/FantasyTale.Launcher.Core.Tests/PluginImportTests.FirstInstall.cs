using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    [Fact]
    public async Task ExplicitInstallAndEnableAppliesToAbsentAndDisabledSelectionsAfterRunningClientExits()
    {
        var game = Game(); var folder = Folder(); var release = StoreRelease(folder);
        var bytes = File.ReadAllBytes(Path.Combine(folder, "plugin.toml"));
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("absent", "Absent", game.Id, game.Builds[0].Id, null, []));
        await store.CreateAsync(new("disabled", "Disabled", game.Id, game.Builds[0].Id, null,
            [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())]));
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example", "source.toml", true, false);
        using var http = new HttpClient(new RecommendationHandler(bytes));
        await using var downloads = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"), _ => source,
            _ => new RecommendationSource(bytes.Length), library, _ => game);
        var running = true;
        var upgrades = new PluginUpgradeActivationService(Path.Combine(_root, "activations"), store, library, downloads,
            _ => game, id => id == "disabled" && running, _ => source);
        foreach (var id in new[] { "absent", "disabled" })
            upgrades.Enqueue(source, release.Package, release.Manifest, id, explicitSelection: true, forceEnable: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (downloads.List().Single().Stage is not ("completed" or "failed")) await Task.Delay(20, deadline.Token);
        Assert.Equal("completed", downloads.List().Single().Stage);
        upgrades.ApplyReady();
        Assert.True(Assert.Single(store.Load("absent").Plugins).Enabled);
        Assert.False(Assert.Single(store.Load("disabled").Plugins).Enabled);
        Assert.Equal(new PluginActivationStatus(1, 1, null), upgrades.Status("disabled"));
        running = false;
        upgrades.ApplyReady();
        Assert.Null(upgrades.Failure); Assert.Equal(0, upgrades.PendingCount);
        Assert.True(Assert.Single(store.Load("disabled").Plugins).Enabled);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("disabled")]
    [InlineData("missing-selection")]
    public async Task FirstInstallationActivatesIndependentlyOfAnotherClientsMissingRequirements(string requirement)
    {
        var original = Game();
        var build = original.Builds[0] with { ClientPolicy = PolicyFixture.Rules("fixture.required", "require") };
        var game = original with { Builds = [build] };
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var requiredFolder = Folder(Manifest.Replace("fixture.optional", "fixture.required", StringComparison.Ordinal));
        library.Install(library.Prepare(requiredFolder, game, build).Token, game, build);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("ready", "Ready", game.Id, build.Id, null,
            [new("fixture.required", "1.0.0", true, false, new Dictionary<string, object>())]));
        var baseline = requirement == "absent" ? Array.Empty<InstancePlugin>()
            : new[] { new InstancePlugin("fixture.required", requirement == "missing-selection" ? "9.0.0" : "1.0.0",
                requirement == "missing-selection", false, new Dictionary<string, object>()) };
        await store.CreateAsync(new("other", "Other", game.Id, build.Id, null, baseline));
        var folder = Folder(); var release = StoreRelease(folder);
        var bytes = File.ReadAllBytes(Path.Combine(folder, "plugin.toml"));
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example", "source.toml", true, false);
        using var http = new HttpClient(new RecommendationHandler(bytes));
        await using var downloads = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"), _ => source,
            _ => new RecommendationSource(bytes.Length), library, _ => game);
        var root = Path.Combine(_root, "activations");
        var upgrades = new PluginUpgradeActivationService(root, store, library, downloads, _ => game, _ => false, _ => source);
        var task = upgrades.Enqueue(source, release.Package, release.Manifest, "ready", explicitSelection: true);
        Assert.Equal(task.Id, upgrades.Enqueue(source, release.Package, release.Manifest, "other", explicitSelection: true).Id);
        Assert.Equal("", task.Target.FromVersion);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (downloads.List().Single().Stage is not ("completed" or "failed")) await Task.Delay(20, deadline.Token);
        Assert.Equal("completed", downloads.List().Single().Stage);
        // Existing durable intents need no reinstall when a fixed launcher resumes processing them.
        var restored = new PluginUpgradeActivationService(root, store, library, downloads, _ => game, _ => false, _ => source);
        restored.ApplyReady();
        Assert.Null(restored.Failure); Assert.Equal(0, restored.PendingCount);
        foreach (var id in new[] { "ready", "other" })
            Assert.True(Assert.Single(store.Load(id).Plugins, selection => selection.Id == "fixture.optional").Enabled);
        Assert.Equivalent(baseline, store.Load("other").Plugins.Where(selection => selection.Id != "fixture.optional").ToArray(), strict: true);
        Assert.ThrowsAny<Exception>(() => PluginResolver.Resolve(store.Load("other"), build, library.Packages(game), requireLibraries: false));
    }

    [Fact]
    public async Task PendingSelectionsAndFailuresBelongToTheirClientAndDoNotUndoDownloadCompletion()
    {
        var game = Game(); var folder = Folder(); var release = StoreRelease(folder);
        var bytes = File.ReadAllBytes(Path.Combine(folder, "plugin.toml"));
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        foreach (var id in new[] { "idle", "running" })
            await store.CreateAsync(new(id, id, game.Id, game.Builds[0].Id, null, []));
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example", "source.toml", true, false);
        using var http = new HttpClient(new RecommendationHandler(bytes));
        await using var downloads = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"), _ => source,
            _ => new RecommendationSource(bytes.Length), library, _ => game);
        var running = true;
        var upgrades = new PluginUpgradeActivationService(Path.Combine(_root, "activations"), store, library, downloads,
            _ => game, id => id == "running" && running, _ => source);
        upgrades.Enqueue(source, release.Package, release.Manifest, "idle", explicitSelection: true);
        upgrades.Enqueue(source, release.Package, release.Manifest, "running", explicitSelection: true);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (downloads.List().Single().Stage is not ("completed" or "failed")) await Task.Delay(20, deadline.Token);
        upgrades.ApplyReady();
        Assert.Equal(new PluginActivationStatus(0, 0, null), upgrades.Status("idle"));
        Assert.Equal(new PluginActivationStatus(1, 1, null), upgrades.Status("running"));
        var installed = Assert.Single(library.Packages(game));
        var manifest = Path.Combine(installed.Folder, "plugin.toml");
        File.AppendAllText(manifest, "\n# changed after download\n");
        running = false;
        upgrades.ApplyReady();
        Assert.Equal(new PluginActivationStatus(1, 0, "integrity"), upgrades.Status("running"));
        Assert.Equal(new PluginActivationStatus(0, 0, null), upgrades.Status("idle"));
        Assert.Equal("completed", downloads.List().Single().Stage);
        File.WriteAllBytes(manifest, bytes);
        upgrades.ApplyReady();
        Assert.Equal(new PluginActivationStatus(0, 0, null), upgrades.Status("running"));
        Assert.True(Assert.Single(store.Load("running").Plugins).Enabled);
    }
}
