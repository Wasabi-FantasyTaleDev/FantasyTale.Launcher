using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Hooks;
using System.Globalization;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Core.Multiplayer;
using FantasyTale.Launcher.DirectoryService;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;
using Tomlyn;
using Tomlyn.Model;

namespace FantasyTale.Launcher.Core.Tests;

[Collection("native hook runtime")]
public sealed partial class LocalServerPackageTests
{
    private sealed class PackageTheoryAttribute : TheoryAttribute
    {
        public PackageTheoryAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_SERVER_PACKAGE")))
                Skip = "Set FT_TEST_SERVER_PACKAGE to a complete managed-stdio server package for isolated integration verification.";
        }
    }

    [PackageTheory]
    [InlineData("temporary", null)]
    [InlineData("local", null)]
    [InlineData("local", "http")]
    [InlineData("local", "alist")]
    public Task ActualPackageSupportsAccountsTicketsOwnedCatalogAndRestartWithoutAClient(string storage, string? transport) => RunPackageAsync(storage, transport);

    private sealed class PublicAListFactAttribute : FactAttribute
    {
        public PublicAListFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_SERVER_PACKAGE")) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_ALIST_URL")))
                Skip = "Set FT_TEST_SERVER_PACKAGE and FT_TEST_ALIST_URL for anonymous real-source provisioning acceptance.";
        }
    }

    [PublicAListFact]
    public Task PublicAListPackageSupportsInstallationSqliteTicketsAndRestart() => RunPackageAsync("local", "alist-live");

    private static async Task RunPackageAsync(string storage, string? transport)
    {
        var root = Directory.CreateTempSubdirectory("ft-server-package-").FullName;
        try
        {
            var package = Path.Combine(root, "package");
            CopyPackage(Environment.GetEnvironmentVariable("FT_TEST_SERVER_PACKAGE")!, package);
            var games = AppContext.BaseDirectory;
            while (!Directory.Exists(Path.Combine(games, "games", "BubbleFighter", "builds"))) games = Directory.GetParent(games)!.FullName;
            games = PrepareHostingDefinitions(Path.Combine(games, "games"), Path.Combine(root, "games"));
            var game = GameCatalog.Load(games).Single(g => g.Id == "bubblefighter");
            var ports = FreePorts();
            var profile = new LocalServerProfile("integration", game.Id, "cn-406", "Local verification", package,
                "private", "127.0.0.1", ports[0], ports[1], ports[2], storage == "temporary") { StorageMode = storage, RequireAuthentication = true };
            var live = transport == "alist-live";
            var published = transport is null || live ? null : await PublishPackageAsync(package);
            await using var publication = published?.Server;
            await using var backend = new LauncherBackend(games, Path.Combine(root, "data"));
            var replies = new ConcurrentQueue<JsonNode>();
            var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
            long sequence = 0;
            async Task<JsonNode?> Call(string method, object? arguments = null, bool failure = false)
            {
                var id = ++sequence;
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }, JsonSerializerOptions.Web), default);
                var reply = replies.Single(r => r["id"]?.GetValue<long>() == id);
                if (failure) Assert.NotNull(reply["error"]); else Assert.True(reply["error"] is null,
                    method + ": " + reply["error"] + "; code=" + reply["code"] + "; http=" + reply["httpStatus"] + "; transport=" + reply["transportCode"]);
                return reply["result"];
            }
            async Task<JsonNode> Ready()
            {
                var watch = Stopwatch.StartNew();
                while (watch.Elapsed < TimeSpan.FromSeconds(100))
                {
                    var state = (await Call("hosting.list"))!["sessions"]![0]!;
                    if (state["state"]!.GetValue<string>() == "ready") return state;
                    Assert.False(state["state"]!.GetValue<string>() == "failed",
                        "Server failed: " + state + "\n" + await Call("hosting.log", new { id = profile.Id, runId = state["runId"]!.GetValue<string>() }));
                    await Task.Delay(100);
                }
                throw new TimeoutException("The actual server package did not become ready.");
            }
            await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false });
            if (published is not null || live)
            {
                await Call("store.saveSource", new { id = "integration-store", name = "Integration", kind = live ? "alist" : transport, baseUrl = live ? Environment.GetEnvironmentVariable("FT_TEST_ALIST_URL")! : published!.Server.Address, catalogPath = "FantasyTale/source.toml", enabled = true });
                var selected = live ? (await Call("store.catalog", new { id = "integration-store" }))!["packages"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == "bubblefighter.server") : null;
                var task = await Call("store.installServer", new { sourceId = "integration-store", packageId = "bubblefighter.server", version = selected?["version"]?.GetValue<string>() ?? "2.25.0", manifestSha256 = selected?["manifestSha256"]?.GetValue<string>() ?? published!.Hash });
                var watch = Stopwatch.StartNew();
                while (true)
                {
                    var state = (await Call("downloads.list"))!.AsArray().Single(t => t!["id"]!.GetValue<string>() == task!["id"]!.GetValue<string>())!;
                    Assert.False(state["stage"]!.GetValue<string>() == "failed",
                        "AList server package download failed: " + state);
                    if (state["stage"]!.GetValue<string>() == "completed") break;
                    if (watch.Elapsed > TimeSpan.FromMinutes(live ? 30 : 3))
                        throw new TimeoutException("Full server package installation timed out after " + watch.Elapsed + ": " + state);
                    await Task.Delay(100);
                }
                var installed = Assert.Single((await Call("hosting.packages"))!.AsArray())!;
                var installedFolder = installed["directory"]!.GetValue<string>();
                Assert.NotEqual(package, installedFolder);
                foreach (var relative in Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(package, p)))
                {
                    using var input = File.OpenRead(Path.Combine(package, relative));
                    using var output = File.OpenRead(Path.Combine(installedFolder, relative));
                    Assert.Equal(await SHA256.HashDataAsync(input), await SHA256.HashDataAsync(output));
                }
                profile = profile with { PackagePath = installedFolder };
                if (published is not null) Assert.Equal(transport == "alist" ? published.ContentObjects + 2 : 0, published.Server.Resolutions);
                await Call("downloads.clearFinished");
                Assert.Single((await Call("hosting.packages"))!.AsArray());
            }
            await Call("hosting.save", profile);
            await Call("hosting.start", new { id = profile.Id });
            var first = await Ready(); var firstRun = first["runId"]!.GetValue<string>();
            var serverId = "local-" + profile.Id;
            var firstDestination = await Call("hosting.destination", new { id = profile.Id, runId = firstRun });
            Assert.Equal(serverId, firstDestination!["id"]!.GetValue<string>());
            Assert.Equal(first["apiUrl"]!.GetValue<string>(), firstDestination["apiUrl"]!.GetValue<string>());
            await Call("hosting.destination", new { id = profile.Id, runId = "stale-run" }, failure: true);
            var firstLogs = (await Call("hosting.log", new { id = profile.Id, runId = firstRun }))!.AsArray();
            Assert.InRange(firstLogs.Count, 1, 128);
            var server = (await Call("servers.list"))!.AsArray().Single(s => s!["id"]!.GetValue<string>() == serverId)!.Deserialize<ServerDefinition>(JsonSerializerOptions.Web)!;
            using var api = new LauncherApiClient();
            var catalog = await api.CatalogAsync(server);
            Assert.Equal("cn-406", Assert.Single(catalog.Instances).Build);
            Assert.Equal("required", catalog.Instances[0].LauncherLogin);
            Assert.Equal(new ServerEndpoint("127.0.0.1", profile.GamePort), catalog.Instances[0].Endpoint);
            var username = "verify_" + Guid.NewGuid().ToString("N")[..8];
            var password = Guid.NewGuid().ToString("N")[..6];
            await Call("accounts.register", new { serverId, username, password });
            await Call("accounts.login", new { serverId, username, password = "wrong!" }, failure: true);
            var account = await Call("accounts.login", new { serverId, username, password });
            Assert.Equal(username, account!["username"]!.GetValue<string>());
            Assert.Single((await Call("accounts.list", new { serverId }))!["accounts"]!.AsArray());
            var alternate = username + "_b";
            await Call("accounts.register", new { serverId, username = alternate, password });
            await Call("accounts.login", new { serverId, username = alternate, password });
            var sessions = await Call("accounts.list", new { serverId });
            Assert.Equal(2, sessions!["accounts"]!.AsArray().Count);
            Assert.Equal(alternate, sessions["current"]!["username"]!.GetValue<string>());
            await Call("accounts.select", new { serverId, username });
            Assert.Equal(username, (await Call("accounts.refresh", new { serverId }))!["username"]!.GetValue<string>());
            await Call("accounts.logout", new { serverId, username = alternate });
            sessions = await Call("accounts.list", new { serverId });
            Assert.Single(sessions!["accounts"]!.AsArray());
            Assert.Equal(username, sessions["current"]!["username"]!.GetValue<string>());
            await api.LoginAsync(server, username, password);
            var build = game.Builds.Single(build => build.Id == "cn-406");
            var credentials = await api.CredentialsAsync(server, build, new(build.ReleaseVersion, null),
                new("en", build.NativeLanguage!, build.NativeLanguage!), true);
            Assert.NotNull(credentials); Assert.Equal(username, credentials.Account);
            // The module adds the native passport marker; the API returns the opaque token only.
            Assert.True(credentials.Ticket.Length == 64 && credentials.Ticket.All(char.IsAsciiHexDigit), "Invalid opaque ticket shape.");
            await using (var directory = await DirectoryHttp.StartAsync(new() { Listen = "http://127.0.0.1:0", DataPath = Path.Combine(root, "directory"), AdminToken = Guid.NewGuid().ToString("N") }))
            {
                var source = new DirectorySource("integration-directory", "Integration", directory.Addresses.Single(), true, false);
                await Call("directory.saveSource", new { source.Id, source.Name, source.BaseUrl, source.Enabled });
                using var directoryClient = new DirectoryClient();
                var room = await directoryClient.PublishAsync(source, new(game.Id, "Local room", server.ApiUrl!, ["cn-406"], ""));
                var joined = await Call("directory.join", new { sourceId = source.Id, entryId = room.Id, build = "cn-406" });
                var joinedId = joined!["id"]!.GetValue<string>(); Assert.NotEqual(server.Id, joinedId);
                Assert.Equal(username, (await Call("accounts.login", new { serverId = joinedId, username, password }))!["username"]!.GetValue<string>());
                await directoryClient.WithdrawAsync(source, room);
                await Call("accounts.refresh", new { serverId = joinedId }, failure: true);
                Assert.DoesNotContain((await Call("servers.list"))!.AsArray(), s => s!["id"]!.GetValue<string>() == joinedId);
            }
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var admin = await http.GetAsync(first["adminUrl"]!.GetValue<string>() + "health");
            admin.EnsureSuccessStatusCode();
            var stopped = await Call("hosting.stop", new { id = profile.Id, runId = firstRun });
            Assert.Equal("stopped", stopped!["state"]!.GetValue<string>());
            Assert.False(stopped["forcedStop"]!.GetValue<bool>()); Assert.Equal(0, stopped["exitCode"]!.GetValue<int>());
            AssertPortsReleased(ports);
            await Call("hosting.destination", new { id = profile.Id, runId = firstRun }, failure: true);
            Assert.DoesNotContain((await Call("servers.list"))!.AsArray(), s => s!["id"]!.GetValue<string>() == serverId);

            profile = profile with { Mode = "room", AdvertisedHost = "192.0.2.10" };
            await Call("hosting.save", profile); await Call("hosting.start", new { id = profile.Id });
            var second = await Ready(); var secondRun = second["runId"]!.GetValue<string>();
            Assert.NotEqual(firstRun, secondRun);
            await Call("hosting.destination", new { id = profile.Id, runId = firstRun }, failure: true);
            var secondDestination = await Call("hosting.destination", new { id = profile.Id, runId = secondRun });
            Assert.Equal(serverId, secondDestination!["id"]!.GetValue<string>());
            Assert.Equal(profile.GamePort, secondDestination["endpoint"]!["port"]!.GetValue<int>());
            Assert.Null(await Call("accounts.current", new { serverId }));
            Assert.Empty((await Call("accounts.list", new { serverId }))!["accounts"]!.AsArray());
            await Call("accounts.login", new { serverId, username, password }, failure: storage == "temporary");
            if (storage == "local")
            {
                await Call("accounts.login", new { serverId, username, password = "wrong!" }, failure: true);
                Assert.True(File.Exists(Path.Combine(root, "data", "hosting", "data", profile.Id, "save.sqlite")));
            }
            var remote = await api.CatalogAsync(server);
            Assert.Equal("192.0.2.10", remote.Instances[0].Endpoint!.Host);
            var owned = await Call("servers.catalog", new { id = serverId });
            Assert.Equal("127.0.0.1", owned!["instances"]![0]!["endpoint"]!["host"]!.GetValue<string>());
            await Call("hosting.stop", new { id = profile.Id, runId = firstRun }, failure: true);
            await Call("hosting.stop", new { id = profile.Id, runId = secondRun }); AssertPortsReleased(ports);
            if (storage == "local")
            {
                var replacement = Path.Combine(root, "replacement-package");
                CopyPackage(Environment.GetEnvironmentVariable("FT_TEST_SERVER_PACKAGE")!, replacement);
                profile = profile with { PackagePath = replacement, Name = "Renamed local save" };
                await Call("hosting.save", profile); await Call("hosting.start", new { id = profile.Id });
                var replaced = await Ready();
                await Call("accounts.login", new { serverId, username, password });
                await Call("hosting.stop", new { id = profile.Id, runId = replaced["runId"]!.GetValue<string>() });
                AssertPortsReleased(ports);

                var independent = profile with { Id = "independent", Name = "Independent save" };
                await Call("hosting.save", independent); await Call("hosting.start", new { id = independent.Id });
                var independentWatch = Stopwatch.StartNew(); JsonNode? independentState = null;
                while (independentWatch.Elapsed < TimeSpan.FromSeconds(100))
                {
                    independentState = (await Call("hosting.list"))!["sessions"]!.AsArray().Single(s => s!["profileId"]!.GetValue<string>() == independent.Id);
                    if (independentState!["state"]!.GetValue<string>() == "ready") break;
                    Assert.NotEqual("failed", independentState["state"]!.GetValue<string>());
                    await Task.Delay(100);
                }
                Assert.Equal("ready", independentState!["state"]!.GetValue<string>());
                var independentServer = "local-" + independent.Id;
                await Call("accounts.login", new { serverId = independentServer, username, password }, failure: true);
                await Call("accounts.register", new { serverId = independentServer, username, password = "other1" });
                await Call("accounts.login", new { serverId = independentServer, username, password = "other1" });
                await Call("hosting.stop", new { id = independent.Id, runId = independentState["runId"]!.GetValue<string>() });
                AssertPortsReleased(ports);
                await Call("hosting.delete", new { id = independent.Id });
                Assert.True(File.Exists(Path.Combine(root, "data", "hosting", "data", independent.Id, "save.sqlite")));

                await Call("hosting.start", new { id = profile.Id }); var original = await Ready();
                await Call("accounts.login", new { serverId, username, password });
                await Call("accounts.login", new { serverId, username, password = "other1" }, failure: true);
                await Call("hosting.stop", new { id = profile.Id, runId = original["runId"]!.GetValue<string>() });
                AssertPortsReleased(ports);
            }
            var logs = Directory.GetFiles(Path.Combine(root, "data", "hosting", "runs"), "server.log*", SearchOption.AllDirectories);
            Assert.NotEmpty(logs);
            foreach (var log in logs)
            {
                var text = await File.ReadAllTextAsync(log);
                Assert.False(text.Contains(password, StringComparison.Ordinal), "A server log contains the password.");
                Assert.False(text.Contains(credentials.Ticket, StringComparison.Ordinal), "A server log contains the ticket.");
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed record PublishedPackage(StoreWireTests.WireServer Server, string Hash, int ContentObjects);

    private static async Task<PublishedPackage> PublishPackageAsync(string package)
    {
        var rows = new StringBuilder("schema = 1\n[release]\nid = \"bubblefighter.server\"\ngame = \"bubblefighter\"\nbuilds = [\"cn-406\"]\nversion = \"2.25.0\"\n");
        var payloads = new Dictionary<string, string>(); long total = 0;
        var files = Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories).ToArray();
        Assert.NotEmpty(files);
        foreach (var path in files)
        {
            var relative = Path.GetRelativePath(package, path).Replace('\\', '/');
            await using var input = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input));
            total += input.Length; payloads["objects/" + hash] = path;
            rows.Append(CultureInfo.InvariantCulture, $"\n[[files]]\npath = {SessionWriter.Quote(relative)}\nsource_path = \"objects/{hash}\"\nsize = {input.Length}\nsha256 = \"{hash}\"\n");
        }
        var manifest = Encoding.UTF8.GetBytes(rows.ToString());
        var manifestHash = Convert.ToHexStringLower(SHA256.HashData(manifest));
        var catalog = Encoding.UTF8.GetBytes($"schema = 1\n[catalog]\nid = \"integration\"\nname = {{ en = \"Integration\" }}\n[[packages]]\nid = \"bubblefighter.server\"\nkind = \"server\"\ngame = \"bubblefighter\"\nbuilds = [\"cn-406\"]\nversion = \"2.25.0\"\nname = {{ en = \"Bubble Fighter server\" }}\npublisher = \"Integration\"\nsize = {total}\nmanifest = \"manifest.toml\"\nmanifest_sha256 = \"{manifestHash}\"\n");
        _ = ClientRelease.Parse(Assert.Single(StoreCatalogFile.Parse("integration", Encoding.UTF8.GetString(catalog)).Packages), manifest);
        return new(new(new ConcurrentDictionary<string, byte[]>(new Dictionary<string, byte[]> { ["FantasyTale/source.toml"] = catalog, ["manifest.toml"] = manifest }), payloads), manifestHash, payloads.Count);
    }

    private static int[] FreePorts()
    {
        var listeners = Enumerable.Range(0, 3).Select(_ => new TcpListener(IPAddress.Loopback, 0)).ToArray();
        try { foreach (var listener in listeners) listener.Start(); return listeners.Select(l => ((IPEndPoint)l.LocalEndpoint).Port).ToArray(); }
        finally { foreach (var listener in listeners) listener.Stop(); }
    }

    private static void AssertPortsReleased(int[] ports)
    {
        foreach (var port in ports)
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.ExclusiveAddressUse = true; socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
        }
    }

    private static void CopyPackage(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            if (name.StartsWith('.') || name == "local.patch.yaml" || name is "logs" or "overrides") continue;
            var attributes = File.GetAttributes(entry);
            Assert.False(attributes.HasFlag(FileAttributes.ReparsePoint), "Test packages cannot contain links.");
            if (attributes.HasFlag(FileAttributes.Directory)) CopyPackage(entry, Path.Combine(target, name));
            else File.Copy(entry, Path.Combine(target, name));
        }
    }

    private static string PrepareHostingDefinitions(string source, string target)
    {
        var sourceGame = Path.Combine(source, "BubbleFighter");
        var targetGame = Path.Combine(target, "BubbleFighter");
        foreach (var file in Directory.EnumerateFiles(sourceGame, "*", SearchOption.AllDirectories)
            .Where(file => Path.GetExtension(file) == ".toml" || Path.GetRelativePath(sourceGame, file).StartsWith("levels" + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            var destination = Path.Combine(targetGame, Path.GetRelativePath(sourceGame, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        // Explicit integration fixtures must not change the installed release's availability switches.
        var manifest = Path.Combine(targetGame, "game.toml");
        var document = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(manifest))!;
        var game = (TomlTable)document["game"];
        game["local_hosting_enabled"] = true;
        game["rooms_enabled"] = true;
        File.WriteAllText(manifest, TomlSerializer.Serialize(document));
        return target;
    }
}
