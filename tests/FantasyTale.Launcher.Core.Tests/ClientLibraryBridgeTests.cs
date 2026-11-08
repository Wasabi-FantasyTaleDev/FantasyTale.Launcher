using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ClientLibraryBridgeTests
{
    [Fact]
    public async Task ListingReportsEachClientsDiagnosticSelectionWithoutRecoveringFiles()
    {
        var root = Directory.CreateTempSubdirectory("ft-client-library-").FullName;
        try
        {
            var games = Path.Combine(root, "games");
            var folder = Directory.CreateDirectory(Path.Combine(games, "Fixture")).FullName;
            var builds = Directory.CreateDirectory(Path.Combine(folder, "builds")).FullName;
            var plugin = Directory.CreateDirectory(Path.Combine(folder, "plugins", "diagnostics")).FullName;
            File.WriteAllText(Path.Combine(folder, "game.toml"), """
                schema = 1
                [game]
                id = "fixture"
                status = "enabled"
                module = "Fixture"
                hook_host = "external"
                name = { en = "Fixture" }
                """);
            File.WriteAllText(Path.Combine(builds, "test-1.toml"), """
                schema = 1
                [build]
                id = "test-1"
                game = "fixture"
                branch = "test"
                protocol_version = 1
                executable = "client.exe"
                name = { en = "Fixture" }
                [launch]
                login = "none"
                """);
            File.WriteAllText(Path.Combine(plugin, "plugin.toml"), """
                schema = 1
                [plugin]
                id = "fixture.diagnostics"
                version = "1.0.0"
                game = "fixture"
                kind = "native"
                name = { en = "Diagnostics" }
                diagnostics = true
                [native]
                library = "diagnostics.dll"
                """);
            var data = Path.Combine(root, "data");
            var store = new InstanceStore(Path.Combine(data, "instances"));
            foreach (var enabled in new[] { false, true })
                await store.CreateAsync(new(enabled ? "enabled" : "disabled", "Fixture", "fixture", "test-1", null,
                    [new("fixture.diagnostics", "1.0.0", enabled, false, new Dictionary<string, object>())]));
            var game = Assert.Single(GameCatalog.Load(games));
            var session = Path.Combine(store.ClientFolder("enabled"), SessionWriter.FileName);
            File.WriteAllText(session, SessionWriter.Render(new("fixture", game.Builds[0], "enabled", Path.Combine(root, "log"), null, null, null, [])));
            await using var backend = new LauncherBackend(games, data);
            var replies = new List<JsonNode>();
            var bridge = new BridgeRouter(raw => replies.Add(JsonNode.Parse(raw)!));
            backend.Register(bridge);
            await bridge.HandleAsync("{\"id\":1,\"method\":\"instances.list\"}", default);
            var reply = replies.Single(row => row["id"]?.GetValue<int>() == 1);
            Assert.Null(reply["error"]);
            var instances = reply["result"]!.AsArray();
            Assert.False(instances.Single(row => row!["id"]!.GetValue<string>() == "disabled")!["diagnosticsEnabled"]!.GetValue<bool>());
            Assert.True(instances.Single(row => row!["id"]!.GetValue<string>() == "enabled")!["diagnosticsEnabled"]!.GetValue<bool>());
            Assert.True(File.Exists(session));
        }
        finally { Directory.Delete(root, true); }
    }
}
