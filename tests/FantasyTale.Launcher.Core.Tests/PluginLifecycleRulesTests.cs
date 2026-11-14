using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    private static string Release(string version, string builds = "\"test-1\"") =>
        Manifest.Replace("version = \"1.0.0\"", $"version = \"{version}\"", StringComparison.Ordinal)
            .Replace("builds = [\"test-1\"]", $"builds = [{builds}]", StringComparison.Ordinal);

    private PluginDefinition Import(PluginLibrary library, GameDefinition game, string text)
    {
        var preview = library.Prepare(Folder(text), game, null, null);
        return library.Install(preview.Token, game);
    }

    private GameDefinition TwoBuildGame()
    {
        var game = Game();
        File.WriteAllText(Path.Combine(game.Folder, "builds", "test-2.toml"), "schema = 1\n[build]\nid = \"test-2\"\ngame = \"fixture\"\nbranch = \"test\"\nprotocol_version = 1\nexecutable = \"client.exe\"\nname = { en = \"Fixture\" }\n[launch]\nlogin = \"none\"\n");
        return Assert.Single(GameCatalog.Load(Path.Combine(_root, "games")));
    }

    [Fact]
    public void SupersededImportsAreRemovedOnlyOnceNoClientSelectsThem()
    {
        var game = Game();
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var first = Import(library, game, Release("1.0.0"));
        var second = Import(library, game, Release("1.1.0"));
        var selectedOld = new HashSet<(string, string, string)> { (game.Id, first.Id, first.Version) };
        Assert.False(library.RetireSupersededImports(selectedOld));
        Assert.Equal(["1.0.0", "1.1.0"], library.Packages(game).Select(p => p.Version).Order());

        var selectedNew = new HashSet<(string, string, string)> { (game.Id, second.Id, second.Version) };
        Assert.True(library.RetireSupersededImports(selectedNew));
        Assert.Equal("1.1.0", Assert.Single(library.Packages(game)).Version);
        // The newest version is never removed, even when nothing selects it.
        Assert.False(library.RetireSupersededImports(new HashSet<(string, string, string)>()));
        Assert.Single(library.Packages(game));
    }

    [Fact]
    public async Task AnUpgradeOffersOnlyTheClientsItNewlySupports()
    {
        var game = TwoBuildGame();
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("old-build", "Old build", game.Id, "test-1", null, []));
        await store.CreateAsync(new("new-build", "New build", game.Id, "test-2", null, []));
        await store.CreateAsync(new("chosen", "Already chosen", game.Id, "test-2", null,
            [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())]));
        var offers = new PluginSupportOffers(Path.Combine(_root, "offers"));
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var announced = new List<(PluginDefinition Installed, IReadOnlyList<PluginDefinition> Previous)>();
        library.PackageInstalled += (installed, previous) => announced.Add((installed, previous));

        var fresh = Import(library, game, Release("1.0.0"));
        Assert.Empty(Assert.Single(announced).Previous);
        Assert.False(offers.Record(fresh, announced[0].Previous, game, store.List()));

        var upgrade = Import(library, game, Release("1.1.0", "\"test-1\", \"test-2\""));
        Assert.Equal("1.0.0", Assert.Single(announced[1].Previous).Version);
        Assert.True(offers.Record(upgrade, announced[1].Previous, game, store.List()));
        var offer = Assert.Single(offers.List());
        Assert.Equal(["new-build"], offer.Instances);

        // Offers survive a restart until answered, and answering removes the clients it covers.
        var reopened = new PluginSupportOffers(Path.Combine(_root, "offers"));
        Assert.Equal(["new-build"], Assert.Single(reopened.List()).Instances);
        reopened.Resolve(game.Id, upgrade.Id, ["new-build"], store.List().Select(item => item.Id).ToArray());
        Assert.Empty(reopened.List());
    }

    [Fact]
    public async Task RecoveryKeepsAnEnabledBundledComponentWhenItsSelectedVersionMoves()
    {
        // Regression: a client's enabled native-ui-bridge selection once fell back to disabled after the bundled copy and the
        // imported copy changed versions between launcher builds. Every combination must keep the selection enabled.
        var game = Game();
        var bundled = Path.Combine(game.Folder, "plugins", "fixture.optional"); Directory.CreateDirectory(bundled);
        File.WriteAllText(Path.Combine(bundled, "plugin.toml"), Release("1.2.0"));
        game = Assert.Single(GameCatalog.Load(Path.Combine(_root, "games")));
        game = game with { DefaultPlugins = ["fixture.optional"], StoreManagedPlugins = ["fixture.optional"] };
        var build = game.Builds[0];
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        foreach (var selected in new[] { "1.0.0", "1.2.0", "1.3.0" })
        {
            var id = "client-" + selected.Replace('.', '-');
            await store.CreateAsync(new(id, id, game.Id, build.Id, null, [new("fixture.optional", selected, true, false, new Dictionary<string, object>())]));
            var recovered = FilePluginResolver.RecoverInstalled(store, id, game, build, library.Packages(game), false, out _);
            var selection = Assert.Single(recovered.Plugins, item => item.Id == "fixture.optional");
            Assert.True(selection.Enabled, $"selection {selected} was turned off");
        }
    }
}
