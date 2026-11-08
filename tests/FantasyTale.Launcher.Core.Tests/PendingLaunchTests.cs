using System.Diagnostics;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class PendingLaunchTests
{
    private sealed class WaitingModule(TaskCompletionSource entered, ManualResetEventSlim release, bool decline = true) : IGameModule
    {
        public string GameId => "fixture";
        public LaunchPlan CreateLaunchPlan(LaunchContext context)
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Pending launch fixture was not released.");
            if (decline) throw new InvalidOperationException("Declined fixture launch.");
            return new(Path.Combine(context.InstanceFolder, context.Build.Executable), [], context.InstanceFolder);
        }
    }

    [Fact]
    public async Task ExitRequestedDuringPreparationStopsTheSubsequentlyStartedProcessAndCleansItsLease()
    {
        var root = Directory.CreateTempSubdirectory("ft-pending-success-").FullName;
        int? processId = null;
        int? exitCode = null;
        try
        {
            var store = new InstanceStore(root);
            await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []), Path.Combine(AppContext.BaseDirectory, "test-client"));
            var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" });
            var build = new BuildDefinition("test-1", "test", 1, name, "FantasyTale.Launcher.TestClient.exe", "", "", null, null, "none", [], []);
            var game = new GameDefinition("fixture", GameStatus.Enabled, name, "fixture.dll", "external", "", null, "", [build]);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            var service = new LaunchService(store);
            var start = service.StartAsync("fixture", game, new WaitingModule(entered, release, decline: false), []);
            Task? shutdown = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                shutdown = service.StopAllAsync();
                Assert.False(shutdown.IsCompleted);
                Assert.True(service.IsActive("fixture"));
            }
            finally
            {
                release.Set();
                var client = await start.WaitAsync(TimeSpan.FromSeconds(15));
                processId = client.ProcessId;
                shutdown ??= service.StopAllAsync();
                await shutdown.WaitAsync(TimeSpan.FromSeconds(15));
                exitCode = (await client.Completion.WaitAsync(TimeSpan.FromSeconds(5))).ExitCode;
            }
            Assert.Empty(service.Running);
            Assert.False(service.HasPendingStarts);
            Assert.False(service.IsActive("fixture"));
            Assert.False(File.Exists(Path.Combine(store.ClientFolder("fixture"), "session.toml")));
            using var lease = store.Acquire("fixture");
        }
        finally { await DeleteFixtureAsync(root, processId, exitCode); }
    }

    [Fact]
    public async Task CanceledPreparationCannotCreateAClientAfterTheModuleReturns()
    {
        var root = Directory.CreateTempSubdirectory("ft-canceled-launch-").FullName;
        try
        {
            var store = new InstanceStore(root);
            await store.CreateAtAsync(new("fixture", "Fixture", "fixture", "test-1", null, []), null);
            var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" });
            var build = new BuildDefinition("test-1", "test", 1, name, "missing.exe", "", "", null, null, "none", [], []);
            var game = new GameDefinition("fixture", GameStatus.Enabled, name, "fixture.dll", "external", "", null, "", [build]);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            using var cancellation = new CancellationTokenSource();
            var service = new LaunchService(store);
            var start = service.StartAsync("fixture", game, new WaitingModule(entered, release, decline: false), [], preparationCancellation: cancellation.Token);
            Task? stop = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cancellation.Cancel();
                stop = service.CancelStartingAsync("fixture");
                Assert.False(stop.IsCompleted);
            }
            finally { release.Set(); }
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
            await stop!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(service.HasPendingStarts);
            Assert.Empty(service.Running);
            Assert.False(File.Exists(Path.Combine(store.ClientFolder("fixture"), "session.toml")));
            using var lease = store.Acquire("fixture");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PreparationBudgetCancelsAModuleWithoutAnExternalCancellationToken()
    {
        var root = Directory.CreateTempSubdirectory("ft-preparation-budget-").FullName;
        try
        {
            var store = new InstanceStore(root);
            await store.CreateAtAsync(new("fixture", "Fixture", "fixture", "test-1", null, []), null);
            var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" });
            var build = new BuildDefinition("test-1", "test", 1, name, "missing.exe", "", "", null, null, "none", [], []);
            var game = new GameDefinition("fixture", GameStatus.Enabled, name, "fixture.dll", "external", "", null, "", [build]);
            var module = new DeadlineModule();
            var service = new LaunchService(store);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StartAsync("fixture", game, module, [],
                startupTimeout: TimeSpan.FromMilliseconds(50)).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(module.SawCancellation);
            Assert.False(service.HasPendingStarts);
            Assert.Empty(service.Running);
            using var lease = store.Acquire("fixture");
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class DeadlineModule : IGameModule
    {
        public string GameId => "fixture";
        internal bool SawCancellation { get; private set; }
        public LaunchPlan CreateLaunchPlan(LaunchContext context)
        {
            if (!context.PreparationCancellation.WaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                throw new TimeoutException("The launch coordinator did not enforce its preparation budget.");
            SawCancellation = true;
            context.PreparationCancellation.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The canceled preparation continued.");
        }
    }

    [Fact]
    public async Task CanceledPostStartRetainsOwnershipWhenTerminationIsDenied()
    {
        var root = Directory.CreateTempSubdirectory("ft-post-start-denied-").FullName;
        int? processId = null;
        int? exitCode = null;
        RunningClient? running = null;
        InstanceStore? store = null;
        try
        {
            store = new InstanceStore(root);
            await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []), Path.Combine(AppContext.BaseDirectory, "test-client"));
            var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" });
            var build = new BuildDefinition("test-1", "test", 1, name, "FantasyTale.Launcher.TestClient.exe", "", "", null, null, "none", [], []);
            var game = new GameDefinition("fixture", GameStatus.Enabled, name, "fixture.dll", "external", "", null, "", [build]);
            using var cancellation = new CancellationTokenSource();
            using var release = new ManualResetEventSlim(true);
            var denied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var service = new LaunchService(store)
            {
                ProcessLauncher = start =>
                {
                    start.UseShellExecute = false;
                    start.CreateNoWindow = true;
                    var child = Process.Start(start)!;
                    processId = child.Id;
                    cancellation.Cancel();
                    return child;
                },
                ProcessTerminator = (_, _) =>
                {
                    denied.TrySetResult();
                    throw new System.ComponentModel.Win32Exception(5);
                }
            };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StartAsync("fixture", game,
                new WaitingModule(new(), release, decline: false), [], preparationCancellation: cancellation.Token));
            await denied.Task.WaitAsync(TimeSpan.FromSeconds(5));
            running = Assert.Single(service.Running);
            Assert.Equal(processId, running.ProcessId);
            Assert.True(service.IsActive("fixture"));
            Assert.False(service.HasPendingStarts);
            Assert.False(running.Completion.IsCompleted);
            Assert.True(File.Exists(Path.Combine(store.ClientFolder("fixture"), "session.toml")));
            Assert.Throws<InstanceBusyException>(() => store.Acquire("fixture"));
            var drain = service.DrainAsync();
            Assert.False(drain.IsCompleted);
            File.WriteAllText(Path.Combine(store.ClientFolder("fixture"), "release"), "exit");
            var failure = await Assert.ThrowsAsync<LaunchCleanupException>(() => running.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(failure.ExitConfirmed);
            Assert.Equal(23, failure.ExitCode);
            exitCode = failure.ExitCode;
            await Assert.ThrowsAsync<LaunchCleanupException>(() => drain);
            Assert.Empty(service.Running);
            Assert.False(File.Exists(Path.Combine(store.ClientFolder("fixture"), "session.toml")));
            using var lease = store.Acquire("fixture");
        }
        finally
        {
            if (processId is { } pid && exitCode is null)
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    exitCode = process.ExitCode;
                }
                catch (ArgumentException) { exitCode = 0; }
                if (running is not null)
                {
                    try { await running.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
                    catch (LaunchCleanupException) { }
                }
            }
            await DeleteFixtureAsync(root, processId, exitCode);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BootstrapDisposeFailureDoesNotPreventSessionCleanupOrCompletion(bool aggregate)
    {
        var root = Directory.CreateTempSubdirectory("ft-bootstrap-cleanup-").FullName;
        int? processId = null;
        int? exitCode = null;
        try
        {
            var store = new InstanceStore(root);
            await store.CreateAsync(new("fixture", "Fixture", "fixture", "test-1", null, []), Path.Combine(AppContext.BaseDirectory, "test-client"));
            var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" });
            var build = new BuildDefinition("test-1", "test", 1, name, "FantasyTale.Launcher.TestClient.exe", "", "", null, null, "none", [], []);
            var game = new GameDefinition("fixture", GameStatus.Enabled, name, "fixture.dll", "external", "", null, "", [build]);
            var module = new FailingBootstrapModule(aggregate);
            var service = new LaunchService(store);
            var running = await service.StartAsync("fixture", game, module, [], "fixture-account", "fixture-ticket");
            processId = running.ProcessId;
            File.WriteAllText(Path.Combine(store.ClientFolder("fixture"), "release"), "exit");
            var error = await Assert.ThrowsAsync<LaunchCleanupException>(() => running.Completion.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("bootstrap-dispose", error.Phase);
            Assert.True(error.ExitConfirmed);
            exitCode = error.ExitCode;
            Assert.Equal(23, exitCode);
            Assert.Equal(1, module.DisposeCount);
            Assert.Empty(service.Running);
            Assert.False(File.Exists(Path.Combine(store.ClientFolder("fixture"), "session.toml")));
            using var lease = store.Acquire("fixture");
        }
        finally { await DeleteFixtureAsync(root, processId, exitCode); }
    }

    private sealed class FailingBootstrapModule(bool aggregate) : IGameModule, ILaunchBootstrapProvider, ILaunchBootstrapLease
    {
        public string GameId => "fixture";
        public ServerEndpoint? GameEndpoint => null;
        internal int DisposeCount { get; private set; }
        public LaunchPlan CreateLaunchPlan(LaunchContext context) =>
            new(Path.Combine(context.InstanceFolder, context.Build.Executable), [], context.InstanceFolder);
        public ValueTask<ILaunchBootstrapLease?> PrepareBootstrapAsync(LaunchContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ILaunchBootstrapLease?>(this);
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.FromException(aggregate ? new AggregateException(new NotSupportedException("Fixture disposal failure."))
                : new NotSupportedException("Fixture disposal failure."));
        }
    }

    private static async Task DeleteFixtureAsync(string root, int? processId, int? exitCode)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try { Directory.Delete(root, true); return; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                var processes = Process.GetProcessesByName("FantasyTale.Launcher.TestClient");
                var exited = false;
                string details;
                try
                {
                    var owned = processes.SingleOrDefault(process => process.Id == processId);
                    var executable = Path.Combine(root, "fixture", "client", "FantasyTale.Launcher.TestClient.exe");
                    var attributes = File.Exists(executable) ? File.GetAttributes(executable) : (FileAttributes)0;
                    exited = exitCode is not null && (owned is null || owned.HasExited) && !attributes.HasFlag(FileAttributes.ReadOnly);
                    details = $"pid={processId}, exitCode={exitCode}, ownedProcessPresent={owned is not null}, ownedProcessExited={owned?.HasExited}, executableAttributes={attributes}, root={root}";
                }
                finally { foreach (var process in processes) process.Dispose(); }
                // A signaled Windows process may still be releasing its image and startup metadata.
                if (!OperatingSystem.IsWindows() || !exited || (error.HResult & 0xffff) is not (5 or 32 or 33) || elapsed.Elapsed >= TimeSpan.FromSeconds(2))
                    throw new IOException("Pending launch cleanup failed: " + details, error);
                await Task.Delay(25);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitedFixtureCleanupWaitsForTransientFileLocksButFailsForPersistentOnes(bool persistent)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ft-pending-cleanup-").FullName;
        try
        {
            var file = Path.Combine(root, "fixture", "client", "FantasyTale.Launcher.TestClient.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "fixture");
            var elapsed = Stopwatch.StartNew();
            using (var reader = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var cleanup = DeleteFixtureAsync(root, null, 0);
                if (persistent)
                {
                    var error = await Assert.ThrowsAsync<IOException>(() => cleanup);
                    Assert.Contains("exitCode=0", error.Message, StringComparison.Ordinal);
                    Assert.InRange(elapsed.Elapsed.TotalSeconds, 1.8, 8);
                    Assert.True(File.Exists(file));
                }
                else
                {
                    await Task.Delay(75);
                    Assert.False(cleanup.IsCompleted);
                    reader.Dispose();
                    await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.False(Directory.Exists(root));
                }
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingStartDoesNotBlockSnapshotsAndShutdownWaitsForItsCleanup(bool stop)
    {
        var root = Directory.CreateTempSubdirectory("ft-pending-launch-").FullName;
        try
        {
            var store = new InstanceStore(root);
            await store.CreateAtAsync(new("fixture", "Fixture", "fixture", "test-1", null, []), null);
            var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" });
            var build = new BuildDefinition("test-1", "test", 1, name, "fixture.exe", "", "", null, null, "none", [], []);
            var game = new GameDefinition("fixture", GameStatus.Enabled, name, "fixture.dll", "external", "", null, "", [build]);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            var module = new WaitingModule(entered, release);
            var service = new LaunchService(store, maximumConcurrent: 1);
            var start = service.StartAsync("fixture", game, module, []);
            Task? shutdown = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(service.HasPendingStarts);
                Assert.True(service.IsActive("fixture"));
                Assert.False(service.IsActive("another"));
                Assert.Empty(await Task.Run(() => service.Running).WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Throws<InvalidOperationException>(() => service.Start("fixture", game, module, []));
                Assert.Throws<InvalidOperationException>(() => service.Start("another", game, module, []));
                Assert.Throws<InstanceBusyException>(() => store.Acquire("fixture"));
                shutdown = stop ? service.StopAllAsync() : service.DrainAsync();
                Assert.False(shutdown.IsCompleted);
            }
            finally { release.Set(); }
            await Assert.ThrowsAsync<InvalidOperationException>(() => start);
            if (shutdown is not null) await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(service.Running);
            Assert.False(service.HasPendingStarts);
            Assert.False(service.IsActive("fixture"));
            using var lease = store.Acquire("fixture");
            Assert.False(File.Exists(Path.Combine(store.ClientFolder("fixture"), "session.toml")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("fixture", game, module, []));
        }
        finally { Directory.Delete(root, true); }
    }
}
