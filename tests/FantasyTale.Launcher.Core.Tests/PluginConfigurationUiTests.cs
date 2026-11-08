using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Plugins;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class PluginConfigurationUiTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-plugin-ui-").FullName;
    private const string Manifest = """
        schema = 1
        [plugin]
        id = "fixture.ui"
        version = "1.0.0"
        game = "fixture"
        kind = "patch"
        name = { en = "Fixture" }
        [[options]]
        key = "color"
        type = "enum"
        default = "white"
        values = ["white", "yellow"]
        label = { en = "Color" }
        """;

    [Fact]
    public void OptionalUiPreservesTheNativeManifestAndLocalizesEnumValues()
    {
        var plugin = Read();
        Assert.Null(PluginConfigurationUi.Load(plugin).Page);
        File.WriteAllText(Path.Combine(_root, "settings.html"), "<label>Settings</label>");
        File.WriteAllText(Path.Combine(_root, "plugin-ui.toml"), "schema = 1\n[ui]\npage = \"settings.html\"\n[[enum_labels]]\noption = \"color\"\nvalue = \"white\"\nlabel = { en = \"White\", \"zh-CN\" = \"白色\" }\n");
        var ui = PluginConfigurationUi.Load(plugin);
        Assert.Equal("settings.html", ui.Page);
        Assert.Equal("白色", ui.Labels["color"]["white"].Resolve("zh-CN"));
        Assert.Equal("<label>Settings</label>", PluginConfigurationUi.ReadPage(_root, ui.Page!));
        Assert.Equal("white", PluginCatalog.ResolveOptionValues(plugin, new Dictionary<string, object>())["color"]);
        Assert.Throws<DefinitionException>(() => PluginCatalog.ResolveOptionValues(plugin, new Dictionary<string, object> { ["color"] = "red" }));
    }

    [Theory]
    [InlineData("../outside.html")]
    [InlineData("/outside.html")]
    [InlineData("C:/outside.html")]
    [InlineData("settings.js")]
    [InlineData("missing.html")]
    public void CustomPagesRejectUnsafeOrMissingPackagePaths(string path)
    {
        var plugin = Read();
        File.WriteAllText(Path.Combine(_root, "plugin-ui.toml"), $"schema = 1\n[ui]\npage = \"{path}\"\n");
        Assert.ThrowsAny<Exception>(() => PluginConfigurationUi.Load(plugin));
    }

    [Fact]
    public void CustomPagesAcceptLargeContentAndRejectInvalidUtf8Files()
    {
        var plugin = Read();
        File.WriteAllText(Path.Combine(_root, "plugin-ui.toml"), "schema = 1\n[ui]\npage = \"settings.html\"\n");
        var page = Path.Combine(_root, "settings.html");
        var content = "<main>" + new string('a', 256 * 1024 + 1) + "</main>";
        File.WriteAllText(page, content);
        Assert.Equal("settings.html", PluginConfigurationUi.Load(plugin).Page);
        Assert.Equal(content, PluginConfigurationUi.ReadPage(_root, "settings.html"));
        File.WriteAllBytes(page, [0xff]);
        Assert.Throws<System.Text.DecoderFallbackException>(() => PluginConfigurationUi.Load(plugin));
    }

    [Theory]
    [InlineData("missing", "white")]
    [InlineData("color", "red")]
    public void EnumLabelsCannotDeclareUndeclaredSettings(string option, string value)
    {
        var plugin = Read();
        File.WriteAllText(Path.Combine(_root, "plugin-ui.toml"), $"schema = 1\n[[enum_labels]]\noption = \"{option}\"\nvalue = \"{value}\"\nlabel = {{ en = \"Caption\" }}\n");
        Assert.Throws<DefinitionException>(() => PluginConfigurationUi.Load(plugin));
    }

    [Fact]
    public void ExplicitPagesControlTheirOwnKindsAndOptionMembership()
    {
        var plugin = Read();
        File.WriteAllText(Path.Combine(_root, "settings.html"), "<main>Color</main>");
        File.WriteAllText(Path.Combine(_root, "plugin-ui.toml"), """
            schema = 1
            [[pages]]
            id = "colors"
            kind = "html"
            path = "settings.html"
            options = ["color"]
            title = { en = "Colors" }
            [[pages]]
            id = "advanced"
            kind = "native"
            options = ["color"]
            title = { en = "Advanced" }
            """);
        var ui = PluginConfigurationUi.Load(plugin);
        Assert.Null(ui.Page);
        Assert.Collection(ui.Pages!,
            page => { Assert.Equal("colors", page.Id); Assert.Equal("html", page.Kind); Assert.Equal("settings.html", page.Path); Assert.Equal("color", Assert.Single(page.Options)); },
            page => { Assert.Equal("advanced", page.Id); Assert.Equal("native", page.Kind); Assert.Null(page.Path); });
    }

    [Theory]
    [InlineData("pages = []")]
    [InlineData("[[pages]]\nid='one'\nkind='unknown'\ntitle={en='One'}\noptions=['color']")]
    [InlineData("[[pages]]\nid='one'\nkind='html'\ntitle={en='One'}\noptions=['color']")]
    [InlineData("[[pages]]\nid='one'\nkind='html'\npath='../outside.html'\ntitle={en='One'}\noptions=['color']")]
    [InlineData("[[pages]]\nid='one'\nkind='native'\npath='settings.html'\ntitle={en='One'}\noptions=['color']")]
    [InlineData("[[pages]]\nid='one'\nkind='native'\ntitle={en='One'}\noptions=['missing']")]
    [InlineData("[[pages]]\nid='one'\nkind='native'\ntitle={en='One'}\noptions=['color','color']")]
    [InlineData("[[pages]]\nid='one'\nkind='native'\ntitle={en='One'}")]
    [InlineData("[[pages]]\nid='one'\nkind='native'\ntitle={en='One'}\noptions=[]\n[[pages]]\nid='one'\nkind='native'\ntitle={en='Again'}\noptions=[]")]
    [InlineData("[ui]\npage='settings.html'\n[[pages]]\nid='one'\nkind='native'\ntitle={en='One'}\noptions=['color']")]
    public void ExplicitPagesRejectInvalidTypesPathsOptionsAndConflicts(string declaration)
    {
        var plugin = Read();
        File.WriteAllText(Path.Combine(_root, "settings.html"), "<main>Settings</main>");
        File.WriteAllText(Path.Combine(_root, "plugin-ui.toml"), "schema=1\n" + declaration);
        Assert.ThrowsAny<Exception>(() => PluginConfigurationUi.Load(plugin));
    }

    private FantasyTale.Launcher.Sdk.PluginDefinition Read()
    {
        var file = Path.Combine(_root, "plugin.toml"); File.WriteAllText(file, Manifest);
        return PluginCatalog.LoadFile(file);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
