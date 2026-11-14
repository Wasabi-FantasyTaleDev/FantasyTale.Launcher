using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

/// <summary>Verifies schema 2 Ban Rules manifest requirements, transitive/disabling policy checks, and host enforcement.</summary>
public sealed class PluginPolicyEnforcementTests
{
    private static readonly string[] AllLanguages = ["en", "zh-CN", "zh-TW", "ko", "id", "vi", "th"];

    /// <summary>Checks that Ban Rules loads as schema 2 with all 7 localized copy entries and its 3 required dependencies.</summary>
    [Fact]
    public void BanRulesManifestDeclaresLocalizedCopyAndAllRuntimeRequirements()
    {
        var repo = TestRepository.StartDirectory().FullName;
        var banRulesPath = Path.Combine(repo, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.banpick", "plugin.toml");
        var previewsPath = Path.Combine(repo, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.item-previews", "plugin.toml");
        var uiResourcesPath = Path.Combine(repo, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.game-ui-resources", "plugin.toml");
        var nirinsouPath = Path.Combine(repo, "games", "BubbleFighter", "plugins", "bubble.fighter.fantasy.nirinsoulink", "plugin.toml");

        var banRules = PluginCatalog.Parse(banRulesPath, File.ReadAllText(banRulesPath));
        var previews = PluginCatalog.Parse(previewsPath, File.ReadAllText(previewsPath));
        var uiResources = PluginCatalog.Parse(uiResourcesPath, File.ReadAllText(uiResourcesPath));
        var nirinsou = PluginCatalog.Parse(nirinsouPath, File.ReadAllText(nirinsouPath));

        Assert.Equal("bubblefighter.fantasy.banpick", banRules.Id);
        Assert.Equal("0.1.71", banRules.MinimumLauncherVersion);
        Assert.Equal("Ban Rules", banRules.Name.Resolve("en"));
        Assert.Equal("禁用规则", banRules.Name.Resolve("zh-CN"));
        Assert.Equal("禁用規則", banRules.Name.Resolve("zh-TW"));
        Assert.NotNull(banRules.Description);
        foreach (var language in AllLanguages)
        {
            Assert.True(banRules.Name.Values.ContainsKey(language), $"Missing localized name for '{language}'");
            Assert.True(banRules.Description!.Values.ContainsKey(language), $"Missing localized description for '{language}'");
        }

        Assert.Equal(
            ["bubble.fighter.fantasy.nirinsoulink", "bubblefighter.fantasy.game-ui-resources", "bubblefighter.fantasy.item-previews"],
            banRules.Policy.Where(rule => rule.Type == "require").Select(rule => rule.Plugin).Order(StringComparer.Ordinal));

        Assert.Equal(["bubblefighter.fantasy.banpick"],
            PluginRequirements.Dependents(previews, [banRules, previews, uiResources, nirinsou]).Select(item => item.Id));

        var hash = new string('a', 64);
        var catalog = new StoreCatalog("store", new Dictionary<string, string> { ["en"] = "Store" },
        [
            StoreRelease(previews),
            StoreRelease(uiResources),
            StoreRelease(nirinsou),
            StoreRelease(banRules),
        ]);
        var planned = PluginRequirements.PlanStoreInstall(catalog, catalog.Packages.Single(item => item.Id == banRules.Id), []);
        Assert.Equal(["bubble.fighter.fantasy.nirinsoulink", "bubblefighter.fantasy.item-previews", "bubblefighter.fantasy.game-ui-resources"],
            planned.Select(item => item.Id));

        StorePackage StoreRelease(PluginDefinition definition) =>
            new(definition.Id, "plugin", definition.Game, [], definition.Version, definition.Name.Values,
                definition.Description?.Values ?? definition.Name.Values, "Test", 1, $"plugins/{definition.Id}/manifest.toml", hash)
            {
                PluginDefinition = definition,
                MinimumLauncherVersion = definition.MinimumLauncherVersion ?? "",
            };
    }

    /// <summary>Verifies asset satisfaction, disabling dependency evaluation, and transitive enabling checks.</summary>
    [Fact]
    public void EvaluateHandlesAssetsSatisfactionDisablingAndTransitiveChains()
    {
        var build = new BuildDefinition("test-1", "test", 1, new LocalizedText(new Dictionary<string, string> { ["en"] = "Test" }),
            "client.exe", "", "profile", 7777, null, "none", [], []);

        var mid = PluginCatalog.Parse("mid.toml", """
            schema = 2
            [plugin]
            id = "fixture.mid"
            version = "1.0.0"
            game = "fixture"
            kind = "patch"
            runtime = ">=3.0.0"
            [plugin.name]
            en = "Mid"
            [compatibility]
            builds = ["test-1"]
            [[plugin_policy]]
            plugin = "fixture.base"
            type = "require"
            [[patches]]
            id = "patch"
            locate = { pattern = "AA BB" }
            expect = "AA"
            write = "90"
            """);

        var top = PluginCatalog.Parse("top.toml", """
            schema = 2
            [plugin]
            id = "fixture.top"
            version = "1.0.0"
            game = "fixture"
            kind = "patch"
            runtime = ">=3.0.0"
            [plugin.name]
            en = "Top"
            [compatibility]
            builds = ["test-1"]
            [[plugin_policy]]
            plugin = "fixture.mid"
            type = "require"
            [[plugin_policy]]
            plugin = "fixture.assets"
            type = "require"
            [[patches]]
            id = "patch2"
            locate = { pattern = "CC DD" }
            expect = "CC"
            write = "90"
            """);

        var basePkg = PluginCatalog.Parse("base.toml", """
            schema = 1
            [plugin]
            id = "fixture.base"
            version = "1.0.0"
            game = "fixture"
            kind = "service"
            [plugin.name]
            en = "Base"
            [compatibility]
            builds = ["test-1"]
            [service]
            provider = "companion"
            """);

        var assetsPkg = PluginCatalog.Parse("assets.toml", """
            schema = 1
            [plugin]
            id = "fixture.assets"
            version = "1.0.0"
            game = "fixture"
            kind = "assets"
            [plugin.name]
            en = "Assets"
            [compatibility]
            builds = ["test-1"]
            [assets]
            root = "assets"
            """);

        var packages = new[] { top, mid, basePkg, assetsPkg };

        var allDisabled = new InstanceDefinition("client", "Client", "fixture", "test-1", null, []);
        var transitiveFindings = PluginPolicy.Evaluate(allDisabled, build, "en", packages, enabling: top);
        Assert.Contains(transitiveFindings, f => f.Plugin == "fixture.top" && f.Rule.Plugin == "fixture.mid" && f.Status == "disabled" && f.Blocks);
        Assert.Contains(transitiveFindings, f => f.Plugin == "fixture.top" && f.Rule.Plugin == "fixture.assets" && f.Status == "satisfied");
        Assert.Contains(transitiveFindings, f => f.Plugin == "fixture.mid" && f.Rule.Plugin == "fixture.base" && f.Status == "disabled" && f.Blocks);

        var enabledOnlyAssetsCheck = PluginPolicy.Enabled(allDisabled, build, packages);
        Assert.True(PluginPolicy.IsRuleSatisfied(top.Policy.Single(r => r.Plugin == "fixture.assets"), top, build, enabledOnlyAssetsCheck, packages));
        Assert.False(PluginPolicy.IsRuleSatisfied(top.Policy.Single(r => r.Plugin == "fixture.mid"), top, build, enabledOnlyAssetsCheck, packages));

        var bothEnabled = allDisabled with
        {
            Plugins =
            [
                new InstancePlugin("fixture.base", "1.0.0", true, false, new Dictionary<string, object>()),
                new InstancePlugin("fixture.mid", "1.0.0", true, false, new Dictionary<string, object>()),
            ]
        };
        var disablingBase = PluginPolicy.Evaluate(bothEnabled, build, "en", packages, disabling: basePkg);
        Assert.Contains(disablingBase, f => f.Plugin == "fixture.mid" && f.Rule.Plugin == "fixture.base" && f.Status == "disabled" && f.Blocks);
    }

    /// <summary>Verifies bridge inventory rule satisfaction, configure blocking, and disabling checks end-to-end.</summary>
    [Fact]
    public async Task BridgeInventoryAndConfigureEnforcePluginPolicyRules()
    {
        var root = Directory.CreateTempSubdirectory("ft-policy-bridge-").FullName;
        try
        {
            var games = Path.Combine(root, "games");
            var fixtureGame = Path.Combine(games, "Fixture");
            Directory.CreateDirectory(Path.Combine(fixtureGame, "builds"));
            await File.WriteAllTextAsync(Path.Combine(fixtureGame, "game.toml"), """
                schema = 1
                [game]
                id = "fixture"
                status = "enabled"
                module = "Fixture"
                hook_host = "external"
                name = { en = "Fixture" }
                """);
            await File.WriteAllTextAsync(Path.Combine(fixtureGame, "builds", "test-1.toml"), """
                schema = 1
                [build]
                id = "test-1"
                game = "fixture"
                branch = "test"
                protocol_version = 1
                executable = "client.exe"
                name = { en = "Test 1" }
                [launch]
                login = "none"
                """);

            var pluginsDir = Path.Combine(fixtureGame, "plugins");
            Directory.CreateDirectory(Path.Combine(pluginsDir, "service"));
            await File.WriteAllTextAsync(Path.Combine(pluginsDir, "service", "plugin.toml"), """
                schema = 1
                [plugin]
                id = "fixture.link"
                version = "1.0.0"
                game = "fixture"
                kind = "service"
                [plugin.name]
                en = "Fixture Link"
                [compatibility]
                builds = ["test-1"]
                [service]
                provider = "companion"
                """);

            var assetsDir = Path.Combine(pluginsDir, "assets");
            Directory.CreateDirectory(Path.Combine(assetsDir, "data"));
            await File.WriteAllTextAsync(Path.Combine(assetsDir, "data", "sample.txt"), "preview");
            await File.WriteAllTextAsync(Path.Combine(assetsDir, "plugin.toml"), """
                schema = 1
                [plugin]
                id = "fixture.previews"
                version = "1.0.0"
                game = "fixture"
                kind = "assets"
                [plugin.name]
                en = "Fixture Previews"
                [compatibility]
                builds = ["test-1"]
                [assets]
                root = "data"
                """);

            Directory.CreateDirectory(Path.Combine(pluginsDir, "rules"));
            await File.WriteAllTextAsync(Path.Combine(pluginsDir, "rules", "plugin.toml"), """
                schema = 2
                [plugin]
                id = "fixture.rules"
                version = "1.0.0"
                game = "fixture"
                kind = "patch"
                runtime = ">=3.0.0"
                [plugin.name]
                en = "Ban Rules"
                "zh-CN" = "禁用规则"
                [compatibility]
                builds = ["test-1"]
                [[plugin_policy]]
                plugin = "fixture.link"
                type = "require"
                [[plugin_policy]]
                plugin = "fixture.previews"
                type = "require"
                [[patches]]
                id = "noop"
                locate = { pattern = "AA BB" }
                expect = "AA"
                write = "90"
                """);

            var data = Path.Combine(root, "data");
            var instances = new InstanceStore(Path.Combine(data, "instances"));
            await instances.CreateAsync(new("client", "Client", "fixture", "test-1", null, []));

            await using var backend = new LauncherBackend(games, data);
            var replies = new ConcurrentQueue<JsonNode>();
            var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!));
            backend.Register(bridge);
            long sequence = 0;

            async Task<JsonNode> Call(string method, object? args = null)
            {
                var id = Interlocked.Increment(ref sequence);
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = args }), default);
                return replies.Single(row => row["id"]?.GetValue<long>() == id);
            }

