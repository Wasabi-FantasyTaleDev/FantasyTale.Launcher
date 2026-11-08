using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class RememberedLoginBridgeTests
{
    [Fact]
    public async Task RememberedCredentialsRemainServerScopedAfterHostRestart()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ft-password-bridge-").FullName;
        try
        {
            var games = Path.Combine(root, "games"); var folder = Path.Combine(games, "Fixture"); Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "game.toml"), "schema=1\n[game]\nid='fixture'\nstatus='enabled'\nmodule='Fixture'\nhook_host='external'\nname={en='Fixture'}\n");
            var data = Path.Combine(root, "data"); var replies = new ConcurrentQueue<JsonNode>(); long sequence = 0;
            BridgeRouter? bridge = null;
            async Task<JsonNode?> Call(string method, object? args = null)
            {
                var id = ++sequence; await bridge!.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = args }), default);
                var reply = replies.Single(row => row["id"]?.GetValue<long>() == id); Assert.Null(reply["error"]); return reply["result"];
            }
            await using (var first = new LauncherBackend(games, data))
            {
                bridge = new(raw => replies.Enqueue(JsonNode.Parse(raw)!)); first.Register(bridge);
                await Call("servers.save", new { id = "one", game = "fixture", name = "One", apiUrl = "https://one.example/" });
                await Call("servers.save", new { id = "two", game = "fixture", name = "Two", apiUrl = "https://two.example/" });
                Assert.Null(await Call("accounts.rememberedLogin", new { serverId = "one" }));
                new ProtectedRememberedLoginStore(data).Save("one", "https://one.example/", "alice", "fixture-password", true);
            }
            await using var reopened = new LauncherBackend(games, data);
            bridge = new(raw => replies.Enqueue(JsonNode.Parse(raw)!)); reopened.Register(bridge);
            var saved = await Call("accounts.rememberedLogin", new { serverId = "one" });
            Assert.Equal("alice", saved!["username"]!.GetValue<string>());
            Assert.Equal("fixture-password", saved["password"]!.GetValue<string>());
            Assert.Null(await Call("accounts.rememberedLogin", new { serverId = "two" }));
            Assert.Null(await Call("accounts.current", new { serverId = "one" }));
            Assert.DoesNotContain("fixture-password", (await Call("servers.list"))!.ToJsonString());
            await Call("servers.save", new { id = "one", game = "fixture", name = "Edited", apiUrl = "https://replacement.example/" });
            Assert.Null(await Call("accounts.rememberedLogin", new { serverId = "one" }));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
