using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RecoveringOneMissingReleaseRepairsEveryClientBeforeVersionSwitch(bool rejected, bool successor)
    {
        var game = Game();
        var buildFile = Path.Combine(game.Folder, "builds", "test-1.toml");
        File.WriteAllText(Path.Combine(game.Folder, "builds", "test-2.toml"), File.ReadAllText(buildFile).Replace("test-1", "test-2", StringComparison.Ordinal));
        game = Assert.Single(GameCatalog.Load(Path.Combine(_root, "games")));
        var data = Path.Combine(_root, "recovery-data");
        string? brokenFolder = null;
        if (rejected)
        {
            using var library = new PluginLibrary(Path.Combine(data, "plugins"));
            var preview = library.Prepare(Folder(), game);
            brokenFolder = library.Install(preview.Token, game, game.Builds.Single(build => build.Id == "test-1")).Folder;
            File.Delete(Path.Combine(brokenFolder, "plugin.toml"));
        }
        var healthy = Directory.CreateDirectory(Path.Combine(game.Folder, "plugins", "fixture.healthy")).FullName;
        File.WriteAllText(Path.Combine(healthy, "plugin.toml"), Manifest.Replace("fixture.optional", "fixture.healthy", StringComparison.Ordinal)
            .Replace("[\"test-1\"]", "[\"test-1\", \"test-2\"]", StringComparison.Ordinal));
        if (successor)
        {
            var next = Directory.CreateDirectory(Path.Combine(game.Folder, "plugins", "fixture.successor")).FullName;
            File.WriteAllText(Path.Combine(next, "plugin.toml"), Manifest.Replace("id = \"fixture.optional\"",
                "id = \"fixture.successor\"\naliases = [\"fixture.optional\"]", StringComparison.Ordinal)
                .Replace("[\"test-1\"]", "[\"test-2\"]", StringComparison.Ordinal));
        }
        var store = new InstanceStore(Path.Combine(data, "instances"));
        await store.CreateAsync(new("first", "First", game.Id, "test-1", null,
            [new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>()),
             new("fixture.healthy", "1.0.0", false, false, new Dictionary<string, object>())]));
        await store.CreateAsync(new("second", "Second", game.Id, "test-2", null,
            [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>()),
             new("fixture.healthy", "1.0.0", true, false, new Dictionary<string, object>())]));
        await store.CreateAsync(new("unrelated", "Unrelated", game.Id, "test-2", null,
            [new("fixture.optional", "2.0.0", false, false, new Dictionary<string, object>())]));
        await using var backend = new LauncherBackend(Path.Combine(_root, "games"), data);
        var replies = new System.Collections.Concurrent.ConcurrentQueue<JsonNode>();
        var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
        async Task Call(long id, string method, object arguments)
        {
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), default);
            Assert.Null(replies.Single(row => row["id"]?.GetValue<long>() == id)["error"]);
        }
        await Call(1, "plugins.list", new { instanceId = "first" });
        Assert.Equal("fixture.healthy", Assert.Single(store.Load("first").Plugins).Id);
        Assert.False(Assert.Single(store.Load("first").Plugins).Enabled);
        Assert.True(Assert.Single(store.Load("second").Plugins, plugin => plugin.Id == "fixture.healthy").Enabled);
        if (successor)
        {
            var migrated = Assert.Single(store.Load("second").Plugins, plugin => plugin.Id == "fixture.successor");
            Assert.False(migrated.Enabled);
        }
        else Assert.Single(store.Load("second").Plugins);
        Assert.Equal("2.0.0", Assert.Single(store.Load("unrelated").Plugins).Version);
        if (brokenFolder is not null) Assert.False(Directory.Exists(brokenFolder));
        await Call(2, "plugins.list", new { instanceId = "second" });
        await Call(3, "plugins.list", new { instanceId = "first" });
        var notice = Assert.Single(Assert.Single(replies, row => row["event"]?.GetValue<string>() == "plugins.recovered")["data"]!.AsArray())!;
        Assert.Equal("fixture.optional", notice["pluginId"]!.GetValue<string>());
        Assert.Equal("1.0.0", notice["version"]!.GetValue<string>());
        await Call(4, "plugins.dismissRecoveryNotice", new { game = game.Id, pluginId = "fixture.optional", version = "1.0.0" });
        await Call(5, "plugins.recoveryNotices", new { });
        Assert.Empty(replies.Single(row => row["id"]?.GetValue<long>() == 5L)["result"]!.AsArray());
    }

    [Fact]
    public async Task UnsupportedClientLibraryShowsOnlyCanonicalIdentity()
    {
        var game = Game();
        File.WriteAllText(Path.Combine(game.Folder, "builds", "test-2.toml"),
            File.ReadAllText(Path.Combine(game.Folder, "builds", "test-1.toml")).Replace("test-1", "test-2", StringComparison.Ordinal));
        game = Assert.Single(GameCatalog.Load(Path.Combine(_root, "games")));
        var data = Path.Combine(_root, "unsupported-data");
        using (var library = new PluginLibrary(Path.Combine(data, "plugins")))
        {
            var preview = library.Prepare(Folder(), game);
            library.Install(preview.Token, game, game.Builds.Single(build => build.Id == "test-1"));
        }
        var bundled = Directory.CreateDirectory(Path.Combine(game.Folder, "plugins", "fixture.successor")).FullName;
        File.WriteAllText(Path.Combine(bundled, "plugin.toml"), Manifest.Replace("id = \"fixture.optional\"",
            "id = \"fixture.successor\"\naliases = [\"fixture.optional\"]", StringComparison.Ordinal));
        var store = new InstanceStore(Path.Combine(data, "instances"));
        await store.CreateAsync(new("client", "Client", game.Id, "test-2", null, []));
        await using var backend = new LauncherBackend(Path.Combine(_root, "games"), data);
        var replies = new System.Collections.Concurrent.ConcurrentQueue<JsonNode>();
        var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
        await bridge.HandleAsync(JsonSerializer.Serialize(new { id = 1L, method = "plugins.list", @params = new { instanceId = "client" } }), default);
        var reply = replies.Single(row => row["id"]?.GetValue<long>() == 1L);
        Assert.Null(reply["error"]);
        var plugin = Assert.Single(reply["result"]!.AsArray())!;
        Assert.Equal("fixture.successor", plugin["id"]!.GetValue<string>());
        Assert.False(plugin["available"]!.GetValue<bool>());
        Assert.DoesNotContain(replies, row => row["event"]?.GetValue<string>() == "plugins.recovered");
    }
}
