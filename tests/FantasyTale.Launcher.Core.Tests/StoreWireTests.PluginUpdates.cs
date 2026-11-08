using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class StoreWireTests
{
    private const string UpgradePlugin = """
        schema = 1
        [plugin]
        id = "fixture.optional"
        game = "fixture"
        version = "1.0.0"
        name = { en = "Optional" }
        kind = "patch"
        runtime = ">=3.0.0 <4.0.0"
        [compatibility]
        builds = ["test-1"]
        [[patches]]
        id = "fixture"
        locate = { pattern = "AA BB CC" }
        expect = "AA"
        write = "90"
        [[options]]
        key = "font_color"
        type = "enum"
        default = "white"
        values = ["white", "yellow"]
        label = { en = "Color" }
        """;

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("0.1.1")]
    public void PluginUpdatesPreferAnEligibleSuccessorWithoutComparingDifferentVersionSeries(string successorVersion)
    {
        var oldPlugin = PluginCatalog.Parse("old-plugin.toml", UpgradePlugin) with { Id = "fixture.legacy-plugin" };
        var publishedDefinition = PluginCatalog.Parse("new-plugin.toml", UpgradePlugin.Replace(
            "id = \"fixture.optional\"", "id = \"fixture.current-plugin\"", StringComparison.Ordinal)) with
        {
            Version = successorVersion, Aliases = [oldPlugin.Id]
        };
        var candidate = new StorePackage(publishedDefinition.Id, "plugin", "fixture", [], publishedDefinition.Version,
            new Dictionary<string, string> { ["en"] = "Test" }, new Dictionary<string, string>(), "Test", 1,
            "manifest.toml", new string('a', 64)) { PluginDefinition = publishedDefinition };
        var origin = new PluginStoreOrigin("source", new string('b', 64), new string('c', 64));
        var build = new BuildDefinition("test-1", "test", 1, new(new Dictionary<string, string> { ["en"] = "Test" }),
            "client.exe", "", "", null, null, "fixture", [], []);

        var formerUpdate = candidate with { Id = oldPlugin.Id, Version = "9.0.0", PluginDefinition = null };
        var (status, package) = PluginUpdateService.Select(oldPlugin, origin, build, new("source", candidate.Name, [formerUpdate, candidate]));

        Assert.Equal("available", status);
        Assert.Same(candidate, package);
        Assert.Same(formerUpdate, PluginUpdateService.Select(oldPlugin, origin, build,
            new("source", candidate.Name, [formerUpdate, candidate with { Builds = ["other-1"] }])).Package);
    }

    [Theory]
    [InlineData("http", false)]
    [InlineData("alist", false)]
    [InlineData("http", true)]
    [InlineData("alist", true)]
    [InlineData("http", false, true)]
    [InlineData("alist", true, true)]
    [InlineData("http", false, true, true)]
    [InlineData("alist", true, true, true)]
    public async Task PluginUpgradesRecheckTheirSourceAndSwitchOnlyTheRequestedClient(string kind, bool zipped, bool latestOnly = false, bool bundled = false)
    {
        var root = Directory.CreateTempSubdirectory("ft-plugin-upgrade-").FullName;
        try
        {
            var games = Path.Combine(root, "games"); var folder = Path.Combine(games, "Fixture"); Directory.CreateDirectory(Path.Combine(folder, "builds"));
            await File.WriteAllTextAsync(Path.Combine(folder, "game.toml"), "schema = 1\n[game]\nid = \"fixture\"\nstatus = \"enabled\"\nmodule = \"Fixture\"\nhook_host = \"external\"\nname = { en = \"Fixture\" }\n");
            await File.WriteAllTextAsync(Path.Combine(folder, "builds", "test-1.toml"), "schema = 1\n[build]\nid = \"test-1\"\ngame = \"fixture\"\nbranch = \"test\"\nprotocol_version = 1\nexecutable = \"client.exe\"\nname = { en = \"Fixture\" }\n[launch]\nlogin = \"none\"\n");
            if (latestOnly) File.AppendAllText(Path.Combine(folder, "builds", "test-1.toml"),
                "\n[[client_policy_rules]]\nplugin = \"fixture.optional\"\nid = \"latest\"\ntype = \"latest\"\n");
            var documents = new ConcurrentDictionary<string, byte[]>();
            (string Catalog, string Hash) Release(string version, string build = "test-1", string? minimumLauncher = null)
            {
                var definition = UpgradePlugin.Replace("1.0.0", version, StringComparison.Ordinal);
                if (minimumLauncher is not null) definition = definition.Replace("kind = \"patch\"", "kind = \"patch\"\nminimum_launcher_version = \"" + minimumLauncher + "\"", StringComparison.Ordinal);
                if (latestOnly) definition = definition.Replace("kind = \"patch\"", "kind = \"patch\"\nupdate_source = \"fixture\"", StringComparison.Ordinal);
                var payload = Encoding.UTF8.GetBytes(definition);
                var file = zipped ? "plugin.zip" : "plugin.toml";
                if (zipped)
                {
                    using var buffer = new MemoryStream();
                    using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true)) { using var entry = zip.CreateEntry("plugin.toml").Open(); entry.Write(payload); }
                    payload = buffer.ToArray();
                }
                var format = zipped ? "format = \"zip\"\n" : "";
                var manifest = Encoding.UTF8.GetBytes($"schema = 1\n[release]\n{format}id = \"fixture.optional\"\ngame = \"fixture\"\nbuilds = [\"{build}\"]\nversion = \"{version}\"\n[[files]]\npath = \"{file}\"\nsource_path = \"{version}/{file}\"\nsize = {payload.Length}\nsha256 = \"{Hash(payload)}\"\n");
                documents[$"{version}/{file}"] = payload; documents[$"{version}/manifest.toml"] = manifest;
                var minimum = minimumLauncher is null ? "" : $"minimum_launcher_version = \"{minimumLauncher}\"\n";
                return ($"\n[[packages]]\nid = \"fixture.optional\"\nkind = \"plugin\"\ngame = \"fixture\"\nbuilds = [\"{build}\"]\nversion = \"{version}\"\n{minimum}name = {{ en = \"Optional\" }}\npublisher = \"Fixture\"\nsize = {payload.Length}\nmanifest = \"{version}/manifest.toml\"\nmanifest_sha256 = \"{Hash(manifest)}\"\n", Hash(manifest));
            }
            const string header = "schema = 2\n[catalog]\nid = \"fixture\"\nname = { en = \"Fixture\" }\n";
            var first = Release("1.0.0"); var next = Release("2.0.0"); var incompatible = Release("3.0.0", "other-1"); var preview = Release("4.0.0-beta.1");
            var future = Release("5.0.0", minimumLauncher: "999.0.0");
            void Catalog(string text) => documents["FantasyTale/source.toml"] = Encoding.UTF8.GetBytes(header + text);
            Catalog(first.Catalog);
            await using var server = new WireServer(documents);
            var dataRoot = Path.Combine(root, "data");
            new ClientUpdateSettings(dataRoot).Save(new(false, false, false, false));
            var sources = new StoreSourceStore(Path.Combine(dataRoot, "sources"));
            sources.Save(sources.List().Single(source => source.BuiltIn) with { Enabled = false });
            if (bundled)
            {
                File.AppendAllText(Path.Combine(folder, "game.toml"), "store_managed_plugins = [\"fixture.optional\"]\n");
                var bundledFolder = Directory.CreateDirectory(Path.Combine(folder, "plugins", "fixture.optional")).FullName;
                File.WriteAllText(Path.Combine(bundledFolder, "plugin.toml"), UpgradePlugin.Replace("kind = \"patch\"",
                    "kind = \"patch\"\nupdate_source = \"fixture\"", StringComparison.Ordinal) + "\n# Bundled metadata differs from the published snapshot.\n");
            }
            await using var backend = new LauncherBackend(games, dataRoot);
            var replies = new ConcurrentQueue<JsonNode>(); var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge); long sequence = 0;
            async Task<JsonNode?> Call(string method, object? args = null, bool fails = false)
            {
                var id = ++sequence; await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = args }), default);
                var reply = replies.Single(r => r["id"]?.GetValue<long>() == id);
                if (fails) Assert.NotNull(reply["error"]); else Assert.True(reply["error"] is null, $"{method}: {reply["error"]}");
                return reply["result"];
            }
            Task<JsonNode?> Source(string url, bool enabled = true) => Call("store.saveSource", new { id = "fixture", name = "Fixture", kind, baseUrl = url, catalogPath = "FantasyTale/source.toml", enabled });
            async Task Finish()
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (true)
                {
                    var tasks = (await Call("downloads.list"))!.AsArray();
                    Assert.True(tasks.All(t => t!["stage"]!.GetValue<string>() != "failed"), tasks.ToJsonString());
                    if (tasks.All(t => t!["stage"]!.GetValue<string>() == "completed")
                        && (await Call("plugins.pendingUpgrades", new { instanceId = "first" }))!["count"]!.GetValue<int>() == 0) return;
                    await Task.Delay(20, deadline.Token);
                }
            }
            async Task<JsonNode> Check() => Assert.Single((await Call("plugins.checkUpdates", new { instanceId = "first" }))!.AsArray())!;
            var install = new { instanceId = "first", id = "fixture.optional", installedVersion = "1.0.0", version = "2.0.0", manifestSha256 = next.Hash };
            await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false, autoCheckPlugins = false, autoUpdatePlugins = false, removeOldPluginVersions = false }); await Source(server.Address);
            foreach (var id in new[] { "first", "second" }) await Call("instances.create", new { id, name = id, game = "fixture", build = "test-1" });
            if (bundled)
            {
                await Call("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "1.0.0", enabled = true });
                Catalog("");
                await Call("plugins.ensureLatest", new { instanceId = "first" }, fails: true);
                Assert.Equal("plugin_latest_unpublished", replies.Last(r => r["id"] is not null)["error"]!.GetValue<string>());
                Catalog(first.Catalog);
                Assert.Single((await Call("plugins.ensureLatest", new { instanceId = "first" }))!.AsArray());
                await Finish();
                Assert.Empty((await Call("plugins.ensureLatest", new { instanceId = "first" }))!.AsArray());
            }
            await Call("store.installPlugin", new { sourceId = "fixture", packageId = "fixture.optional", version = "1.0.0", manifestSha256 = first.Hash, instanceId = "first" }); await Finish();
            foreach (var id in new[] { "first", "second" }) await Call("plugins.configure", new { instanceId = id, id = "fixture.optional", version = "1.0.0", enabled = true,
                options = new { font_color = id == "first" ? "yellow" : "white" } });
            var baseline = System.IO.Directory.GetFiles(Path.Combine(dataRoot, "plugins", "packages"), "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)), StringComparer.Ordinal);
            Catalog(first.Catalog + next.Catalog + incompatible.Catalog + preview.Catalog + future.Catalog);
            await Call("store.catalog", new { id = "fixture", force = true });
            backend.FrontendReady();
            await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false, autoCheckPlugins = true, autoUpdatePlugins = false, removeOldPluginVersions = false });
            var snapshot = await Check();
            Assert.Equal("available", snapshot["status"]!.GetValue<string>());
            await Call("settings.setUpdates", new { autoCheck = false, autoDownload = false, autoCheckPlugins = false, autoUpdatePlugins = false, removeOldPluginVersions = false });
            var available = await Check(); Assert.Equal("available", available["status"]!.GetValue<string>()); Assert.Equal("2.0.0", available["version"]!.GetValue<string>());
            await Call("store.saveSource", new { id = "mirror", name = "Mirror", kind, baseUrl = server.Address,
                catalogPath = "FantasyTale/source.toml", enabled = true });
            await Source(server.Address + "changed/");
            Assert.Equal("available", (await Check())["status"]!.GetValue<string>());
            await Source(server.Address, false);
            Assert.Equal("available", (await Check())["status"]!.GetValue<string>());
            await Source(server.Address);
            Catalog(first.Catalog + next.Catalog.Replace(next.Hash, new string('a', 64), StringComparison.Ordinal));
            await Call("plugins.installUpdate", install, fails: true);
            Catalog(first.Catalog.Replace(first.Hash, new string('b', 64), StringComparison.Ordinal) + next.Catalog);
            Assert.Equal("available", (await Check())["status"]!.GetValue<string>());
            Catalog(first.Catalog + next.Catalog + incompatible.Catalog + preview.Catalog + future.Catalog);
            if (latestOnly)
            {
                await Call("plugins.ensureLatest", new { instanceId = "first" }, fails: true);
                var rejected = replies.Last(r => r["id"] is not null);
                Assert.Equal("launcher_update_required", rejected["code"]!.GetValue<string>());
                Assert.Equal("999.0.0", rejected["minimumLauncherVersion"]!.GetValue<string>());
                AssertSelection("first", "1.0.0", true, "yellow");
                Assert.All((await Call("downloads.list"))!.AsArray(), task => Assert.Equal("1.0.0", task!["target"]!["toVersion"]!.GetValue<string>()));
                Catalog(first.Catalog + next.Catalog + incompatible.Catalog + preview.Catalog);
            }
            await Source(server.Address, false);
            if (latestOnly) Assert.Single((await Call("plugins.ensureLatest", new { instanceId = "first" }))!.AsArray());
            else await Call("plugins.installUpdate", install);
            await Finish();
            Assert.Contains((await Call("downloads.list"))!.AsArray(), task =>
                task!["target"]!["toVersion"]!.GetValue<string>() == "2.0.0" && task["target"]!["sourceName"]!.GetValue<string>() == "Mirror");
            var updates = (await Call("plugins.checkUpdates", new { instanceId = "first" }))!.AsArray();
            Assert.Equal("installed", updates.Single(x => x!["installedVersion"]!.GetValue<string>() == "1.0.0")!["status"]!.GetValue<string>());
            Assert.Equal("current", updates.Single(x => x!["installedVersion"]!.GetValue<string>() == "2.0.0")!["status"]!.GetValue<string>());
            foreach (var id in new[] { "first", "second" })
            {
                var plugins = (await Call("plugins.list", new { instanceId = id }))!.AsArray(); Assert.Equal(2, plugins.Count);
                Assert.Equal(id == "first" ? "2.0.0" : "1.0.0", Assert.Single(plugins, p => p!["selection"] is not null)!["version"]!.GetValue<string>());
            }
            AssertSelection("first", "2.0.0", true, "yellow");
            AssertSelection("second", "1.0.0", true, "white");
            if (latestOnly)
            {
                Assert.Empty((await Call("plugins.ensureLatest", new { instanceId = "first" }))!.AsArray());
                await Call("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "1.0.0", enabled = true }, fails: true);
                Catalog(first.Catalog);
                await Call("plugins.ensureLatest", new { instanceId = "first" }, fails: true);
                Catalog(first.Catalog + next.Catalog);
                var local = System.IO.Directory.GetFiles(Path.Combine(dataRoot, "plugins", "packages"), "plugin.toml", SearchOption.AllDirectories)
                    .Single(path => File.ReadAllText(path).Contains("version = \"2.0.0\"", StringComparison.Ordinal));
                File.AppendAllText(local, "\n# changed\n");
                await Call("plugins.ensureLatest", new { instanceId = "first" }, fails: true);
                return;
            }
            var instances = new InstanceStore(Path.Combine(dataRoot, "instances"));
            using (instances.Acquire("first"))
                await Call("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "2.0.0", enabled = true }, fails: true);
            await Call("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "2.0.0", enabled = true });
            Assert.Equal("2.0.0", Assert.Single((await Call("plugins.list", new { instanceId = "first" }))!.AsArray(), p => p!["selection"] is not null)!["version"]!.GetValue<string>());
            Assert.Equal("1.0.0", Assert.Single((await Call("plugins.list", new { instanceId = "second" }))!.AsArray(), p => p!["selection"] is not null)!["version"]!.GetValue<string>());
            AssertSelection("first", "2.0.0", true, "yellow");
            AssertSelection("second", "1.0.0", true, "white");
            await Call("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "2.0.0", enabled = false });
            AssertSelection("first", "2.0.0", false, "yellow");
            await Call("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "1.0.0", enabled = true });
            AssertSelection("first", "1.0.0", true, "yellow");
            AssertSelection("second", "1.0.0", true, "white");
            await Call("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "1.0.0", enabled = false });
            await Call("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "2.0.0", enabled = false,
                selectVersion = true, expectedVersion = "1.0.0" });
            AssertSelection("first", "2.0.0", false, "yellow");
            AssertSelection("second", "1.0.0", true, "white");
            await Call("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "1.0.0", enabled = false }, fails: true);
            await Call("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "1.0.0", enabled = false,
                selectVersion = true, expectedVersion = "1.0.0" }, fails: true);
            AssertSelection("first", "2.0.0", false, "yellow");
            await Call("plugins.configure", new { instanceId = "first", id = "fixture.optional", version = "1.0.0", enabled = false,
                selectVersion = true, expectedVersion = "2.0.0" });
            AssertSelection("first", "1.0.0", false, "yellow");
            foreach (var (path, digest) in baseline) Assert.Equal(digest, SHA256.HashData(File.ReadAllBytes(path)));

            void AssertSelection(string id, string version, bool enabled, string color)
            {
                var persisted = new InstanceStore(Path.Combine(dataRoot, "instances")).Load(id);
                var selected = Assert.Single(persisted.Plugins);
                Assert.Equal(version, selected.Version);
                Assert.Equal(enabled, selected.Enabled);
                Assert.Equal(color, selected.Options["font_color"]);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("1.0.0", "2.0.0-beta.1", "current")]
    [InlineData("2.0.0-beta.1", "2.0.0-beta.2", "available")]
    [InlineData("2.0.0-beta.2", "2.0.0", "available")]
    [InlineData("2.0.0", "1.9.0", "current")]
    [InlineData("1.0.0+one", "1.0.0+two", "current")]
    public void PluginUpgradeSelectionRespectsSemVerAndStableReleases(string installedVersion, string nextVersion, string expected)
    {
        var root = Directory.CreateTempSubdirectory("ft-plugin-select-").FullName;
        try
        {
            var file = Path.Combine(root, "plugin.toml"); File.WriteAllText(file, UpgradePlugin.Replace("1.0.0", installedVersion, StringComparison.Ordinal));
            var plugin = PluginCatalog.LoadFile(file);
            var build = new BuildDefinition("test-1", "test", 1, new(new Dictionary<string, string> { ["en"] = "Test" }), "client.exe", "", "", null, null, "fixture", [], []);
            var p = new StorePackage(plugin.Id, "plugin", "fixture", [], nextVersion, new Dictionary<string, string> { ["en"] = "Test" }, new Dictionary<string, string>(), "Test", 1, "manifest.toml", new string('a', 64));
            var origin = new PluginStoreOrigin("source", new string('b', 64), new string('c', 64));
            Assert.Equal(expected, PluginUpdateService.Select(plugin, origin, build, new("source", p.Name, [p])).Status);
            var distinct = p with { Version = "99.0.0+one" };
            Assert.Equal("integrity", PluginUpdateService.Select(plugin, origin, build, new("source", p.Name, [distinct, distinct with { Version = "99.0.0+two" }])).Status);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(null, "available")]
    [InlineData("0.0.1", "available")]
    [InlineData("999.0.0", "current")]
    public void PluginUpdatesOfferOnlyLauncherCompatibleReleases(string? minimumLauncher, string expected)
    {
        var plugin = PluginCatalog.Parse("plugin.toml", UpgradePlugin);
        var build = new BuildDefinition("test-1", "test", 1, new(new Dictionary<string, string> { ["en"] = "Test" }),
            "client.exe", "", "", null, null, "fixture", [], []);
        var candidate = new StorePackage(plugin.Id, "plugin", "fixture", [], "2.0.0", new Dictionary<string, string> { ["en"] = "Test" },
            new Dictionary<string, string>(), "Test", 1, "manifest.toml", new string('a', 64))
            { MinimumLauncherVersion = minimumLauncher ?? "" };
        var origin = new PluginStoreOrigin("source", new string('b', 64), new string('c', 64));

        var (status, package) = PluginUpdateService.Select(plugin, origin, build, new("source", candidate.Name, [candidate]));

        Assert.Equal(expected, status);
        Assert.Equal(expected == "available" ? candidate : null, package);
        var alteredBaseline = candidate with { Version = plugin.Version, MinimumLauncherVersion = "999.0.0" };
        Assert.Equal(expected, PluginUpdateService.Select(plugin, origin, build,
            new("source", candidate.Name, [alteredBaseline, candidate])).Status);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
