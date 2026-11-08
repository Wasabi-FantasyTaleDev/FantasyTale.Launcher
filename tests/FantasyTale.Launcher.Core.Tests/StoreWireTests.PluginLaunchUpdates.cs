using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class StoreWireTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupDiscoverySharesFirstCatalogBeforeFrontendAndLaunchReadsCachedProgress(bool automatic)
    {
        var root = Directory.CreateTempSubdirectory("ft-plugin-startup-").FullName;
        var releaseCatalog = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var payloadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePayload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var games = Path.Combine(root, "games"); var folder = Path.Combine(games, "Fixture");
            Directory.CreateDirectory(Path.Combine(folder, "builds"));
            File.WriteAllText(Path.Combine(folder, "game.toml"), "schema=1\n[game]\nid='fixture'\nstatus='enabled'\nmodule='Fixture'\nhook_host='external'\nname={en='Fixture'}\nstore_managed_plugins=['fixture.optional']\nlauncher_login='none'\n");
            File.WriteAllText(Path.Combine(folder, "builds", "test-1.toml"), "schema=1\n[build]\nid='test-1'\ngame='fixture'\nbranch='test'\nprotocol_version=1\nexecutable='client.exe'\nname={en='Fixture'}\n[launch]\nlogin='none'\n");
            var buildProfile = Path.Combine(games, "Fixture", "builds", "test-1.toml");
            File.AppendAllText(buildProfile, "\n[[client_policy_rules]]\nplugin = \"fixture.optional\"\nid = \"latest\"\ntype = \"latest\"\n");
            var definition = UpgradePlugin.Replace("kind = \"patch\"", "kind = \"patch\"\nupdate_source='fixture'", StringComparison.Ordinal);
            var bundled = Directory.CreateDirectory(Path.Combine(folder, "plugins", "fixture.optional")).FullName;
            File.WriteAllText(Path.Combine(bundled, "plugin.toml"), definition);
            var payload = Encoding.UTF8.GetBytes(definition.Replace("1.0.0", "2.0.0", StringComparison.Ordinal));
            var manifest = Encoding.UTF8.GetBytes($"schema=1\n[release]\nid='fixture.optional'\ngame='fixture'\nbuilds=['test-1']\nversion='2.0.0'\n[[files]]\npath='plugin.toml'\nsource_path='2/plugin.toml'\nsize={payload.Length}\nsha256='{Hash(payload)}'\n");
            var documents = new ConcurrentDictionary<string, byte[]>();
            documents["2/plugin.toml"] = payload; documents["2/manifest.toml"] = manifest;
            documents["store.toml"] = Encoding.UTF8.GetBytes($"schema=2\n[catalog]\nid='fixture'\nname={{en='Fixture'}}\n[[packages]]\nid='fixture.optional'\nkind='plugin'\ngame='fixture'\nbuilds=['test-1']\nversion='2.0.0'\nname={{en='Authentication'}}\npublisher='Fixture'\nsize={payload.Length}\nmanifest='2/manifest.toml'\nmanifest_sha256='{Hash(manifest)}'\n");
            await using var server = new WireServer(documents);
            server.BeforeGet = async (path, ct) =>
            {
                if (path == "store.toml") await releaseCatalog.Task.WaitAsync(ct);
                if (path == "2/plugin.toml") { payloadEntered.TrySetResult(); await releasePayload.Task.WaitAsync(ct); }
            };
            var data = Path.Combine(root, "data"); var sources = new StoreSourceStore(Path.Combine(data, "sources"));
            foreach (var source in sources.List()) sources.Save(source with { Enabled = false });
            sources.Save(new("fixture", "Fixture", "http", server.Address, "store.toml", true, false));
            var settings = new ClientUpdateSettings(data);
            settings.Save(settings.Value with { AutoCheck = false, AutoDownload = false, AutoCheckPlugins = true, AutoUpdatePlugins = automatic });
            var instances = new InstanceStore(Path.Combine(data, "instances"));
            await instances.CreateAsync(new("client", "Client", "fixture", "test-1", null,
                [new("fixture.optional", "1.0.0", true, false, new Dictionary<string, object>())]));
            new ServerStore(Path.Combine(data, "servers")).Save(new("one", "fixture", "One", null, new("127.0.0.1", 7100), false, false));
            await using var backend = new LauncherBackend(games, data);
            var replies = new ConcurrentQueue<JsonNode>(); long sequence = 0;
            var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge);
            async Task<JsonNode> Call(string method, object parameters)
            {
                var id = ++sequence;
                await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = parameters }), default);
                return replies.Single(row => row["id"]?.GetValue<long>() == id);
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                while (!(await Call("plugins.updateSnapshot", new { instanceId = "client" }))["result"]!["checking"]!.GetValue<bool>())
                    await Task.Delay(10, deadline.Token);
                releaseCatalog.TrySetResult();
                JsonNode pending;
                while (true)
                {
                    pending = (await Call("plugins.launchUpdates", new { instanceId = "client", serverId = "one" }))["result"]!;
                    if (pending["items"]!.AsArray().Count > 0) break;
                    await Task.Delay(10, deadline.Token);
                }
                if (automatic)
                {
                    await payloadEntered.Task.WaitAsync(deadline.Token);
                    pending = (await Call("plugins.launchUpdates", new { instanceId = "client", serverId = "one" }))["result"]!;
                }
                var update = Assert.Single(pending["items"]!.AsArray())!;
                Assert.Equal("1.0.0", update["installedVersion"]!.GetValue<string>());
                Assert.Equal("2.0.0", update["version"]!.GetValue<string>());
                Assert.Equal(automatic, update["task"] is not null);
                Assert.Equal(1, server.GetPaths.Count(path => path == "store.toml"));
                var unconfirmed = await Call("instances.launch", new { id = "client", serverId = "one" });
                Assert.Equal("plugin_updates_changed", unconfirmed["error"]!.GetValue<string>());
                var taskIds = update["task"] is { } transfer ? new[] { transfer["id"]!.GetValue<string>() } : [];
                var confirmed = await Call("instances.launch", new { id = "client", serverId = "one",
                    confirmedRequiredPluginUpdates = new[] { update["key"]!.GetValue<string>() }, confirmedPendingPlugins = taskIds });
                Assert.NotNull(confirmed["error"]);
                Assert.DoesNotContain("plugin_", confirmed["error"]!.GetValue<string>(), StringComparison.Ordinal);
                Assert.Equal(1, server.GetPaths.Count(path => path == "store.toml"));
                releasePayload.TrySetResult();
                if (automatic)
                {
                    while ((await Call("plugins.launchUpdates", new { instanceId = "client", serverId = "one" }))["result"]!["items"]!.AsArray().Count > 0)
                        await Task.Delay(20, deadline.Token);
                    Assert.Equal("2.0.0", Assert.Single(instances.Load("client").Plugins).Version);
                }
            }
            finally { releaseCatalog.TrySetResult(); releasePayload.TrySetResult(); }
        }
        finally { releaseCatalog.TrySetResult(); releasePayload.TrySetResult(); Directory.Delete(root, true); }
    }
}
