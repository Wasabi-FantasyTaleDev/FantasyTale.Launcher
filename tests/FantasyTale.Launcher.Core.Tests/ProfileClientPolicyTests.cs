using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ProfileClientPolicyTests
{
    private static readonly System.Text.Json.JsonSerializerOptions PolicyJson = new(System.Text.Json.JsonSerializerDefaults.Web);
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void RecommendationDefaultSelectionRoundTripsAndDefaultsToUnchecked(bool? selected)
    {
        var extra = selected is { } value ? "default_selected = " + (value ? "true" : "false") + "\n" : "";
        var build = GameCatalog.ParseBuild("test-1.toml", Definition +
            "\n[[client_policy_rules]]\nplugin=\"fixture.plugin\"\nid=\"offer\"\ntype=\"recommend\"\nsources=[]\n" + extra, "fixture");
        var rule = Assert.Single(Assert.Single(build.ClientPolicy.Plugins).Rules);
        Assert.Equal(selected, rule.DefaultSelected);
        Assert.Equal(selected == true, Assert.Single(ProfileClientPolicy.Rules(build)).DefaultSelected);
        var text = new System.Text.StringBuilder();
        FantasyTale.Launcher.Core.Stores.StoreCatalogFile.WriteClientPolicy(text, build.ClientPolicy);
        var restored = GameCatalog.ParseBuild("test-1.toml", Definition + text, "fixture");
        Assert.Equal(selected, Assert.Single(Assert.Single(restored.ClientPolicy.Plugins).Rules).DefaultSelected);
        var json = System.Text.Json.JsonSerializer.Serialize(rule, PolicyJson);
        if (selected is null) Assert.DoesNotContain("defaultSelected", json, StringComparison.Ordinal);
        else Assert.Contains("defaultSelected", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("require")]
    [InlineData("disable")]
    [InlineData("latest")]
    public void NonRecommendationRulesRejectDefaultSelection(string type)
    {
        foreach (var selected in new[] { false, true })
        {
            var rule = new ClientPolicyRule("rule", type, []) { DefaultSelected = selected };
            Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(new ClientPolicy(1, [], [new("fixture.plugin", [rule])])));
        }
    }

    [Fact]
    public void CombinedRecommendationsOnlyDefaultToCheckedWhenEveryApplicableOfferOptsIn()
    {
        var first = new ServerPluginRule("fixture.plugin") { Recommendation = "first", DefaultSelected = true };
        var second = first with { Recommendation = "second" };
        Assert.True(Assert.Single(ProfileClientPolicy.CanonicalRules([first, second], [])).DefaultSelected);
        Assert.False(Assert.Single(ProfileClientPolicy.CanonicalRules([first, second with { DefaultSelected = false }], [])).DefaultSelected);
        Assert.False(Assert.Single(ProfileClientPolicy.CanonicalRules([new("fixture.plugin") { Recommendation = "omitted" }], [])).DefaultSelected);
    }

    private const string Definition = """
        schema = 1
        [build]
        id = "test-1"
        game = "fixture"
        branch = "test"
        protocol_version = 1
        executable = "fixture.exe"
        name = { en = "Fixture" }
        [launch]
        login = "none"
        """;

    [Theory]
    [InlineData("required")]
    [InlineData("recommended")]
    public void RemovedBuildFieldsFailRatherThanSilentlyLosingTheirRules(string key)
    {
        var error = Assert.Throws<DefinitionException>(() => GameCatalog.ParseBuild("test-1.toml",
            Definition + "\n[plugins]\n" + key + " = [\"fixture.plugin\"]\n", "fixture"));
        Assert.Equal("plugins." + key, error.Key);
    }

    [Theory]
    [InlineData("require")]
    [InlineData("disable")]
    [InlineData("recommend")]
    [InlineData("min_version")]
    [InlineData("max_version")]
    [InlineData("latest")]
    public void StoreAndLocalBuildsReadEveryExistingTypedRule(string type)
    {
        var extra = type == "recommend" ? "sources = []\n" : type is "min_version" or "max_version" ? "version = \"1.2.3\"\n" : "";
        var build = GameCatalog.ParseBuild("test-1.toml", Definition +
            $"\n[[client_policy_rules]]\nplugin = \"fixture.plugin\"\nid = \"rule\"\ntype = \"{type}\"\nbuilds = [\"test-1\"]\n" + extra, "fixture");
        Assert.True(ProfileClientPolicy.Has(build, "fixture.plugin", type));
        Assert.False(ProfileClientPolicy.Has(build with { Id = "other-1" }, "fixture.plugin", type));
    }

    [Fact]
    public void PublishedProfileInventoryUsesExclusiveActivationRules()
    {
        var game = GameCatalog.Load(Path.Combine(TestRepository.StartDirectory().FullName, "games")).Single(game => game.Id == "bubblefighter");
        var chinese = new[] { "cn-279", "cn-324", "cn-404", "cn-406", "cn-408", "cn-706", "cn-717", "cn-735", "cn-774" };
        var expected = chinese.SelectMany(build => new[] { (build, "bubblefighter.fantasy.login-fix-cn", "require"), (build, "bubblefighter.fantasy.login-fix-cn", "latest") })
            .Concat(new[]
            {
                ("kr-1256", "bubblefighter.fantasy.login-fix-kr", "require"),
                ("kr-1256", "bubblefighter.fantasy.login-fix-kr", "latest"), ("kr-1256", "bubblefighter.fantasy.shop-unlock", "require"),
            }).Order().ToArray();
        var actual = game.Builds.SelectMany(build => build.ClientPolicy.Plugins.SelectMany(plugin => plugin.Rules
            .Where(rule => rule.Type != "recommend" && (rule.Builds.Count == 0 || rule.Builds.Contains(build.Id))).Select(rule => (build.Id, plugin.Id, rule.Type)))).Order().ToArray();
        Assert.Equal(expected, actual);
        Assert.All(game.Builds.SelectMany(build => build.ClientPolicy.Plugins.SelectMany(plugin => plugin.Rules)), rule => Assert.Single(rule.Builds));
    }

    [Fact]
    public void AServerCannotDisableALocalRequirementOrRelaxItsVersionBounds()
    {
        var local = new ClientPolicy(1, [], [new("fixture.plugin",
            [new("required", "require", []), new("minimum", "min_version", [], "2.0.0")])]);
        var build = GameCatalog.ParseBuild("test-1.toml", Definition, "fixture") with { ClientPolicy = local };
        Assert.Throws<InvalidOperationException>(() => ProfileClientPolicy.ForDestination(build, PolicyFixture.Rules("fixture.plugin", "disable")));
        var remote = new ClientPolicy(1, [], [new("fixture.plugin", [new("minimum", "min_version", [], "1.0.0")])]);
        var rule = Assert.Single(ProfileClientPolicy.ForDestination(build, remote));
        Assert.True(rule.Required);
        Assert.Equal("2.0.0", rule.Minimum);
        Assert.False(rule.Allows("1.9.9"));
    }

    [Theory]
    [InlineData("require", "disable")]
    [InlineData("require", "recommend")]
    [InlineData("recommend", "require")]
    [InlineData("latest", "max_version")]
    [InlineData("min_version", "max_version")]
    public void ContradictoryProfileAndDestinationRulesFailBeforeAnyDownload(string local, string remote)
    {
        static ClientPolicy Policy(string type) => new(1, [], [new("fixture.plugin", [new("rule", type, [],
            type == "min_version" ? "2.0.0" : type == "max_version" ? "1.0.0" : null,
            Sources: type == "recommend" ? [] : null)])]);
        var build = GameCatalog.ParseBuild("test-1.toml", Definition, "fixture") with { ClientPolicy = Policy(local) };
        Assert.Throws<InvalidOperationException>(() => ProfileClientPolicy.ForDestination(build, Policy(remote)));
    }

    [Fact]
    public void LocalDisableAffectsTheLaunchSnapshotAndRetainsTheSavedSelection()
    {
        var build = GameCatalog.ParseBuild("test-1.toml", Definition, "fixture") with { ClientPolicy = PolicyFixture.Rules("fixture.plugin", "disable") };
        var instance = new InstanceDefinition("fixture", "Fixture", "fixture", build.Id, null,
            [new("fixture.plugin", "1.0.0", true, false, new Dictionary<string, object>())]);
        var launch = ServerPluginPolicy.ForLaunch(instance, build, [], ProfileClientPolicy.Rules(build));
        Assert.False(Assert.Single(launch.Plugins).Enabled);
        Assert.True(Assert.Single(instance.Plugins).Enabled);
    }

    [Fact]
    public void DestinationRecommendationIdentityIsRetainedBesideTheLocalOffer()
    {
        var build = GameCatalog.ParseBuild("test-1.toml", Definition, "fixture") with
        { ClientPolicy = new(1, [], [new("fixture.plugin", [new("local", "recommend", [], Sources: [])])]) };
        static ClientPolicy Remote(string id) => new(1, [], [new("fixture.plugin", [new(id, "recommend", [], Sources: [])])]);
        Assert.Equal("local+server-one", Assert.Single(ProfileClientPolicy.ForDestination(build, Remote("server-one"))).Recommendation);
        Assert.Equal("local+server-two", Assert.Single(ProfileClientPolicy.ForDestination(build, Remote("server-two"))).Recommendation);
    }

    [Fact]
    public void DestinationSourceRestrictionsIntersectRatherThanReplacingLocalSources()
    {
        static ClientPolicy Policy(string address) => new(1, [new("trusted", "http", address, "catalog.toml")],
            [new("fixture.plugin", [new("offer", "recommend", [], Sources: ["trusted"])])]);
        var build = GameCatalog.ParseBuild("test-1.toml", Definition, "fixture") with { ClientPolicy = Policy("https://one.invalid/") };
        Assert.Single(Assert.Single(ProfileClientPolicy.ForDestination(build, Policy("https://one.invalid/"))).Sources!);
        Assert.Throws<InvalidOperationException>(() => ProfileClientPolicy.ForDestination(build, Policy("https://two.invalid/")));
    }

    [Fact]
    public async Task CombinedRuleAcknowledgementsPersistWithoutAcceptingLegacyPluginOnlyKeys()
    {
        var folder = Directory.CreateTempSubdirectory("ft-policy-ack-").FullName;
        try
        {
            var store = new FantasyTale.Launcher.Core.Instances.InstanceStore(folder);
            await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []));
            const string key = "fixture.plugin@local+server";
            Assert.Contains(key, ClientPolicyActivation.ClaimServer(store, "fixture", [key]));
            Assert.Empty(ClientPolicyActivation.ClaimServer(new(folder), "fixture", [key]));
            Assert.Throws<DefinitionException>(() => ClientPolicyActivation.ClaimServer(store, "fixture", ["fixture.plugin"]));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void OnePolicyModelOwnsActivationInsteadOfPackageOrBuildAliases()
    {
        Assert.Null(typeof(BuildDefinition).GetProperty("RequiredPlugins"));
        Assert.Null(typeof(BuildDefinition).GetProperty("RecommendedPlugins"));
        Assert.Null(typeof(PluginDefinition).GetProperty("RequiredOn"));
        Assert.Null(typeof(PluginDefinition).GetProperty("WarnOn"));
        Assert.Null(typeof(PluginDefinition).GetProperty("RequireLatest"));
        Assert.Null(typeof(GameDefinition).GetProperty("PluginPolicies"));
    }
}
