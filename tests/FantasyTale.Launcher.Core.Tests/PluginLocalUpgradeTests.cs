using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    [Fact]
    public async Task ANewRequiredPolicyCannotCancelACompletedUpgradeOrDisableItsSelection()
    {
        var game = Game(); var build = game.Builds[0];
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var preview = library.Prepare(Folder(), game, build); library.Install(preview.Token, game, build);
        preview = library.Prepare(Folder(Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal)), game, build);
        var imported = library.Install(preview.Token, game, build);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null,
            [new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>())]));
        using var http = new HttpClient(new RecommendationHandler([], stalled: true));
        await using var downloads = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"),
            _ => throw new InvalidOperationException("No network expected"),
            _ => throw new InvalidOperationException("No network expected"), library, _ => game);
        var running = true;
        var upgrades = new PluginUpgradeActivationService(Path.Combine(_root, "activations"), store, library, downloads,
            _ => game, _ => running, _ => null, () => false);
        upgrades.EnqueueImported(imported, game);
        build = build with { ClientPolicy = PolicyFixture.Rules(imported.Id, "require") };
        game = game with { Builds = [build] };
        var repaired = FantasyTale.Launcher.Core.Hooks.FilePluginResolver.EnsureInstalled(store, "first", game, build, library.Packages(game));
        Assert.True(Assert.Single(repaired.Plugins).Required);
        Assert.Equal("1.0.0", Assert.Single(repaired.Plugins).Version);
        running = false;
        upgrades.ApplyReady("first");
        Assert.Null(upgrades.Failure);
        Assert.Equal(0, upgrades.PendingCount);
        var active = Assert.Single(store.Load("first").Plugins);
        Assert.Equal("2.0.0", active.Version);
        Assert.True(active.Enabled);
        Assert.True(active.Required);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("changed-selection")]
    [InlineData("changed-payload")]
    [InlineData("renamed")]
    [InlineData("renamed-changed-selection")]
    public async Task LocalImportUpgradesSurviveRestartWithoutNetworkAndRespectLaterChoices(string scenario)
    {
        var game = Game(); var build = game.Builds[0];
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var preview = library.Prepare(Folder(), game, build); library.Install(preview.Token, game, build);
        var renamed = scenario.StartsWith("renamed", StringComparison.Ordinal);
        var successor = Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal);
        if (renamed) successor = successor.Replace("id = \"fixture.optional\"",
            "id = \"fixture.renamed\"\naliases = [\"fixture.optional\", \"fixture.ancient\"]", StringComparison.Ordinal);
        preview = library.Prepare(Folder(successor), game, build);
        var imported = library.Install(preview.Token, game, build);
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null,
            [new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>())]));
        await store.CreateAsync(new("disabled", "Disabled", game.Id, build.Id, null,
            [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())]));
        await store.CreateAsync(new("unselected", "Unselected", game.Id, build.Id, null, []));
        using var http = new HttpClient(new RecommendationHandler([], stalled: true));
        await using var downloads = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"),
            _ => throw new InvalidOperationException("No network source for local import"),
            _ => throw new InvalidOperationException("No network transport for local import"), library, _ => game);
        var intents = Path.Combine(_root, "activations");
        var upgrades = new PluginUpgradeActivationService(intents, store, library, downloads, _ => game,
            id => id == "first", _ => throw new InvalidOperationException("Local import must not resolve a source"));
        upgrades.EnqueueImported(imported, game);
        upgrades.ApplyReady();
        Assert.Null(upgrades.Failure);
        Assert.Equal(1, upgrades.PendingCount);
        Assert.Empty(upgrades.TasksFor("first"));
        Assert.Equal("1.0.0", Assert.Single(store.Load("first").Plugins).Version);
        var disabled = Assert.Single(store.Load("disabled").Plugins);
        Assert.Equal("2.0.0", disabled.Version); Assert.False(disabled.Enabled);
        Assert.Equal(renamed ? "fixture.renamed" : "fixture.optional", disabled.Id);
        Assert.Empty(store.Load("unselected").Plugins);
        Assert.Empty(downloads.List());
        if (scenario.EndsWith("changed-selection", StringComparison.Ordinal))
            store.Save(store.Load("first") with { Plugins = [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())] });
        if (scenario == "changed-payload") File.AppendAllText(Path.Combine(imported.Folder, "plugin.toml"), "\n# external mutation\n");
        var restored = new PluginUpgradeActivationService(intents, store, library, downloads, _ => game, _ => false,
            _ => throw new InvalidOperationException("Local import must not resolve a source"));
        restored.ApplyReady();
        var selection = Assert.Single(store.Load("first").Plugins);
        var activated = scenario is "normal" or "renamed";
        Assert.Equal(activated ? "2.0.0" : "1.0.0", selection.Version);
        Assert.Equal(activated && renamed ? "fixture.renamed" : "fixture.optional", selection.Id);
        Assert.Equal(!scenario.EndsWith("changed-selection", StringComparison.Ordinal), selection.Enabled);
        Assert.Equal(scenario == "changed-payload" ? "integrity" : null, restored.Failure);
        Assert.Equal(scenario == "changed-payload" ? 1 : 0, restored.PendingCount);
    }
}
