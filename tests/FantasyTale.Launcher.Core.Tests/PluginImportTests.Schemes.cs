using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class PluginImportTests
{
    [Fact]
    public void EmptyMaintenanceAndReaderDisposalDoNotAcquireTheSharedLibraryLease()
    {
        var folder = Path.Combine(_root, "idle-library");
        using var library = new PluginLibrary(folder);
        var store = new InstanceStore(Path.Combine(_root, "idle-instances"));
        using var mutation = new FileStream(Path.Combine(folder, ".library.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        library.CleanupUninstalled(store, _ => false);
        library.Dispose();
    }

    [Fact]
    public void PendingImportDisposalStillRequiresTheLibraryLeaseAndCanRetryAfterRelease()
    {
        var game = Game();
        var folder = Path.Combine(_root, "pending-library");
        using var library = new PluginLibrary(folder);
        _ = library.Prepare(Folder(), game);
        using (var mutation = new FileStream(Path.Combine(folder, ".library.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => library.Dispose());
        library.Dispose();
        Assert.Empty(Directory.GetDirectories(Path.Combine(folder, "staging")));
    }

    [Fact]
    public async Task UnchangedSchemesAndInventoryRemainReadableWhileRealEditsReportTheLeaseContext()
    {
        var game = Game();
        var data = Path.Combine(_root, "scheme-lease-data");
        var store = new InstanceStore(Path.Combine(data, "instances"));
        using (var library = new PluginLibrary(Path.Combine(data, "plugins")))
            library.Install(library.Prepare(Folder(), game).Token, game, game.Builds[0]);
        var instance = await store.CreateAsync(new("client", "Client", game.Id, game.Builds[0].Id, null,
            [new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>())]));
        await using var backend = new LauncherBackend(Path.Combine(_root, "games"), data);
        var replies = new System.Collections.Concurrent.ConcurrentQueue<JsonNode>();
        var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
        long sequence = 0;
        async Task<JsonNode> Call(string method, object arguments)
        {
            var id = ++sequence;
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), default);
            return replies.Single(row => row["id"]?.GetValue<long>() == id);
        }
        using (store.Acquire(instance.Id))
        {
            var unchanged = await Call("plugins.applyScheme", new { instanceId = instance.Id,
                plugins = instance.Plugins.Select(plugin => new { id = plugin.Id, version = plugin.Version, enabled = plugin.Enabled, options = plugin.Options }) });
            Assert.Null(unchanged["error"]);
            Assert.True(Assert.Single(unchanged["result"]!["plugins"]!.AsArray())!["enabled"]!.GetValue<bool>());
            Assert.Null((await Call("plugins.list", new { instanceId = instance.Id }))["error"]);
            var edit = await Call("plugins.applyScheme", new
            {
                instanceId = instance.Id,
                plugins = new[] { new { id = "fixture.optional", version = "1.0.0", enabled = false, options = new { } } },
            });
            Assert.Equal("instance_in_use", edit["code"]!.GetValue<string>());
            var context = edit["instanceFailure"]!;
            Assert.Equal("plugins.applyScheme", context["operation"]!.GetValue<string>());
            Assert.Equal(instance.Id, context["instanceId"]!.GetValue<string>());
            Assert.Equal(store.ClientFolder(instance.Id), context["clientPath"]!.GetValue<string>());
            Assert.EndsWith(".lock", context["lockPath"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.True(context["osError"]!.GetValue<int>() is 32 or 33);
            Assert.True(Assert.Single(store.Load(instance.Id).Plugins).Enabled);
        }
        var retried = await Call("plugins.applyScheme", new
        {
            instanceId = instance.Id,
            plugins = new[] { new { id = "fixture.optional", version = "1.0.0", enabled = false, options = new { } } },
        });
        Assert.Null(retried["error"]);
        Assert.False(Assert.Single(store.Load(instance.Id).Plugins).Enabled);
    }

    [Fact]
    public async Task SchemeRepairsBrokenPinsBeforeShowingMissingRequiredComponents()
    {
        var game = Game();
        var gameFile = Path.Combine(game.Folder, "game.toml");
        File.AppendAllText(gameFile, "default_plugins = [\"fixture.bridge\"]\nstore_managed_plugins = [\"fixture.login\"]\n");
        var buildFile = Path.Combine(game.Folder, "builds", "test-1.toml");
        File.AppendAllText(buildFile, "\n[[client_policy_rules]]\nplugin = \"fixture.login\"\nid = \"login-required\"\ntype = \"require\"\n");
        game = Assert.Single(GameCatalog.Load(Path.Combine(_root, "games")));
        var build = game.Builds[0];
        var bundle = Path.Combine(game.Folder, "plugins", "fixture.bridge");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, "plugin.toml"), Manifest.Replace("fixture.optional", "fixture.bridge", StringComparison.Ordinal));
        var data = Path.Combine(_root, "upgrade-data");
        string brokenFolder;
        using (var library = new PluginLibrary(Path.Combine(data, "plugins")))
        {
            var preview = library.Prepare(Folder(), game);
            brokenFolder = library.Install(preview.Token, game, build).Folder;
        }
        File.Delete(Path.Combine(brokenFolder, "plugin.toml"));
        var store = new InstanceStore(Path.Combine(data, "instances"));
        await store.CreateAsync(new("client", "Client", game.Id, build.Id, null,
            [new("fixture.bridge", "3.1.0", true, false, new Dictionary<string, object>()),
             new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>()),
             new("fixture.login", "1.0.0", true, true, new Dictionary<string, object>())]));
        await using var backend = new LauncherBackend(Path.Combine(_root, "games"), data);
        var replies = new System.Collections.Concurrent.ConcurrentQueue<JsonNode>();
        var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
        long sequence = 0;
        async Task<JsonNode?> Call(string method, object arguments)
        {
            var id = ++sequence;
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), default);
            var reply = replies.Single(row => row["id"]?.GetValue<long>() == id);
            Assert.Null(reply["error"]);
            return reply["result"];
        }
        await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false });
        var changed = await Call("plugins.applyScheme", new { instanceId = "client", plugins = Array.Empty<object>() });
        var selected = Assert.Single(changed!["plugins"]!.AsArray())!;
        Assert.Equal("fixture.bridge", selected["id"]!.GetValue<string>());
        Assert.Equal("1.0.0", selected["version"]!.GetValue<string>());
        Assert.False(Directory.Exists(brokenFolder));
        var recovery = Assert.Single(replies, row => row["event"]?.GetValue<string>() == "plugins.recovered")["data"]!.AsArray();
        Assert.Contains(recovery, row => row!["pluginId"]!.GetValue<string>() == "fixture.bridge" && row["version"]!.GetValue<string>() == "3.1.0");
        Assert.Contains(recovery, row => row!["pluginId"]!.GetValue<string>() == "fixture.optional" && row["reason"]!.GetValue<string>() == "corrupt");
        var warnings = (await Call("plugins.launchWarnings", new { instanceId = "client" }))!.AsArray();
        var missing = Assert.Single(warnings)!;
        Assert.Equal("fixture.login", missing["id"]!.GetValue<string>());
        Assert.False(missing["installed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task LibraryAndLaunchWarningsUseOneEnabledRequiredSuccessorAfterMigration()
    {
        var game = Game();
        var build = game.Builds[0];
        File.AppendAllText(Path.Combine(game.Folder, "builds", build.Id + ".toml"), """

            [[client_policy_rules]]
            plugin = "fixture.successor"
            id = "required-login"
            type = "require"
            """);
        game = Assert.Single(GameCatalog.Load(Path.Combine(_root, "games")));
        var data = Path.Combine(_root, "migration-data");
        string formerFolder;
        using (var library = new PluginLibrary(Path.Combine(data, "plugins")))
        {
            var preview = library.Prepare(Folder(), game);
            formerFolder = library.Install(preview.Token, game, build).Folder;
        }

        var bundled = Directory.CreateDirectory(Path.Combine(game.Folder, "plugins", "fixture.successor")).FullName;
        File.WriteAllText(Path.Combine(bundled, "plugin.toml"), Manifest
            .Replace("id = \"fixture.optional\"", "id = \"fixture.successor\"\naliases = [\"fixture.optional\"]", StringComparison.Ordinal)
            .Replace("version = \"1.0.0\"", "version = \"0.1.1\"", StringComparison.Ordinal));
        var store = new InstanceStore(Path.Combine(data, "instances"));
        await store.CreateAsync(new("client", "Client", game.Id, build.Id, null,
            [new("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())]));

        await using var backend = new LauncherBackend(Path.Combine(_root, "games"), data);
        var replies = new System.Collections.Concurrent.ConcurrentQueue<JsonNode>();
        var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
        long sequence = 0;
        async Task<JsonNode?> Call(string method, object arguments)
        {
            var id = ++sequence;
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), default);
            var reply = replies.Single(row => row["id"]?.GetValue<long>() == id);
            Assert.Null(reply["error"]);
            return reply["result"];
        }

        var row = Assert.Single((await Call("plugins.list", new { instanceId = "client" }))!.AsArray())!;
        // Inventory stays readable while the background update check owns the migration lease.
        using var migrationDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (row["selection"] is null)
        {
            await Task.Delay(20, migrationDeadline.Token);
            row = Assert.Single((await Call("plugins.list", new { instanceId = "client" }))!.AsArray())!;
        }
        Assert.Equal("fixture.successor", row["id"]!.GetValue<string>());
        Assert.Equal("0.1.1", row["version"]!.GetValue<string>());
        Assert.True(row["required"]!.GetValue<bool>());
        Assert.NotNull(row["selection"]);
        Assert.True(row["selection"]!["enabled"]!.GetValue<bool>());
        var scheme = await Call("plugins.applyScheme", new
        {
            instanceId = "client",
            plugins = new[]
            {
                new { id = "fixture.optional", version = "1.0.0", enabled = false, options = new { } },
                new { id = "fixture.successor", version = "0.1.1", enabled = true, options = new { } },
            },
        });
        Assert.Equal("fixture.successor", Assert.Single(scheme!["plugins"]!.AsArray())!["id"]!.GetValue<string>());
        Assert.Empty((await Call("plugins.launchWarnings", new { instanceId = "client" }))!.AsArray());
        Assert.True(Directory.Exists(formerFolder));
    }

    [Fact]
    public async Task SharedSchemeAppliesJsonScalarOptionsAndIgnoresUninstalledPackages()
    {
        var game = Game();
        var data = Path.Combine(_root, "scheme-data");
        await using var backend = new LauncherBackend(Path.Combine(_root, "games"), data);
        var replies = new List<JsonNode>();
        var bridge = new BridgeRouter(raw => replies.Add(JsonNode.Parse(raw)!)); backend.Register(bridge);
        long sequence = 0;
        async Task<JsonNode?> Call(string method, object arguments)
        {
            var id = ++sequence;
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = arguments }), default);
            var reply = replies.Single(row => row["id"]?.GetValue<long>() == id);
            Assert.Null(reply["error"]);
            return reply["result"];
        }
        await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false });
        var folder = Folder(Manifest + """

            [[options]]
            key = "color"
            type = "enum"
            default = "white"
            values = ["white", "yellow"]
            label = { en = "Color" }
            [[options]]
            key = "limit"
            type = "int"
            default = 1
            label = { en = "Limit" }
            [[options]]
            key = "active"
            type = "bool"
            default = false
            label = { en = "Active" }
            """);
        var preview = await Call("plugins.prepareImport", new { game = game.Id, path = folder });
        await Call("plugins.installImport", new { game = game.Id, token = preview!["token"]!.GetValue<string>() });
        await Call("instances.create", new { id = "client", name = "Client", game = game.Id, build = game.Builds[0].Id });
        var updated = await Call("plugins.applyScheme", new
        {
            instanceId = "client",
            plugins = new object[]
            {
                new { id = "fixture.optional", version = "1.0.0", enabled = true, required = true, options = new { color = "yellow", limit = 7, active = true } },
                new { id = "fixture.missing", version = "1.0.0", enabled = true, options = new { } },
            },
        });
        var selected = Assert.Single(updated!["plugins"]!.AsArray())!;
        Assert.False(selected["required"]!.GetValue<bool>());
        Assert.Equal(7L, selected["options"]!["limit"]!.GetValue<long>());
        Assert.True(selected["options"]!["active"]!.GetValue<bool>());
        var saved = new InstanceStore(Path.Combine(data, "instances")).Load("client");
        Assert.Equal("yellow", Assert.Single(saved.Plugins).Options["color"]);
        Assert.Equal(7L, Assert.Single(saved.Plugins).Options["limit"]);
    }
}
