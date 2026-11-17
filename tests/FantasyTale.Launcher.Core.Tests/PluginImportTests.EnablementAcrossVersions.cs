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
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedGrantsStayOffUntilTheUserApprovesThem(bool policyRequires)
    {
        var original = Game(); var build = original.Builds[0] with
        { ClientPolicy = policyRequires ? PolicyFixture.Rules("fixture.optional", "require") : ClientPolicy.Empty };
        var game = original with { Builds = [build] };
        var approved = FixtureVersion("1.0.0"); var changed = FixtureVersion("1.1.0", "fixture.shared");
        var instance = new InstanceDefinition("client", "Client", game.Id, build.Id, null,
            [Selected(approved, true, PluginGrants.Fingerprint(approved))]);
        var repaired = FilePluginResolver.EnsureComponents(instance, game, build, [changed]);
        var selection = Assert.Single(repaired.Plugins);
        Assert.Equal("1.1.0", selection.Version);
        Assert.False(selection.Enabled);
        Assert.False(selection.Required);
        Assert.Equal(PluginGrants.Fingerprint(approved), selection.Grants);
        // Repeating the repair does not turn it back on, even when the policy requires the plugin.
        Assert.False(Assert.Single(FilePluginResolver.EnsureComponents(repaired, game, build, [changed]).Plugins).Enabled);
    }

    [Fact]
    public async Task AnExplicitChoiceApprovesChangedGrantsAndRecordsThem()
    {
        var game = Game(); var build = game.Builds[0];
        var approved = FixtureVersion("1.0.0"); var changed = FixtureVersion("1.1.0", "fixture.shared");
        var store = new InstanceStore(Path.Combine(_root, "grant-consent-instances"));
        await store.CreateAsync(new("grant", "Grant", game.Id, build.Id, null, [Selected(approved, true, PluginGrants.Fingerprint(approved))]));
        var repaired = FilePluginResolver.RecoverInstalled(store, "grant", game, build, [changed], false, out _);
        Assert.False(Assert.Single(repaired.Plugins).Enabled);
        ClientPolicyActivation.EnableExplicit(store, "grant", game, [changed], changed.Id, changed.Version, forceEnable: true);
        var stored = Assert.Single(store.Load("grant").Plugins);
        Assert.True(stored.Enabled);
        Assert.Equal("1.1.0", stored.Version);
        Assert.Equal(PluginGrants.Fingerprint(changed), stored.Grants);
    }

    [Fact]
    public async Task AnAutomaticUpgradeKeepsTheEnabledStateAndWaitsForChangedGrants()
    {
        var game = Game(); var build = game.Builds[0];
        var older = FixtureVersion("1.0.0"); var same = FixtureVersion("1.1.0"); var changed = FixtureVersion("1.2.0", "fixture.shared");
        var store = new InstanceStore(Path.Combine(_root, "upgrade-consent-instances"));
        await store.CreateAsync(new("on", "On", game.Id, build.Id, null, [Selected(older, true, PluginGrants.Fingerprint(older))]));
        await store.CreateAsync(new("off", "Off", game.Id, build.Id, null, [Selected(older, false, PluginGrants.Fingerprint(older))]));

        using (store.Acquire("on"))
            ClientPolicyActivation.EnableLeased(store, "on", game, [older, same], same.Id, same.Version,
                ClientPolicyActivation.SelectionStamp(store.Load("on"), same.Id), upgrade: true);
        Assert.True(Assert.Single(store.Load("on").Plugins).Enabled);

        using (store.Acquire("off"))
            ClientPolicyActivation.EnableLeased(store, "off", game, [older, same], same.Id, same.Version,
                ClientPolicyActivation.SelectionStamp(store.Load("off"), same.Id), upgrade: true);
        var disabled = Assert.Single(store.Load("off").Plugins);
        Assert.False(disabled.Enabled);
        Assert.Equal("1.1.0", disabled.Version);

        // A grant change moves the selection to the new version, but it stays off and keeps the approved grants until renewed.
        using (store.Acquire("on"))
            ClientPolicyActivation.EnableLeased(store, "on", game, [older, same, changed], changed.Id, changed.Version,
                ClientPolicyActivation.SelectionStamp(store.Load("on"), changed.Id), upgrade: true);
        var waiting = Assert.Single(store.Load("on").Plugins);
        Assert.Equal("1.2.0", waiting.Version);
        Assert.False(waiting.Enabled);
        Assert.Equal(PluginGrants.Fingerprint(same), waiting.Grants);
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
