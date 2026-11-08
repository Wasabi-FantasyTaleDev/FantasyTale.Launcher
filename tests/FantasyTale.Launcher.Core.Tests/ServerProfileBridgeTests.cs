using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ServerProfileBridgeTests
{
    [Fact]
    public async Task OptionalIdentityIsGeneratedAndNewServerCollisionsCannotOverwriteSavedSettings()
    {
        var root = Directory.CreateTempSubdirectory("ft-server-profile-bridge-").FullName;
        try
        {
            var games = Path.Combine(root, "games"); var folder = Path.Combine(games, "Fixture"); Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "game.toml"), "schema=1\n[game]\nid='fixture'\nstatus='enabled'\nmodule='Fixture'\nhook_host='external'\nname={en='Fixture'}\n");
            var data = Path.Combine(root, "data"); var replies = new ConcurrentQueue<JsonNode>(); long sequence = 0;
            await using var backend = new LauncherBackend(games, data);
            var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
            async Task<JsonNode> Call(object args)
            {
                var id = ++sequence;
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method = "servers.save", @params = args }), default);
                return replies.Single(row => row["id"]?.GetValue<long>() == id);
            }
            var automatic = await Call(new { game = "fixture", name = "Automatic", apiUrl = "https://game.example/", createOnly = true });
            Assert.Null(automatic["error"]);
            Assert.Equal("game_example_443", automatic["result"]!["id"]!.GetValue<string>());
            var file = Path.Combine(data, "servers", "game_example_443.toml");
            var original = await File.ReadAllTextAsync(file);
            var collision = await Call(new { id = "GAME_EXAMPLE_443", game = "fixture", name = "Replacement", apiUrl = "https://other.example/", createOnly = true });
            Assert.NotNull(collision["error"]);
            Assert.Equal(original, await File.ReadAllTextAsync(file));
            var endpoint = await Call(new { id = "", game = "fixture", name = "Manual", endpoint = new { host = "127.0.0.1", port = 8080 }, createOnly = true });
            Assert.Null(endpoint["error"]);
            Assert.True(File.Exists(Path.Combine(data, "servers", "127_0_0_1_8080.toml")));
            var custom = await Call(new { id = "Community_Server-2", game = "fixture", name = "Custom", apiUrl = "https://custom.example/", createOnly = true });
            Assert.Null(custom["error"]);
            Assert.True(File.Exists(Path.Combine(data, "servers", "Community_Server-2.toml")));
            foreach (var invalid in new[] { "has space", "server.example", "CON", "../outside", "caf\u00e9", "\u670d\u52a1\u5668" })
                Assert.NotNull((await Call(new { id = invalid, game = "fixture", name = "Invalid", apiUrl = "https://custom.example/", createOnly = true }))["error"]);
        }
        finally { Directory.Delete(root, true); }
    }
}
