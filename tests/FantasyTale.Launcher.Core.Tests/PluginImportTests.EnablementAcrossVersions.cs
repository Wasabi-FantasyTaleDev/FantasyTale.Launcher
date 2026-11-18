using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    /// <summary>Loads the fixture package at one version; a requirement stands in for a grant change.</summary>
    private PluginDefinition FixtureVersion(string version, string? requires = null)
    {
        var text = Manifest.Replace("version = \"1.0.0\"", $"version = \"{version}\"", StringComparison.Ordinal);
        if (requires is not null)
            text = text.Replace("kind = \"patch\"", $"kind = \"patch\"\nrequires = [\"{requires}\"]", StringComparison.Ordinal);
        return PluginCatalog.LoadFile(Path.Combine(Folder(text), "plugin.toml"));
    }

    private static InstancePlugin Selected(PluginDefinition package, bool enabled, string? grants) =>
        new(package.Id, package.Version, enabled, false, new Dictionary<string, object>(), grants);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEnabledOrDisabledChoiceSurvivesAVersionBumpWithTheSameGrants(bool enabled)
    {
        var game = Game(); var build = game.Builds[0];
        var older = FixtureVersion("1.0.0"); var newer = FixtureVersion("1.1.0");
        var instance = new InstanceDefinition("client", "Client", game.Id, build.Id, null,
            [Selected(older, enabled, PluginGrants.Fingerprint(older))]);
        var repaired = FilePluginResolver.EnsureComponents(instance, game, build, [newer]);
        var selection = Assert.Single(repaired.Plugins);
        Assert.Equal("1.1.0", selection.Version);
        Assert.Equal(enabled, selection.Enabled);
        Assert.Equal(PluginGrants.Fingerprint(newer), selection.Grants);
        Assert.Equal(enabled, ServerPluginPolicy.Enabled(repaired, build, [newer], newer.Id));
        // The repaired record is stable: a second repair changes nothing.
        Assert.Same(repaired, FilePluginResolver.EnsureComponents(repaired, game, build, [newer]));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void ChangedGrantsDoNotChangeTheEnabledState(bool enabled, bool policyRequires)
    {
        var original = Game(); var build = original.Builds[0] with
        { ClientPolicy = policyRequires ? PolicyFixture.Rules("fixture.optional", "require") : ClientPolicy.Empty };
        var game = original with { Builds = [build] };
        var approved = FixtureVersion("1.0.0"); var changed = FixtureVersion("1.1.0", "fixture.shared");
        var instance = new InstanceDefinition("client", "Client", game.Id, build.Id, null,
            [Selected(approved, enabled, PluginGrants.Fingerprint(approved))]);
        var repaired = FilePluginResolver.EnsureComponents(instance, game, build, [changed]);
        var selection = Assert.Single(repaired.Plugins);
        Assert.Equal("1.1.0", selection.Version);
        // A required policy keeps the plugin on; otherwise the user's choice is untouched.
        Assert.Equal(enabled || policyRequires, selection.Enabled);
        Assert.Equal(PluginGrants.Fingerprint(changed), selection.Grants);
        Assert.Equal(enabled || policyRequires, ServerPluginPolicy.Enabled(repaired, build, [changed], changed.Id));
        Assert.Equal(enabled || policyRequires, Assert.Single(FilePluginResolver.EnsureComponents(repaired, game, build, [changed]).Plugins).Enabled);
    }

    [Fact]
    public async Task AnExplicitChoiceRecordsTheGrantsOfTheSelectedPackage()
    {
        var game = Game(); var build = game.Builds[0];
        var approved = FixtureVersion("1.0.0"); var changed = FixtureVersion("1.1.0", "fixture.shared");
        var store = new InstanceStore(Path.Combine(_root, "grant-consent-instances"));
        await store.CreateAsync(new("grant", "Grant", game.Id, build.Id, null, [Selected(approved, true, PluginGrants.Fingerprint(approved))]));
        var repaired = FilePluginResolver.RecoverInstalled(store, "grant", game, build, [changed], false, out _);
        Assert.True(Assert.Single(repaired.Plugins).Enabled);
        ClientPolicyActivation.EnableExplicit(store, "grant", game, [changed], changed.Id, changed.Version, forceEnable: true);
        var stored = Assert.Single(store.Load("grant").Plugins);
        Assert.True(stored.Enabled);
        Assert.Equal("1.1.0", stored.Version);
        Assert.Equal(PluginGrants.Fingerprint(changed), stored.Grants);
    }

    [Fact]
    public async Task AnAutomaticUpgradeKeepsTheEnabledAndDisabledStateAndOptions()
    {
        var game = Game(); var build = game.Builds[0];
        const string color = "\n[[options]]\nkey = \"font_color\"\ntype = \"enum\"\ndefault = \"white\"\nvalues = [\"white\", \"yellow\"]\nlabel = { en = \"Color\" }\n";
        const string old = "\n[[options]]\nkey = \"old_setting\"\ntype = \"bool\"\ndefault = false\nlabel = { en = \"Old\" }\n";
        PluginDefinition Versioned(string version, string? requires, string options)
        {
            var text = Manifest.Replace("version = \"1.0.0\"", $"version = \"{version}\"", StringComparison.Ordinal) + options;
            if (requires is not null) text = text.Replace("kind = \"patch\"", $"kind = \"patch\"\nrequires = [\"{requires}\"]", StringComparison.Ordinal);
            return PluginCatalog.LoadFile(Path.Combine(Folder(text), "plugin.toml"));
        }
        var older = Versioned("1.0.0", null, color + old); var same = Versioned("1.1.0", null, color); var changed = Versioned("1.2.0", "fixture.shared", color);
        var chosen = new Dictionary<string, object> { ["font_color"] = "yellow", ["old_setting"] = true };
        var store = new InstanceStore(Path.Combine(_root, "upgrade-consent-instances"));
        foreach (var (id, enabled) in new[] { ("on", true), ("off", false) })
            await store.CreateAsync(new(id, id, game.Id, build.Id, null,
                [new(older.Id, older.Version, enabled, false, chosen, PluginGrants.Fingerprint(older))]));

        // Two successive automatic upgrades, the second changing the grants, leave each client exactly as it was.
        foreach (var (id, enabled) in new[] { ("on", true), ("off", false) })
            foreach (var next in new[] { same, changed })
            {
                using var lease = store.Acquire(id);
                ClientPolicyActivation.EnableLeased(store, id, game, [older, same, changed], next.Id, next.Version,
                    ClientPolicyActivation.SelectionStamp(store.Load(id), next.Id), upgrade: true);
                var selection = Assert.Single(store.Load(id).Plugins);
                Assert.Equal(next.Version, selection.Version);
                Assert.Equal(enabled, selection.Enabled);
                Assert.Equal("yellow", selection.Options["font_color"]);
                Assert.DoesNotContain("old_setting", selection.Options.Keys);
                Assert.Equal(PluginGrants.Fingerprint(next), selection.Grants);
            }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASelectionSavedBeforeGrantsWereRecordedKeepsItsStateAndRecordsGrantsWhenItMoves(bool enabled)
    {
        var game = Game(); var build = game.Builds[0];
        var installed = FixtureVersion("1.0.0"); var next = FixtureVersion("1.1.0");

        // A read never writes: the pinned legacy record is returned unchanged.
        var legacy = new InstanceDefinition("legacy", "Legacy", game.Id, build.Id, null, [Selected(installed, enabled, null)]);
        Assert.Same(legacy, FilePluginResolver.EnsureComponents(legacy, game, build, [installed]));

        // Moving to the newer package keeps the enabled state, whatever its grants, and records the grants it now runs with.
        var moved = Assert.Single(FilePluginResolver.EnsureComponents(legacy, game, build, [next]).Plugins);
        Assert.Equal("1.1.0", moved.Version);
        Assert.Equal(enabled, moved.Enabled);
        Assert.Equal(PluginGrants.Fingerprint(next), moved.Grants);

        // The recorded grants survive the instance file.
        var file = Path.Combine(_root, "legacy-instance.toml");
        File.WriteAllText(file, InstanceFile.Render(legacy with { Plugins = [moved] }));
        Assert.Equal(moved.Grants, Assert.Single(InstanceFile.Load(file).Plugins).Grants);
    }

    [Fact]
    public void ServerPolicyCountsTheIdNotTheStoredVersion()
    {
        var game = Game(); var build = game.Builds[0];
        var next = FixtureVersion("1.1.0");
        var stale = new InstanceDefinition("stale", "Stale", game.Id, build.Id, null,
            [Selected(FixtureVersion("1.0.0"), true, null)]);
        // The stored version is gone, but the newer installed package of the same id is what launch would run.
        Assert.True(ServerPluginPolicy.Enabled(stale, build, [next], next.Id));
        Assert.False(ServerPluginPolicy.Enabled(stale with { Plugins = [stale.Plugins[0] with { Enabled = false }] }, build, [next], next.Id));
    }

    [Fact]
    public void AMalformedGrantsDigestIsRejectedWhenTheFileIsRead()
    {
        var file = Path.Combine(_root, "bad-grants-instance.toml");
        File.WriteAllText(file, """
            schema = 1

            [instance]
            id = "bad"
            name = "Bad"
            game = "fixture"
            build = "test-1"
            check_integrity = true

            [[plugins]]
            id = "fixture.optional"
            version = "1.0.0"
            enabled = true
            required = false
            grants = "not-a-digest"
            """);
        Assert.Throws<DefinitionException>(() => InstanceFile.Load(file));
    }
}
