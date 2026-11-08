using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class PluginTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-plugin-tests-").FullName;
    private const string Manifest = """
        schema = 1
        [plugin]
        id = "test.plugin"
        version = "1.2.3"
        game = "test"
        kind = "patch"
        runtime = ">=3.0.0 <4.0.0"
        name = { en = "Test" }
        [compatibility]
        builds = ["branch:kr"]
        exclude = ["kr-2"]
        [[options]]
        key = "weight"
        type = "int"
        default = 400
        min = 100
        max = 900
        label = { en = "Weight" }
        [[patches]]
        id = "gate"
        locate = { pattern = "AA ?? BB" }
        expect = "AA"
        write = "90"
        """;

    private PluginDefinition Read(string text)
    {
        var file = Path.Combine(_root, "plugin.toml"); File.WriteAllText(file, text); return PluginCatalog.LoadFile(file);
    }
    private static BuildDefinition Build(string id = "kr-1") => new(id, "kr", 1, new LocalizedText(new Dictionary<string, string> { ["en"] = "Test" }), "test.exe", "", "", null, null, "test", [], []);

    [Fact]
    public void GameLanguagePatchGateAndSessionRemainIndependentOfInterfaceLanguage()
    {
        var plugin = Read(Manifest.Replace("id = \"gate\"", "id = \"gate\"\nclient_languages = [\"zh-CN\", \"zh-TW\"]", StringComparison.Ordinal));
        Assert.Equal(["zh-CN", "zh-TW"], Assert.Single(plugin.Patches).ClientLanguages);
        var spec = new SessionSpec("test", Build(), "fixture", "hook.log", null, null, null, []) { ClientLanguage = "ko" };
        Assert.Contains("client_language = \"ko\"", SessionWriter.Render(spec), StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => SessionWriter.Render(spec with { ClientLanguage = "invalid\nvalue" }));
        Assert.Throws<DefinitionException>(() => Read(Manifest.Replace("id = \"gate\"", "id = \"gate\"\nclient_languages = [\"zh-CN\", \"zh-CN\"]", StringComparison.Ordinal)));
    }

    [Fact]
    public void FilePackagesActivateOnlyMatchingMemoryPatchesAndPreserveDeployment()
    {
        const string manifest = """
            schema = 1
            [plugin]
            id = "test.plugin"
            version = "1.2.3"
            game = "test"
            kind = "files"
            runtime = ">=3.7.1 <4.0.0"
            name = { en = "Component" }
            warning = { en = "Enable this component." }
            [compatibility]
            builds = ["branch:kr"]
            [[files]]
            source = "component.dll"
            target = "component.dll"
            size = 1
            sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            [[patches]]
            id = "startup"
            builds = ["kr-1"]
            locate = { pattern = "AA BB CC" }
            expect = "AA"
            write = "90"
            """;
        var package = Read(manifest);
        var selection = new InstancePlugin(package.Id, package.Version, true, false, new Dictionary<string, object>());
        var instance = new InstanceDefinition("test", "Test", "test", "kr-1", null, [selection]);
        Assert.Equal(package.Id, Assert.Single(PluginResolver.Resolve(instance, Build(), [package])).Id);
        Assert.Single(package.Files);
        Assert.Empty(PluginResolver.Resolve(instance with { Build = "kr-3" }, Build("kr-3"), [package]));
        Assert.Empty(PluginResolver.Resolve(instance with { Plugins = [selection with { Enabled = false }] }, Build(), [package]));
        Assert.Throws<DefinitionException>(() => Read(manifest + "\n[[options]]\nkey='extra'\ntype='bool'\ndefault=false\nlabel={en='Extra'}\n"));
        var gated = Read(manifest.Replace("id = \"startup\"", "id = \"startup\"\nwhen = \"extra\"\nclient_languages = [\"zh-CN\"]", StringComparison.Ordinal)
            + "\n[[options]]\nkey='extra'\ntype='bool'\ndefault=false\nlabel={en='Extra'}\n");
        Assert.Equal("extra", Assert.Single(gated.Patches).When);
        Assert.Single(gated.Options);
        Assert.Single(gated.Files);
    }

    [Theory]
    [InlineData("required_on = [\"kr-1\"]")]
    [InlineData("warn_on = [\"kr-1\"]")]
    [InlineData("require_latest = true")]
    public void PackagesCannotDeclareRemovedActivationPolicyFields(string field)
    {
        var error = Assert.Throws<DefinitionException>(() => Read(Manifest.Replace("[compatibility]", field + "\n[compatibility]", StringComparison.Ordinal)));
        Assert.Equal("plugin." + field.Split(' ')[0], error.Key);
    }

    [Fact]
    public void RepositoryManifestsLoad()
    {
        var directory = TestRepository.StartDirectory();
        while (!File.Exists(Path.Combine(directory!.FullName, "AGENTS.md"))) directory = directory.Parent;
        var packages = PluginCatalog.Load(Path.Combine(directory.FullName, "games", "BubbleFighter", "plugins"));
        Assert.Contains(packages, p => p.Id == "bubblefighter.fantasy.native-style-components");
        var overlay = packages.Single(p => p.Id == "bubblefighter.fantasy.overlay");
        Assert.Equal("overlay", overlay.Kind);
        Assert.Equal("bubblefighter.fantasy.overlay", overlay.Id);
        Assert.NotNull(overlay.Overlay);
        Assert.Null(overlay.Overlay!.Entry);
        Assert.Empty(overlay.Options);
        Assert.Empty(overlay.Patches);
        var diagnostic = packages.Single(p => p.Id == "bubblefighter.fantasy.diagnostics");
        Assert.Equal(8, diagnostic.Options.Count);
        Assert.Contains(diagnostic.Options, o => o.Key == "write_dump" && Equals(o.Default, true));
    }

    [Fact]
    public void OverlayManifestIsOptionalAndCannotCarryRuntimePayloads()
    {
        const string overlayManifest = """
            schema = 1
            [plugin]
            id = "test.overlay"
            version = "1.0.0"
            game = "test"
            kind = "overlay"
            name = { en = "Overlay" }
            [overlay]
            entry = "panel.html"
            """;
        var overlay = Read(overlayManifest);
        Assert.Equal("panel.html", overlay.Overlay!.Entry);
        var instance = new InstanceDefinition("test", "Test", "test", "kr-1", null,
            [new(overlay.Id, overlay.Version, true, false, new Dictionary<string, object>())]);
        Assert.Empty(PluginResolver.Resolve(instance, Build(), [overlay], requireLibraries: false));

        Assert.Throws<DefinitionException>(() => Read(overlayManifest.Replace("[overlay]", "", StringComparison.Ordinal)));
        Assert.Throws<DefinitionException>(() => Read(overlayManifest.Replace("entry = \"panel.html\"", "entry = \"..\\panel.html\"", StringComparison.Ordinal)));
        Assert.Throws<DefinitionException>(() => Read(overlayManifest.Replace("[overlay]", "[[options]]\nkey = \"x\"\ntype = \"bool\"\ndefault = false\nlabel = { en = \"X\" }\n\n[overlay]", StringComparison.Ordinal)));
        Assert.Throws<DefinitionException>(() => Read(overlayManifest.Replace("kind = \"overlay\"", "kind = \"patch\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void FutureLauncherPluginStaysVisibleAndDisabledButCannotBeEnabled()
    {
        var plugin = Read(Manifest.Replace("runtime =", "minimum_launcher_version = \"999.0.0\"\nruntime =", StringComparison.Ordinal));
        Assert.Equal("999.0.0", plugin.MinimumLauncherVersion);
        var instance = new InstanceDefinition("test", "Test", "test", "kr-1", null,
            [new(plugin.Id, plugin.Version, false, false, new Dictionary<string, object>())]);
        Assert.Empty(PluginResolver.Resolve(instance, Build(), [plugin], requireLibraries: false));
        var enabled = instance with { Plugins = [instance.Plugins[0] with { Enabled = true }] };
        Assert.Throws<LauncherUpgradeRequiredException>(() => PluginResolver.Resolve(enabled, Build(), [plugin], requireLibraries: false));
        Assert.Throws<DefinitionException>(() => Read(Manifest.Replace("runtime =", "minimum_launcher_version = \"1.0\"\nruntime =", StringComparison.Ordinal)));
    }

    [Fact]
    public void ExperimentalNumericHealthPackageRestrictsBuildAndBoundsPositions()
    {
        var directory = TestRepository.StartDirectory();
        while (!File.Exists(Path.Combine(directory!.FullName, "AGENTS.md"))) directory = directory.Parent;
        var plugin = PluginCatalog.LoadFile(Path.Combine(directory.FullName, "native", "samples",
            "bubblefighter.fantasy.numeric-health", "plugin.toml"));
        var game = GameCatalog.Load(Path.Combine(directory.FullName, "games")).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "kr-812");
        Assert.True(PluginCatalog.IsAvailable(plugin, build));
        Assert.False(PluginCatalog.IsAvailable(plugin, game.Builds.Single(b => b.Id == "id-1809")));
        var defaults = PluginCatalog.ResolveOptions(plugin, build, new Dictionary<string, object>());
        Assert.Equal("yellow", defaults["font_color"]);
        Assert.Equal("native", defaults["placement"]);
        Assert.Equal(true, defaults["show_percent"]);
        Assert.Equal(false, PluginCatalog.ResolveOptions(plugin, build,
            new Dictionary<string, object> { ["show_percent"] = false })["show_percent"]);
        Assert.Equal("yellow", PluginCatalog.ResolveOptions(plugin, build,
            new Dictionary<string, object> { ["font_color"] = "yellow" })["font_color"]);
        Assert.Throws<DefinitionException>(() => PluginCatalog.ResolveOptions(plugin, build,
            new Dictionary<string, object> { ["font_color"] = "red" }));
        Assert.Equal(false, defaults["raw_health"]);
        Assert.Equal(false, defaults["trace_timing"]);
        Assert.Equal(true, PluginCatalog.ResolveOptions(plugin, build,
            new Dictionary<string, object> { ["trace_timing"] = true })["trace_timing"]);
        Assert.Throws<DefinitionException>(() => PluginCatalog.ResolveOptions(plugin, build,
            new Dictionary<string, object> { ["offset_x"] = 501L }));
        Assert.Equal("血量数值显示", plugin.Name.Resolve("zh-CN"));
    }

    [Fact]
    public void AutoDashUsesOnePluginWithVerifiedBuildScopedPatchesAndExcludesNativeSupport()
    {
        var directory = TestRepository.StartDirectory();
        while (!File.Exists(Path.Combine(directory!.FullName, "AGENTS.md"))) directory = directory.Parent;
        var plugin = PluginCatalog.LoadFile(Path.Combine(directory.FullName, "store-plugins", "BubbleFighter",
            "bubblefighter.fantasy.auto-dash", "plugin.toml"));
        var game = GameCatalog.Load(Path.Combine(directory.FullName, "games")).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "kr-812");
        Assert.True(PluginCatalog.IsAvailable(plugin, build));
        var supported = new[] { "kr-812", "cn-279", "cn-324", "cn-406", "cn-404", "cn-706", "cn-717", "cn-735", "id-400", "id-1809", "tw-454" };
        Assert.All(game.Builds.Where(b => supported.Contains(b.Id)), b => Assert.True(PluginCatalog.IsAvailable(plugin, b)));
        Assert.All(game.Builds.Where(b => !supported.Contains(b.Id)), b => Assert.False(PluginCatalog.IsAvailable(plugin, b)));
        Assert.Equal("一键跑步", plugin.Name.Resolve("zh-CN"));
        Assert.Equal("Auto Dash", plugin.Name.Resolve("en"));
        Assert.Equal(true, PluginCatalog.ResolveOptions(plugin, build, new Dictionary<string, object>())["auto_dash"]);
        Assert.Equal(false, PluginCatalog.ResolveOptions(plugin, build,
            new Dictionary<string, object> { ["auto_dash"] = false })["auto_dash"]);
        Assert.Equal(supported.Length - 1, plugin.Patches.Count);
        Assert.DoesNotContain(plugin.Patches, patch => patch.Builds.Contains("cn-279"));
        var manifest = File.ReadAllText(Path.Combine(directory.FullName, "store-plugins", "BubbleFighter",
            "bubblefighter.fantasy.auto-dash", "plugin.toml"));
        Assert.Contains("kind = \"native\"", manifest);
        Assert.Contains("library = \"auto_dash.dll\"", manifest);
        var movement = File.ReadAllText(Path.Combine(directory.FullName, "store-plugins", "BubbleFighter",
            "bubblefighter.fantasy.auto-dash", "movement.toml"));
        Assert.Contains("cn-279 = \"timer\"", movement);
        foreach (var id in supported.Where(id => id != "cn-279"))
            Assert.Contains($"{id} = \"patch\"", movement);
        Assert.All(plugin.Patches, patch =>
        {
            Assert.Equal("auto_dash", patch.When);
            Assert.True(patch.Builds.Count == 1);
            Assert.Equal(patch.Builds.Single() == "tw-454" ? "EB 26" : "90 90", patch.Write);
        });
        var krPatch = plugin.Patches.Single(p => p.Builds.Single() == "kr-812");
        Assert.Equal("74 23", krPatch.Expect);
        var twPatch = plugin.Patches.Single(p => p.Builds.Single() == "tw-454");
        Assert.Equal("74 4A", twPatch.Expect);
    }

    [Fact]
    public void ManagedCompatibilityUsesTheShippedNativeRuntimeVersion()
    {
        var directory = TestRepository.StartDirectory();
        while (!File.Exists(Path.Combine(directory!.FullName, "AGENTS.md"))) directory = directory.Parent;
        var configuration = System.Xml.Linq.XDocument.Load(Path.Combine(directory.FullName, "native", "Native.Configuration.props"));
        var version = configuration.Descendants().Single(element => element.Name.LocalName == "FtHookVersion").Value;
        Assert.Equal(PluginCatalog.RuntimeVersion, version);
    }

    [Fact]
    public void KoreanAuthenticationIsMandatoryAndLatestOnlyForKr1256()
    {
        var directory = TestRepository.StartDirectory();
        while (!File.Exists(Path.Combine(directory!.FullName, "AGENTS.md"))) directory = directory.Parent;
        var plugin = PluginCatalog.LoadFile(Path.Combine(directory.FullName, "store-plugins", "BubbleFighter",
            "bubblefighter.fantasy.login-fix-kr", "plugin.toml"));
        var game = GameCatalog.Load(Path.Combine(directory.FullName, "games")).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "kr-1256");
        Assert.Equal("韩服认证组件", plugin.Name.Resolve("zh-CN"));
        Assert.True(FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Has(build, plugin.Id, "latest"));
        Assert.Equal("fantasy-tale", plugin.UpdateSource);
        Assert.Contains(plugin.Id, game.StoreManagedPlugins);
        Assert.Contains(plugin.Id, build.DefaultPlugins);
        Assert.Contains(plugin.Id, FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Rules(build).Where(rule => rule.Required).Select(rule => rule.Id));
        Assert.Contains(plugin.Id, FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Offered(build));
        Assert.True(PluginCatalog.IsRequired(plugin, build));
        Assert.True(FantasyTale.Launcher.Core.Plugins.ClientPolicyActivation.IsMandatory(plugin, build));
        Assert.All(game.Builds.Where(b => b.Id != "kr-1256"), b => Assert.False(PluginCatalog.IsAvailable(plugin, b)));
        var instance = new InstanceDefinition("auth-test", "Test", game.Id, build.Id, null, []);
        Assert.Empty(PluginResolver.Resolve(instance, build, [], requireLibraries: false, requireRequiredPlugins: false));
        Assert.Empty(PluginResolver.Resolve(instance, build, [plugin], requireLibraries: false, requireRequiredPlugins: false));
        Assert.Throws<DefinitionException>(() => PluginResolver.Resolve(instance, build, [], requireLibraries: false));
        Assert.Throws<DefinitionException>(() => PluginResolver.Resolve(instance, build, [plugin], requireLibraries: false));
        var selected = new InstancePlugin(plugin.Id, plugin.Version, true, true, new Dictionary<string, object>());
        var shop = PluginCatalog.LoadFile(Path.Combine(directory.FullName, "store-plugins", "BubbleFighter",
            "bubblefighter.fantasy.shop-unlock", "plugin.toml"));
        var shopSelection = new InstancePlugin(shop.Id, shop.Version, true, true, new Dictionary<string, object>());
        Assert.Equal(2, PluginResolver.Resolve(instance with { Plugins = [selected, shopSelection] }, build, [plugin, shop], requireLibraries: false).Count);
        Assert.Throws<DefinitionException>(() => PluginResolver.Resolve(instance with { Plugins = [selected with { Enabled = false }, shopSelection] },
            build, [plugin, shop], requireLibraries: false));
        Assert.Throws<DefinitionException>(() => PluginResolver.Resolve(instance with { Plugins = [selected with { Enabled = false }, shopSelection] },
            build, [plugin, shop], requireLibraries: false, requireRequiredPlugins: false));
    }

    [Theory]
    [InlineData("version = \"1.2.3\"", "version = \"01.2.3\"", "plugin.version")]
    [InlineData("runtime = \">=3.0.0 <4.0.0\"", "runtime = \">=3.0.0 garbage\"", "plugin.runtime")]
    [InlineData("default = 400", "default = 999", "options[0].default")]
    [InlineData("pattern = \"AA ?? BB\"", "rva = 123", "patches[0].locate.rva")]
    [InlineData("write = \"90\"", "write = \"??\"", "patches[0].write")]
    [InlineData("write = \"90\"", "write = \"90 90\"", "patches[0].write")]
    [InlineData("id = \"gate\"", "id = \"gate\"\nwhen = \"weight\"", "patches[0].when")]
    [InlineData("name = { en = \"Test\" }", "name = { en = \"Test\" }\ntypo = true", "plugin.typo")]
    public void InvalidManifestsNameTheKey(string from, string to, string key) =>
        Assert.Equal(key, Assert.Throws<DefinitionException>(() => Read(Manifest.Replace(from, to, StringComparison.Ordinal))).Key);

    [Fact]
    public void OptionsCompatibilityAndRequiredLocksAreEnforced()
    {
        var plugin = Read(Manifest);
        Assert.True(PluginCatalog.IsAvailable(plugin, Build()));
        Assert.False(PluginCatalog.IsAvailable(plugin, Build("kr-2")));
        Assert.Equal(400L, PluginCatalog.ResolveOptions(plugin, Build(), new Dictionary<string, object>())["weight"]);
        Assert.Throws<DefinitionException>(() => PluginCatalog.ResolveOptions(plugin, Build(), new Dictionary<string, object> { ["weight"] = true }));
        Assert.Throws<DefinitionException>(() => PluginCatalog.ResolveOptions(plugin, Build(), new Dictionary<string, object> { ["typo"] = 1L }));
        var required = plugin;
        var requiredBuild = Build() with { ClientPolicy = PolicyFixture.Rules(plugin.Id, "require") };
        var instance = new InstanceDefinition("sample", "Sample", "test", "kr-1", null, []);
        Assert.Throws<DefinitionException>(() => PluginResolver.Resolve(instance, requiredBuild, [required]));
        Assert.Throws<DefinitionException>(() => PluginResolver.Resolve(instance with { Plugins = [new InstancePlugin(plugin.Id, "9.9.9", true, false, new Dictionary<string, object>())] }, Build(), [plugin]));
        var duplicate = plugin with { Patches = [plugin.Patches[0], plugin.Patches[0]] };
        Assert.Throws<DefinitionException>(() => PluginCatalog.ResolveOptions(duplicate, Build(), new Dictionary<string, object>()));
    }

    [Fact]
    public void StringLimitUsesUtf8Bytes()
    {
        var manifest = Manifest
            .Replace("type = \"int\"", "type = \"string\"", StringComparison.Ordinal)
            .Replace("default = 400", "default = \"中\"", StringComparison.Ordinal)
            .Replace("max = 900", "max_length = 3", StringComparison.Ordinal);
        var plugin = Read(manifest);
        Assert.Equal("中", PluginCatalog.ResolveOptions(plugin, Build(), new Dictionary<string, object>())["weight"]);
        Assert.Throws<DefinitionException>(() => PluginCatalog.ResolveOptions(plugin, Build(), new Dictionary<string, object> { ["weight"] = "中文" }));
    }

    public void Dispose() => Directory.Delete(_root, true);
}
