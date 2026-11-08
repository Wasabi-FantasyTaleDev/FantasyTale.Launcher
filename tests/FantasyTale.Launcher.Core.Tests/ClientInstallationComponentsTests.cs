using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ClientInstallationComponentsTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public async Task InstallationLocksMandatoryComponentsAndRetainsMultipleTranslationPackages(bool optional, bool translations, bool bounded)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "component-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var games = Path.Combine(root, "games"); var folder = Path.Combine(games, "Fixture");
            Directory.CreateDirectory(Path.Combine(folder, "builds"));
            File.WriteAllText(Path.Combine(folder, "game.toml"), "schema = 1\n[game]\nid = \"fixture\"\nstatus = \"enabled\"\nmodule = \"Fixture\"\nhook_host = \"external\"\nname = { en = \"Fixture\" }\n");
            File.WriteAllText(Path.Combine(folder, "builds", "test-1.toml"), """
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
                native_language = "id"
                additional_languages = { "zh-CN" = "fixture.zh", ko = "fixture.ko" }
                [[client_policy_rules]]
                plugin = "fixture.optional"
                id = "offer"
                type = "recommend"
                sources = []
                default_selected = true
                [[client_policy_rules]]
                plugin = "fixture.required"
                id = "required"
                type = "require"
                """);
            if (bounded) File.AppendAllText(Path.Combine(folder, "builds", "test-1.toml"), "\n[[client_policy_rules]]\nplugin = \"fixture.optional\"\nid = \"maximum\"\ntype = \"max_version\"\nversion = \"1.0.0\"\n");
            void Patch(string id, string version = "1.0.0")
            {
                var path = Directory.CreateDirectory(Path.Combine(folder, "plugins", id + "-" + version)).FullName;
                File.WriteAllText(Path.Combine(path, "plugin.toml"), $$"""
                    schema = 1
                    [plugin]
                    id = "{{id}}"
                    game = "fixture"
                    version = "{{version}}"
                    name = { en = "{{id}}" }
                    kind = "patch"
                    runtime = ">=3.0.0 <4.0.0"
                    [compatibility]
                    builds = ["test-1"]
                    [[patches]]
                    id = "fixture"
                    locate = { pattern = "AA BB CC" }
                    expect = "AA"
                    write = "90"
                    """);
            }
            Patch("fixture.optional"); Patch("fixture.required");
            Patch("fixture.optional", "2.0.0");
            foreach (var language in new[] { "zh", "ko" })
            {
                var path = Directory.CreateDirectory(Path.Combine(folder, "plugins", "fixture." + language)).FullName;
                File.WriteAllText(Path.Combine(path, "text.txt"), language);
                var payload = File.ReadAllBytes(Path.Combine(path, "text.txt"));
                File.WriteAllText(Path.Combine(path, "plugin.toml"), $$"""
                    schema = 1
                    [plugin]
                    id = "fixture.{{language}}"
                    game = "fixture"
                    version = "1.0.0"
                    name = { en = "Translation" }
                    kind = "files"
                    [compatibility]
                    builds = ["test-1"]
                    [[files]]
                    source = "text.txt"
                    target = "text.txt"
                    size = {{payload.Length}}
                    sha256 = "{{Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()}}"
                    """);
            }
            var data = Path.Combine(root, "data");
            var sources = new StoreSourceStore(Path.Combine(data, "sources")); var bundled = sources.List().Single(); sources.Save(bundled with { Enabled = false });
            await using var backend = new LauncherBackend(games, data);
            var replies = new ConcurrentQueue<JsonNode>(); var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!)); backend.Register(bridge); long sequence = 0;
            async Task<JsonNode?> Call(string method, object args, bool fails = false)
            {
                var id = ++sequence; await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = args }), default);
                var reply = replies.Single(item => item["id"]?.GetValue<long>() == id);
                if (fails) Assert.NotNull(reply["error"]); else Assert.True(reply["error"] is null, $"{method}: {reply["error"]}");
                return reply["result"];
            }
            var choices = (await Call("plugins.installationPlan", new { game = "fixture", build = "test-1" }))!.AsArray();
            await Call("plugins.claimRecommendations", new { instanceId = "client" }, fails: true);
            await Call("plugins.acceptRecommendations", new { instanceId = "client", ids = Array.Empty<string>() }, fails: true);
            Assert.Equal(2, choices.Count);
            Assert.True(choices.Single(item => item!["id"]!.GetValue<string>() == "fixture.optional")!["defaultSelected"]!.GetValue<bool>());
            Assert.True(choices.Single(item => item!["id"]!.GetValue<string>() == "fixture.required")!["required"]!.GetValue<bool>());
            Assert.Empty(Directory.EnumerateFiles(data, "client-policy.seen", SearchOption.AllDirectories));
            await Call("instances.create", new { id = "client", game = "fixture", build = "test-1", name = "Fixture" });
            var initialStore = new InstanceStore(Path.Combine(data, "instances"));
            var initial = initialStore.Load("client");
            initialStore.Save(initial with { Plugins = initial.Plugins.Concat([new InstancePlugin("fixture.optional", "1.0.0", false, false, new Dictionary<string, object>())]).ToArray() }, initial);
            await Call("plugins.installationComponents", new { instanceId = "client", ids = (string[])["undeclared"], languages = (string[])["id"] }, fails: true);
            await Call("plugins.installationComponents", new { instanceId = "client", ids = Array.Empty<string>(), languages = (string[])["unknown"] }, fails: true);
            var tasks = (await Call("plugins.installationComponents", new { instanceId = "client", ids = optional ? (string[])["fixture.optional"] : [],
                languages = translations ? (string[])["id", "zh-CN", "ko"] : ["id"] }))!.AsArray();
            Assert.Empty(tasks);
            var store = new InstanceStore(Path.Combine(data, "instances")); var instance = store.Load("client");
            Assert.Contains(instance.Plugins, item => item.Id == "fixture.required" && item.Enabled);
            Assert.Equal(optional, instance.Plugins.Any(item => item.Id == "fixture.optional" && item.Enabled));
            if (optional) Assert.Equal(bounded ? "1.0.0" : "2.0.0", Assert.Single(instance.Plugins, item => item.Id == "fixture.optional").Version);
            Assert.Equal(translations, instance.Plugins.Any(item => item.Id == "fixture.zh" && item.Enabled));
            Assert.Equal(translations, instance.Plugins.Any(item => item.Id == "fixture.ko" && item.Enabled));
            File.WriteAllText(Path.Combine(store.ClientFolder("client"), "client.exe"), "fixture");
            Assert.Empty((await Call("clientPolicy.claimPlugins", new { instanceId = "client" }))!.AsArray());
        }
        finally { Directory.Delete(root, true); }
    }
}
