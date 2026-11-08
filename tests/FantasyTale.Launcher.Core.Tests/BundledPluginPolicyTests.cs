using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Host.Bridge;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class BundledPluginPolicyTests
{
    private static readonly string[] Bundled = ["bubble.fighter.fantasy.nirinsoulink", "bubblefighter.fantasy.diagnostics", "bubblefighter.fantasy.native-style-components", "bubblefighter.fantasy.native-ui-bridge", "bubblefighter.fantasy.overlay"];
    private static readonly string[] Defaults = ["bubble.fighter.fantasy.nirinsoulink", "bubblefighter.fantasy.overlay", "bubblefighter.fantasy.native-ui-bridge", "bubblefighter.fantasy.native-style-components"];

    [Fact]
    public void NativeControlsAndCommunicationHaveSeparatePresentationResponsibilities()
    {
        var root = TestRepository.StartDirectory().FullName;
        var packages = PluginCatalog.Load(Path.Combine(root, "games", "BubbleFighter", "plugins"));
        var controls = packages.Single(package => package.Id == "bubblefighter.fantasy.native-ui-bridge");
        Assert.Equal("native", controls.Kind);
        Assert.Equal("game_controls.dll", controls.Library);
        Assert.Null(controls.Service);
        Assert.Null(controls.Overlay);
        Assert.Empty(controls.CompanionMessages);
        Assert.True(PluginVersion.Satisfies(PluginCatalog.RuntimeVersion, controls.Runtime));
        var communication = packages.Single(package => package.Id == "bubble.fighter.fantasy.nirinsoulink");
        Assert.Equal("companion", communication.Service!.Provider);
        Assert.Equal(1, communication.Service.Messages["ui.notification.show"]);
        Assert.Equal(1, communication.Service.Messages["ui.notification.result"]);
        Assert.Contains(communication.Options, option => option.Key == "max_dialogs");
    }

    [Fact]
    public void DistributionContainsExactlyFivePluginsWithFourExplicitDefaults()
    {
        var root = TestRepository.StartDirectory().FullName;
        var game = GameCatalog.Load(Path.Combine(root, "games")).Single(game => game.Id == "bubblefighter");
        var packages = PluginCatalog.Load(Path.Combine(game.Folder, "plugins"));
        Assert.Equal(Bundled, packages.Select(package => package.Id));
        Assert.Equal(Defaults, game.DefaultPlugins);
        Assert.All(packages, package =>
        {
            Assert.Contains(package.Id, game.StoreManagedPlugins);
            Assert.Equal("fantasy-tale", package.UpdateSource);
        });
        foreach (var build in game.Builds)
        {
            var selected = PluginResolver.Defaults(game, build, packages);
            Assert.Equal(game.DefaultPlugins, selected.Select(plugin => plugin.Id));
            Assert.All(selected, plugin => Assert.True(plugin.Enabled));
        }
        var catalog = PluginCatalog.Load(Path.Combine(root, "store-plugins", "BubbleFighter"));
        Assert.DoesNotContain(catalog, plugin => plugin.Id.Contains("forward-jump", StringComparison.Ordinal));
        var korean = game.Builds.Single(build => build.Id == "kr-1256");
        Assert.Contains("bubblefighter.fantasy.shop-unlock", FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Offered(korean));
        Assert.Contains("bubblefighter.fantasy.shop-unlock", FantasyTale.Launcher.Core.Plugins.ProfileClientPolicy.Rules(korean).Where(rule => rule.Required).Select(rule => rule.Id));
        Assert.True(ClientPolicyActivation.IsMandatory(catalog.Single(plugin => plugin.Id == "bubblefighter.fantasy.shop-unlock"), korean));
    }

    [Fact]
    public void ExistingShopSelectionRetainsItsProfileRequirementAfterIdentityMigration()
    {
        var root = TestRepository.StartDirectory().FullName;
        var game = GameCatalog.Load(Path.Combine(root, "games")).Single(game => game.Id == "bubblefighter");
        var build = game.Builds.Single(build => build.Id == "kr-1256");
        var packages = PluginCatalog.Load(Path.Combine(game.Folder, "plugins"))
            .Concat(PluginCatalog.Load(Path.Combine(root, "store-plugins", "BubbleFighter"))).ToArray();
        var instance = new FantasyTale.Launcher.Sdk.InstanceDefinition("fixture", "Fixture", game.Id, build.Id, null,
            [new("bubblefighter.fantasy.kr1256-shop-gates", "1.0.0", true, true, new Dictionary<string, object>())]);
        var updated = FilePluginResolver.EnsureComponents(instance, game, build, packages);
        var shop = Assert.Single(updated.Plugins, plugin => plugin.Id == "bubblefighter.fantasy.shop-unlock");
        Assert.True(shop.Enabled);
        Assert.True(shop.Required);
    }

    [Fact]
    public async Task SeparatelyDownloadedAuthenticationRemainsMandatoryInTheInstallationPlan()
    {
        var root = Directory.CreateTempSubdirectory("ft-bundle-plan-").FullName;
        try
        {
            var sources = new StoreSourceStore(Path.Combine(root, "sources"));
            foreach (var source in sources.List()) sources.Save(source with { Enabled = false });
            var replies = new ConcurrentQueue<JsonNode>();
            await using var backend = new LauncherBackend(Path.Combine(TestRepository.StartDirectory().FullName, "games"), root);
            var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
            await bridge.HandleAsync("""{"id":1,"method":"plugins.installationPlan","params":{"game":"bubblefighter","build":"kr-1256"}}""", default);
            var reply = replies.Single(item => item["id"]?.GetValue<int>() == 1);
            Assert.Null(reply["error"]);
            var plan = reply["result"]!.AsArray();
            Assert.True(plan.Single(item => item!["id"]!.GetValue<string>() == "bubblefighter.fantasy.login-fix-kr")!["required"]!.GetValue<bool>());
            Assert.True(plan.Single(item => item!["id"]!.GetValue<string>() == "bubblefighter.fantasy.shop-unlock")!["required"]!.GetValue<bool>());
        }
        finally { Directory.Delete(root, true); }
    }
}
