using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class StoreWireTests
{
    [Theory]
    [InlineData("http", false)]
    [InlineData("alist", false)]
    [InlineData("http", true)]
    [InlineData("alist", true)]
    public async Task ServerSuggestedClientUsesTheSharedInstallAndUpdatePipeline(string kind, bool fileSync)
    {
        var root = Directory.CreateTempSubdirectory("ft-store-wire-").FullName;
        try
        {
            var games = Path.Combine(root, "games"); var folder = Path.Combine(games, "Fixture"); Directory.CreateDirectory(Path.Combine(folder, "builds"));
            await File.WriteAllTextAsync(Path.Combine(folder, "game.toml"), "schema = 1\n[game]\nid = \"fixture\"\nstatus = \"enabled\"\nmodule = \"Fixture\"\nhook_host = \"external\"\nsupports_plugins = false\nname = { en = \"Fixture\" }\n");
            await File.WriteAllTextAsync(Path.Combine(folder, "builds", "test-1.toml"), "schema = 1\n[build]\nid = \"test-1\"\ngame = \"fixture\"\nbranch = \"test\"\nprotocol_version = 1\nexecutable = \"client.exe\"\nname = { en = \"Fixture\" }\n[launch]\nlogin = \"none\"\n");
            byte[] data = [1, 4, 9, 16, 25];
            var digest = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            var manifest = Encoding.UTF8.GetBytes($"schema = 1\n[release]\nid = \"fixture.client\"\ngame = \"fixture\"\nbuild = \"test-1\"\nversion = \"1\"\n[[files]]\npath = \"client.exe\"\nsource_path = \"client.exe\"\nsize = 5\nsha256 = \"{digest}\"\n");
            var manifestHash = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
            var catalog = Encoding.UTF8.GetBytes($"schema = 1\n[catalog]\nid = \"fixture\"\nname = {{ en = \"Fixture\" }}\n[[packages]]\nid = \"fixture.client\"\nkind = \"client\"\ngame = \"fixture\"\nbuilds = [\"test-1\"]\nversion = \"1\"\nname = {{ en = \"Fixture\" }}\npublisher = \"Fixture\"\nsize = 5\nmanifest = \"manifest.toml\"\nmanifest_sha256 = \"{manifestHash}\"\n");
            catalog = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(catalog) + "revision = 1\n");
            var documents = new ConcurrentDictionary<string, byte[]>(new Dictionary<string, byte[]> { ["FantasyTale/source.toml"] = catalog, ["manifest.toml"] = manifest, ["client.exe"] = data });
            await using var server = new WireServer(documents);
            await using var backend = new LauncherBackend(games, Path.Combine(root, "data"));
            var replies = new ConcurrentQueue<JsonNode>(); var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
            long sequence = 0;
            async Task<JsonNode?> Call(string method, object? arguments = null)
            {
                var id = ++sequence;
                using var callDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), callDeadline.Token);
                var reply = replies.Single(item => item["id"]?.GetValue<long>() == id);
                Assert.Null(reply["error"]); return reply["result"];
            }
            documents["v1/catalog"] = JsonSerializer.SerializeToUtf8Bytes(new { schema = 1, game = "fixture", name = "Fixture", registrationEnabled = false, registrationFields = Array.Empty<object>(),
                instances = new[] { new { id = "test", build = "test-1", name = new { en = "Fixture" }, downloadSource = new { kind, baseUrl = server.Address, catalogPath = "FantasyTale/source.toml", packageId = "fixture.client" } } } });
            await Call("servers.save", new { id = "fixture", name = "Fixture", game = "fixture", apiUrl = server.Address });
            var sourcesBefore = (await Call("store.sources"))!.AsArray().Count;
            var release = await Call("servers.clientRelease", new { serverId = "fixture", presetId = "test" });
            Assert.Equal(sourcesBefore, (await Call("store.sources"))!.AsArray().Count);
            var sourceId = release!["source"]!["id"]!.GetValue<string>();
            Assert.StartsWith("server-", sourceId);
            await Call("instances.create", new { id = "fixture", name = "Fixture", game = "fixture", build = "test-1" });
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id = ++sequence, method = "servers.installClient", @params = new { serverId = "fixture", presetId = "test", sourceId, manifestSha256 = new string('0', 64), instanceId = "fixture" } }), default);
            Assert.NotNull(replies.Single(r => r["id"]?.GetValue<long>() == sequence)["error"]);
            Assert.Equal(sourcesBefore, (await Call("store.sources"))!.AsArray().Count);
            async Task Reject(string method, object arguments)
            {
                var id = ++sequence;
                using var callDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), callDeadline.Token);
                Assert.NotNull(replies.Single(r => r["id"]?.GetValue<long>() == id)["error"]);
                Assert.Empty((await Call("downloads.list"))!.AsArray());
            }
            var apiCatalog = documents["v1/catalog"];
            var withoutDownload = JsonNode.Parse(apiCatalog)!;
            withoutDownload["instances"]![0]!.AsObject().Remove("downloadSource");
            documents["v1/catalog"] = JsonSerializer.SerializeToUtf8Bytes(withoutDownload);
            Assert.Null(await Call("servers.clientRelease", new { serverId = "fixture", presetId = "test" }));
            documents["v1/catalog"] = apiCatalog;
            foreach (var (field, value) in new[] { ("kind", "plugin"), ("game", "other"), ("builds", "other-1") })
            {
                var original = field == "builds" ? "builds = [\"test-1\"]" : $"{field} = \"{(field == "kind" ? "client" : "fixture")}\"";
                var replacement = field == "builds" ? "builds = [\"other-1\"]" : $"{field} = \"{value}\"";
                documents["FantasyTale/source.toml"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(catalog).Replace(original, replacement, StringComparison.Ordinal));
                await Reject("servers.clientRelease", new { serverId = "fixture", presetId = "test" });
            }
            documents["FantasyTale/source.toml"] = catalog;
            var changedSource = JsonNode.Parse(apiCatalog)!;
            changedSource["instances"]![0]!["downloadSource"]!["catalogPath"] = "other/FantasyTale/source.toml";
            documents["other/FantasyTale/source.toml"] = catalog;
            documents["v1/catalog"] = JsonSerializer.SerializeToUtf8Bytes(changedSource);
            await Reject("servers.installClient", new { serverId = "fixture", presetId = "test", sourceId, manifestSha256 = manifestHash, instanceId = "fixture" });
            documents["v1/catalog"] = apiCatalog;
            foreach (var badPath in new[] { "../source.toml", "https://example.invalid/source.toml" })
            {
                var invalid = JsonNode.Parse(apiCatalog)!;
                invalid["instances"]![0]!["downloadSource"]!["catalogPath"] = badPath;
                documents["v1/catalog"] = JsonSerializer.SerializeToUtf8Bytes(invalid);
                await Reject("servers.clientRelease", new { serverId = "fixture", presetId = "test" });
            }
            documents["v1/catalog"] = apiCatalog;
            var duplicate = Encoding.UTF8.GetString(catalog);
            documents["FantasyTale/source.toml"] = Encoding.UTF8.GetBytes(duplicate + duplicate[duplicate.IndexOf("[[packages]]", StringComparison.Ordinal)..]);
            await Reject("servers.clientRelease", new { serverId = "fixture", presetId = "test" });
            documents["FantasyTale/source.toml"] = catalog;
            Assert.Equal(sourcesBefore, (await Call("store.sources"))!.AsArray().Count);
            await Call("store.saveSource", new { id = sourceId, name = "Fixture", kind, baseUrl = server.Address, catalogPath = "FantasyTale/source.toml", enabled = false });
            await Reject("servers.installClient", new { serverId = "fixture", presetId = "test", sourceId, manifestSha256 = manifestHash, instanceId = "fixture" });
            Assert.False((await Call("store.sources"))!.AsArray().Single(s => s!["id"]!.GetValue<string>() == sourceId)!["enabled"]!.GetValue<bool>());
            await Call("store.removeSource", new { id = sourceId });
            await Call("servers.installClient", new { serverId = "fixture", presetId = "test", sourceId, manifestSha256 = manifestHash, instanceId = "fixture" });
            Assert.Equal(sourcesBefore + 1, (await Call("store.sources"))!.AsArray().Count);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var tasks = (await Call("downloads.list"))!.AsArray(); var stage = tasks[0]!["stage"]!.GetValue<string>();
                Assert.NotEqual("failed", stage);
                if (stage == "completed") break;
                await Task.Delay(20, deadline.Token);
            }
            var instances = (await Call("instances.list"))!.AsArray(); Assert.True(instances[0]!["clientInstalled"]!.GetValue<bool>());
            Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(instances[0]!["installationPath"]!.GetValue<string>(), "client.exe")));
            Assert.True(kind == "alist" ? server.Resolutions >= 6 : server.Resolutions == 0);
            await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false });
            byte[] updated = [2, 5, 10, 17, 26];
            var updatedManifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest).Replace("version = \"1\"", "version = \"2\"", StringComparison.Ordinal)
                .Replace(digest, Convert.ToHexString(SHA256.HashData(updated)).ToLowerInvariant(), StringComparison.Ordinal));
            if (fileSync)
            {
                updatedManifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(updatedManifest).Replace("version = \"2\"\n", "", StringComparison.Ordinal)
                    .Replace("\npath = \"client.exe\"", "\npath = \"Data/settings.ini\"", StringComparison.Ordinal)
                    .Replace("source_path = \"client.exe\"", "source_path = \"settings.ini\"", StringComparison.Ordinal));
            }
            var updatedHash = Convert.ToHexString(SHA256.HashData(updatedManifest)).ToLowerInvariant();
            documents[fileSync ? "settings.ini" : "client.exe"] = updated; documents["manifest.toml"] = updatedManifest;
            documents["FantasyTale/source.toml"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(catalog).Replace("version = \"1\"", "version = \"2\"", StringComparison.Ordinal)
                .Replace("revision = 1", "revision = 2", StringComparison.Ordinal).Replace(manifestHash, updatedHash, StringComparison.Ordinal));
            if (fileSync) documents["FantasyTale/source.toml"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(documents["FantasyTale/source.toml"])
                .Replace("version = \"2\"\n", "", StringComparison.Ordinal).Replace("revision = 2\n", "", StringComparison.Ordinal));
            var beforeUpdateGets = server.GetPaths.Count;
            var discovery = await Call("updates.check");
            var candidate = Assert.Single(discovery!["clients"]!.AsArray()); Assert.Equal("available", candidate!["status"]!.GetValue<string>());
            Assert.Equal(fileSync ? "" : "2", candidate["version"]!.GetValue<string>());
            var updateTask = await Call("updates.install", new { instanceId = "fixture", manifestSha256 = updatedHash });
            var updateId = updateTask!["id"]!.GetValue<string>();
            while (true)
            {
                var task = (await Call("downloads.list"))!.AsArray().Single(t => t!["id"]!.GetValue<string>() == updateId)!;
                var stage = task["stage"]!.GetValue<string>(); Assert.NotEqual("failed", stage);
                if (stage == "completed") break;
                await Task.Delay(20, deadline.Token);
            }
            var installedPath = instances[0]!["installationPath"]!.GetValue<string>();
            Assert.Equal(updated, await File.ReadAllBytesAsync(Path.Combine(installedPath, fileSync ? "Data/settings.ini" : "client.exe")));
            if (fileSync)
            {
                Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(installedPath, "client.exe")));
                Assert.DoesNotContain("client.exe", server.GetPaths.Skip(beforeUpdateGets));
            }
            Assert.Equal("current", (await Call("updates.check"))!["clients"]![0]!["status"]!.GetValue<string>());
            await backend.DisposeAsync();
            await using var restarted = new LauncherBackend(games, Path.Combine(root, "data"));
            bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); restarted.Register(bridge);
            var history = (await Call("downloads.list"))!.AsArray(); Assert.Equal(2, history.Count);
            Assert.All(history, t => Assert.Equal("completed", t!["stage"]!.GetValue<string>()));
            Assert.Equal(fileSync ? "" : "2", (await Call("updates.check"))!["clients"]![0]!["installedVersion"]!.GetValue<string>());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

}
