using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class FilePluginTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-file-plugins-").FullName;
    private static string Repository
    {
        get
        {
            var folder = TestRepository.StartDirectory();
            while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "AGENTS.md"))) folder = folder.Parent;
            return folder!.FullName;
        }
    }

    [Fact]
    public void ChineseComponentUsesOneIdentityAndDifferentLoginTargets()
    {
        var game = GameCatalog.Load(Path.Combine(Repository, "games")).Single(g => g.Id == "bubblefighter");
        var packages = PluginCatalog.Load(Path.Combine(Repository, "store-plugins", "BubbleFighter"));
        var plugin = Assert.Single(packages, p => p.Id == "bubblefighter.fantasy.login-fix-cn");
        Assert.Equal("国服认证组件", plugin.Name.Resolve("zh-CN"));
        Assert.False(plugin.RestoreFiles);
        foreach (var build in game.Builds)
        {
            Assert.Equal(build.Branch == "cn", PluginCatalog.IsAvailable(plugin, build));
            Assert.False(PluginCatalog.NeedsWarning(plugin, build));
            if (build.Branch != "cn") continue;
            var defaults = PluginResolver.Defaults(game, build, packages);
            var selected = Assert.Single(defaults, s => s.Id == plugin.Id);
            Assert.True(selected.Enabled); Assert.True(selected.Required);
            var instance = new InstanceDefinition("sample", "Sample", game.Id, build.Id, null, defaults);
            var files = FilePluginResolver.Resolve(instance, build, packages);
            Assert.Contains(files, f => f.Target == "GPKitClt.dll");
            Assert.Contains(files, f => f.Target == (build.Id is "cn-279" or "cn-324" ? "GameWidget.dll" : "sdologinentry.dll"));
            Assert.Contains(files, f => f.Target == "native-passports.toml");
            Assert.Equal(3, files.Count);
            Assert.All(files, file => Assert.False(file.RestoreOnExit));
            var session = PluginResolver.Resolve(instance, build, packages);
            if (build.Id == "cn-706") Assert.Contains(session, p => p.Id == plugin.Id && p.Required);
            else Assert.DoesNotContain(session, p => p.Id == plugin.Id);
        }
    }

    [Fact]
    public void FileComponentOwnsAuthenticationWithoutDuplicateOrHiddenDeployment()
    {
        var game = GameCatalog.Load(Path.Combine(Repository, "games")).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "cn-404");
        var context = new LaunchContext(game, build, _root, new("127.0.0.1", 7240), "fixture")
        { FilePluginTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sdologinentry.dll", "GPKitClt.dll" } };
        var plan = new BubbleFighterModule().CreateLaunchPlan(context);
        Assert.Equal("nmcogame.dll", Assert.Single(plan.Runtime!.Files).Target);
        Assert.DoesNotContain(plan.Runtime.Files, f => f.Target == "sdologinentry.dll");
    }

    [Fact]
    public void ResourcePackagesCanBeImportedWithoutLoadingHookCode()
    {
        var (game, build, plugin) = Fixture();
        var manifest = Path.Combine(plugin.Folder, "plugin.toml");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("warning = { en = \"Component disabled\" }", "", StringComparison.Ordinal));
        using var library = new PluginLibrary(Path.Combine(_root, "library"));
        var preview = library.Prepare(plugin.Folder, game, build);
        Assert.Equal("files", preview.Plugin.Kind);
        var installed = library.Install(preview.Token, game, build);
        Assert.True(File.Exists(Path.Combine(installed.Folder, "payload", "image.txt")));
        File.WriteAllText(Path.Combine(plugin.Folder, "payload", "image.txt"), "bad");
        Assert.Throws<IOException>(() => library.Prepare(plugin.Folder, game, build));
    }

    [Fact]
    public async Task RealBridgeInstallsAndRetainsTheRequiredChineseComponent()
    {
        var data = Path.Combine(_root, "data");
        var game = GameCatalog.Load(Path.Combine(Repository, "games")).Single(g => g.Id == "bubblefighter");
        _ = TestRepository.InstallStorePlugin(Path.Combine(data, "plugins"),
            Path.Combine(Repository, "store-plugins", "BubbleFighter", "bubblefighter.fantasy.login-fix-cn"), game,
            game.Builds.Single(build => build.Id == "cn-404"), "fantasy-tale");
        var store = new InstanceStore(Path.Combine(data, "instances"));
        await store.CreateAtAsync(new("sample", "Sample", "bubblefighter", "cn-404", null, []), null);
        var replies = new ConcurrentQueue<JsonNode>(); long sequence = 0;
        var bridge = new BridgeRouter(raw => replies.Enqueue(JsonNode.Parse(raw)!));
        await using var backend = new LauncherBackend(Path.Combine(Repository, "games"), data);
        backend.Register(bridge);
        async Task<JsonNode> Call(string method, object args)
        {
            var id = ++sequence;
            await bridge.HandleAsync(JsonSerializer.Serialize(new { id, method, @params = args }), default);
            return replies.Single(row => row["id"]?.GetValue<long>() == id);
        }
        JsonNode component;
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            // Startup discovery can hold the client lease; listing deliberately returns the last saved selection until it finishes.
            do
            {
                var list = await Call("plugins.list", new { instanceId = "sample" }); Assert.Null(list["error"]);
                component = list["result"]!.AsArray().Single(p => p!["id"]!.GetValue<string>() == "bubblefighter.fantasy.login-fix-cn")!;
                if (component["selection"] is not null) break;
                await Task.Delay(25, deadline.Token);
            } while (true);
        }
        Assert.False(component["warningRequired"]!.GetValue<bool>());
        Assert.True(component["selection"]!["enabled"]!.GetValue<bool>());
        Assert.True(component["required"]!.GetValue<bool>());
        var enabled = await Call("plugins.configure", new { instanceId = "sample", id = "bubblefighter.fantasy.login-fix-cn", version = component["version"]!.GetValue<string>(), enabled = true });
        Assert.Null(enabled["error"]);
        Assert.True(store.Load("sample").Plugins.Single(p => p.Id == "bubblefighter.fantasy.login-fix-cn").Enabled);
        var disabled = await Call("plugins.configure", new { instanceId = "sample", id = "bubblefighter.fantasy.login-fix-cn", version = component["version"]!.GetValue<string>(), enabled = false });
        Assert.NotNull(disabled["error"]);
        Assert.Contains("required plugin cannot be disabled", disabled["error"]!.ToJsonString(), StringComparison.Ordinal);
        var warnings = await Call("plugins.launchWarnings", new { instanceId = "sample" }); Assert.Null(warnings["error"]);
        Assert.Empty(warnings["result"]!.AsArray());
        var rejected = await Call("instances.launch", new { id = "sample", serverId = "missing", confirmedWarnings = Array.Empty<string>() });
        Assert.NotNull(rejected["error"]);
        Assert.DoesNotContain("explicit confirmation", rejected["error"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.True(store.Load("sample").Plugins.Single(p => p.Id == "bubblefighter.fantasy.login-fix-cn").Enabled);
    }

    [Fact]
    public async Task FileOnlyClientProcessRestoresResourcesAfterExit()
    {
        var (game, build, plugin) = Fixture();
        build = build with { Executable = "FantasyTale.Launcher.TestClient.exe" };
        game = game with { Builds = [build] };
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAsync(new("sample", "Sample", game.Id, build.Id, null, PluginResolver.Defaults(game, build, [plugin])), Path.Combine(AppContext.BaseDirectory, "test-client"));
        var folder = store.ClientFolder("sample");
        Directory.CreateDirectory(Path.Combine(folder, "UI"));
        File.WriteAllText(Path.Combine(folder, "UI", "血量.txt"), "original");
        var service = new LaunchService(store);
        var running = service.Start("sample", game, new FileOnlyModule(), [plugin]);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!File.Exists(Path.Combine(folder, "ready"))) await Task.Delay(25, deadline.Token);
            Assert.Equal("replacement", File.ReadAllText(Path.Combine(folder, "UI", "血量.txt")));
            Assert.Equal("not-required", (await running.Readiness).State);
            Assert.DoesNotContain("[[plugins]]", File.ReadAllText(Path.Combine(folder, "session.toml")), StringComparison.Ordinal);
        }
        finally { File.WriteAllText(Path.Combine(folder, "release"), "exit"); await running.Completion.WaitAsync(TimeSpan.FromSeconds(15)); }
        Assert.Equal("original", File.ReadAllText(Path.Combine(folder, "UI", "血量.txt")));
        Assert.False(File.Exists(Path.Combine(folder, "session.toml")));
        Assert.False(Directory.Exists(Path.Combine(folder, ".fantasytale-runtime")));
    }

    [Fact]
    public async Task ExistingClientsAcquireTheComponentOnceAndRetainAnExplicitDisable()
    {
        var (game, build, plugin) = Fixture();
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAtAsync(new("sample", "Sample", game.Id, build.Id, null, []), null);
        var installed = FilePluginResolver.EnsureInstalled(store, "sample", game, build, [plugin]);
        Assert.True(Assert.Single(installed.Plugins).Enabled);
        store.Save(installed with { Plugins = [installed.Plugins[0] with { Enabled = false }] });
        installed = FilePluginResolver.EnsureInstalled(store, "sample", game, build, [plugin]);
        Assert.False(Assert.Single(installed.Plugins).Enabled);
        Assert.Single(FilePluginResolver.Warnings(installed, build, [plugin]));
        Assert.Empty(PluginResolver.Resolve(installed, build, [plugin]));
        Assert.Empty(FilePluginResolver.Resolve(installed, build, [plugin]));
    }

    [Fact]
    public void FileReplacementIsVerifiedRestorableAndNeverTouchesOtherFiles()
    {
        var (game, build, plugin) = Fixture();
        var client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        Directory.CreateDirectory(Path.Combine(client, "UI"));
        File.WriteAllText(Path.Combine(client, "UI", "血量.txt"), "original");
        var instance = new InstanceDefinition("sample", "Sample", game.Id, build.Id, null, PluginResolver.Defaults(game, build, [plugin]));
        var files = FilePluginResolver.Resolve(instance, build, [plugin]);
        var deployment = RuntimeDeployment.Apply(client, game.Id, null, files);
        Assert.Equal("replacement", File.ReadAllText(Path.Combine(client, "UI", "血量.txt")));
        Assert.Empty(PluginResolver.Resolve(instance, build, [plugin]));
        deployment.Restore();
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "UI", "血量.txt")));
        File.WriteAllText(files[0].Source, "tampered");
        Assert.Throws<IOException>(() => FilePluginResolver.Resolve(instance, build, [plugin]));
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "UI", "血量.txt")));
        Assert.Throws<IOException>(() => RuntimeDeployment.Apply(client, game.Id, null, files));
        Assert.False(Directory.Exists(Path.Combine(client, ".fantasytale-runtime")));
    }

    [Fact]
    public void LargeResourceSelectionRestoresOriginalsAfterStoppedClientRecovery()
    {
        var (game, build, plugin) = Fixture();
        var manifest = Path.Combine(plugin.Folder, "plugin.toml");
        var text = new System.Text.StringBuilder(File.ReadAllText(manifest));
        for (var index = 0; index < 179; index++)
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"\n[[files]]\nsource = \"payload/image.txt\"\ntarget = \"UI/resource-{index}.bin\"\nsize = {plugin.Files[0].Size}\nsha256 = \"{plugin.Files[0].Sha256}\"\n");
        var large = Path.Combine(plugin.Folder, "payload/large.bin");
        using (var output = File.Create(large)) output.SetLength(65L * 1024 * 1024);
        string hash;
        using (var input = File.OpenRead(large)) hash = Convert.ToHexStringLower(SHA256.HashData(input));
        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"\n[[files]]\nsource = \"payload/large.bin\"\ntarget = \"Data/language.bin\"\nsize = {new FileInfo(large).Length}\nsha256 = \"{hash}\"\n");
        File.WriteAllText(manifest, text.ToString());
        plugin = PluginCatalog.LoadFile(manifest);
        var client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        Directory.CreateDirectory(Path.Combine(client, "UI"));
        Directory.CreateDirectory(Path.Combine(client, "Data"));
        File.WriteAllText(Path.Combine(client, "UI/血量.txt"), "original");
        File.WriteAllText(Path.Combine(client, "Data/language.bin"), "original language");
        var instance = new InstanceDefinition("sample", "Sample", game.Id, build.Id, null, PluginResolver.Defaults(game, build, [plugin]));
        var files = FilePluginResolver.Resolve(instance, build, [plugin]);
        Assert.Equal(181, files.Count);
        _ = RuntimeDeployment.Apply(client, game.Id, null, files);
        Assert.Equal("replacement", File.ReadAllText(Path.Combine(client, "UI/血量.txt")));
        Assert.Equal(new FileInfo(large).Length, new FileInfo(Path.Combine(client, "Data/language.bin")).Length);
        File.AppendAllText(Path.Combine(client, ".fantasytale-runtime/journal.json"), new string(' ', 129 * 1024));
        RuntimeDeployment.RecoverStoppedClient(client);
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "UI/血量.txt")));
        Assert.Equal("original language", File.ReadAllText(Path.Combine(client, "Data/language.bin")));
        Assert.All(files.Skip(1).SkipLast(1), file => Assert.False(File.Exists(Path.Combine(client, file.Target))));
        Assert.False(Directory.Exists(Path.Combine(client, ".fantasytale-runtime")));
    }

    [Fact]
    public void InterruptedResourceReplacementPreservesExternalChangesAndRecovers()
    {
        var (game, build, plugin) = Fixture();
        var client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        var instance = new InstanceDefinition("sample", "Sample", game.Id, build.Id, null, PluginResolver.Defaults(game, build, [plugin]));
        _ = RuntimeDeployment.Apply(client, game.Id, null, FilePluginResolver.Resolve(instance, build, [plugin]));
        var target = Path.Combine(client, "UI", "血量.txt");
        File.WriteAllText(target, "external");
        Assert.Throws<IOException>(() => RuntimeDeployment.RecoverStoppedClient(client));
        Assert.Equal("external", File.ReadAllText(target));
        Assert.True(File.Exists(Path.Combine(client, ".fantasytale-runtime", "journal.json")));
        File.WriteAllText(target, "replacement");
        RuntimeDeployment.RecoverStoppedClient(client);
        Assert.False(File.Exists(target));
        Assert.False(Directory.Exists(Path.Combine(client, ".fantasytale-runtime")));
    }

    [Fact]
    public void PermanentFilesRemainInstalledAndMatchingFilesAreNotRewritten()
    {
        var (game, build, plugin) = Fixture();
        var manifest = Path.Combine(plugin.Folder, "plugin.toml");
        File.WriteAllText(Path.Combine(plugin.Folder, "file-install.toml"), "schema=1\nrestore_files=false\n");
        plugin = PluginCatalog.LoadFile(manifest);
        Assert.False(plugin.RestoreFiles);
        var client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        var instance = new InstanceDefinition("sample", "Sample", game.Id, build.Id, null, PluginResolver.Defaults(game, build, [plugin]));
        var files = FilePluginResolver.Resolve(instance, build, [plugin]);
        var target = Path.Combine(client, "UI", "血量.txt");
        var deployment = RuntimeDeployment.Apply(client, game.Id, null, files);
        deployment.Restore();
        Assert.Equal("replacement", File.ReadAllText(target));
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(target, timestamp);
        _ = RuntimeDeployment.Apply(client, game.Id, null, files);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(target));
        RuntimeDeployment.RecoverStoppedClient(client);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(target));
        Assert.Equal("replacement", File.ReadAllText(target));
        File.WriteAllText(target, "outdated");
        RuntimeDeployment.Apply(client, game.Id, null, files).Restore();
        Assert.Equal("replacement", File.ReadAllText(target));
        Assert.False(Directory.Exists(Path.Combine(client, ".fantasytale-runtime")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PermanentRecoveryRetainsOnlyCompletedInstallationsAndStillRestoresTemporaryFiles(bool committed)
    {
        var (game, build, plugin) = Fixture();
        var client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        var instance = new InstanceDefinition("sample", "Sample", game.Id, build.Id, null, PluginResolver.Defaults(game, build, [plugin]));
        var file = Assert.Single(FilePluginResolver.Resolve(instance, build, [plugin]));
        File.WriteAllText(Path.Combine(client, "persistent.txt"), "old component");
        File.WriteAllText(Path.Combine(client, "temporary.txt"), "old runtime");
        _ = RuntimeDeployment.Apply(client, game.Id, null,
            [file with { Target = "persistent.txt", RestoreOnExit = false }, file with { Target = "temporary.txt" }]);
        if (!committed) File.Delete(Path.Combine(client, ".fantasytale-runtime", "applied"));
        RuntimeDeployment.RecoverStoppedClient(client);
        Assert.Equal(committed ? "replacement" : "old component", File.ReadAllText(Path.Combine(client, "persistent.txt")));
        Assert.Equal("old runtime", File.ReadAllText(Path.Combine(client, "temporary.txt")));
        Assert.False(Directory.Exists(Path.Combine(client, ".fantasytale-runtime")));
    }

    [Fact]
    public void DestinationCollisionsAreRejectedBeforeMutation()
    {
        var (game, build, plugin) = Fixture();
        var client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        var instance = new InstanceDefinition("sample", "Sample", game.Id, build.Id, null, PluginResolver.Defaults(game, build, [plugin]));
        var file = Assert.Single(FilePluginResolver.Resolve(instance, build, [plugin]));
        Assert.Throws<IOException>(() => RuntimeDeployment.Apply(client, game.Id, null, [file, file]));
        Assert.Throws<IOException>(() => RuntimeDeployment.Apply(client, game.Id, null, [file, file with { Target = "UI" }]));
        Assert.False(Directory.Exists(Path.Combine(client, ".fantasytale-runtime")));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("UI/../outside.dll")]
    [InlineData("UI/con.dll")]
    [InlineData("UI/session.toml")]
    [InlineData(".fantasytale-runtime/journal.json")]
    [InlineData("UI/file:stream")]
    public void UnsafeResourcePathsAreRejected(string path) => Assert.ThrowsAny<IOException>(() => FilePluginResolver.RelativePath(path));

    [Fact]
    public async Task DisabledComponentCannotLaunchWithoutConfirmationAndMissingPackageCannotBeBypassed()
    {
        var (game, build, plugin) = Fixture();
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAtAsync(new("sample", "Sample", game.Id, build.Id, null,
            [new(plugin.Id, plugin.Version, false, false, new Dictionary<string, object>())]), null);
        var service = new LaunchService(store);
        var module = new UnreachableModule();
        Assert.Throws<InvalidOperationException>(() => service.Start("sample", game, module, [plugin]));
        Assert.Throws<DefinitionException>(() => service.Start("sample", game, module, [], confirmedWarnings: [plugin.Id + "@" + plugin.Version]));
        Assert.Throws<InvalidOperationException>(() => service.Start("sample", game, module, [plugin], confirmedWarnings: [plugin.Id + "@0.9.0"]));
        Assert.Throws<NotSupportedException>(() => service.Start("sample", game, module, [plugin], confirmedWarnings: [plugin.Id + "@" + plugin.Version]));
        Assert.False(File.Exists(Path.Combine(store.ClientFolder("sample"), "session.toml")));
    }

    private (GameDefinition Game, BuildDefinition Build, PluginDefinition Plugin) Fixture()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "plugin")).FullName;
        Directory.CreateDirectory(Path.Combine(folder, "payload"));
        var bytes = "replacement"u8.ToArray();
        File.WriteAllBytes(Path.Combine(folder, "payload", "image.txt"), bytes);
        File.WriteAllText(Path.Combine(folder, "plugin.toml"), $$"""
            schema = 1
            [plugin]
            id = "test.resource"
            version = "1.0.0"
            game = "test"
            kind = "files"
            name = { en = "Resource" }
            warning = { en = "Component disabled" }
            [compatibility]
            builds = ["test-1"]
            [[files]]
            source = "payload/image.txt"
            target = "UI/血量.txt"
            size = {{bytes.Length}}
            sha256 = "{{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}}"
            """);
        var plugin = PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml"));
        var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Test" });
        var build = new BuildDefinition("test-1", "test", 1, name, "game.exe", "standin", "", null, null, "nmco", [], [plugin.Id])
            { ClientPolicy = PolicyFixture.Rules(plugin.Id, "recommend") };
        return (new("test", GameStatus.Enabled, name, "test.dll", "", "", null, folder, [build]), build, plugin);
    }

    private sealed class UnreachableModule : IGameModule
    {
        public string GameId => "test";
        public LaunchPlan CreateLaunchPlan(LaunchContext context) => throw new NotSupportedException("Launch plan reached after confirmation.");
    }

    private sealed class FileOnlyModule : IGameModule
    {
        public string GameId => "test";
        public LaunchPlan CreateLaunchPlan(LaunchContext context) => new(Path.Combine(context.InstanceFolder, context.Build.Executable), [], context.InstanceFolder);
    }

    public void Dispose() => Directory.Delete(_root, true);
}
