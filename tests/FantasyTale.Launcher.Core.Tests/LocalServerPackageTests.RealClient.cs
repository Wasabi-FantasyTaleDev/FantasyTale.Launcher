using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LocalServerPackageTests
{
    private sealed class RealClientFactAttribute : FactAttribute
    {
        public RealClientFactAttribute()
        {
            if (!OperatingSystem.IsWindows() ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_SERVER_PACKAGE")) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FT_TEST_CN406_CLIENT")))
                Skip = "Set FT_TEST_SERVER_PACKAGE and FT_TEST_CN406_CLIENT on Windows to launch the actual CN2013 client twice.";
        }
    }

    [RealClientFact]
    public async Task ActualClientRetainsPlayerIdentityThroughOwnedServerRestart()
    {
        var client = Path.GetFullPath(Environment.GetEnvironmentVariable("FT_TEST_CN406_CLIENT")!);
        var libraries = new[] { "BubbleFighter.exe", "nmcogame.dll", "sdologinentry.dll" };
        Assert.False(File.Exists(Path.Combine(client, "session.toml")), "The client already has a session file.");
        var runningClients = Process.GetProcessesByName("BubbleFighter");
        try { Assert.Empty(runningClients); }
        finally { foreach (var process in runningClients) process.Dispose(); }
        var original = libraries.ToDictionary(name => name, name => SHA256.HashData(File.ReadAllBytes(Path.Combine(client, name))));
        var root = Directory.CreateTempSubdirectory("ft-real-cn406-").FullName;
        var passed = false;
        try
        {
            var sourceGames = AppContext.BaseDirectory;
            while (!Directory.Exists(Path.Combine(sourceGames, "games", "BubbleFighter", "builds")))
                sourceGames = Directory.GetParent(sourceGames)!.FullName;
            var games = PrepareHostingDefinitions(Path.Combine(sourceGames, "games"), Path.Combine(root, "games"));
            foreach (var file in Directory.EnumerateFiles(Path.Combine(sourceGames, "games", "BubbleFighter"), "*.dll", SearchOption.AllDirectories))
            {
                var target = Path.Combine(games, "BubbleFighter", Path.GetRelativePath(Path.Combine(sourceGames, "games", "BubbleFighter"), file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var package = Path.Combine(root, "package");
            CopyPackage(Environment.GetEnvironmentVariable("FT_TEST_SERVER_PACKAGE")!, package);
            var ticketSetting = Environment.GetEnvironmentVariable("FT_TEST_LAUNCH_TICKET_SECONDS");
            if (ticketSetting is not null)
            {
                Assert.True(int.TryParse(ticketSetting, out var seconds) && seconds > 0, "Invalid ticket lifetime override.");
                await File.AppendAllTextAsync(Path.Combine(package, "profiles", "cn-406", "patch.yaml"),
                    $"\n- set:\n    id: launcher.http\n    config:\n      ticket_seconds: {seconds}\n");
            }
            var ports = FreePorts();
            var profile = new LocalServerProfile("live", "bubblefighter", "cn-406", "Client lifecycle acceptance", package,
                "private", "127.0.0.1", ports[0], ports[1], ports[2], false) { RequireAuthentication = true, StorageMode = "local" };
            await using (var backend = new LauncherBackend(games, Path.Combine(root, "data")))
            {
                try
                {
                    var replies = new ConcurrentQueue<JsonNode>();
                    var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!));
                    backend.Register(bridge);
                    long sequence = 0;
                    async Task<JsonNode?> Call(string method, object? parameters = null)
                    {
                        var id = ++sequence;
                        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                        await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = parameters }, JsonSerializerOptions.Web), timeout.Token);
                        var reply = replies.Single(row => row["id"]?.GetValue<long>() == id);
                        Assert.True(reply["error"] is null, method + ": " + reply["error"] + "; diagnostics: " + root);
                        return reply["result"];
                    }
                    async Task<JsonNode> Ready()
                    {
                        var watch = Stopwatch.StartNew();
                        while (watch.Elapsed < TimeSpan.FromSeconds(100))
                        {
                            var session = (await Call("hosting.list"))!["sessions"]!.AsArray().Single()!;
                            var status = session["state"]!.GetValue<string>();
                            Assert.True(status != "failed", "Server failed: " + session + "; diagnostics: " + root);
                            if (status == "ready") return session.DeepClone();
                            await Task.Delay(200);
                        }
                        throw new TimeoutException("Owned server did not become ready; diagnostics: " + root);
                    }
                    await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false });
                    await Call("hosting.save", profile);
                    var username = "live_" + Guid.NewGuid().ToString("N")[..10];
                    var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
                    long? identity = null;
                    var accepted = new JsonArray();
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    for (var cycle = 0; cycle < 2; cycle++)
                    {
                        await Call("hosting.start", new { id = profile.Id });
                        var server = await Ready();
                        var runId = server["runId"]!.GetValue<string>();
                        const string serverId = "local-live";
                        if (cycle == 0)
                        {
                            await Call("accounts.register", new { serverId, username, password });
                            await Call("accounts.login", new { serverId, username, password });
                            await Call("instances.create", new { id = "cn406-live", name = "Actual client acceptance", game = profile.Game,
                                build = profile.Build, installationPath = client, serverId });
                        }
                        else
                        {
                            Assert.Empty((await Call("accounts.list", new { serverId }))!["accounts"]!.AsArray());
                            await Call("accounts.login", new { serverId, username, password });
                        }
                        await Call("plugins.ensureLatest", new { instanceId = "cn406-live" });
                        var transfers = Stopwatch.StartNew();
                        while (true)
                        {
                            var pending = (await Call("plugins.launchTransfers", new { instanceId = "cn406-live" }))!.AsArray();
                            Assert.DoesNotContain(pending, task => task?["stage"]?.GetValue<string>() == "failed");
                            if (pending.Count == 0) break;
                            Assert.True(transfers.Elapsed < TimeSpan.FromMinutes(2), "Authentication plugin transfer did not complete; diagnostics: " + root);
                            await Task.Delay(200);
                        }
                        var game = GameCatalog.Load(games).Single(g => g.Id == profile.Game);
                        var build = game.Builds.Single(b => b.Id == profile.Build);
                        using (var library = new PluginLibrary(Path.Combine(root, "data", "plugins")))
                        {
                            var instance = new InstanceStore(Path.Combine(root, "data", "instances")).Load("cn406-live");
                            foreach (var payload in FilePluginResolver.Resolve(instance, build, library.Packages(game)).Where(file => !file.RestoreOnExit))
                                original[payload.Target] = Convert.FromHexString(payload.Sha256);
                        }
                        var launchRequestedAt = DateTimeOffset.UtcNow;
                        var started = (await Call("instances.launch", new { id = "cn406-live", serverId }))!;
                        var processId = started["processId"]!.GetValue<int>();
                        var watch = Stopwatch.StartNew();
                        JsonNode? player = null;
                        while (watch.Elapsed < TimeSpan.FromSeconds(90))
                        {
                            var running = (await Call("instances.running"))!.AsArray();
                            Assert.True(running.Any(row => row?["processId"]?.GetValue<int>() == processId),
                                "The client exited before reaching MainMenu; diagnostics: " + root);
                            var sessions = JsonNode.Parse(await http.GetStringAsync(server["adminUrl"]!.GetValue<string>() + "api/sessions"))!.AsArray();
                            player = sessions.Select(row => row?["player"]).FirstOrDefault(row =>
                                row?["username"]?.GetValue<string>() == username && row?["phase"]?.GetValue<string>() == "MainMenu");
                            if (player is not null)
                            {
                                var instance = running.Single(row => row?["processId"]?.GetValue<int>() == processId)!;
                                Assert.Equal("ready", instance["hook"]!["state"]!.GetValue<string>());
                                break;
                            }
                            await Task.Delay(500);
                        }
                        Assert.True(player is not null, "Client did not reach MainMenu; diagnostics: " + root);
                        var playerIdentity = player["userNum"]!.GetValue<long>();
                        if (identity is not null) Assert.Equal(identity.Value, playerIdentity);
                        identity = playerIdentity;
                        accepted.Add(new JsonObject { ["cycle"] = cycle, ["processId"] = processId, ["playerIdentity"] = playerIdentity,
                            ["phase"] = "MainMenu", ["hook"] = "ready", ["serverRunId"] = runId,
                            ["launchRequestedAt"] = launchRequestedAt.ToString("O"), ["acceptedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                            ["ticketSecondsOverride"] = ticketSetting });
                        await File.WriteAllTextAsync(Path.Combine(root, "acceptance.json"), accepted.ToJsonString(new() { WriteIndented = true }));
                        // A caller can hold the accepted client for independent visual inspection.
                        if (int.TryParse(Environment.GetEnvironmentVariable("FT_TEST_CLIENT_HOLD_SECONDS"), out var hold) && hold > 0)
                            await Task.Delay(TimeSpan.FromSeconds(hold));
                        await Call("instances.stop", new { id = "cn406-live", processId });
                        VerifyRestored();
                        var stopped = (await Call("hosting.stop", new { id = profile.Id, runId }))!;
                        Assert.Equal("stopped", stopped["state"]!.GetValue<string>());
                        Assert.False(stopped["forcedStop"]!.GetValue<bool>());
                        AssertPortsReleased(ports);
                    }
                    Assert.True(File.Exists(Path.Combine(root, "data", "hosting", "data", profile.Id, "save.sqlite")));
                    foreach (var log in Directory.EnumerateFiles(Path.Combine(root, "data", "hosting", "runs"), "server.log*", SearchOption.AllDirectories))
                        Assert.DoesNotContain(password, await File.ReadAllTextAsync(log), StringComparison.Ordinal);
                    Console.WriteLine("Real-client acceptance: " + accepted.ToJsonString());
                }
                finally { await backend.StopAllAsync(); }
            }
            VerifyRestored();
            passed = true;
        }
        finally
        {
            VerifyRestored();
            if (passed) Directory.Delete(root, true);
        }

        void VerifyRestored()
        {
            foreach (var entry in original)
                Assert.True(CryptographicOperations.FixedTimeEquals(entry.Value, SHA256.HashData(File.ReadAllBytes(Path.Combine(client, entry.Key)))),
                    "Client file differs from its restoration or permanent-install policy: " + entry.Key + "; diagnostics: " + root);
            Assert.False(File.Exists(Path.Combine(client, "session.toml")), "Client session cleanup is incomplete; diagnostics: " + root);
        }
    }
}
