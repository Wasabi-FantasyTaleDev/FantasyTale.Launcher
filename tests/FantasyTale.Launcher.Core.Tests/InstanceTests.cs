using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

[Collection("native hook runtime")]
public sealed class InstanceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-instance-tests-").FullName;
    private static InstanceDefinition Definition(string id = "sample") => new(id, "Sample 客户端", "test", "test-1", new ServerEndpoint("127.0.0.1", 1234), []);

    [Theory]
    [InlineData("../outside")]
    [InlineData("c:\\outside")]
    [InlineData("con")]
    [InlineData("lpt1")]
    [InlineData(".")]
    [InlineData("name.")]
    public void RejectsUnsafeInstanceIds(string id) => Assert.Throws<DefinitionException>(() => InstanceFile.Render(Definition(id)));

    [Fact]
    public async Task InstallationPathRequiresConfirmationAndNeverCopiesOrDeletesExistingGameFiles()
    {
        var store = new InstanceStore(Path.Combine(_root, "metadata"));
        var target = Directory.CreateDirectory(Path.Combine(_root, "game")).FullName;
        var gameFile = Path.Combine(target, "game.exe");
        File.WriteAllText(gameFile, "existing game");
        Assert.True(store.InspectInstallationPath("sample", target).ContainsFiles);
        await Assert.ThrowsAsync<IOException>(() => store.CreateAtAsync(Definition(), target));
        Assert.Empty(store.List());
        await store.CreateAtAsync(Definition(), target, allowExistingFiles: true);
        Assert.Equal(target, store.ClientFolder("sample"));
        Assert.Equal(target, new InstanceStore(Path.Combine(_root, "metadata")).Load("sample").InstallationPath);
        Assert.False(Directory.Exists(Path.Combine(_root, "metadata", "sample", "client")));
        await store.CloneAsync("sample", "copy", "Independent copy");
        Assert.Null(store.Load("copy").InstallationPath);
        Assert.Equal("existing game", File.ReadAllText(Path.Combine(store.ClientFolder("copy"), "game.exe")));
        store.Delete("sample");
        Assert.Equal("existing game", File.ReadAllText(gameFile));
    }

    [Fact]
    public async Task InstallationPathsRejectOverlappingClientsAndMetadataAndCannotBeEditedInPlace()
    {
        var store = new InstanceStore(Path.Combine(_root, "metadata"));
        var target = Path.Combine(_root, "games", "one");
        Assert.False(store.InspectInstallationPath("sample", target).ContainsFiles);
        Assert.False(Directory.Exists(target));
        await store.CreateAtAsync(Definition(), target);
        await Assert.ThrowsAsync<IOException>(() => store.CreateAtAsync(Definition("second"), target, true));
        Assert.Throws<IOException>(() => store.InspectInstallationPath("second", Path.Combine(target, "child")));
        Assert.Throws<IOException>(() => store.InspectInstallationPath("second", Path.GetDirectoryName(target)));
        Assert.Throws<IOException>(() => store.InspectInstallationPath("second", Path.Combine(_root, "metadata", "unsafe")));
        Assert.Throws<IOException>(() => store.InspectInstallationPath("second", _root));
        Assert.Throws<ArgumentException>(() => store.InspectInstallationPath("second", "relative-folder"));
        Assert.Throws<InvalidOperationException>(() => store.Save(store.Load("sample") with { InstallationPath = Path.Combine(_root, "moved") }));
        await store.CreateAtAsync(Definition("default"), null);
        Assert.Null(store.Load("default").InstallationPath);
        Assert.True(Directory.Exists(store.ClientFolder("default")));
    }

    [Fact]
    public async Task RepeatedClientNamesUseTheFirstAvailableNumberAndPersistIt()
    {
        var store = new InstanceStore(_root);
        var first = await store.CreateAtAsync(Definition("first") with { Name = "国服 2011" }, null);
        var second = await store.CreateAtAsync(Definition("second") with { Name = "国服 2011" }, null);
        var third = await store.CreateAtAsync(Definition("third") with { Name = "国服 2011" }, null);
        Assert.Equal("国服 2011", first.Name); Assert.Equal("国服 2011 (1)", second.Name); Assert.Equal("国服 2011 (2)", third.Name);
        Assert.Equal(third.Name, new InstanceStore(_root).Load("third").Name);
        store.Delete("second");
        Assert.Equal("国服 2011 (1)", (await store.CreateAtAsync(Definition("replacement") with { Name = "国服 2011" }, null)).Name);
        Assert.Equal("Custom", (await store.CreateAsync(Definition("custom") with { Name = "Custom" })).Name);
        Assert.Equal("custom (1)", (await store.CreateAsync(Definition("custom-copy") with { Name = "custom" })).Name);
    }

    [Fact]
    public async Task NumberedClientNamesRespectTheStoredLengthLimit()
    {
        var store = new InstanceStore(_root); var name = new string('a', 128);
        await store.CreateAtAsync(Definition("first") with { Name = name }, null);
        var copy = await store.CreateAtAsync(Definition("second") with { Name = name }, null);
        Assert.Equal(new string('a', 124) + " (1)", copy.Name);
        Assert.Equal(128, copy.Name.Length);
    }

    [Fact]
    public async Task CreatesClonesAndDeletesIndependentFiles()
    {
        var store = new InstanceStore(_root);
        await store.CreateAsync(Definition());
        var client = store.ClientFolder("sample");
        File.WriteAllText(Path.Combine(client, "data.bin"), "original");
        File.WriteAllText(Path.Combine(client, "session.toml"), "secret");
        File.WriteAllText(Path.Combine(store.LogFolder("sample"), "fthook.log"), "old log");
        await store.CloneAsync("sample", "copy", "Clone");
        Assert.False(File.Exists(Path.Combine(store.ClientFolder("copy"), "session.toml")));
        Assert.Empty(Directory.GetFiles(store.LogFolder("copy")));
        File.WriteAllText(Path.Combine(store.ClientFolder("copy"), "data.bin"), "changed");
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "data.bin")));
        Assert.Equal(["copy", "sample"], store.List().Select(i => i.Id));
        store.Delete("copy");
        Assert.Single(store.List());
    }

    [Fact]
    public async Task FailedCopyPublishesNothing()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        File.WriteAllText(Path.Combine(source, "large"), "12345");
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        using (var locked = new FileStream(Path.Combine(source, "large"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => store.CreateAsync(Definition(), source));
        Assert.Empty(store.List());
        Assert.Empty(Directory.GetDirectories(Path.Combine(_root, "instances"), ".creating-*"));
        Assert.Equal("12345", File.ReadAllText(Path.Combine(source, "large")));
    }

    [Fact]
    public void IncompleteInstanceDirectoryDoesNotBreakListing()
    {
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        Directory.CreateDirectory(Path.Combine(_root, "instances", "cn-404-627f229b"));
        Assert.Empty(store.List());
    }

    [Fact]
    public async Task DefinitionRoundTripsAndRejectsSecrets()
    {
        var store = new InstanceStore(_root);
        await store.CreateAsync(Definition());
        Assert.Equal(Definition().Name, store.Load("sample").Name);
        var file = Path.Combine(_root, "sample", "instance.toml");
        File.AppendAllText(file, "\n[login]\nticket = \"secret\"\n");
        Assert.Equal("login", Assert.Throws<DefinitionException>(() => store.Load("sample")).Key);
    }

    [Fact]
    public async Task ProcessExitCleansSessionAndUnlocksInstance()
    {
        var store = new InstanceStore(_root);
        await store.CreateAsync(Definition(), Path.Combine(AppContext.BaseDirectory, "test-client"));
        var service = new LaunchService(store);
        var game = TestGame();
        var running = service.Start("sample", game, new TestModule(), [], "test-account", "ephemeral-ticket");
        var folder = store.ClientFolder("sample");
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!File.Exists(Path.Combine(folder, "ready"))) await Task.Delay(25, deadline.Token);
            Assert.Equal("session-present", File.ReadAllText(Path.Combine(folder, "ready")));
            Assert.Contains("ephemeral-ticket", File.ReadAllText(Path.Combine(folder, "session.toml")), StringComparison.Ordinal);
            Assert.DoesNotContain("ephemeral-ticket", File.ReadAllText(Path.Combine(_root, "sample", "instance.toml")), StringComparison.Ordinal);
            Assert.Throws<InstanceBusyException>(() => store.Delete("sample"));
            Assert.Throws<InvalidOperationException>(() => service.Start("sample", game, new TestModule(), []));
            var anotherService = new LaunchService(new InstanceStore(_root));
            Assert.Throws<InstanceBusyException>(() => anotherService.Start("sample", game, new TestModule(), []));
        }
        finally
        {
            File.WriteAllText(Path.Combine(folder, "release"), "exit");
            await running.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        }
        var result = await running.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(23, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(folder, "session.toml")));
        Assert.Empty(service.Running);
        store.Delete("sample");
    }

    [Fact]
    public async Task StartupFailureCleansSessionButNeverReplacesStaleSession()
    {
        var store = new InstanceStore(_root);
        await store.CreateAsync(Definition());
        var folder = store.ClientFolder("sample");
        File.WriteAllText(Path.Combine(folder, "FantasyTale.Launcher.TestClient.exe"), "not executable");
        var service = new LaunchService(store);
        Assert.Throws<System.ComponentModel.Win32Exception>(() => service.Start("sample", TestGame(), new TestModule(), [], ticket: "secret"));
        Assert.False(File.Exists(Path.Combine(folder, "session.toml")));
        File.WriteAllText(Path.Combine(folder, "session.toml"), "existing-session");
        Assert.Throws<IOException>(() => service.Start("sample", TestGame(), new TestModule(), []));
        Assert.Equal("existing-session", File.ReadAllText(Path.Combine(folder, "session.toml")));
    }

    [Fact]
    public async Task ConfirmedStopIgnoresExitedTargetsAndPreservesReplacementProcesses()
    {
        var store = new InstanceStore(_root);
        await store.CreateAsync(Definition(), Path.Combine(AppContext.BaseDirectory, "test-client"));
        var service = new LaunchService(store);
        Assert.Null(await service.StopAsync("sample", 1));
        var first = service.Start("sample", TestGame(), new TestModule(), []);
        var folder = store.ClientFolder("sample");
        File.WriteAllText(Path.Combine(folder, "release"), "exit");
        await first.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Null(await service.StopAsync("sample", first.ProcessId));
        File.Delete(Path.Combine(folder, "release"));
        var second = service.Start("sample", TestGame(), new TestModule(), []);
        try
        {
            Assert.Null(await service.StopAsync("sample", first.ProcessId));
            Assert.Equal(second.ProcessId, Assert.Single(service.Running).ProcessId);
            Assert.False(second.Completion.IsCompleted);
        }
        finally { await service.StopAsync("sample", second.ProcessId).WaitAsync(TimeSpan.FromSeconds(15)); }
        Assert.Empty(service.Running);
        Assert.False(File.Exists(Path.Combine(folder, SessionWriter.FileName)));
    }

    private static GameDefinition TestGame()
    {
        var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Test" });
        var build = new BuildDefinition("test-1", "test", 1, name, "FantasyTale.Launcher.TestClient.exe", "", "", null, null, "test", [], []);
        return new GameDefinition("test", GameStatus.Enabled, name, "test.dll", "external", "", null, "", [build]);
    }

    [Fact]
    public async Task LaunchAutomaticallyRecoversAVerifiedStoppedSession()
    {
        var store = new InstanceStore(_root);
        await store.CreateAsync(Definition(), Path.Combine(AppContext.BaseDirectory, "test-client"));
        var folder = store.ClientFolder("sample");
        var game = TestGame();
        File.WriteAllText(Path.Combine(folder, SessionWriter.FileName), SessionWriter.Render(new("test", game.Builds[0], "sample", Path.Combine(_root, "old.log"), null, "stale-ticket", null, [])));
        var service = new LaunchService(store);
        var running = service.Start("sample", game, new TestModule(), [], ticket: "new-ticket");
        try
        {
            Assert.DoesNotContain("stale-ticket", File.ReadAllText(Path.Combine(folder, SessionWriter.FileName)), StringComparison.Ordinal);
        }
        finally
        {
            File.WriteAllText(Path.Combine(folder, "release"), "exit");
            await running.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        }
        Assert.False(InstanceRecovery.Required(folder));
    }

    private sealed class TestModule : IGameModule
    {
        public string GameId => "test";
        public LaunchPlan CreateLaunchPlan(LaunchContext context) => new(Path.Combine(context.InstanceFolder, context.Build.Executable), [], context.InstanceFolder);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeSessionUsesPreparedGameEndpointWithoutReplacingAuthenticationServer(bool bootstrapEndpoint, bool explicitEndpoint)
    {
        var store = new InstanceStore(_root);
        await store.CreateAsync(Definition(), Path.Combine(AppContext.BaseDirectory, "test-client"));
        var service = new LaunchService(store);
        var authority = new ServerEndpoint("127.0.0.1", 1234);
        var direct = new ServerEndpoint("127.0.0.1", 17110);
        var gateway = new ServerEndpoint("127.0.0.1", 51433);
        var module = new EndpointBootstrapModule(bootstrapEndpoint ? gateway : null);
        var running = service.Start("sample", TestGame(), module, [], "test-account", "ephemeral-ticket",
            gameEndpoint: explicitEndpoint ? direct : null);
        var folder = store.ClientFolder("sample");
        try
        {
            var context = Assert.IsType<LaunchContext>(module.PreparedContext);
            Assert.Equal(authority, context.Server);
            var expected = bootstrapEndpoint ? gateway : explicitEndpoint ? direct : authority;
            var session = Tomlyn.TomlSerializer.Deserialize<Tomlyn.Model.TomlTable>(File.ReadAllText(Path.Combine(folder, SessionWriter.FileName)))!;
            var endpoint = Assert.IsType<Tomlyn.Model.TomlTable>(session["endpoint"]);
            Assert.Equal(expected.Host, endpoint["host"]);
            Assert.Equal((long)expected.Port, endpoint["port"]);
            Assert.Equal("ephemeral-ticket", Assert.IsType<Tomlyn.Model.TomlTable>(session["login"])["ticket"]);
        }
        finally
        {
            File.WriteAllText(Path.Combine(folder, "release"), "exit");
            await running.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        }
        Assert.False(File.Exists(Path.Combine(folder, SessionWriter.FileName)));
    }

    private sealed class EndpointBootstrapModule(ServerEndpoint? endpoint) : IGameModule, ILaunchBootstrapProvider
    {
        internal LaunchContext? PreparedContext { get; private set; }
        public string GameId => "test";
        public LaunchPlan CreateLaunchPlan(LaunchContext context)
        {
            PreparedContext = context;
            return new(Path.Combine(context.InstanceFolder, context.Build.Executable), [], context.InstanceFolder);
        }
        public ValueTask<ILaunchBootstrapLease?> PrepareBootstrapAsync(LaunchContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ILaunchBootstrapLease?>(endpoint is null ? null : new EndpointBootstrapLease(endpoint));
    }

    private sealed class EndpointBootstrapLease(ServerEndpoint endpoint) : ILaunchBootstrapLease
    {
        public ServerEndpoint? GameEndpoint => endpoint;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task BootstrapExitFailureStillRestoresClientFilesAndRetainsConflictingBackups(bool externalChange, bool nativeFailure)
    {
        var store = new InstanceStore(_root);
        await store.CreateAsync(Definition(), Path.Combine(AppContext.BaseDirectory, "test-client"));
        var folder = store.ClientFolder("sample");
        var runtime = Directory.CreateDirectory(Path.Combine(_root, "runtime")).FullName;
        var payload = "replacement"u8.ToArray();
        File.WriteAllBytes(Path.Combine(runtime, "payload.dll"), payload);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(payload));
        File.WriteAllText(Path.Combine(runtime, "runtime.toml"),
            $"schema = 1\n[runtime]\ngame = \"test\"\nversion = \"3.7.0\"\nsession_schema = 2\n[[files]]\npath = \"payload.dll\"\nsize = {payload.Length}\nsha256 = \"{hash}\"\n");
        var resource = Path.Combine(folder, "fixture-resource.bin");
        File.WriteAllText(resource, "original");
        var service = new LaunchService(store);
        var module = new FailedBootstrapModule(runtime, nativeFailure);
        var running = service.Start("sample", TestGame(), module, []);
        try
        {
            Assert.NotNull(module.BootstrapLaunchId);
            Assert.Equal(32, module.BootstrapLaunchId.Length);
            Assert.All(module.BootstrapLaunchId, digit => Assert.True(char.IsAsciiHexDigitLower(digit)));
            Assert.Contains(module.BootstrapLaunchId, File.ReadAllText(Path.Combine(folder, SessionWriter.FileName)), StringComparison.Ordinal);
            Assert.Equal("replacement", File.ReadAllText(resource));
            if (externalChange) File.WriteAllText(resource, "external-change");
        }
        finally
        {
            File.WriteAllText(Path.Combine(folder, "release"), "exit");
            var failure = await Assert.ThrowsAsync<LaunchCleanupException>(() => running.Completion.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.True(failure.ExitConfirmed);
            Assert.Equal(23, failure.ExitCode);
            Assert.Equal(externalChange ? "runtime-restore" : "bootstrap-dispose", failure.Phase);
            if (nativeFailure) Assert.IsType<System.ComponentModel.Win32Exception>(failure.InnerException);
            else Assert.IsType<IOException>(failure.InnerException);
        }
        Assert.Empty(service.Running);
        Assert.False(service.IsActive("sample"));
        Assert.Equal(externalChange, File.Exists(Path.Combine(folder, SessionWriter.FileName)));
        Assert.Equal(externalChange, Directory.Exists(Path.Combine(folder, ".fantasytale-runtime")));
        Assert.Equal(externalChange ? "external-change" : "original", File.ReadAllText(resource));
        if (externalChange) Assert.Equal("original", File.ReadAllText(Path.Combine(folder, ".fantasytale-runtime", "old-0")));
        using var lease = store.Acquire("sample");
    }

    private sealed class FailedBootstrapModule(string runtime, bool nativeFailure) : IGameModule, ILaunchBootstrapProvider
    {
        internal string? BootstrapLaunchId { get; private set; }
        public string GameId => "test";
        public LaunchPlan CreateLaunchPlan(LaunchContext context) => new(Path.Combine(context.InstanceFolder, context.Build.Executable), [], context.InstanceFolder)
        { Runtime = new(runtime, [new("payload.dll", "fixture-resource.bin")]) };
        public ValueTask<ILaunchBootstrapLease?> PrepareBootstrapAsync(LaunchContext context, CancellationToken cancellationToken)
        {
            BootstrapLaunchId = context.LaunchId;
            return ValueTask.FromResult<ILaunchBootstrapLease?>(new FailedBootstrapLease(nativeFailure));
        }
    }

    private sealed class FailedBootstrapLease(bool nativeFailure) : ILaunchBootstrapLease
    {
        private bool _disposed;
        public ServerEndpoint? GameEndpoint => null;
        public ValueTask DisposeAsync()
        {
            if (nativeFailure) return ValueTask.FromException(new System.ComponentModel.Win32Exception(5));
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            return ValueTask.FromException(new IOException("Support process exited unsuccessfully."));
        }
    }

    public void Dispose() => Directory.Delete(_root, true);
}
