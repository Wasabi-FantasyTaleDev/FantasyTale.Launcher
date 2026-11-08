using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerPluginPolicyTests
{
    private static readonly ServerDefinition Server = new("community", "fixture", "Community", "https://game.example/", new("127.0.0.1", 7100), false, false);
    private static readonly ServerStoreSource Community = new("community", "http", "https://plugins.example/", "source.toml");

    private static ClientPolicy Policy(params ClientPolicyRule[] rules) => new(1, [Community], [new("fixture.policy", rules)]);
    private static ClientPolicyRule Rule(string id, string type, string[]? builds = null, string? version = null, string[]? sources = null) => new(id, type, builds ?? [], version, sources);

    [Fact]
    public void FormerIdentityCannotHideARequiredRecommendationConflict()
    {
        var (_, package, _) = Client("1.0.0");
        package = package with { Aliases = ["fixture.former"] };
        var rules = new[] { new ServerPluginRule(package.Id) { Required = true },
            new ServerPluginRule("fixture.former") { Recommendation = "offer" } };
        var error = Assert.Throws<InvalidOperationException>(() => FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.CanonicalRules(rules, [package]));
        Assert.Equal("client_policy_conflict:" + package.Id, error.Message);
    }

    [Theory]
    [InlineData("disable", "require")]
    [InlineData("disable", "recommend")]
    [InlineData("disable", "min_version")]
    [InlineData("latest", "max_version")]
    [InlineData("require", "require")]
    [InlineData("require", "recommend")]
    [InlineData("recommend", "require")]
    public void ConflictingRulesOnOverlappingBuildsAreRejected(string first, string second)
    {
        ClientPolicyRule Typed(string id, string type, string[] builds) => type switch
        {
            "recommend" => Rule(id, type, builds, sources: []),
            "min_version" or "max_version" => Rule(id, type, builds, "1.0.0"),
            _ => Rule(id, type, builds),
        };
        var error = Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(Policy(Typed("a", first, ["kr-1", "cn-1"]), Typed("b", second, []))));
        Assert.Contains("fixture.policy", error.Message);
        ServerMetadataPolicy.Validate(Policy(Typed("a", first, ["kr-1"]), Typed("b", second, ["cn-1"])));
    }

    [Fact]
    public void AMinimumAboveTheMaximumConflictsAndUnknownTypesOrSourcesAreRefused()
    {
        Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(Policy(Rule("lo", "min_version", version: "2.0.1"), Rule("hi", "max_version", version: "2.0.0"))));
        ServerMetadataPolicy.Validate(Policy(Rule("lo", "min_version", version: "2.0.0"), Rule("hi", "max_version", version: "2.0.0")));
        Assert.Contains("newer Launcher", Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(Policy(Rule("a", "teleport")))).Message);
        Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(Policy(Rule("a", "recommend", sources: ["elsewhere"]))));
        Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(Policy(Rule("a", "require", version: "1.0.0"))));
        Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(Policy(Rule("a", "min_version", version: "1.0"))));
        Assert.Throws<ArgumentException>(() => ServerMetadataPolicy.Validate(Policy() with { Version = 2 }));
    }

    [Fact]
    public void RulesCombinePerBuild()
    {
        var policy = Policy(Rule("need", "require", ["kr-1"]), Rule("offer", "recommend", ["cn-1"], sources: ["community"]),
            Rule("off", "disable", ["id-1"]), Rule("floor", "min_version", ["kr-1", "cn-1"], "1.2.0"), Rule("cap", "max_version", ["cn-1"], "2.0.0"));
        ServerMetadataPolicy.Validate(policy);
        var kr = Assert.Single(ServerPluginPolicy.ForBuild(policy, "kr-1"));
        Assert.True(kr.Required); Assert.Null(kr.Recommendation); Assert.Equal("1.2.0", kr.Minimum); Assert.Null(kr.Maximum);
        var cn = Assert.Single(ServerPluginPolicy.ForBuild(policy, "cn-1"));
        Assert.False(cn.Required); Assert.Equal("offer", cn.Recommendation); Assert.Equal([Community], cn.Sources!);
        Assert.True(cn.Allows("1.2.0")); Assert.True(cn.Allows("2.0.0")); Assert.False(cn.Allows("1.1.9")); Assert.False(cn.Allows("2.0.1"));
        Assert.True(Assert.Single(ServerPluginPolicy.ForBuild(policy, "id-1")).Disabled);
        Assert.Empty(ServerPluginPolicy.ForBuild(policy, "other"));
    }

    [Fact]
    public void ProfilesRoundTripThePolicyAndRejectConflicts()
    {
        var policy = Policy(Rule("need", "require", ["kr-1"]), Rule("offer", "recommend", ["cn-1"], sources: ["community"]), Rule("cap", "max_version", ["kr-1"], "2.0.0"));
        var parsed = ServerProfileFile.Parse("community.toml", ServerProfileFile.Format(Server with { ClientPolicy = policy }));
        Assert.Equal(policy.Sources, parsed.ClientPolicy.Sources);
        var plugin = Assert.Single(parsed.ClientPolicy.Plugins);
        Assert.Equal(["need", "offer", "cap"], plugin.Rules.Select(rule => rule.Id));
        Assert.Equal("2.0.0", plugin.Rules[2].Version);
        Assert.Equal(["community"], plugin.Rules[1].Sources!);
        Assert.Throws<DefinitionException>(() => ServerProfileFile.Parse("community.toml", """
            schema = 1
            [server]
            id = "community"
            game = "fixture"
            name = "Community"
            host = "127.0.0.1"
            port = 7100
            [[client_policy_rules]]
            plugin = "fixture.policy"
            id = "need"
            type = "require"
            [[client_policy_rules]]
            plugin = "fixture.policy"
            id = "off"
            type = "disable"
            """));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedProfilePoliciesApplyWithoutLiveDiscoveryAndOnlyDisableTheLaunchCopy(bool hasApi)
    {
        var profile = Server with { ApiUrl = hasApi ? Server.ApiUrl : null, ClientPolicy = Policy(Rule("off", "disable", ["kr-1"])) };
        using var api = new LauncherApiClient();
        var rules = ServerPluginPolicy.ForBuild(api.ClientPolicy(profile), "kr-1");
        Assert.True(Assert.Single(rules).Disabled);
        var (build, package, instance) = Client("1.0.0");
        var launch = ServerPluginPolicy.ForLaunch(instance, build, [package], rules);
        Assert.False(Assert.Single(launch.Plugins).Enabled);
        Assert.True(Assert.Single(instance.Plugins).Enabled);
    }

    [Fact]
    public void LaunchesRefuseVersionsOutsideTheServersBounds()
    {
        var (build, package, instance) = Client("1.5.0");
        ServerPluginPolicy.RequireEnabled(instance, build, [package], ServerPluginPolicy.ForBuild(Policy(Rule("floor", "min_version", version: "1.5.0")), "kr-1"));
        Assert.Equal("server_plugin_version_low:fixture.policy", Assert.Throws<InvalidOperationException>(() => ServerPluginPolicy.RequireEnabled(instance, build, [package],
            ServerPluginPolicy.ForBuild(Policy(Rule("floor", "min_version", version: "1.6.0")), "kr-1"))).Message);
        Assert.Equal("server_plugin_version_high:fixture.policy", Assert.Throws<InvalidOperationException>(() => ServerPluginPolicy.RequireEnabled(instance, build, [package],
            ServerPluginPolicy.ForBuild(Policy(Rule("cap", "max_version", version: "1.4.9")), "kr-1"))).Message);
        Assert.Equal("server_plugin_required:fixture.policy", Assert.Throws<InvalidOperationException>(() => ServerPluginPolicy.RequireEnabled(
            instance with { Plugins = [instance.Plugins[0] with { Enabled = false }] }, build, [package], ServerPluginPolicy.ForBuild(Policy(Rule("need", "require")), "kr-1"))).Message);
    }

    [Fact]
    public async Task TheLiveCatalogReplacesTheSavedPolicyAndAnInvalidOneKeepsTheLastValid()
    {
        var profile = Server with { ClientPolicy = new(1, [], [new("fixture.saved", [Rule("need", "require")])]) };
        using var handler = new CatalogHandler();
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http);
        Assert.Equal("fixture.saved", Assert.Single(api.ClientPolicy(profile).Plugins).Id);
        await api.CatalogAsync(profile);
        Assert.Empty(api.ClientPolicy(profile).Plugins);
        handler.Policy = Policy(Rule("need", "require"), Rule("off", "disable"));
        await Assert.ThrowsAsync<InvalidDataException>(() => api.CatalogAsync(profile));
        Assert.Empty(api.ClientPolicy(profile).Plugins);
        handler.Policy = Policy(Rule("off", "disable", ["kr-1"]));
        await api.CatalogAsync(profile);
        Assert.True(Assert.Single(ServerPluginPolicy.ForBuild(api.ClientPolicy(profile), "kr-1")).Disabled);
        Assert.Equal("fixture.saved", Assert.Single(api.ClientPolicy(profile with { ApiUrl = "https://other.example/" }).Plugins).Id);
    }

    [Fact]
    public async Task AnUnknownPolicySectionIsRefusedRatherThanIgnored()
    {
        using var handler = new CatalogHandler { ExtraSection = true };
        using var http = new HttpClient(handler);
        using var api = new LauncherApiClient(http);
        await Assert.ThrowsAnyAsync<Exception>(() => api.CatalogAsync(Server));
    }

    private static (BuildDefinition Build, PluginDefinition Package, InstanceDefinition Instance) Client(string version)
    {
        var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" });
        var build = new BuildDefinition("kr-1", "kr", 1, name, "game.exe", "", "", null, null, "fixture", [], []);
        var package = new PluginDefinition("fixture.policy", version, "fixture", "native", "", "", name, null, [], "", "", ["kr-1"], [], [], [], null, null);
        var instance = new InstanceDefinition("client", "Fixture", "fixture", build.Id, null,
            [new("fixture.policy", version, true, false, new Dictionary<string, object>())]);
        return (build, package, instance);
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        internal ClientPolicy Policy = ClientPolicy.Empty;
        internal bool ExtraSection;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/v1/catalog", request.RequestUri!.AbsolutePath);
            var body = JsonSerializer.SerializeToNode(new LauncherCatalog(1, "fixture", "Community", false, [], []) { ClientPolicy = Policy }, JsonSerializerOptions.Web)!;
            if (ExtraSection) body["clientPolicy"]!["client"] = new System.Text.Json.Nodes.JsonObject();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }
}
