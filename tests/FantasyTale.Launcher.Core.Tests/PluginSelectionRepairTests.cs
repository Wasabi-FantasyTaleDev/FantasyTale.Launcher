using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    [Fact]
    public async Task SharedRecoveryRespectsFileOperationsAndOnlyChangesFutureRunningSelections()
    {
        var game = Game(); var build = game.Builds[0];
        var store = new InstanceStore(Path.Combine(_root, "recover-instances"));
        await store.CreateAsync(new("client", "Client", game.Id, build.Id, null,
            [new("fixture.missing", "1.0.0", true, false, new Dictionary<string, object>()),
             new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())]));
        var plugin = PluginCatalog.LoadFile(Path.Combine(Folder(), "plugin.toml"));
        var payload = Path.Combine(store.ClientFolder("client"), "running-payload.bin");
        File.WriteAllText(payload, "live game payload");
        using (store.Acquire("client"))
        {
            Assert.Throws<InstanceBusyException>(() => FilePluginResolver.RecoverInstalled(store, "client", game, build, [plugin], false, out _));
            Assert.Equal(2, store.Load("client").Plugins.Count);
            var changed = FilePluginResolver.RecoverInstalled(store, "client", game, build, [plugin], true, out var notices);
            Assert.False(Assert.Single(changed.Plugins).Enabled);
            Assert.Equal("fixture.missing", Assert.Single(notices).PluginId);
            Assert.Equal("live game payload", File.ReadAllText(payload));
        }
        Assert.False(Assert.Single(store.Load("client").Plugins).Enabled);
    }

    [Fact]
    public void PolicyFlagsDoNotChangeUserChoiceStampsButUserEditsStillDo()
    {
        var selection = new InstancePlugin("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>());
        var instance = new InstanceDefinition("selection", "Selection", "fixture", "test-1", null, [selection]);
        var stamp = ClientPolicyActivation.SelectionStamp(instance, selection.Id);
        var required = instance with { Plugins = [selection with { Required = true }] };
        Assert.Equal(stamp, ClientPolicyActivation.SelectionStamp(required, selection.Id));
        var legacy = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            InstanceFile.Render(required)))).ToLowerInvariant();
        Assert.True(ClientPolicyActivation.SelectionMatches(instance, selection.Id, legacy));
        Assert.False(ClientPolicyActivation.SelectionMatches(instance with { Plugins = [selection with { Enabled = false }] }, selection.Id, stamp));
        Assert.False(ClientPolicyActivation.SelectionMatches(instance with { Plugins = [selection with { Version = "2.0.0" }] }, selection.Id, stamp));
        Assert.False(ClientPolicyActivation.SelectionMatches(instance with { Plugins = [selection with { Options = new Dictionary<string, object> { ["changed"] = true } }] }, selection.Id, stamp));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InstalledSuccessorRepairsAnAbsentSelectedVersionWithoutDroppingEnablement(bool required, bool enabled)
    {
        var original = Game(); var build = original.Builds[0] with
        { ClientPolicy = required ? PolicyFixture.Rules("fixture.optional", "require") : ClientPolicy.Empty };
        var game = original with { Builds = [build], StoreManagedPlugins = ["fixture.optional"] };
        var plugin = PluginCatalog.LoadFile(Path.Combine(Folder(Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal)), "plugin.toml"));
        var selection = new InstancePlugin(plugin.Id, "1.0.0", enabled, false, new Dictionary<string, object>());
        var instance = new InstanceDefinition("fixture", "Fixture", game.Id, build.Id, null, [selection]);
        var repaired = FilePluginResolver.EnsureComponents(instance, game, build, [plugin], out var recoveries);
        var active = Assert.Single(repaired.Plugins);
        Assert.Equal("2.0.0", active.Version);
        Assert.Equal(required || enabled, active.Enabled);
        Assert.Equal(required, active.Required);
        Assert.Empty(recoveries);
    }

    [Fact]
    public void AnInstalledMandatoryPackageIsAddedWithoutARepeatedDownload()
    {
        var original = Game(); var build = original.Builds[0] with { ClientPolicy = PolicyFixture.Rules("fixture.optional", "require") };
        var game = original with { Builds = [build], StoreManagedPlugins = ["fixture.optional"] };
        var plugin = PluginCatalog.LoadFile(Path.Combine(Folder(), "plugin.toml"));
        var instance = new InstanceDefinition("fixture", "Fixture", game.Id, build.Id, null, []);
        var active = Assert.Single(FilePluginResolver.EnsureComponents(instance, game, build, [plugin]).Plugins);
        Assert.True(active.Enabled);
        Assert.True(active.Required);
        Assert.Equal(plugin.Version, active.Version);
        Assert.Empty(FilePluginResolver.EnsureComponents(instance, game, build, []).Plugins);
    }

    [Fact]
    public void SelectionRepairDoesNotReplaceAnInstalledPinOrCrossVersionConstraints()
    {
        var original = Game(); var build = original.Builds[0];
        var older = PluginCatalog.LoadFile(Path.Combine(Folder(), "plugin.toml"));
        var newer = PluginCatalog.LoadFile(Path.Combine(Folder(Manifest.Replace("1.0.0", "2.0.0", StringComparison.Ordinal)), "plugin.toml"));
        var instance = new InstanceDefinition("fixture", "Fixture", original.Id, build.Id, null,
            [new(older.Id, older.Version, true, false, new Dictionary<string, object>())]);
        Assert.Same(instance, FilePluginResolver.EnsureComponents(instance, original, build, [older, newer]));
        build = build with { ClientPolicy = new(1, [], [new(older.Id, [new("maximum", "max_version", [], "1.5.0")])]) };
        var game = original with { Builds = [build] };
        Assert.Equal("1.0.0", Assert.Single(FilePluginResolver.EnsureComponents(instance, game, build, [newer]).Plugins).Version);
        Assert.Equal("1.0.0", Assert.Single(FilePluginResolver.EnsureComponents(instance, original, original.Builds[0],
            [newer with { Builds = ["other"] }]).Plugins).Version);
    }
}
