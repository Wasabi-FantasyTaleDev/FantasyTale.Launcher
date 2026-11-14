using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class PluginPolicyTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-policy-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private static string Manifest(string id, string version = "1.0.0", string policy = "", int schema = 2, string kind = "overlay",
        string builds = "\"*\"", string plugin = "")
    {
        var overlay = kind == "overlay" ? "[overlay]\nentry = \"page.html\"" : "";
        return $$"""
            schema = {{schema}}
            [plugin]
            id = "{{id}}"
            version = "{{version}}"
            game = "fixture"
            kind = "{{kind}}"
            name = { en = "{{id}}" }
            {{plugin}}
            [compatibility]
            builds = [{{builds}}]
            {{overlay}}
            {{policy}}
            """;
    }

    private PluginDefinition Package(string text)
    {
        var folder = Path.Combine(_root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        return PluginCatalog.Parse(Path.Combine(folder, "plugin.toml"), text);
    }

    private (BuildDefinition First, BuildDefinition Second) Builds()
    {
        var folder = Path.Combine(_root, "games-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "Fixture", "builds"));
        File.WriteAllText(Path.Combine(folder, "Fixture", "game.toml"), "schema = 1\n[game]\nid = \"fixture\"\nstatus = \"enabled\"\nmodule = \"Fixture\"\nhook_host = \"external\"\nname = { en = \"Fixture\" }\n");
        foreach (var id in new[] { "test-1", "test-2" })
            File.WriteAllText(Path.Combine(folder, "Fixture", "builds", id + ".toml"), $"schema = 1\n[build]\nid = \"{id}\"\ngame = \"fixture\"\nbranch = \"test\"\nprotocol_version = 1\nexecutable = \"client.exe\"\nname = {{ en = \"Fixture\" }}\n[launch]\nlogin = \"none\"\n");
        var builds = GameCatalog.Load(folder).Single().Builds;
        return (builds.Single(b => b.Id == "test-1"), builds.Single(b => b.Id == "test-2"));
    }

    private static InstanceDefinition Instance(BuildDefinition build, PluginDefinition[] enabled, params PluginDefinition[] disabled) =>
        new("fixture-client", "Fixture", "fixture", build.Id, null,
            enabled.Select(p => new InstancePlugin(p.Id, p.Version, true, false, new Dictionary<string, object>()))
                .Concat(disabled.Select(p => new InstancePlugin(p.Id, p.Version, false, false, new Dictionary<string, object>()))).ToArray());

    private const string RequireBridge = "[[plugin_policy]]\nplugin = \"fixture.bridge\"\ntype = \"require\"\nversion = \">=1.2.0\"";

    [Fact]
    public void SchemaTwoReadsSeveralRulesAndSchemaOneStillLoads()
    {
        var package = Package(Manifest("fixture.mode", policy: RequireBridge
            + "\n[[plugin_policy]]\nplugin = \"fixture.font\"\ntype = \"recommend\"\nlanguages = [\"zh\"]\ndefault_selected = true"
            + "\n[[plugin_policy]]\nplugin = \"fixture.legacy\"\ntype = \"conflict\"\nbuilds = [\"test-2\"]"));
        Assert.Equal(3, package.Policy.Count);
        Assert.Equal(("fixture.font", "recommend", true), (package.Policy[1].Plugin, package.Policy[1].Type, package.Policy[1].DefaultSelected));
        Assert.Equal(["zh"], package.Policy[1].Languages);
        Assert.Equal(["test-2"], package.Policy[2].Builds);
        // Install planning and uninstall protection still see every requirement.
        Assert.Equal("fixture.bridge@>=1.2.0", Assert.Single(package.Requires).ToString());

        var legacy = Package(Manifest("fixture.old", schema: 1, plugin: "requires = [\"fixture.bridge@>=1.0.0\"]"));
        var rule = Assert.Single(legacy.Policy);
        Assert.Equal(("fixture.bridge", "require", ">=1.0.0"), (rule.Plugin, rule.Type, rule.Range));
        Assert.Empty(rule.Builds); Assert.Empty(rule.Languages);
    }

    [Theory]
    [InlineData(1, "[[plugin_policy]]\nplugin = \"fixture.bridge\"\ntype = \"require\"", "requires schema = 2")]
    [InlineData(3, "", "must be 1 or 2")]
    [InlineData(2, "[[plugin_policy]]\nplugin = \"fixture.bridge\"\ntype = \"suggest\"", "require, recommend or conflict")]
    [InlineData(2, "[[plugin_policy]]\nplugin = \"fixture.mode\"\ntype = \"require\"", "another package")]
    [InlineData(2, "[[plugin_policy]]\nplugin = \"fixture.bridge\"\ntype = \"require\"\ndefault_selected = true", "only a recommendation")]
    [InlineData(2, "[[plugin_policy]]\nplugin = \"fixture.bridge\"\ntype = \"require\"\nlanguages = [\"zh CN\"]", "language tags")]
    public void InvalidPolicyIsRejected(int schema, string policy, string message)
    {
        var error = Assert.Throws<DefinitionException>(() => Package(Manifest("fixture.mode", schema: schema, policy: policy)));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("zh", "zh-TW", true)]
    [InlineData("zh", "zh-CN", true)]
    [InlineData("zh-TW", "zh-CN", false)]
    [InlineData("zh-TW", "ZH-tw", true)]
    [InlineData("zh", "en", false)]
    [InlineData("zh", null, false)]
    public void LanguageScopesARuleWithoutForbiddingThePackage(string tag, string? language, bool applies)
    {
        var (build, _) = Builds();
        var rule = new PluginPolicyRule("fixture.font", "recommend", "", [], [tag], false);
        Assert.Equal(applies, PluginPolicy.Applies(rule, build, language));
        Assert.True(PluginPolicy.Applies(rule with { Languages = [] }, build, language));
    }

    [Fact]
    public void RequirementsReportMissingDisabledVersionAndSatisfied()
    {
        var (build, _) = Builds();
        var mode = Package(Manifest("fixture.mode", policy: RequireBridge));
        var oldBridge = Package(Manifest("fixture.bridge", "1.1.0"));
        var bridge = Package(Manifest("fixture.bridge", "1.2.0"));

        Finding(Instance(build, [mode]), build, [mode], "missing");
        Finding(Instance(build, [mode], oldBridge), build, [mode, oldBridge], "version");
        Finding(Instance(build, [mode], bridge), build, [mode, bridge], "disabled");
        var satisfied = PluginPolicy.Evaluate(Instance(build, [mode, bridge]), build, "en", [mode, bridge]).Single();
        Assert.Equal("satisfied", satisfied.Status); Assert.False(satisfied.Blocks);
    }

    private static void Finding(InstanceDefinition instance, BuildDefinition build, PluginDefinition[] packages, string status)
    {
        var finding = PluginPolicy.Evaluate(instance, build, "en", packages).Single();
        Assert.Equal(status, finding.Status);
        Assert.True(finding.Blocks);
    }

    [Fact]
    public void BuildScopedRulesApplyOnlyToTheirBuild()
    {
        var (first, second) = Builds();
        var mode = Package(Manifest("fixture.mode", policy: "[[plugin_policy]]\nplugin = \"fixture.legacy\"\ntype = \"conflict\"\nbuilds = [\"test-2\"]"));
        var legacy = Package(Manifest("fixture.legacy"));
        // On a build the rule does not name it is not evaluated at all.
        Assert.Empty(PluginPolicy.Evaluate(Instance(first, [mode, legacy]), first, null, [mode, legacy]));
        var conflict = PluginPolicy.Evaluate(Instance(second, [mode, legacy]), second, null, [mode, legacy]).Single();
        Assert.Equal("conflict", conflict.Status); Assert.True(conflict.Blocks);
    }

    [Fact]
    public void EnablingIsCheckedBeforeItIsSaved()
    {
        var (build, _) = Builds();
        var mode = Package(Manifest("fixture.mode", policy: RequireBridge));
        var findings = PluginPolicy.Evaluate(Instance(build, []), build, null, [mode], enabling: mode);
        Assert.Equal("missing", Assert.Single(findings).Status);
    }

    [Fact]
    public void RecommendationsNeverBlockAndSharedDataNeedsNoSelection()
    {
        var (build, _) = Builds();
        var mode = Package(Manifest("fixture.mode", policy: "[[plugin_policy]]\nplugin = \"fixture.font\"\ntype = \"recommend\"\nlanguages = [\"zh\"]"
            + "\n[[plugin_policy]]\nplugin = \"fixture.art\"\ntype = \"require\""));
        var art = Package(Manifest("fixture.art", kind: "assets", policy: "[assets]\nroot = \"data\""));
        var english = PluginPolicy.Evaluate(Instance(build, [mode]), build, "en", [mode, art]);
        Assert.Equal("satisfied", Assert.Single(english).Status);
        var chinese = PluginPolicy.Evaluate(Instance(build, [mode]), build, "zh-TW", [mode, art]);
        var recommendation = chinese.Single(item => item.Rule.Type == "recommend");
        Assert.Equal("missing", recommendation.Status); Assert.False(recommendation.Blocks);
    }

    [Fact]
    public void LaunchRequirementsFollowTheRuleScope()
    {
        var (first, second) = Builds();
        var mode = Package(Manifest("fixture.mode", policy: "[[plugin_policy]]\nplugin = \"fixture.bridge\"\ntype = \"require\"\nbuilds = [\"test-2\"]\nlanguages = [\"ko\"]"));
        _ = PluginRequirements.RequireSatisfied(Instance(first, [mode]), first, [mode], "ko");
        _ = PluginRequirements.RequireSatisfied(Instance(second, [mode]), second, [mode], "en");
        var error = Assert.Throws<DefinitionException>(() => PluginRequirements.RequireSatisfied(Instance(second, [mode]), second, [mode], "ko"));
        Assert.Contains("which is not installed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchRefusesAnEnabledConflict()
    {
        var (build, _) = Builds();
        var mode = Package(Manifest("fixture.mode", policy: "[[plugin_policy]]\nplugin = \"fixture.legacy\"\ntype = \"conflict\""));
        var legacy = Package(Manifest("fixture.legacy"));
        var error = Assert.Throws<DefinitionException>(() => PluginRequirements.RequireSatisfied(Instance(build, [mode, legacy]), build, [mode, legacy]));
        Assert.Contains("conflicts with 'fixture.legacy'", error.Message, StringComparison.Ordinal);
        _ = PluginRequirements.RequireSatisfied(Instance(build, [mode], legacy), build, [mode, legacy]);
    }

    [Fact]
    public void ClientPolicyLanguagesScopeRecommendations()
    {
        var policy = new ClientPolicy(1, [], [new ClientPluginRules("fixture.font", [new ClientPolicyRule("profile-recommend", "recommend", []) { Languages = ["zh"] }])]);
        Assert.Empty(ServerPluginPolicy.ForBuild(policy, "test-1", "en"));
        Assert.Empty(ServerPluginPolicy.ForBuild(policy, "test-1", null));
        Assert.Equal("profile-recommend", Assert.Single(ServerPluginPolicy.ForBuild(policy, "test-1", "zh-CN")).Recommendation);
    }
}
