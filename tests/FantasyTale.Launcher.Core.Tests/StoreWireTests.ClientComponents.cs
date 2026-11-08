using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class StoreWireTests
{
    [Fact]
    public async Task InstallationComponentsShareDownloadsAndActivateAfterTheClientLeaseBecomesAvailable()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "component-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var games = Path.Combine(root, "games"); var folder = Directory.CreateDirectory(Path.Combine(games, "Fixture", "builds")).Parent!.FullName;
            File.WriteAllText(Path.Combine(folder, "game.toml"), "schema = 1\n[game]\nid = \"fixture\"\nstatus = \"enabled\"\nmodule = \"Fixture\"\nhook_host = \"external\"\nname = { en = \"Fixture\" }\nstore_managed_plugins = [\"fixture.optional\"]\n");
            File.WriteAllText(Path.Combine(folder, "builds", "test-1.toml"), "schema = 1\n[build]\nid = \"test-1\"\ngame = \"fixture\"\nbranch = \"test\"\nprotocol_version = 1\nexecutable = \"client.exe\"\nname = { en = \"Fixture\" }\n[launch]\nlogin = \"none\"\n[[client_policy_rules]]\nplugin = \"fixture.optional\"\nid = \"offer\"\ntype = \"recommend\"\nsources = []\n");
            var documents = new ConcurrentDictionary<string, byte[]>();
            var plugin = Encoding.UTF8.GetBytes(UpgradePlugin); var client = Encoding.UTF8.GetBytes("fixture client payload");
            byte[] Manifest(string id, string file, byte[] payload) => Encoding.UTF8.GetBytes($"schema = 1\n[release]\nid = \"{id}\"\ngame = \"fixture\"\nbuilds = [\"test-1\"]\nversion = \"1.0.0\"\n[[files]]\npath = \"{file}\"\nsource_path = \"{file}\"\nsize = {payload.Length}\nsha256 = \"{Hash(payload)}\"\n");
            var pluginManifest = Manifest("fixture.optional", "plugin.toml", plugin); var clientManifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Manifest("fixture.client", "client.exe", client)).Replace("builds = [\"test-1\"]", "build = \"test-1\"", StringComparison.Ordinal));
            string Package(string id, string kind, string manifest, byte[] bytes, int size) => $"\n[[packages]]\nid = \"{id}\"\nkind = \"{kind}\"\ngame = \"fixture\"\nbuilds = [\"test-1\"]\nversion = \"1.0.0\"\nname = {{ en = \"Fixture\" }}\npublisher = \"Fixture\"\nsize = {size}\nmanifest = \"{manifest}\"\nmanifest_sha256 = \"{Hash(bytes)}\"\n";
            documents["plugin.toml"] = plugin; documents["client.exe"] = client;
            documents["plugin-release.toml"] = pluginManifest; documents["client-release.toml"] = clientManifest;
            documents["FantasyTale/source.toml"] = Encoding.UTF8.GetBytes("schema = 1\n[catalog]\nid = \"fixture\"\nname = { en = \"Fixture\" }\n"
                + Package("fixture.optional", "plugin", "plugin-release.toml", pluginManifest, plugin.Length)
                + Package("fixture.client", "client", "client-release.toml", clientManifest, client.Length));
            await using var server = new WireServer(documents);
            var data = Path.Combine(root, "data"); var sources = new StoreSourceStore(Path.Combine(data, "sources")); var bundled = sources.List().Single(); sources.Save(bundled with { Enabled = false });
            await using var backend = new LauncherBackend(games, data);
            var replies = new ConcurrentQueue<JsonNode>(); var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge); long sequence = 0;
            async Task<JsonNode?> Call(string method, object? args = null)
            {
                var id = ++sequence; await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = args }), default);
                var reply = replies.Single(item => item["id"]?.GetValue<long>() == id);
                Assert.True(reply["error"] is null, $"{method}: {reply["error"]}"); return reply["result"];
            }
            await Call("store.saveSource", new { id = "fixture", name = "Fixture", kind = "http", baseUrl = server.Address, catalogPath = "FantasyTale/source.toml", enabled = true });
            await Call("instances.create", new { id = "client", game = "fixture", build = "test-1", name = "Fixture" });
            var components = (await Call("plugins.installationComponents", new { instanceId = "client", ids = (string[])["fixture.optional"], languages = Array.Empty<string>() }))!.AsArray();
            Assert.Single(components);
            await Call("store.installClient", new { sourceId = "fixture", packageId = "fixture.client", version = "1.0.0", manifestSha256 = Hash(clientManifest), instanceId = "client" });
            var store = new InstanceStore(Path.Combine(data, "instances"));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var tasks = (await Call("downloads.list"))!.AsArray();
                Assert.True(tasks.All(task => task!["stage"]!.GetValue<string>() != "failed"), tasks.ToJsonString());
                if (tasks.All(task => task!["stage"]!.GetValue<string>() == "completed") && store.Load("client").Plugins.Any(item => item.Id == "fixture.optional" && item.Enabled)) break;
                await Task.Delay(20, deadline.Token);
            }
            Assert.Equal(client, File.ReadAllBytes(Path.Combine(store.ClientFolder("client"), "client.exe")));
        }
        finally { Directory.Delete(root, true); }
    }
}