            var initialList = await Call("plugins.list", new { instanceId = "client", language = "en" });
            Assert.Null(initialList["error"]);
            var rulesMeta = initialList["result"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "fixture.rules")!;
            var initialPolicy = rulesMeta["policy"]!.AsArray();
            Assert.True(initialPolicy.Single(r => r!["plugin"]!.GetValue<string>() == "fixture.previews")!["satisfied"]!.GetValue<bool>());
            Assert.False(initialPolicy.Single(r => r!["plugin"]!.GetValue<string>() == "fixture.link")!["satisfied"]!.GetValue<bool>());

            var blockedEnable = await Call("plugins.configure", new { instanceId = "client", id = "fixture.rules", version = "1.0.0", enabled = true });
            Assert.Equal("plugin_policy_require_unmet:Fixture Link", blockedEnable["error"]!.GetValue<string>());

            var checkEnable = await Call("pluginPolicy.check", new
            {
                instanceId = "client",
                language = "en",
                enabling = new { id = "fixture.rules", version = "1.0.0" },
            });
            Assert.Null(checkEnable["error"]);
            var enableFinding = Assert.Single(checkEnable["result"]!.AsArray())!;
            Assert.Equal("fixture.link", enableFinding["other"]!.GetValue<string>());
            Assert.Equal("disabled", enableFinding["status"]!.GetValue<string>());

