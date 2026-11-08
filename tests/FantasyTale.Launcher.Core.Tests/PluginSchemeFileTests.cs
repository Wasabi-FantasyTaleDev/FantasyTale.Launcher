using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Plugins;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class PluginSchemeFileTests
{
    [Fact]
    public void RoundTripKeepsNestedBuildsSelectionsAndScalarOptionsIndependent()
    {
        var document = new PluginSchemeDocument("fixture", "Shared configuration", [
            new("build-one", [
                new("fixture.first", "1.0.0", true, new Dictionary<string, object> { ["limit"] = 12L, ["mode.name"] = "a\nb\"c\\d", ["active"] = false }),
                new("fixture.second", "2.0.0", false, new Dictionary<string, object> { ["limit"] = 4L })]),
            new("build-two", [new("fixture.first", "1.1.0", false, new Dictionary<string, object> { ["limit"] = 24L })])]);
        var text = PluginSchemeFile.Render(document);
        var restored = PluginSchemeFile.Parse("scheme.toml", text);
        Assert.Equal(document.Name, restored.Name);
        Assert.Equal(2, restored.Builds.Count);
        Assert.Equal(12L, restored.Builds[0].Plugins[0].Options["limit"]);
        Assert.Equal("a\nb\"c\\d", restored.Builds[0].Plugins[0].Options["mode.name"]);
        Assert.False(restored.Builds[0].Plugins[1].Enabled);
        Assert.Equal(4L, restored.Builds[0].Plugins[1].Options["limit"]);
        Assert.Equal(24L, restored.Builds[1].Plugins[0].Options["limit"]);
        Assert.Equal(text, PluginSchemeFile.Render(restored));
    }

    [Theory]
    [InlineData("schema = 2\ngame = 'fixture'\nname = 'Test'", "schema")]
    [InlineData("schema = 1\ngame = '../fixture'\nname = 'Test'", "id")]
    [InlineData("schema = 1\ngame = 'fixture'\nname = 'Test'\n[[builds]]\nid = 'one'\n[[builds]]\nid = 'one'", "duplicate build")]
    [InlineData("schema = 1\ngame = 'fixture'\nname = 'Test'\n[[builds]]\nid = 'one'\n[[builds.plugins]]\nid = 'fixture.test'\nversion = '1.0.0'\n[[builds.plugins]]\nid = 'fixture.test'\nversion = '1.1.0'", "duplicate plugin")]
    [InlineData("schema = 1\ngame = 'fixture'\nname = 'Test'\n[[builds]]\nid = 'one'\n[[builds.plugins]]\nid = 'fixture.test'\nversion = '1.0.0'\n[builds.plugins.options]\nsetting = ['unexpected']", "options.setting")]
    public void MalformedSharedDocumentsAreRejectedWithTheirLocation(string text, string reason)
    {
        var error = Assert.Throws<DefinitionException>(() => PluginSchemeFile.Parse("shared.toml", text));
        Assert.Contains("shared.toml", error.Message);
        Assert.Contains(reason, error.Message);
    }
}
