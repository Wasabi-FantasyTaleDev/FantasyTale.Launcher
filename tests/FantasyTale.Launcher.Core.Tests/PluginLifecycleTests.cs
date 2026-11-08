using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    [Fact]
    public void FormerPackagesAreVisibleOnlyForClientsThatStillNeedThem()
    {
        var build = Game().Builds[0];
        var other = build with { Id = "other" };
        var former = PluginCatalog.Parse("former/plugin.toml", Manifest);
        var successor = PluginCatalog.Parse("next/plugin.toml", Manifest.Replace("fixture.optional", "fixture.next", StringComparison.Ordinal)
            .Replace("version = \"1.0.0\"", "aliases = [\"fixture.optional\"]\nversion = \"0.1.0\"", StringComparison.Ordinal));
        Assert.Equal([successor], PluginIdentityMigration.VisiblePackages([former, successor], [other]));
        Assert.Equal([successor], PluginIdentityMigration.VisiblePackages([former, successor], [build, other]));
        Assert.Equal([successor], PluginIdentityMigration.VisiblePackages([former, successor], []));
        Assert.Equal([former], PluginIdentityMigration.VisiblePackages([former], [other]));
        var unavailable = successor with { Builds = [other.Id] };
        Assert.Equal([former, unavailable], PluginIdentityMigration.VisiblePackages([former, unavailable], [build]));
        var blocked = build with { ClientPolicy = PolicyFixture.Rules(successor.Id, "disable") };
        Assert.Equal([former, successor], PluginIdentityMigration.VisiblePackages([former, successor], [blocked]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitRenamedActivationKeepsSettingsAndRespectsChangesAfterConfirmation(bool changed)
    {
        var game = Game(); var build = game.Builds[0];
        var successor = PluginCatalog.Parse("next/plugin.toml", Manifest.Replace("fixture.optional", "fixture.next", StringComparison.Ordinal)
            .Replace("version = \"1.0.0\"", "aliases = [\"fixture.optional\"]\nversion = \"2.0.0\"", StringComparison.Ordinal)
            + "\n[[options]]\nkey=\"keep\"\ntype=\"bool\"\ndefault=true\nlabel={en=\"Keep\"}\n");
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null,
            [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object> { ["keep"] = false })]));
        var baseline = store.Load("first");
        var stamp = ClientPolicyActivation.SelectionStamp(baseline, "fixture.optional");
        if (changed) store.Save(baseline with { Plugins = [baseline.Plugins[0] with { Options = new Dictionary<string, object> { ["keep"] = true } }] });
        ClientPolicyActivation.EnableExplicit(store, "first", game, [successor], successor.Id, successor.Version, stamp, forceEnable: true);
        var selected = Assert.Single(store.Load("first").Plugins);
        Assert.Equal(changed ? "fixture.optional" : successor.Id, selected.Id);
        Assert.Equal(!changed, selected.Enabled); Assert.Equal(changed, (bool)selected.Options["keep"]);
    }

    [Fact]
    public void SchemeMigrationRetainsDisabledStateAndSupportedOptionsAcrossMultipleFormerIds()
    {
        var game = Game();
        var manifest = Manifest.Replace("fixture.optional", "fixture.next", StringComparison.Ordinal)
            .Replace("version = \"1.0.0\"", "aliases = [\"fixture.optional\", \"fixture.ancient\"]\nversion = \"2.0.0\"", StringComparison.Ordinal)
            + "\n[[options]]\nkey=\"keep\"\ntype=\"bool\"\ndefault=true\nlabel={en=\"Keep\"}\n";
        var successor = PluginCatalog.Parse("successor/plugin.toml", manifest);
        var plugins = new[] { new PluginSchemePlugin("fixture.ancient", "3.1.0", false,
            new Dictionary<string, object> { ["keep"] = false, ["removed"] = 42L }) };
        var document = new PluginSchemeDocument(game.Id, "Fixture", [new(game.Builds[0].Id, plugins), new("unknown-build", plugins)]);
        var migrated = PluginSchemeFile.Migrate(document, game, [successor]);
        var selected = Assert.Single(migrated.Builds[0].Plugins);
        Assert.Equal(successor.Id, selected.Id); Assert.Equal(successor.Version, selected.Version); Assert.False(selected.Enabled);
        Assert.False((bool)selected.Options["keep"]); Assert.Single(selected.Options);
        Assert.Equal("fixture.ancient", Assert.Single(migrated.Builds[1].Plugins).Id);
        var roundtrip = PluginSchemeFile.Parse("scheme.toml", PluginSchemeFile.Render(migrated));
        Assert.Equal(selected.Id, Assert.Single(roundtrip.Builds[0].Plugins).Id);
        var duplicates = document with { Builds = [new(game.Builds[0].Id, [plugins[0], plugins[0] with { Id = successor.Id, Version = successor.Version }])] };
        Assert.Throws<InvalidDataException>(() => PluginSchemeFile.Migrate(duplicates, game, [successor]));
    }

    [Theory]
    [InlineData("owner", false, true)]
    [InlineData("owner", true, true)]
    [InlineData("other-source", false, true)]
    [InlineData("changed-source", false, true)]
    [InlineData("import", false, true)]
    [InlineData("builtin-import", true, true)]
    [InlineData("disabled", false, false)]
    public async Task DeprecationSurvivesSourceChangesAndRetainsRunningPayloads(string scenario, bool running, bool removed)
    {
        var game = Game(); var build = game.Builds[0];
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var source = new StoreSource("fixture", "Fixture", "http", "https://storage.example", "source.toml", true, false);
        var folder = Folder(); PluginDefinition installed;
        if (scenario is "import" or "builtin-import")
        {
            var preview = library.Prepare(folder, game, build); installed = library.Install(preview.Token, game, build);
        }
        else
        {
            var release = StoreRelease(folder);
            installed = library.InstallFromStore(folder, game, build, release.Package, release.Manifest, source.Id, StoreSourceIdentity.Of(source));
        }
        if (scenario == "other-source") source = source with { Id = "other" };
        if (scenario == "changed-source") source = source with { BaseUrl = "https://other.example" };
        if (scenario == "builtin-import") source = source with { BuiltIn = true };
        if (scenario == "disabled") source = source with { Enabled = false };
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, build.Id, null,
            [new(installed.Id, installed.Version, true, false, new Dictionary<string, object>())]));
        using var http = new HttpClient();
        await using var downloads = new ClientDownloadService(store, http, Path.Combine(_root, "tasks"),
            _ => source, _ => throw new InvalidOperationException("No network expected"), library, _ => game);
        var upgrades = new PluginUpgradeActivationService(Path.Combine(_root, "activations"), store, library, downloads, _ => game, _ => running, _ => source);
        var service = new PluginDeprecationService(library, upgrades, id => id == game.Id ? game : null);
        var catalog = new StoreCatalog("fixture", new Dictionary<string, string> { ["en"] = "Fixture" }, [])
        {
            DeprecatedPlugins = [new(installed.Id, game.Id, new Dictionary<string, string> { ["en"] = "Retired fixture" },
                new Dictionary<string, string> { ["en"] = "No longer needed" })],
        };
        service.Apply(source, catalog);
        Assert.Equal(removed, store.Load("first").Plugins.Count == 0);
        Assert.Equal(removed, library.Packages(game).Count == 0);
        Assert.Equal(!removed || running, Directory.Exists(installed.Folder));
        var notices = service.DrainNotices(); Assert.Equal(removed ? 1 : 0, notices.Count);
        if (removed) { Assert.Equal("Retired fixture", notices[0].Name["en"]); Assert.Equal([installed.Version], notices[0].Versions); }
        service.Apply(source, catalog); Assert.Empty(service.DrainNotices());
        if (removed && running) { library.CleanupUninstalled(store, _ => false); Assert.False(Directory.Exists(installed.Folder)); }
    }

    [Fact]
    public void AliasOwnershipRejectsAmbiguityCrossGameAndCyclesAcrossIndependentVersions()
    {
        var former = PluginCatalog.Parse("former/plugin.toml", Manifest);
        var next = PluginCatalog.Parse("next/plugin.toml", Manifest.Replace("fixture.optional", "fixture.next", StringComparison.Ordinal)
            .Replace("version = \"1.0.0\"", "aliases = [\"fixture.optional\", \"fixture.ancient\"]\nversion = \"2.0.0\"", StringComparison.Ordinal));
        PluginCatalog.ValidateIdentityConflicts([former, next], allowInstalledPredecessors: true);
        Assert.Equal("fixture.next", PluginCatalog.CanonicalId([former, next], "fixture.ancient"));
        Assert.Throws<DefinitionException>(() => PluginCatalog.ValidateIdentityConflicts([former, next]));
        PluginCatalog.ValidateIdentityConflicts([former with { Version = "3.1.0" }, next], allowInstalledPredecessors: true);
        Assert.Throws<DefinitionException>(() => PluginCatalog.ValidateIdentityConflicts([former with { Game = "other" }, next], allowInstalledPredecessors: true));
        Assert.Throws<DefinitionException>(() => PluginCatalog.ValidateIdentityConflicts([next, next with { Id = "fixture.third" }]));
        Assert.Throws<DefinitionException>(() => PluginCatalog.ValidateIdentityConflicts([former with { Aliases = [next.Id] }, next], allowInstalledPredecessors: true));
        var third = next with { Id = "fixture.third", Version = "3.0.0", Aliases = [former.Id, next.Id, "fixture.ancient"] };
        PluginCatalog.ValidateIdentityConflicts([former, next, third], allowInstalledPredecessors: true);
        Assert.Equal(third.Id, PluginCatalog.CanonicalId([former, next, third], former.Id));
        Assert.Equal(third.Id, PluginCatalog.CanonicalId([former, next, third], "fixture.ancient"));
    }
}
