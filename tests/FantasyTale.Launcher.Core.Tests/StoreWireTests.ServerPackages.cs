using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class StoreWireTests
{
    [Theory]
    [InlineData("http")]
    [InlineData("alist")]
    public async Task ServerPackagesInstallWithoutClientsRemainSeparateAndRespectHostingLease(string kind)
    {
        var root = Directory.CreateTempSubdirectory("ft-server-store-wire-").FullName;
        try
        {
            var games = Path.Combine(root, "games"); var gameFolder = Path.Combine(games, "Fixture");
            Directory.CreateDirectory(Path.Combine(gameFolder, "builds"));
            await File.WriteAllTextAsync(Path.Combine(gameFolder, "game.toml"), "schema = 1\n[game]\nid = \"fixture\"\nstatus = \"enabled\"\nmodule = \"Fixture\"\nhook_host = \"external\"\nname = { en = \"Fixture\" }\n");
            await File.WriteAllTextAsync(Path.Combine(gameFolder, "builds/test-1.toml"), "schema = 1\n[build]\nid = \"test-1\"\ngame = \"fixture\"\nbranch = \"test\"\nprotocol_version = 1\nexecutable = \"client.exe\"\nhosting_adapter = \"fixture\"\nserver_profile = \"test-1\"\nname = { en = \"Fixture\" }\n[launch]\nlogin = \"none\"\n");
            var documents = new ConcurrentDictionary<string, byte[]>();
            string Publish(string version, byte[] payload)
            {
                var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
                var manifest = Encoding.UTF8.GetBytes($"schema = 1\n[release]\nid = \"fixture.server\"\ngame = \"fixture\"\nbuilds = [\"test-1\"]\nversion = \"{version}\"\n[[files]]\npath = \"server.exe\"\nsource_path = \"objects/{hash}\"\nsize = {payload.Length}\nsha256 = \"{hash}\"\n");
                var manifestHash = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
                documents["objects/" + hash] = payload; documents["manifests/" + manifestHash] = manifest;
                documents["FantasyTale/source.toml"] = Encoding.UTF8.GetBytes($"schema = 1\n[catalog]\nid = \"fixture\"\nname = {{ en = \"Fixture\" }}\n[[packages]]\nid = \"fixture.server\"\nkind = \"server\"\ngame = \"fixture\"\nbuilds = [\"test-1\"]\nversion = \"{version}\"\nname = {{ en = \"Fixture server\" }}\npublisher = \"Fixture\"\nsize = {payload.Length}\nmanifest = \"manifests/{manifestHash}\"\nmanifest_sha256 = \"{manifestHash}\"\n");
                return manifestHash;
            }
            var firstHash = Publish("1.0.0", [1, 2, 3]);
            await using var wire = new WireServer(documents);
            var dataRoot = Path.Combine(root, "data");
            await using (var backend = new LauncherBackend(games, dataRoot))
            {
                var replies = new ConcurrentQueue<JsonNode>(); var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
                long sequence = 0;
                async Task<JsonNode?> Call(string method, object? arguments = null)
                {
                    var id = ++sequence; await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), default);
                    var reply = replies.Single(item => item["id"]?.GetValue<long>() == id);
                    Assert.Null(reply["error"]); return reply["result"];
                }
                async Task<JsonNode> AwaitTask(string taskId, string stage)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    while (true)
                    {
                        var task = (await Call("downloads.list"))!.AsArray().Single(t => t!["id"]!.GetValue<string>() == taskId)!;
                        if (task["stage"]!.GetValue<string>() == stage) return task;
                        if (stage == "completed") Assert.NotEqual("failed", task["stage"]!.GetValue<string>());
                        await Task.Delay(20, timeout.Token);
                    }
                }
                await Call("store.saveSource", new { id = "fixture", name = "Fixture", kind, baseUrl = wire.Address, catalogPath = "FantasyTale/source.toml", enabled = true });
                var queued = await Call("store.installServer", new { sourceId = "fixture", packageId = "fixture.server", version = "1.0.0", manifestSha256 = firstHash });
                await AwaitTask(queued!["id"]!.GetValue<string>(), "completed");
                var installed = Assert.Single((await Call("hosting.packages"))!.AsArray())!;
                var firstFolder = installed["directory"]!.GetValue<string>();
                Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(firstFolder, "server.exe")));
                Assert.Empty((await Call("instances.list"))!.AsArray());
                Assert.Empty((await Call("hosting.list"))!["sessions"]!.AsArray());
                await Call("downloads.clearFinished");
                string retryId;
                using (var lease = new FileStream(Path.Combine(firstFolder, ".fantasytale-host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    var blocked = await Call("store.installServer", new { sourceId = "fixture", packageId = "fixture.server", version = "1.0.0", manifestSha256 = firstHash });
                    retryId = blocked!["id"]!.GetValue<string>();
                    var failed = await AwaitTask(retryId, "failed"); Assert.Equal("busy", failed["failure"]!.GetValue<string>());
                    Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(firstFolder, "server.exe")));
                }
                await Call("downloads.action", new { id = retryId, action = "retry" }); await AwaitTask(retryId, "completed");
                var nextHash = Publish("2.0.0", [4, 5, 6]);
                var next = await Call("store.installServer", new { sourceId = "fixture", packageId = "fixture.server", version = "2.0.0", manifestSha256 = nextHash });
                await AwaitTask(next!["id"]!.GetValue<string>(), "completed");
                var packages = (await Call("hosting.packages"))!.AsArray(); Assert.Equal(2, packages.Count);
                var nextFolder = packages.Single(p => p!["package"]!["version"]!.GetValue<string>() == "2.0.0")!["directory"]!.GetValue<string>();
                Assert.NotEqual(firstFolder, nextFolder);
                Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(Path.Combine(nextFolder, "server.exe")));
                Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(firstFolder, "server.exe")));
                await Call("store.saveSource", new { id = "alias", name = "Alias", kind, baseUrl = wire.Address, catalogPath = "FantasyTale/source.toml", enabled = true });
                var alias = await Call("store.installServer", new { sourceId = "alias", packageId = "fixture.server", version = "2.0.0", manifestSha256 = nextHash });
                await AwaitTask(alias!["id"]!.GetValue<string>(), "completed");
                var aliasPackage = (await Call("hosting.packages"))!.AsArray().Single(p => p!["sourceId"]!.GetValue<string>() == "alias")!;
                Assert.NotEqual(nextFolder, aliasPackage["directory"]!.GetValue<string>());
                await Call("downloads.clearFinished");
            }
            await using var reopened = new LauncherBackend(games, dataRoot);
            var restored = new ConcurrentQueue<JsonNode>(); var restoredBridge = new BridgeRouter(raw => restored.Enqueue(JsonNode.Parse(raw)!)); reopened.Register(restoredBridge);
            await restoredBridge.HandleAsync("{\"id\":1,\"method\":\"hosting.packages\"}", default);
            Assert.Equal(3, restored.Single()["result"]!.AsArray().Count);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
