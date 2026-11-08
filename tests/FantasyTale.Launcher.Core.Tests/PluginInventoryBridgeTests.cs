using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class PluginInventoryBridgeTests
{
    [Theory]
    [InlineData("")]
    [InlineData(",\"params\":null")]
    [InlineData(",\"params\":{}")]
    public async Task WholeLibraryInventoryAcceptsOmittedOptionalFilters(string parameters)
    {
        var root = Directory.CreateTempSubdirectory("ft-plugin-inventory-").FullName;
        try
        {
            var games = Path.Combine(root, "games");
            var folder = Path.Combine(games, "Fixture");
            var plugins = Path.Combine(folder, "plugins", "optional");
            Directory.CreateDirectory(plugins);
            await File.WriteAllTextAsync(Path.Combine(folder, "game.toml"), """
                schema = 1
                [game]
                id = "fixture"
                status = "enabled"
                module = "Fixture"
                hook_host = "external"
                name = { en = "Fixture" }
                """);
            await File.WriteAllTextAsync(Path.Combine(plugins, "plugin.toml"), """
                schema = 1
                [plugin]
                id = "fixture.optional"
                game = "fixture"
                version = "1.0.0"
                name = { en = "Optional" }
                kind = "patch"
                runtime = ">=3.0.0 <4.0.0"
                [compatibility]
                builds = ["test-1"]
                [[patches]]
                id = "fixture"
                locate = { pattern = "AA BB CC" }
                expect = "AA"
                write = "90"
                """);
            var replies = new ConcurrentQueue<JsonNode>();
            await using var backend = new LauncherBackend(games, Path.Combine(root, "data"));
            var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!));
            backend.Register(bridge);
            await bridge.HandleAsync("{\"id\":1,\"method\":\"plugins.list\"" + parameters + "}", default);
            var reply = replies.Single(row => row["id"]?.GetValue<int>() == 1);
            Assert.Null(reply["error"]);
            var plugin = Assert.Single(reply["result"]!.AsArray())!;
            Assert.Equal("fixture.optional", plugin["id"]!.GetValue<string>());
            Assert.True(plugin["builtIn"]!.GetValue<bool>());
            Assert.Null(plugin["selection"]);

            await bridge.HandleAsync("{\"id\":2,\"method\":\"plugins.list\",\"params\":[]}", default);
            Assert.NotNull(replies.Single(row => row["id"]?.GetValue<int>() == 2)["error"]);
            await bridge.HandleAsync("{\"id\":3,\"method\":\"plugins.list\",\"params\":{\"unexpected\":true}}", default);
            Assert.NotNull(replies.Single(row => row["id"]?.GetValue<int>() == 3)["error"]);
        }
        finally { Directory.Delete(root, true); }
    }
}