            Assert.Null((await Call("plugins.configure", new { instanceId = "client", id = "fixture.link", version = "1.0.0", enabled = true }))["error"]);
            Assert.Null((await Call("plugins.configure", new { instanceId = "client", id = "fixture.rules", version = "1.0.0", enabled = true }))["error"]);

            var updatedList = await Call("plugins.list", new { instanceId = "client", language = "en" });
            var updatedPolicy = updatedList["result"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "fixture.rules")!["policy"]!.AsArray();
            Assert.All(updatedPolicy, rule => Assert.True(rule!["satisfied"]!.GetValue<bool>()));

            var blockedDisable = await Call("plugins.configure", new { instanceId = "client", id = "fixture.link", version = "1.0.0", enabled = false });
            Assert.Equal("plugin_policy_required_by:Ban Rules", blockedDisable["error"]!.GetValue<string>());

            var checkDisable = await Call("pluginPolicy.check", new
            {
                instanceId = "client",
                language = "zh-CN",
                disabling = new { id = "fixture.link", version = "1.0.0" },
            });
            Assert.Null(checkDisable["error"]);
            var disableFinding = Assert.Single(checkDisable["result"]!.AsArray())!;
            Assert.Equal("fixture.rules", disableFinding["plugin"]!.GetValue<string>());
            Assert.True(disableFinding["pluginActive"]!.GetValue<bool>());

            Assert.Null((await Call("plugins.configure", new { instanceId = "client", id = "fixture.rules", version = "1.0.0", enabled = false }))["error"]);
            Assert.Null((await Call("plugins.configure", new { instanceId = "client", id = "fixture.link", version = "1.0.0", enabled = false }))["error"]);
        }
        finally { Directory.Delete(root, true); }
    }
}
