using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hosting;
using FantasyTale.Launcher.Core.Multiplayer;
using FantasyTale.Launcher.DirectoryService;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class FeatureAvailabilityTests
{
    [Fact]
    public async Task DisabledFeaturesRejectBackendOperationsAndRetainServerBrowsingAndProfiles()
    {
        var root = Directory.CreateTempSubdirectory("ft-features-").FullName;
        try
        {
            var games = Path.Combine(root, "games");
            var folder = Path.Combine(games, "Fixture");
            Directory.CreateDirectory(Path.Combine(folder, "builds"));
            var definitionPath = Path.Combine(folder, "game.toml");
            const string definition = "schema=1\n[game]\nid='fixture'\nstatus='enabled'\nmodule='Fixture'\nhook_host='external'\nname={en='Fixture'}\n";
            await File.WriteAllTextAsync(definitionPath, definition + "rooms_enabled=false\nlocal_hosting_enabled=false\n");
            await File.WriteAllTextAsync(Path.Combine(folder, "builds", "test-1.toml"), "schema=1\n[build]\nid='test-1'\ngame='fixture'\nbranch='test'\nprotocol_version=1\nexecutable='client.exe'\nname={en='Fixture'}\n[launch]\nlogin='unverified'\narguments=[]\n");
            var game = GameCatalog.Load(games).Single();
            var data = Path.Combine(root, "data");
            var profile = new LocalServerProfile("retained", "fixture", "test-1", "Retained", root, "private", "127.0.0.1", 17230, 18090, 18094, false);
            var profiles = new LocalServerProfiles(Path.Combine(data, "hosting", "profiles"));
            profiles.Save(profile);
            await using (var hosting = new LocalServerService(Path.Combine(root, "probe-runs")))
            {
                var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => hosting.StartAsync(profile, game, new UnreachableModule()));
                Assert.Equal("feature_in_development", failure.Message);
                Assert.Empty(hosting.List());
            }
            await using var directory = await DirectoryHttp.StartAsync(new DirectoryOptions
                { Listen = "http://127.0.0.1:0", DataPath = Path.Combine(root, "directory"), AdminToken = Guid.NewGuid().ToString("N") });
            var source = new DirectorySource("test", "Test", directory.Addresses.Single(), true, false);
            using var client = new DirectoryClient();
            var details = new DirectoryAdvertisement("fixture", "Fixture", "http://127.0.0.1:18090", ["test-1"], "Fixture");
            var room = await client.PublishAsync(source, details);
            var replies = new List<JsonNode>(); long sequence = 0;
            var bridge = new BridgeRouter(raw => replies.Add(JsonNode.Parse(raw)!));
            await using var backend = new LauncherBackend(games, data);
            backend.Register(bridge);
            async Task<JsonNode> Call(string method, object args)
            {
                var id = ++sequence;
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = args }, JsonSerializerOptions.Web), default);
                return replies.Single(row => row["id"]?.GetValue<long>() == id);
            }
            Assert.Null((await Call("directory.saveSource", new { source.Id, source.Name, source.BaseUrl, source.Enabled }))["error"]);
            Assert.Null((await Call("directory.list", new { sourceId = source.Id }))["error"]);
            Assert.Null((await Call("directory.submit", new { sourceId = source.Id, details }))["error"]);
            Assert.Null((await Call("servers.save", new { id = "one", game = "fixture", name = "One", bundledInstances = true }))["error"]);
            Assert.Single((await Call("servers.catalog", new { id = "one" }))["result"]!["instances"]!.AsArray());
            foreach (var request in new (string Method, object Args)[]
            {
                ("hosting.save", profile),
                ("hosting.start", new { id = profile.Id }),
                ("hosting.destination", new { id = profile.Id, runId = "unused" }),
                ("directory.publish", new { id = profile.Id, runId = "unused", sourceId = source.Id }),
                ("directory.join", new { sourceId = source.Id, entryId = room.Id, build = "test-1" }),
            })
                Assert.Equal("feature_in_development", (await Call(request.Method, request.Args))["error"]!.GetValue<string>());
            Assert.Equal(profile, Assert.Single(profiles.List()));
            Assert.Empty((await Call("hosting.list", new { }))["result"]!["sessions"]!.AsArray());
            await File.WriteAllTextAsync(definitionPath, definition);
            var restored = GameCatalog.Load(games).Single();
            restored.RequireRoomsEnabled(); restored.RequireLocalHostingEnabled();
            await File.WriteAllTextAsync(definitionPath, definition + "rooms_enabled='false'\n");
            Assert.Throws<DefinitionException>(() => GameCatalog.Load(games));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class UnreachableModule : ILocalServerModule
    {
        public LocalServerPlan CreateServerPlan(LocalServerContext context) => throw new InvalidOperationException("Disabled hosting reached the module.");
    }
}
