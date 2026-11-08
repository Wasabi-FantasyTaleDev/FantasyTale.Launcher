using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class BuildAvailabilityTests
{
    [Fact]
    public async Task DisabledBuildRetainsAnExistingClientButCannotReachLaunchPreparation()
    {
        var root = Directory.CreateTempSubdirectory("ft-build-availability-").FullName;
        try
        {
            var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" });
            var build = new BuildDefinition("test-1", "test", 1, name, "client.exe", "", "", null, 123, "unverified", [], [])
                { Enabled = false, Hidden = true };
            var game = new GameDefinition("fixture", GameStatus.Enabled, name, "Fixture", "external", "", null, root, [build]);
            var store = new InstanceStore(root);
            await store.CreateAsync(new("existing", "Existing", "fixture", build.Id, null, []));
            await File.WriteAllTextAsync(Path.Combine(store.ClientFolder("existing"), "retained.txt"), "retained");
            var service = new LaunchService(store);
            var error = Assert.Throws<InvalidOperationException>(() => service.Start("existing", game, new UnreachableModule(), []));
            Assert.Equal("build_disabled", error.Message);
            Assert.False(service.HasPendingStarts);
            Assert.Empty(service.Running);
            Assert.Equal("retained", await File.ReadAllTextAsync(Path.Combine(store.ClientFolder("existing"), "retained.txt")));
            Assert.Equal(123u, build.PeTimestamp);
            (build with { Enabled = true }).RequireEnabled();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task HiddenDisabledBuildIsOmittedFromPresetsAndCanBeRestoredByDataAlone()
    {
        var root = Directory.CreateTempSubdirectory("ft-build-visibility-").FullName;
        try
        {
            var games = Path.Combine(root, "games");
            var folder = Path.Combine(games, "Fixture");
            Directory.CreateDirectory(Path.Combine(folder, "builds"));
            await File.WriteAllTextAsync(Path.Combine(folder, "game.toml"), "schema=1\n[game]\nid='fixture'\nstatus='enabled'\nmodule='Fixture'\nhook_host='external'\nname={en='Fixture'}\n");
            const string definition = "schema=1\n[build]\nid='test-1'\ngame='fixture'\nbranch='test'\nbuild_date='2017-01-01 00:00:00'\nprotocol_version=1\nexecutable='client.exe'\nname={en='Fixture'}\n[launch]\nlogin='unverified'\narguments=[]\n";
            var buildPath = Path.Combine(folder, "builds", "test-1.toml");
            await File.WriteAllTextAsync(buildPath, definition.Replace("build_date='2017-01-01 00:00:00'", "build_date='2017-01-01 00:00:00'\nenabled=false\nhidden=true", StringComparison.Ordinal));
            var data = Path.Combine(root, "data");
            var replies = new ConcurrentQueue<JsonNode>(); long sequence = 0;
            var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!));
            await using var backend = new LauncherBackend(games, data);
            backend.Register(bridge);
            async Task<JsonNode> Call(string method, object args)
            {
                var id = ++sequence;
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = args }), default);
                return replies.Single(row => row["id"]?.GetValue<long>() == id);
            }
            Assert.Null((await Call("servers.save", new { id = "one", game = "fixture", name = "One", bundledInstances = true }))["error"]);
            Assert.Empty((await Call("servers.catalog", new { id = "one" }))["result"]!["instances"]!.AsArray());
            var rejected = await Call("instances.create", new { id = "new", name = "New", game = "fixture", build = "test-1" });
            Assert.Equal("build_disabled", rejected["error"]!.GetValue<string>());
            var store = new InstanceStore(Path.Combine(data, "instances"));
            Assert.Empty(store.List());
            await store.CreateAsync(new("existing", "Existing", "fixture", "test-1", null, []));
            Assert.Equal("build_disabled", (await Call("instances.launch", new { id = "existing", serverId = "one" }))["error"]!.GetValue<string>());
            Assert.Single(store.List());
            var retained = GameCatalog.Load(games).Single().Builds.Single();
            Assert.False(retained.Enabled); Assert.True(retained.Hidden);

            await File.WriteAllTextAsync(buildPath, definition);
            var restored = GameCatalog.Load(games).Single().Builds.Single();
            Assert.True(restored.Enabled); Assert.False(restored.Hidden);
            Assert.Single((await Call("servers.catalog", new { id = "one" }))["result"]!["instances"]!.AsArray());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class UnreachableModule : IGameModule
    {
        public string GameId => "fixture";
        public LaunchPlan CreateLaunchPlan(LaunchContext context) => throw new InvalidOperationException("Disabled build reached the game module.");
    }
}
