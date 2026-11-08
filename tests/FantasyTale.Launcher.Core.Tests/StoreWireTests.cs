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
    private static readonly string[] FixturePluginTargets = ["fixture"];
    [Theory]
    [InlineData("http", false, false)]
    [InlineData("alist", false, false)]
    [InlineData("http", true, false)]
    [InlineData("alist", true, false)]
    [InlineData("http", false, true)]
    [InlineData("alist", true, true)]
    [InlineData("http", false, false, true)]
    [InlineData("http", false, true, true)]
    public async Task StorePluginsInstallThroughTheSharedQueueWithoutChangingClientSelections(string kind, bool zipped, bool preload, bool multipleTargets = false)
    {
        var root = Directory.CreateTempSubdirectory("ft-plugin-wire-").FullName;
        try
        {
            var games = Path.Combine(root, "games"); var folder = Path.Combine(games, "Fixture"); Directory.CreateDirectory(Path.Combine(folder, "builds"));
            await File.WriteAllTextAsync(Path.Combine(folder, "game.toml"), "schema = 1\n[game]\nid = \"fixture\"\nstatus = \"enabled\"\nmodule = \"Fixture\"\nhook_host = \"external\"\nname = { en = \"Fixture\" }\n");
            await File.WriteAllTextAsync(Path.Combine(folder, "builds", "test-1.toml"), "schema = 1\n[build]\nid = \"test-1\"\ngame = \"fixture\"\nbranch = \"test\"\nprotocol_version = 1\nexecutable = \"client.exe\"\nname = { en = \"Fixture\" }\n[launch]\nlogin = \"none\"\n");
            var plugin = Encoding.UTF8.GetBytes("schema = 1\n[plugin]\nid = \"fixture.optional\"\ngame = \"fixture\"\nversion = \"1.0.0\"\nname = { en = \"Optional\" }\nkind = \"patch\"\nruntime = \">=3.0.0 <4.0.0\"\n[compatibility]\nbuilds = [\"test-1\"]\n[[patches]]\nid = \"fixture\"\nlocate = { pattern = \"AA BB CC\" }\nexpect = \"AA\"\nwrite = \"90\"\n");
            var transportPath = zipped ? "optional.zip" : "plugin.toml";
            if (zipped)
            {
                using var buffer = new MemoryStream();
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                { using var file = zip.CreateEntry("optional/plugin.toml").Open(); file.Write(plugin); }
                plugin = buffer.ToArray();
            }
            var hash = Convert.ToHexString(SHA256.HashData(plugin)).ToLowerInvariant();
            var manifest = Encoding.UTF8.GetBytes($"schema = 1\n[release]\nid = \"fixture.optional\"\ngame = \"fixture\"\nbuilds = [\"test-1\"]\nversion = \"1.0.0\"\n[[files]]\npath = \"{transportPath}\"\nsource_path = \"{transportPath}\"\nsize = {plugin.Length}\nsha256 = \"{hash}\"\n");
            if (zipped) manifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest).Replace("[release]", "[release]\nformat = \"zip\"", StringComparison.Ordinal));
            var digest = Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant();
            var catalog = Encoding.UTF8.GetBytes($"schema = 1\n[catalog]\nid = \"fixture\"\nname = {{ en = \"Fixture\" }}\n[[packages]]\nid = \"fixture.optional\"\nkind = \"plugin\"\ngame = \"fixture\"\nbuilds = [\"test-1\"]\nversion = \"1.0.0\"\nname = {{ en = \"Optional\" }}\npublisher = \"Fixture\"\nsize = {plugin.Length}\nmanifest = \"manifest.toml\"\nmanifest_sha256 = \"{digest}\"\n");
            catalog = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(catalog).Replace("name = { en = \"Optional\" }",
                "name = { en = \"Optional\", zh-CN = \"可选插件\", ko = \"선택 플러그인\" }", StringComparison.Ordinal));
            var documents = new ConcurrentDictionary<string, byte[]>(new Dictionary<string, byte[]> { ["FantasyTale/source.toml"] = catalog, ["manifest.toml"] = manifest, [transportPath] = plugin });
            await using var server = new WireServer(documents);
            var dataRoot = Path.Combine(root, "data");
            await using (var backend = new LauncherBackend(games, dataRoot))
            {
                var replies = new ConcurrentQueue<JsonNode>(); var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
                long sequence = 0;
                async Task<JsonNode?> Call(string method, object? args = null)
                {
                    var id = ++sequence; await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = args }), default);
                    var reply = replies.Single(r => r["id"]?.GetValue<long>() == id); Assert.Null(reply["error"]); return reply["result"];
                }
                await Call("store.saveSource", new { id = "fixture", name = "Fixture", kind, baseUrl = server.Address, catalogPath = "FantasyTale/source.toml", enabled = true });
                if (!preload) await Call("instances.create", new { id = "fixture", name = "Fixture", game = "fixture", build = "test-1" });
                var expectedSource = new { kind, baseUrl = server.Address, catalogPath = "FantasyTale/source.toml" };
                var queued = preload && !multipleTargets
                    ? await Call("store.downloadPlugin", new { sourceId = "fixture", expectedSource, packageId = "fixture.optional", version = "1.0.0", manifestSha256 = digest })
                    : multipleTargets
                        ? await Call("store.installPluginTargets", new { sourceId = "fixture", expectedSource, packageId = "fixture.optional", game = "fixture", version = "1.0.0", manifestSha256 = digest, instanceIds = preload ? [] : FixturePluginTargets })
                        : await Call("store.installPlugin", new { sourceId = "fixture", expectedSource, packageId = "fixture.optional", version = "1.0.0", manifestSha256 = digest, instanceId = "fixture" });
                Assert.Equal("可选插件", queued!["target"]!["nameLocalized"]!["zh-CN"]!.GetValue<string>());
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (true)
                {
                    var task = Assert.Single((await Call("downloads.list"))!.AsArray())!;
                    Assert.Equal("선택 플러그인", task["target"]!["nameLocalized"]!["ko"]!.GetValue<string>());
                    Assert.Equal("plugin", task["target"]!["kind"]!.GetValue<string>()); Assert.NotEqual("failed", task["stage"]!.GetValue<string>());
                    if (task["stage"]!.GetValue<string>() == "completed"
                        && (!multipleTargets || preload || (await Call("plugins.pendingUpgrades", new { instanceId = "fixture" }))!["count"]!.GetValue<int>() == 0)) break;
                    await Task.Delay(20, deadline.Token);
                }
                if (preload)
                {
                    Assert.Empty((await Call("instances.list"))!.AsArray());
                    await Call("instances.create", new { id = "fixture", name = "Fixture", game = "fixture", build = "test-1" });
                }
                var installed = Assert.Single((await Call("plugins.list", new { instanceId = "fixture" }))!.AsArray())!;
                Assert.Equal("fixture.optional", installed["id"]!.GetValue<string>());
                if (multipleTargets && !preload) Assert.Equal("1.0.0", installed["selection"]!["version"]!.GetValue<string>());
                else Assert.Null(installed["selection"]);
                var client = Assert.Single((await Call("instances.list"))!.AsArray())!;
                if (multipleTargets && !preload) Assert.Single(client["plugins"]!.AsArray());
                else Assert.Empty(client["plugins"]!.AsArray());
                Assert.False(client["clientInstalled"]!.GetValue<bool>());
                Assert.Equal(kind == "alist" ? 3 : 0, server.Resolutions);
            }
            foreach (var path in Directory.EnumerateFiles(Path.Combine(dataRoot, "downloads"), "*.json"))
                Assert.Null(JsonNode.Parse(await File.ReadAllTextAsync(path))!["Snapshot"]!["Target"]!["NameLocalized"]);
            await using var reopened = new LauncherBackend(games, dataRoot);
            var restored = new ConcurrentQueue<JsonNode>(); var restoredBridge = new BridgeRouter(raw => restored.Enqueue(JsonNode.Parse(raw)!)); reopened.Register(restoredBridge);
            await restoredBridge.HandleAsync("{\"id\":1,\"method\":\"downloads.list\"}", default);
            Assert.Equal("completed", restored.Single()["result"]![0]!["stage"]!.GetValue<string>());
            Assert.Equal("可选插件", restored.Single()["result"]![0]!["target"]!["nameLocalized"]!["zh-CN"]!.GetValue<string>());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("http", false)]
    [InlineData("alist", false)]
    [InlineData("http", true)]
    [InlineData("alist", true)]
    public async Task StoreBridgeInstallsTheSameReleaseOverActualHttpForBothSources(string kind, bool fileSync)
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
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), default);
                var reply = replies.Single(item => item["id"]?.GetValue<long>() == id);
                Assert.Null(reply["error"]); return reply["result"];
            }
            await Call("store.saveSource", new { id = "fixture", name = "Fixture", kind, baseUrl = server.Address, catalogPath = "FantasyTale/source.toml", enabled = true });
            var listed = await Call("store.catalog", new { id = "fixture" }); Assert.Equal("fixture", listed!["id"]!.GetValue<string>());
            await Call("instances.create", new { id = "fixture", name = "Fixture", game = "fixture", build = "test-1", checkIntegrity = !fileSync });
            await Call("store.installClient", new { sourceId = "fixture", packageId = "fixture.client", version = "1", manifestSha256 = manifestHash, instanceId = "fixture" });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var tasks = (await Call("downloads.list"))!.AsArray(); var stage = tasks[0]!["stage"]!.GetValue<string>();
                Assert.True(stage != "failed", tasks[0]!.ToJsonString());
                if (stage == "completed") break;
                await Task.Delay(20, deadline.Token);
            }
            var instances = (await Call("instances.list"))!.AsArray(); Assert.True(instances[0]!["clientInstalled"]!.GetValue<bool>());
            Assert.Equal(!fileSync, instances[0]!["checkIntegrity"]!.GetValue<bool>());
            Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(instances[0]!["installationPath"]!.GetValue<string>(), "client.exe")));
            Assert.Equal(kind == "alist" ? 3 : 0, server.Resolutions);
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

    internal sealed class WireServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _worker;
        internal string Address { get; }
        internal int Resolutions;
        internal readonly ConcurrentQueue<string> GetPaths = new();
        internal Func<string, CancellationToken, Task>? BeforeGet;
        internal Func<string, bool>? Unavailable;
        internal WireServer(ConcurrentDictionary<string, byte[]> documents, IReadOnlyDictionary<string, string>? payloadFiles = null)
        {
            _listener.Start(); Address = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _worker = ServeAsync(documents, payloadFiles);
        }
        private async Task ServeAsync(ConcurrentDictionary<string, byte[]> documents, IReadOnlyDictionary<string, string>? payloadFiles)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var peer = await _listener.AcceptTcpClientAsync(_stop.Token); await using var stream = peer.GetStream();
                    try
                    {
                        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                        var requestLine = await reader.ReadLineAsync(_stop.Token);
                        // A canceled pooled connection may close before sending a request.
                        if (requestLine is null) continue;
                        var first = requestLine.Split(' '); var length = 0; var chunked = false;
                        for (var count = 0; count < 64; count++)
                        {
                            var header = await reader.ReadLineAsync(_stop.Token); if (string.IsNullOrEmpty(header)) break;
                            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(header[15..], System.Globalization.CultureInfo.InvariantCulture);
                            if (header.Equals("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase)) chunked = true;
                        }
                        byte[] response;
                        if (first[0] == "POST")
                        {
                            // Source-edit cases also exercise the same API under a different base path.
                            Assert.True(first[1] is "/api/fs/get" or "/changed/api/fs/get", first[1]);
                            var body = new StringBuilder();
                            do
                            {
                                if (chunked) length = int.Parse((await reader.ReadLineAsync(_stop.Token))!, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                                Assert.InRange(length, 0, 4096 - body.Length);
                                var part = new char[length]; Assert.Equal(length, await reader.ReadBlockAsync(part.AsMemory(), _stop.Token)); body.Append(part);
                                if (chunked) Assert.Equal("", await reader.ReadLineAsync(_stop.Token));
                            } while (chunked && length != 0);
                            var path = JsonNode.Parse(body.ToString())!["path"]!.GetValue<string>().TrimStart('/');
                            if (Unavailable?.Invoke(path) == true)
                            {
                                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), _stop.Token);
                                continue;
                            }
                            var size = payloadFiles?.TryGetValue(path, out var payload) == true ? new FileInfo(payload).Length : documents[path].LongLength;
                            response = JsonSerializer.SerializeToUtf8Bytes(new { code = 200, data = new { size, is_dir = false, raw_url = Address + path } });
                            Interlocked.Increment(ref Resolutions);
                        }
                        else
                        {
                            Assert.Equal("GET", first[0]); var path = first[1].TrimStart('/');
                            if (path.StartsWith("changed/", StringComparison.Ordinal)) path = path["changed/".Length..];
                            GetPaths.Enqueue(path);
                            if (Unavailable?.Invoke(path) == true)
                            {
                                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), _stop.Token);
                                continue;
                            }
                            if (BeforeGet is not null) await BeforeGet(path, _stop.Token);
                            if (payloadFiles?.TryGetValue(path, out var payload) == true)
                            {
                                await using var input = new FileStream(payload, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
                                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {input.Length}\r\nConnection: close\r\n\r\n"), _stop.Token);
                                await input.CopyToAsync(stream, _stop.Token);
                                continue;
                            }
                            response = documents[path];
                        }
                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {response.Length}\r\nConnection: close\r\n\r\n"), _stop.Token);
                        await stream.WriteAsync(response, _stop.Token);
                    }
                    // Catalog consumers may cancel a redundant request after reusing a verified snapshot.
                    catch (IOException error) when (error.InnerException is SocketException) { }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Stop(); await _worker; _stop.Dispose(); }
    }
}
