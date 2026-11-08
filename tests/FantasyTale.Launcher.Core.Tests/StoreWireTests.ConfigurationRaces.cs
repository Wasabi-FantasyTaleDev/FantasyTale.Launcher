using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class StoreWireTests
{
    [Theory]
    [InlineData("instances.update")]
    public async Task DelayedConfigurationCannotOverwriteSettingsSavedDuringCatalogDiscovery(string method)
    {
        var root = Directory.CreateTempSubdirectory("ft-settings-race-").FullName;
        try
        {
            var games = Path.Combine(root, "games");
            var game = Path.Combine(games, "Fixture");
            Directory.CreateDirectory(Path.Combine(game, "builds"));
            Directory.CreateDirectory(Path.Combine(game, "plugins", "optional"));
            await File.WriteAllTextAsync(Path.Combine(game, "game.toml"), "schema = 1\n[game]\nid = \"fixture\"\nstatus = \"enabled\"\nmodule = \"Fixture\"\nhook_host = \"external\"\nname = { en = \"Fixture\" }\n");
            await File.WriteAllTextAsync(Path.Combine(game, "builds", "test-1.toml"), "schema = 1\n[build]\nid = \"test-1\"\ngame = \"fixture\"\nbranch = \"test\"\nprotocol_version = 1\nexecutable = \"client.exe\"\nname = { en = \"Fixture\" }\n[launch]\nlogin = \"none\"\n");
            await File.WriteAllTextAsync(Path.Combine(game, "plugins", "optional", "plugin.toml"), UpgradePlugin);
            var documents = new ConcurrentDictionary<string, byte[]>();
            documents["v1/catalog"] = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = 1, game = "fixture", name = "Fixture", supportsPlugins = true,
                registrationEnabled = false, registrationFields = Array.Empty<object>(), instances = Array.Empty<object>()
            });
            await using var server = new WireServer(documents);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            server.BeforeGet = async (path, ct) =>
            {
                if (path != "v1/catalog") return;
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            };
            var data = Path.Combine(root, "data");
            await using var backend = new LauncherBackend(games, data);
            var replies = new ConcurrentQueue<JsonNode>();
            var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!));
            backend.Register(bridge);
            long sequence = 0;
            async Task<JsonNode> Call(string name, object args)
            {
                var id = Interlocked.Increment(ref sequence);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method = name, @params = args }), timeout.Token);
                return replies.Single(item => item["id"]?.GetValue<long>() == id);
            }
            Assert.Null((await Call("instances.create", new { id = "first", name = "Original", game = "fixture", build = "test-1" }))["error"]);
            Assert.Null((await Call("servers.save", new { id = "fixture", name = "Fixture", game = "fixture", apiUrl = server.Address }))["error"]);
            object edit = new { id = "first", name = "Delayed", serverId = "fixture" };
            var pending = Call(method, edit);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var store = new InstanceStore(Path.Combine(data, "instances"));
                var original = store.Load("first");
                store.Save(original with { Name = "Newer settings" });
            }
            finally { release.TrySetResult(); }
            var rejected = await pending;
            Assert.Contains("settings changed", rejected["error"]!.GetValue<string>(), StringComparison.Ordinal);
            var persisted = new InstanceStore(Path.Combine(data, "instances")).Load("first");
            Assert.Equal("Newer settings", persisted.Name);
            Assert.Empty(persisted.Plugins);
            Assert.Null((await Call(method, edit))["error"]);
            persisted = new InstanceStore(Path.Combine(data, "instances")).Load("first");
            Assert.Equal("Delayed", persisted.Name);
        }
        finally { Directory.Delete(root, true); }
    }
}
