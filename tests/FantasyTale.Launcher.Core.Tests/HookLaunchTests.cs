using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

[Collection("native hook runtime")]
public sealed class HookLaunchTests(Xunit.Abstractions.ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-hook-chain-").FullName;
    private bool _preserve;
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
    public void DeploymentVerifiesBeforeMutationAndRestoresAnOriginalAndAlias()
    {
        var (client, package, plan) = DeploymentFixture();
        File.WriteAllText(Path.Combine(package, "host.dll"), "tampered");
        Assert.Throws<IOException>(() => RuntimeDeployment.Apply(client, "test", plan));
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "host.dll")));
        Assert.False(File.Exists(Path.Combine(client, "original.dll")));
        Assert.False(Directory.Exists(Path.Combine(client, ".fantasytale-runtime")));
        File.WriteAllText(Path.Combine(package, "host.dll"), "new-host");
        var deployment = RuntimeDeployment.Apply(client, "test", plan);
        Assert.Equal("new-host", File.ReadAllText(Path.Combine(client, "host.dll")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "original.dll")));
        deployment.Restore();
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "host.dll")));
        Assert.False(File.Exists(Path.Combine(client, "original.dll")));
    }

    [Fact]
    public void InterruptedDeploymentCanBeRecoveredAndExternalChangesPreserveBackups()
    {
        var (client, _, plan) = DeploymentFixture();
        _ = RuntimeDeployment.Apply(client, "test", plan);
        File.WriteAllText(Path.Combine(client, "host.dll"), "external-change");
        Assert.Throws<IOException>(() => RuntimeDeployment.RecoverStoppedClient(client));
        Assert.True(File.Exists(Path.Combine(client, ".fantasytale-runtime", "old-1")));
        Assert.Equal("external-change", File.ReadAllText(Path.Combine(client, "host.dll")));
        File.WriteAllText(Path.Combine(client, "host.dll"), "new-host");
        RuntimeDeployment.RecoverStoppedClient(client);
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "host.dll")));
    }

    [Fact]
    public void RecoveryCompletesAfterOriginalBackupWasAtomicallyRestored()
    {
        var (client, _, plan) = DeploymentFixture();
        _ = RuntimeDeployment.Apply(client, "test", plan);
        File.Move(Path.Combine(client, ".fantasytale-runtime", "old-1"), Path.Combine(client, "host.dll"), true);
        RuntimeDeployment.RecoverStoppedClient(client);
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "host.dll")));
        Assert.False(File.Exists(Path.Combine(client, "original.dll")));
        Assert.False(Directory.Exists(Path.Combine(client, ".fantasytale-runtime")));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("con.dll")]
    [InlineData("host.dll:payload")]
    [InlineData("session.toml")]
    public void DeploymentRejectsUnsafeTargets(string target)
    {
        var (client, _, plan) = DeploymentFixture();
        Assert.Throws<IOException>(() => RuntimeDeployment.Apply(client, "test", plan with { Files = [new("host.dll", target)] }));
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "host.dll")));
    }

    [Fact]
    public async Task RestorationWaitsForTransientReaderWithoutLosingOriginal()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (client, _, plan) = DeploymentFixture();
        var deployment = RuntimeDeployment.Apply(client, "test", plan);
        var reader = new FileStream(Path.Combine(client, "host.dll"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var restored = Task.Run(deployment.Restore);
        try
        {
            await Task.Delay(200);
            Assert.False(restored.IsCompleted);
        }
        finally { reader.Dispose(); }
        await restored.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "host.dll")));
        Assert.False(Directory.Exists(Path.Combine(client, ".fantasytale-runtime")));
    }

    [Fact]
    public void RestorationStopsAfterBoundedPersistentLockAndRetainsRecovery()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (client, _, plan) = DeploymentFixture();
        var deployment = RuntimeDeployment.Apply(client, "test", plan);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using (var reader = new FileStream(Path.Combine(client, "host.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(deployment.Restore);
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
            Assert.InRange(watch.Elapsed.TotalSeconds, 1.8, 8);
            Assert.Equal("new-host", File.ReadAllText(Path.Combine(client, "host.dll")));
            Assert.Equal("original", File.ReadAllText(Path.Combine(client, ".fantasytale-runtime", "old-1")));
            Assert.True(File.Exists(Path.Combine(client, ".fantasytale-runtime", "journal.json")));
        }
        RuntimeDeployment.RecoverStoppedClient(client);
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, "host.dll")));
    }

    [Fact]
    public async Task RestorationRechecksExternalChangesWhileWaitingForReader()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (client, _, plan) = DeploymentFixture();
        var deployment = RuntimeDeployment.Apply(client, "test", plan);
        var reader = new FileStream(Path.Combine(client, "host.dll"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var restored = Task.Run(() => Record.Exception(deployment.Restore));
        try
        {
            await Task.Delay(200);
            Assert.False(restored.IsCompleted);
            reader.SetLength(0); reader.Write("external-change"u8); reader.Flush(true);
        }
        finally { reader.Dispose(); }
        Assert.IsType<IOException>(await restored.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("external-change", File.ReadAllText(Path.Combine(client, "host.dll")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(client, ".fantasytale-runtime", "old-1")));
    }

    [Fact]
    public async Task RealNativeHostReportsReadinessAndAnswersAuthenticatedControlRequests()
    {
        var (store, service, game, module) = await NativeFixture();
        var client = store.ClientFolder("sample");
        var running = service.Start("sample", game, module, [], "test-account", "test-ticket-never-log");
        try
        {
            var ready = await running.Readiness.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("ready", ready.State);
            Assert.Equal("pong", await running.Control!.RequestAsync("ping"));
            var status = await running.Control.RequestAsync("status");
            Assert.Contains("state = \"ready\"", status, StringComparison.Ordinal);
            Assert.DoesNotContain("test-ticket", status, StringComparison.Ordinal);
            Assert.Throws<IOException>(() => File.WriteAllText(Path.Combine(client, "session.toml"), "replacement"));
        }
        finally { File.WriteAllText(Path.Combine(client, "release"), "exit"); }
        Assert.Equal(23, (await running.Completion.WaitAsync(TimeSpan.FromSeconds(15))).ExitCode);
        Assert.False(File.Exists(Path.Combine(client, "session.toml")));
        Assert.Equal("original-messenger", File.ReadAllText(Path.Combine(client, "nmcogame.dll")));
        Assert.False(Directory.Exists(Path.Combine(client, ".fantasytale-runtime")));
        Assert.DoesNotContain("test-ticket", File.ReadAllText(Directory.GetFiles(store.LogFolder("sample"), "fthook.log", SearchOption.AllDirectories).Single()), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(store.LogFolder("sample"), "crash.log", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(store.LogFolder("sample"), "exception.dmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(store.LogFolder("sample"), "receive.ftnet", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExplicitStopUsesTheNativeChannelAndCleansTheOverlay()
    {
        var (store, service, game, module) = await NativeFixture();
        var running = service.Start("sample", game, module, []);
        Assert.Equal("ready", (await running.Readiness.WaitAsync(TimeSpan.FromSeconds(15))).State);
        var result = await service.StopAsync("sample").WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(service.Running);
        Assert.False(File.Exists(Path.Combine(store.ClientFolder("sample"), "session.toml")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostControlChannelKeepsTheNativeClientAliveUntilExplicitStopAndRestoration(bool diagnostics)
    {
        var (store, service, game, module) = await NativeFixture();
        var packages = new List<PluginDefinition>();
        if (diagnostics)
        {
            var package = CurrentCrashRecorderPackage();
            packages.Add(package);
            store.Save(store.Load("sample") with { Plugins = [new(package.Id, package.Version, true, false, new Dictionary<string, object>())] });
        }
        var client = store.ClientFolder("sample");
        var running = service.Start("sample", game, module, packages);
        try
        {
            Assert.Equal("ready", (await running.Readiness.WaitAsync(TimeSpan.FromSeconds(15))).State);
            Assert.Equal("pong", await running.Control!.RequestAsync("ping"));
            running.Control.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => running.Control.RequestAsync("ping"));
            File.WriteAllText(Path.Combine(client, "probe-net"), "receive");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(Path.Combine(client, "probe-done"))) await Task.Delay(20, timeout.Token);
            Assert.False(running.Completion.IsCompleted);
            Assert.Single(service.Running);
            Assert.True(File.Exists(Path.Combine(client, "session.toml")));
            Assert.Equal(-1, (await service.StopAsync("sample").WaitAsync(TimeSpan.FromSeconds(15))).ExitCode);
            Assert.Empty(service.Running);
            Assert.False(File.Exists(Path.Combine(client, "session.toml")));
            Assert.Equal("original-messenger", File.ReadAllText(Path.Combine(client, "nmcogame.dll")));
            Assert.False(Directory.Exists(Path.Combine(client, ".fantasytale-runtime")));
            using var lease = store.Acquire("sample");
            var traces = Directory.GetFiles(store.LogFolder("sample"), "receive.ftnet", SearchOption.AllDirectories);
            if (diagnostics)
            {
                Assert.Contains("diagnostic-loopback-payload", Encoding.ASCII.GetString(File.ReadAllBytes(Assert.Single(traces))), StringComparison.Ordinal);
                Assert.Single(Directory.GetFiles(store.LogFolder("sample"), "exception.dmp.status.txt", SearchOption.AllDirectories));
            }
            else Assert.Empty(traces);
            Assert.Empty(Directory.GetFiles(store.LogFolder("sample"), "exception.dmp", SearchOption.AllDirectories));
        }
        finally
        {
            if (!running.Completion.IsCompleted)
            {
                File.WriteAllText(Path.Combine(client, "release"), "exit");
                await running.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            }
        }
    }

    [Fact]
    public async Task RequiredPluginFailureIsReportedWithoutPretendingTheGameIsReady()
    {
        var (store, service, game, module) = await NativeFixture();
        var folder = Directory.CreateDirectory(Path.Combine(_root, "test.required")).FullName;
        var manifest = Path.Combine(folder, "plugin.toml");
        File.WriteAllText(manifest, """
            schema = 1
            [plugin]
            id = "test.required"
            version = "1.0.0"
            game = "bubblefighter"
            kind = "patch"
            name = { en = "Required fixture" }
            [compatibility]
            [[patches]]
            id = "missing"
            deadline_ms = 100
            locate = { pattern = "01 34 67 9A BC DE F0 12 44 99 88 77 66 55" }
            expect = "01"
            write = "02"
            """);
        var package = PluginCatalog.LoadFile(manifest);
        game = game with { Builds = game.Builds.Select(build => build with { ClientPolicy = PolicyFixture.Rules(package.Id, "require") }).ToArray() };
        store.Save(store.Load("sample") with { Plugins = [new(package.Id, package.Version, true, true, new Dictionary<string, object>())] });
        var running = service.Start("sample", game, module, [package]);
        try
        {
            var status = await running.Readiness.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("failed", status.State); Assert.Equal("required-plugin", status.Error);
            Assert.Single(status.Plugins); Assert.True(status.Plugins[0].Required);
        }
        finally
        {
            File.WriteAllText(Path.Combine(store.ClientFolder("sample"), "release"), "exit");
            await running.Completion.WaitAsync(TimeSpan.FromSeconds(15));        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentStopsShareNativeTerminationAndCleanup(bool exitLauncher)
    {
        var (store, service, game, module) = await NativeFixture();
        var running = service.Start("sample", game, module, []);
        try
        {
            var first = service.StopAsync("sample");
            Task second;
            if (exitLauncher) second = service.StopAllAsync();
            else
            {
                var repeated = service.StopAsync("sample");
                Assert.Same(first, repeated);
                second = repeated;
            }
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(0, (await first).ExitCode);
            Assert.Empty(service.Running);
            Assert.False(File.Exists(Path.Combine(store.ClientFolder("sample"), "session.toml")));
            Assert.Equal("original-messenger", File.ReadAllText(Path.Combine(store.ClientFolder("sample"), "nmcogame.dll")));
            using var lease = store.Acquire("sample");
        }
        finally
        {
            if (!running.Completion.IsCompleted)
            {
                File.WriteAllText(Path.Combine(store.ClientFolder("sample"), "release"), "exit");
                await running.Completion.WaitAsync(TimeSpan.FromSeconds(15));
            }
        }
    }

    [Theory]
    [InlineData("id-400")]
    [InlineData("cn-406")]
    [InlineData("kr-812")]
    [InlineData("cn-279")]
    [InlineData("cn-324")]
    [InlineData("cn-706")]
    [InlineData("cn-717")]
    [InlineData("id-1809")]
    [InlineData("kr-1256")]
    public async Task RealCrashRetainsAParseableDumpAndReceiveTraceAfterRestoringTheClient(string buildId)
    {
        _preserve = true;
        var (store, service, game, module) = await NativeFixture(buildId: buildId);
        var package = CurrentCrashRecorderPackage();
        store.Save(store.Load("sample") with { Plugins = [new(package.Id, package.Version, true, false, new Dictionary<string, object>())] });
        var running = service.Start("sample", game, module, [package]);
        var client = store.ClientFolder("sample");
        try
        {
            var status = await running.Readiness.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("ready", status.State); Assert.Equal("installed", Assert.Single(status.Plugins).Outcome);
            File.WriteAllText(Path.Combine(client, "probe-net"), "receive");
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(Path.Combine(client, "probe-done"))) await Task.Delay(20, deadline.Token);
            File.WriteAllText(Path.Combine(client, "crash"), "fault");
            Assert.Equal(unchecked((int)0xC0000005), (await running.Completion.WaitAsync(TimeSpan.FromSeconds(20))).ExitCode);
            var dump = File.ReadAllBytes(Directory.GetFiles(store.LogFolder("sample"), "exception.dmp", SearchOption.AllDirectories).Single());
            Assert.Contains("build=" + buildId, File.ReadAllText(Directory.GetFiles(store.LogFolder("sample"), "crash.log", SearchOption.AllDirectories).Single()), StringComparison.Ordinal);
            Assert.Equal("MDMP", Encoding.ASCII.GetString(dump, 0, 4));
            Assert.InRange(dump.Length, 32, 32 * 1024 * 1024);
            var streams = BitConverter.ToInt32(dump, 8); var directory = BitConverter.ToInt32(dump, 12);
            var exceptions = Enumerable.Range(0, streams).Select(i => directory + 12 * i).Where(offset => BitConverter.ToInt32(dump, offset) == 6).ToArray();
            var exceptionRva = BitConverter.ToInt32(dump, Assert.Single(exceptions) + 8);
            Assert.Equal(0xC0000005u, BitConverter.ToUInt32(dump, exceptionRva + 8));
            var dumpStatus = File.ReadAllText(Directory.GetFiles(store.LogFolder("sample"), "exception.dmp.status.txt", SearchOption.AllDirectories).Single());
            Assert.Contains("written error=0", dumpStatus, StringComparison.Ordinal);
            Assert.InRange(Encoding.UTF8.GetByteCount(dumpStatus), 1, 1024);
            var trace = File.ReadAllBytes(Directory.GetFiles(store.LogFolder("sample"), "receive.ftnet", SearchOption.AllDirectories).Single());
            Assert.Equal("FTNET", Encoding.ASCII.GetString(trace, 0, 5));
            Assert.Contains("diagnostic-loopback-payload", Encoding.ASCII.GetString(trace), StringComparison.Ordinal);
            Assert.Equal("original-messenger", File.ReadAllText(Path.Combine(client, "nmcogame.dll")));
            Assert.False(File.Exists(Path.Combine(client, "session.toml")));
            _preserve = false;
        }
        finally
        {
            if (!running.Completion.IsCompleted) { File.WriteAllText(Path.Combine(client, "release"), "exit"); await running.Completion.WaitAsync(TimeSpan.FromSeconds(15)); }
        }
    }

    [Theory]
    [InlineData("valid", false)]
    [InlineData("missing", false)]
    [InlineData("malformed", false)]
    [InlineData("required-plugin", false)]
    [InlineData("valid", true)]
    [InlineData("missing", true)]
    [InlineData("malformed", true)]
    [InlineData("required-plugin", true)]
    public async Task LoginBridgeUsesManagedIdentityAfterReadinessAndRestoresBothLibraries(string scenario, bool legacy)
    {
        var (store, service, game, module) = await NativeFixture(loginBridge: true);
        var client = store.ClientFolder("sample");
        File.WriteAllText(Path.Combine(client, "sdologinentry.dll"), "original-login");
        var account = "launcher-account-longer-than-fifteen";
        var ticket = scenario == "malformed" ? "not-a-launch-ticket" : new string('a', 64);
        var packages = new List<PluginDefinition>();
        if (scenario == "required-plugin")
        {
            var folder = Directory.CreateDirectory(Path.Combine(_root, "test.login-required")).FullName;
            var manifest = Path.Combine(folder, "plugin.toml");
            File.WriteAllText(manifest, """
                schema = 1
                [plugin]
                id = "test.login-required"
                version = "1.0.0"
                game = "bubblefighter"
                kind = "patch"
                name = { en = "Required login fixture" }
                [compatibility]
                    [[patches]]
                id = "missing"
                timing = "immediate"
                locate = { pattern = "01 34 67 9A BC DE F0 12 44 99 88 77 66 55" }
                expect = "01"
                write = "02"
                """);
            var package = PluginCatalog.LoadFile(manifest); packages.Add(package);
            game = game with { Builds = game.Builds.Select(build => build with { ClientPolicy = PolicyFixture.Rules(package.Id, "require") }).ToArray() };
            store.Save(store.Load("sample") with { Plugins = [new(package.Id, package.Version, true, true, new Dictionary<string, object>())] });
        }
        var running = service.Start("sample", game, module, packages, scenario == "missing" ? null : account, scenario == "missing" ? null : ticket);
        try
        {
            // Probe immediately, so the bridge must wait for readiness itself.
            if (legacy) File.WriteAllText(Path.Combine(client, "probe-login-legacy"), "legacy");
            File.WriteAllText(Path.Combine(client, scenario == "valid" ? "probe-login" : "probe-login-denied"), "probe");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var resultPath = Path.Combine(client, "login-result");
            while (true)
            {
                if (!File.Exists(resultPath)) { await Task.Delay(20, timeout.Token); continue; }
                try
                {
                    Assert.Equal("ok", await File.ReadAllTextAsync(resultPath, timeout.Token));
                    break;
                }
                catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
                {
                    // File scanners can briefly lease the atomically published result.
                    await Task.Delay(20, timeout.Token);
                }
            }
        }
        catch { _preserve = true; throw; }
        finally
        {
            File.WriteAllText(Path.Combine(client, "release"), "exit");
            await RetainOnCleanupFailureAsync(running);
        }
        Assert.Equal("original-login", File.ReadAllText(Path.Combine(client, "sdologinentry.dll")));
        Assert.Equal("original-messenger", File.ReadAllText(Path.Combine(client, "nmcogame.dll")));
        Assert.False(File.Exists(Path.Combine(client, "session.toml")));
        var logFile = Directory.GetFiles(store.LogFolder("sample"), "fthook.log", SearchOption.AllDirectories).Single();
        var log = await File.ReadAllTextAsync(logFile);
        Assert.DoesNotContain(ticket, log, StringComparison.Ordinal);
        Assert.DoesNotContain(account, log, StringComparison.Ordinal);
        Assert.DoesNotContain("bff-ticket:", log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("kr-812")]
    [InlineData("id-400")]
    [InlineData("id-1809")]
    public async Task DeployedMessengerReturnsMarkedPassportFromTheManagedSession(string buildId)
    {
        var (store, service, game, module) = await NativeFixture(buildId: buildId);
        var client = store.ClientFolder("sample");
        var ticket = new string('a', 64);
        var running = service.Start("sample", game, module, [], "managed-passport-account", ticket);
        try
        {
            File.WriteAllText(Path.Combine(client, "probe-passport"), "probe");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!File.Exists(Path.Combine(client, "passport-result"))) await Task.Delay(20, timeout.Token);
            Assert.Equal("ok", await ReadPublishedProbeAsync(Path.Combine(client, "passport-result"), timeout.Token));
        }
        catch { _preserve = true; throw; }
        finally
        {
            File.WriteAllText(Path.Combine(client, "release"), "exit");
            await running.Completion.WaitAsync(TimeSpan.FromSeconds(15));
        }
        Assert.Equal("original-messenger", File.ReadAllText(Path.Combine(client, "nmcogame.dll")));
        Assert.False(File.Exists(Path.Combine(client, "session.toml")));
        var log = File.ReadAllText(Directory.GetFiles(store.LogFolder("sample"), "fthook.log", SearchOption.AllDirectories).Single());
        Assert.Contains("ticket provided", log, StringComparison.Ordinal);
        Assert.DoesNotContain(ticket, log, StringComparison.Ordinal);
        Assert.DoesNotContain("managed-passport-account", log, StringComparison.Ordinal);
    }

    private async Task RetainOnCleanupFailureAsync(RunningClient running)
    {
        // Windows file replacement can remain inside MoveFileEx after the native process has exited.
        try { await running.Completion.WaitAsync(TimeSpan.FromMinutes(2)); }
        catch (TimeoutException error)
        {
            _preserve = true;
            var processState = "exited";
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(running.ProcessId);
                processState = process.HasExited ? "exited" : "running";
            }
            catch (ArgumentException) { }
            throw new TimeoutException($"Native fixture cleanup timed out: pid={running.ProcessId}, process={processState}, hook={running.Hook.State}, completion={running.Completion.Status}.", error);
        }
        catch { _preserve = true; throw; }
    }

    [Theory]
    [InlineData("granted")]
    [InlineData("gateway")]
    [InlineData("denied")]
    [InlineData("unselected")]
    [InlineData("old-runtime")]
    [InlineData("patch-owner")]
    [InlineData("no-ticket")]
    [InlineData("no-endpoint")]
    public async Task AuthenticationGrantCrossesTheManagedNativeBoundaryOnlyForItsSelectedOwner(string scenario)
    {
        var (store, service, game, module) = await NativeFixture();
        var client = store.ClientFolder("sample");
        var folder = Directory.CreateDirectory(Path.Combine(_root, "authentication-plugin")).FullName;
        var eventName = "Local\\FtHookManagedGrant-" + Guid.NewGuid().ToString("N");
        using var cleanup = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        var native = scenario != "patch-owner";
        var text = "schema=1\n[plugin]\nid=\"test.native\"\nversion=\"1.0.0\"\ngame=\"bubblefighter\"\n"
            + "kind=\"" + (native ? "native" : "patch") + "\"\nname={en=\"Authentication contract fixture\"}\n";
        if (native)
        {
            File.Copy(Path.Combine(Repository, "native/out/Release/native_fixture.dll"), Path.Combine(folder, "native_fixture.dll"));
            text += "[native]\nlibrary=\"native_fixture.dll\"\n"
                + "[[options]]\nkey=\"behavior\"\ntype=\"string\"\ndefault=\"authentication-" + (scenario == "denied" ? "denied" : "api") + "\"\nlabel={en=\"Behavior\"}\n"
                + "[[options]]\nkey=\"cleanup_event\"\ntype=\"string\"\ndefault=" + SessionWriter.Quote(eventName) + "\nlabel={en=\"Cleanup event\"}\n";
        }
        else
        {
            text += "[[patches]]\nid=\"fixture\"\nlocate={pattern=\"91 23 45 67 AA BB\"}\nexpect=\"91\"\nwrite=\"90\"\ntiming=\"immediate\"\n";
        }
        var manifest = Path.Combine(folder, "plugin.toml");
        File.WriteAllText(manifest, text);
        var package = PluginCatalog.LoadFile(manifest);
        if (scenario != "unselected")
            store.Save(store.Load("sample") with { Plugins = [new(package.Id, package.Version, true, true, new Dictionary<string, object>())] });
        string? olderPackage = null;
        if (scenario == "old-runtime")
        {
            olderPackage = Directory.CreateDirectory(Path.Combine(_root, "older-runtime")).FullName;
            var originalPackage = Path.Combine(Repository, "games/BubbleFighter/runtime/nmcogame");
            foreach (var file in Directory.GetFiles(originalPackage)) File.Copy(file, Path.Combine(olderPackage, Path.GetFileName(file)));
            var path = Path.Combine(olderPackage, "runtime.toml");
            File.WriteAllText(path, File.ReadAllText(path).Replace(PluginCatalog.RuntimeVersion, "3.3.3", StringComparison.Ordinal));
        }
        var authentication = new AuthenticationModule(module, scenario == "denied" ? null : "test.native", olderPackage);
        module = scenario == "gateway" ? new BootstrapAuthenticationModule(authentication) : authentication;
        if (scenario is "unselected" or "old-runtime" or "patch-owner" or "no-ticket" or "no-endpoint")
        {
            Assert.Throws<InvalidOperationException>(() => service.Start("sample", game, module, [package], "account",
                scenario == "no-ticket" ? null : "fixture-private-ticket",
                scenario == "no-endpoint" ? null : new ServerEndpoint("127.0.0.1", 7100), useServerOverride: true));
            Assert.False(File.Exists(Path.Combine(client, "session.toml")));
            Assert.Equal("original-messenger", File.ReadAllText(Path.Combine(client, "nmcogame.dll")));
            Assert.Empty(service.Running);
            return;
        }
        var running = service.Start("sample", game, module, [package], "account", "fixture-private-ticket",
            new ServerEndpoint("127.0.0.1", 7100), useServerOverride: true);
        try
        {
            Assert.Equal("ready", (await running.Readiness.WaitAsync(TimeSpan.FromSeconds(15))).State);
            var session = File.ReadAllText(Path.Combine(client, "session.toml"));
            Assert.Equal(scenario is "granted" or "gateway", session.Contains("credential_plugin = \"test.native\"", StringComparison.Ordinal));
            if (module is BootstrapAuthenticationModule gateway)
            {
                var context = Assert.IsType<LaunchContext>(gateway.Context);
                Assert.Equal(new ServerEndpoint("127.0.0.1", 7100), context.Server);
                Assert.Equal(new ServerEndpoint("127.0.0.1", 7200), context.GameEndpoint);
                Assert.Contains("port = 7200", session, StringComparison.Ordinal);
                Assert.DoesNotContain("port = 7100", session, StringComparison.Ordinal);
            }
        }
        finally
        {
            File.WriteAllText(Path.Combine(client, "release"), "exit");
            await RetainOnCleanupFailureAsync(running);
        }
        Assert.False(File.Exists(Path.Combine(client, "session.toml")));
        if (module is BootstrapAuthenticationModule completedGateway) Assert.True(completedGateway.Disposed);
        var log = File.ReadAllText(Directory.GetFiles(store.LogFolder("sample"), "fthook.log", SearchOption.AllDirectories).Single());
        Assert.DoesNotContain("fixture-private-ticket", log, StringComparison.Ordinal);
    }

    private sealed class AuthenticationModule(IGameModule inner, string? owner, string? package) : IGameModule
    {
        public string GameId => inner.GameId;
        public LaunchPlan CreateLaunchPlan(LaunchContext context)
        {
            var plan = inner.CreateLaunchPlan(context);
            return plan with { AuthenticationPlugin = owner, Runtime = package is null ? plan.Runtime : plan.Runtime! with { PackageFolder = package } };
        }
    }

    private sealed class BootstrapAuthenticationModule(IGameModule inner) : IGameModule, ILaunchBootstrapProvider, ILaunchBootstrapLease
    {
        public string GameId => inner.GameId;
        internal LaunchContext? Context { get; private set; }
        internal bool Disposed { get; private set; }
        public ServerEndpoint? GameEndpoint => new("127.0.0.1", 7200);
        public ValueTask<ILaunchBootstrapLease?> PrepareBootstrapAsync(LaunchContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ILaunchBootstrapLease?>(this);
        public LaunchPlan CreateLaunchPlan(LaunchContext context)
        {
            Context = context;
            return inner.CreateLaunchPlan(context);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private static async Task<string> ReadPublishedProbeAsync(string path, CancellationToken ct)
    {
        // Atomic result publication can still encounter a short Windows sharing conflict.
        for (var attempt = 0; ; attempt++)
        {
            try { return await File.ReadAllTextAsync(path, ct); }
            catch (IOException error) when (attempt < 9 && (error.HResult & 0xffff) is 32 or 33)
            { await Task.Delay(20, ct); }
        }
    }

    private async Task<(InstanceStore Store, LaunchService Service, GameDefinition Game, IGameModule Module)> NativeFixture(bool loginBridge = false, string buildId = "id-400")
    {
        var binary = Path.Combine(Repository, "native/out/Release/hook_test_client.exe");
        Assert.True(File.Exists(binary), "Build native/FantasyTale.Native.sln Release|x86 before running hook integration tests.");
        foreach (var (payloadName, target) in new[] {
            ("nmcogame.dll", "runtime/nmcogame/nmcogame.dll"),
            ("login_bridge.dll", "runtime/nmcogame/login_bridge.dll") })
        {
            var built = Path.Combine(Repository, "native/out/Release", payloadName);
            var packaged = Path.Combine(Repository, "games/BubbleFighter", target);
            const string instruction = "Run tools/package-native.ps1 -RuntimeOnly after building native Release|x86; hook tests must use the current binaries.";
            Assert.True(File.Exists(built) && File.Exists(packaged), payloadName + ": " + instruction);
            using var builtFile = File.OpenRead(built); using var packagedFile = File.OpenRead(packaged);
            Assert.True(SHA256.HashData(builtFile).AsSpan().SequenceEqual(SHA256.HashData(packagedFile)), payloadName + ": " + instruction);
        }
        var store = new InstanceStore(Path.Combine(_root, "instances"));
        await store.CreateAtAsync(new("sample", "Native fixture", "bubblefighter", buildId, null, []), null);
        File.Copy(binary, Path.Combine(store.ClientFolder("sample"), "hook_test_client.exe"));
        File.WriteAllText(Path.Combine(store.ClientFolder("sample"), "nmcogame.dll"), "original-messenger");
        var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Native fixture" });
        var build = new BuildDefinition(buildId, buildId.Split('-')[0], 1, name, "hook_test_client.exe", "standin", "", null, null, "nmco", [], []);
        var game = new GameDefinition("bubblefighter", GameStatus.Enabled, name, "test.dll", "nmcogame", "", null, "", [build]);
        return (store, new LaunchService(store, diagnosticSink: new Diagnostics(output)), game, new NativeModule(Path.Combine(Repository, "games/BubbleFighter/runtime/nmcogame"), loginBridge));
    }

    private sealed class Diagnostics(Xunit.Abstractions.ITestOutputHelper output) : ILaunchDiagnosticSink
    {
        public void Record(string name, string? detail = null) => output.WriteLine($"{DateTime.UtcNow:O} {name} {detail}");
    }

    private PluginDefinition CurrentCrashRecorderPackage()
    {
        // Exercise the freshly built plugin in an isolated fixture without mutating installed/published payloads.
        var built = Path.Combine(Repository, "native/out/Release/crash_recorder.dll");
        Assert.True(File.Exists(built), "Build native Release|x86 before running crash integration tests.");
        var folder = Directory.CreateDirectory(Path.Combine(_root, "crash-plugin")).FullName;
        var manifest = Path.Combine(folder, "plugin.toml");
        File.Copy(Path.Combine(Repository, "games/BubbleFighter/plugins/bubblefighter.fantasy.diagnostics/plugin.toml"), manifest, true);
        var staged = Path.Combine(folder, "crash_recorder.dll");
        File.Copy(built, staged, true);
        using var source = File.OpenRead(built);
        using var copy = File.OpenRead(staged);
        Assert.Equal(SHA256.HashData(source), SHA256.HashData(copy));
        return PluginCatalog.LoadFile(manifest);
    }

    private sealed class NativeModule(string package, bool loginBridge = false) : IGameModule
    {
        public string GameId => "bubblefighter";
        public LaunchPlan CreateLaunchPlan(LaunchContext context) => new(Path.Combine(context.InstanceFolder, context.Build.Executable), [], context.InstanceFolder)
        { Runtime = new(package, loginBridge
            ? [new("nmcogame.dll", "nmcogame.dll"), new("login_bridge.dll", "sdologinentry.dll")]
            : [new("nmcogame.dll", "nmcogame.dll")]) };
    }

    private (string Client, string Package, RuntimeDeploymentPlan Plan) DeploymentFixture()
    {
        var client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        var package = Directory.CreateDirectory(Path.Combine(_root, "package")).FullName;
        File.WriteAllText(Path.Combine(client, "host.dll"), "original");
        File.WriteAllText(Path.Combine(package, "host.dll"), "new-host");
        var bytes = Encoding.UTF8.GetBytes("new-host");
        File.WriteAllText(Path.Combine(package, "runtime.toml"), $$"""
            schema = 1
            [runtime]
            game = "test"
            version = "3.1.0"
            session_schema = 2
            [[files]]
            path = "host.dll"
            size = {{bytes.Length}}
            sha256 = "{{Convert.ToHexStringLower(SHA256.HashData(bytes))}}"
            """);
        return (client, package, new(package, [new("host.dll", "host.dll", "original.dll")]));
    }

    public void Dispose()
    {
        if (_preserve) Console.WriteLine("Failure artifacts retained at " + _root);
        else Directory.Delete(_root, true);
    }
}
