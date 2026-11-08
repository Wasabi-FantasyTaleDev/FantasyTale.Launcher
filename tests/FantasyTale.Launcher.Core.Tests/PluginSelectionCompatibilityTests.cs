using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class PluginSelectionCompatibilityTests
{
    private static readonly LocalizedText Name = new(new Dictionary<string, string> { ["en"] = "Fixture" });
    private static readonly BuildDefinition Build = new("test", "test", 1, Name, "client.exe", "standin", "", null, null, "none", [], []);
    private static PluginDefinition Package(string id) => new(id, "1.0.0", "fixture", "patch", "", ".", Name, null, [], "", "", ["*"], [], [],
        [new("fixture", [], "", "AA", null, null, 0, "unique", "AA", "90", "immediate", 0)], null, null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitSelectionDisablesEitherDirectionOfConflictAndPreservesSettings(bool reverse)
    {
        var old = Package("fixture.old");
        var replacement = Package("fixture.new") with { ExclusiveWith = [old.Id] };
        var options = new Dictionary<string, object> { ["saved"] = "keep me" };
        var initial = new InstanceDefinition("client", "Client", "fixture", Build.Id, null,
            [new(old.Id, old.Version, !reverse, false, options), new(replacement.Id, replacement.Version, reverse, false, new Dictionary<string, object>())]);
        var chosen = reverse ? initial.Plugins[0] with { Enabled = true } : initial.Plugins[1] with { Enabled = true };
        var updated = PluginSelectionCompatibility.Select(initial, chosen, Build, [old, replacement]);
        Assert.True(updated.Plugins.Single(p => p.Id == chosen.Id).Enabled);
        Assert.False(updated.Plugins.Single(p => p.Id != chosen.Id).Enabled);
        Assert.Same(options, updated.Plugins.Single(p => p.Id == old.Id).Options);
        Assert.Equal(old.Version, updated.Plugins.Single(p => p.Id == old.Id).Version);
    }

    [Fact]
    public void RequiredConflictIsRejectedAndPersistedConflictsCannotLaunch()
    {
        var old = Package("fixture.old");
        var replacement = Package("fixture.new") with { ExclusiveWith = [old.Id] };
        var current = new InstanceDefinition("client", "Client", "fixture", Build.Id, null,
            [new(old.Id, old.Version, true, true, new Dictionary<string, object>())]);
        var selected = new InstancePlugin(replacement.Id, replacement.Version, true, false, new Dictionary<string, object>());
        Assert.Throws<DefinitionException>(() => PluginSelectionCompatibility.Select(current, selected, Build, [old, replacement]));
        Assert.True(Assert.Single(current.Plugins).Enabled);
        var invalid = current with { Plugins = current.Plugins.Append(selected).ToArray() };
        Assert.Contains("cannot be enabled with", Assert.Throws<DefinitionException>(() => PluginResolver.Resolve(invalid, Build, [old, replacement])).Message);
        var disabled = PluginSelectionCompatibility.Select(current, selected with { Enabled = false }, Build, [old, replacement]);
        Assert.True(disabled.Plugins.Single(p => p.Id == old.Id).Enabled);
    }

    [Theory]
    [InlineData("[\"fixture.new\"]")]
    [InlineData("[\"fixture.old\", \"fixture.old\"]")]
    [InlineData("[\"../unsafe\"]")]
    public void ManifestRejectsInvalidExclusiveIdentities(string value)
    {
        var text = "schema = 1\n[plugin]\nid = \"fixture.new\"\ngame = \"fixture\"\nversion = \"1.0.0\"\nkind = \"patch\"\nname = { en = \"New\" }\nexclusive_with = " + value;
        Assert.Throws<DefinitionException>(() => PluginCatalog.Parse("plugin.toml", text));
    }
}
