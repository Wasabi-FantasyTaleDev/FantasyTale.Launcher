using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecommendationDefaultSelectionReachesInstallationAndFirstRunCards(bool? selected)
    {
        var game = Game();
        var flag = selected is { } value ? "default_selected = " + (value ? "true" : "false") + "\n" : "";
        File.AppendAllText(Path.Combine(game.Folder, "builds", "test-1.toml"),
            "\n[[client_policy_rules]]\nplugin=\"fixture.optional\"\nid=\"offer\"\ntype=\"recommend\"\nsources=[]\n" + flag);
        var data = Path.Combine(_root, "recommend-defaults");
        var store = new InstanceStore(Path.Combine(data, "instances"));
        await store.CreateAsync(new("client", "Client", game.Id, "test-1", null, []));
        File.WriteAllText(Path.Combine(store.ClientFolder("client"), "client.exe"), "fixture");
        await using var backend = new LauncherBackend(Path.Combine(_root, "games"), data);
        var replies = new System.Collections.Concurrent.ConcurrentQueue<JsonNode>();
        var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
        long id = 0;
        foreach (var method in new[] { "plugins.installationPlan", "clientPolicy.claimPlugins" })
        {
            var arguments = method == "plugins.installationPlan" ? (object)new { game = game.Id, build = "test-1" } : new { instanceId = "client" };
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id = ++id, method, @params = arguments }), default);
            var reply = replies.Single(row => row["id"]?.GetValue<long>() == id);
            Assert.Null(reply["error"]);
            var card = Assert.Single(reply["result"]!.AsArray())!;
            Assert.Equal("fixture.optional", card["id"]!.GetValue<string>());
            Assert.False(card["required"]!.GetValue<bool>());
            Assert.Equal(selected == true, card["defaultSelected"]!.GetValue<bool>());
        }
    }
}
