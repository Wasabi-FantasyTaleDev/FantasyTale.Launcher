using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ClientRemovalBridgeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BridgeRequiresExplicitModeAndPreservesUnlistedFiles(bool deleteFiles)
    {
        var root = Directory.CreateTempSubdirectory("ft-removal-bridge-").FullName;
        try
        {
            var games = Path.Combine(root, "games");
            var folder = Directory.CreateDirectory(Path.Combine(games, "Fixture", "builds")).FullName;
            File.WriteAllText(Path.Combine(folder, "..", "game.toml"), """
                schema = 1
                [game]
                id = "fixture"
                status = "enabled"
                module = "Fixture"
                hook_host = "external"
                name = { en = "Fixture" }
                """);
            File.WriteAllText(Path.Combine(folder, "test-1.toml"), """
                schema = 1
                [build]
                id = "test-1"
                game = "fixture"
                branch = "test"
                protocol_version = 1
                executable = "client.exe"
                name = { en = "Fixture" }
                [launch]
                login = "none"
                """);
            var data = Path.Combine(root, "data");
            var store = new InstanceStore(Path.Combine(data, "instances"));
            await store.CreateAsync(new("sample", "Sample", "fixture", "test-1", null, []));
            var client = store.ClientFolder("sample");
            File.WriteAllText(Path.Combine(client, "client.exe"), "imported game");
            File.WriteAllText(Path.Combine(client, "personal.txt"), "personal");
            await using var backend = new LauncherBackend(games, data);
            var replies = new ConcurrentQueue<JsonNode>();
            var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
            await bridge.HandleAsync("""{"id":1,"method":"instances.delete","params":{"id":"sample"}}""", default);
            Assert.NotNull(replies.Single(row => row["id"]?.GetValue<int>() == 1)["error"]);
            Assert.Single(store.List());
            await bridge.HandleAsync("{\"id\":2,\"method\":\"instances.delete\",\"params\":{\"id\":\"sample\",\"deleteFiles\":" + (deleteFiles ? "true" : "false") + "}}", default);
            var reply = replies.Single(row => row["id"]?.GetValue<int>() == 2);
            if (deleteFiles)
            {
                Assert.Equal("client_remove_inventory", reply["error"]!.GetValue<string>()); Assert.Single(store.List());
            }
            else
            {
                Assert.Null(reply["error"]); Assert.Equal(0, reply["result"]!["deletedFiles"]!.GetValue<int>());
                Assert.Empty(store.List());
                Assert.Contains(replies, row => row["event"]?.GetValue<string>() == "instances.changed");
            }
            Assert.Equal("imported game", File.ReadAllText(Path.Combine(client, "client.exe")));
            Assert.Equal("personal", File.ReadAllText(Path.Combine(client, "personal.txt")));
        }
        finally { Directory.Delete(root, true); }
    }
}
