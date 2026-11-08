using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Host.Window;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class WindowExitTests
{
    private sealed class DecliningModule : IGameModule
    {
        public string GameId => "fixture";
        internal bool Entered { get; private set; }
        public LaunchPlan CreateLaunchPlan(LaunchContext context)
        {
            Entered = true;
            throw new IOException("Fixture plan declined before process creation.");
        }
    }
    [Fact]
    public async Task ConcurrentExitRequestsWaitForCleanupAndCloseExactlyOnce()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stops = 0; var closes = 0;
        var operation = new WindowExitOperation(async () =>
        {
            Interlocked.Increment(ref stops); entered.TrySetResult(); await release.Task;
        }, () => Interlocked.Increment(ref closes));
        var first = operation.RequestAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(first, operation.RequestAsync()); Assert.False(first.IsCompleted);
        Assert.Equal(0, closes);
        release.TrySetResult(); await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(first, operation.RequestAsync());
        Assert.Equal(1, stops); Assert.Equal(1, closes);
    }

    [Fact]
    public async Task FailedExitReachesTheBridgeAndCanBeRetriedWithoutClosingTheWindow()
    {
        var stops = 0; var closes = 0;
        var operation = new WindowExitOperation(() => ++stops == 1 ? Task.FromException(new IOException("Fixture cleanup failed.")) : Task.CompletedTask,
            () => closes++);
        var replies = new List<JsonNode>(); var trace = new List<string>();
        var bridge = new BridgeRouter(raw => replies.Add(JsonNode.Parse(raw)!), (method, phase) => trace.Add(method + ":" + phase));
        bridge.Add("window.exit", async (_, _) => { await operation.RequestAsync(); return null; });
        await bridge.HandleAsync("{\"id\":1,\"method\":\"window.exit\"}", default);
        Assert.Equal("Fixture cleanup failed.", replies[0]["error"]!.GetValue<string>());
        Assert.Equal(0, closes);
        await bridge.HandleAsync("{\"id\":2,\"method\":\"window.exit\"}", default);
        Assert.Null(replies[1]["error"]); Assert.Equal(1, closes);
        Assert.Contains("window.exit:failed-IOException", trace);
    }

    [Fact]
    public async Task RemainingProcessCleanupFailureDoesNotPermanentlyCloseTheLaunchGate()
    {
        var root = Directory.CreateTempSubdirectory("ft-exit-gate-").FullName;
        try
        {
            var store = new InstanceStore(root);
            await store.CreateAtAsync(new("fixture", "Fixture", "fixture", "test-1", null, []), null);
            var service = new LaunchService(store);
            await Assert.ThrowsAsync<IOException>(() => service.StopAllAsync(() => Task.FromException(new IOException("Fixture remaining cleanup failed."))));
            var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" });
            var build = new BuildDefinition("test-1", "test", 1, name, "fixture.exe", "", "", null, null, "none", [], []);
            var game = new GameDefinition("fixture", GameStatus.Enabled, name, "fixture.dll", "external", "", null, "", [build]);
            var module = new DecliningModule();
            var error = await Assert.ThrowsAsync<IOException>(() => service.StartAsync("fixture", game, module, []));
            Assert.Contains("Fixture plan declined", error.Message, StringComparison.Ordinal);
            Assert.True(module.Entered);
            var entered = false;
            await service.StopAllAsync(() => { entered = true; return Task.CompletedTask; });
            Assert.True(entered);
            var closing = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("fixture", game, module, []));
            Assert.Equal("The launcher is shutting down.", closing.Message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ConfirmedUpdateCleanupDoesNotPermanentlyCloseTheLaunchGate()
    {
        var root = Directory.CreateTempSubdirectory("ft-update-gate-").FullName;
        try
        {
            var store = new InstanceStore(root);
            await store.CreateAtAsync(new("fixture", "Fixture", "fixture", "test-1", null, []), null);
            var service = new LaunchService(store);
            var stopped = false;
            await service.StopForUpdateAsync(() => { stopped = true; return Task.CompletedTask; });
            Assert.True(stopped);
            var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Fixture" });
            var build = new BuildDefinition("test-1", "test", 1, name, "fixture.exe", "", "", null, null, "none", [], []);
            var game = new GameDefinition("fixture", GameStatus.Enabled, name, "fixture.dll", "external", "", null, "", [build]);
            var module = new DecliningModule();
            await Assert.ThrowsAsync<IOException>(() => service.StartAsync("fixture", game, module, []));
            Assert.True(module.Entered);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AFailedDiagnosticSinkCannotSuppressTheWindowReply()
    {
        JsonNode? reply = null;
        var bridge = new BridgeRouter(raw => reply = JsonNode.Parse(raw), (_, _) => throw new IOException("Fixture recording unavailable."));
        bridge.Add("window.exit", _ => 42);
        await bridge.HandleAsync("{\"id\":1,\"method\":\"window.exit\"}", default);
        Assert.Equal(42, reply!["result"]!.GetValue<int>());
        Assert.Null(reply["error"]);
    }

    [Fact]
    public async Task LifecycleTraceNeverReceivesBridgeArgumentsRepliesOrExceptionContents()
    {
        var root = Directory.CreateTempSubdirectory("ft-lifecycle-").FullName;
        try
        {
            var log = new HostLifecycleLog(root);
            var bridge = new BridgeRouter(_ => { }, (method, phase) => log.Write("bridge-" + phase, method));
            bridge.Add("accounts.login", _ => throw new InvalidOperationException("secret-reply-token"));
            await bridge.HandleAsync("{\"id\":1,\"method\":\"accounts.login\",\"params\":{\"password\":\"secret-password\",\"refreshToken\":\"secret-refresh\"}}", default);
            var text = File.ReadAllText(Path.Combine(root, "host-lifecycle.jsonl"));
            Assert.DoesNotContain("secret-", text, StringComparison.Ordinal);
            Assert.Contains("accounts.login", text, StringComparison.Ordinal);
            Assert.Contains("failed-InvalidOperationException", text, StringComparison.Ordinal);
            log.Record("launch-cleanup-failed", "bootstrap-dispose-IOException");
            var launchPath = Path.Combine(root, "launch-lifecycle.jsonl");
            Assert.DoesNotContain("accounts.login", File.ReadAllText(launchPath), StringComparison.Ordinal);
            foreach (var line in File.ReadLines(Path.Combine(root, "host-lifecycle.jsonl"))) Assert.NotNull(JsonNode.Parse(line));
            File.WriteAllText(Path.Combine(root, "host-lifecycle.jsonl"), new string(' ', HostLifecycleLog.MaximumBytes));
            log.Write("rotation-probe", "completed");
            Assert.Contains("bootstrap-dispose-IOException", File.ReadAllText(launchPath), StringComparison.Ordinal);
            foreach (var path in Directory.EnumerateFiles(root)) Assert.InRange(new FileInfo(path).Length, 1, HostLifecycleLog.MaximumBytes);
            Assert.True(File.Exists(Path.Combine(root, "host-lifecycle.jsonl.1")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CopyableLaunchRecordsAreBoundedAndExcludeUnrecognizedFields()
    {
        var root = Directory.CreateTempSubdirectory("ft-launch-records-").FullName;
        try
        {
            var log = new HostLifecycleLog(root);
            for (var i = 0; i < 600; i++) log.Record("launch-cleanup-failed", "bootstrap-dispose-IOException");
            var path = Path.Combine(root, "launch-lifecycle.jsonl");
            File.AppendAllText(path, "{\"name\":\"launch-cleanup-failed\",\"detail\":\"invalid secret\"}\n");
            File.AppendAllText(path, "{\"utc\":\"2026-10-31T00:00:00Z\",\"pid\":1,\"name\":\"launch-cleanup-failed\",\"detail\":null,\"password\":\"private-secret\"}\n");
            File.AppendAllText(path, "incomplete-json");
            var text = log.ReadLaunchRecords();
            Assert.Equal(512, text.Split('\n').Length);
            Assert.Contains("bootstrap-dispose-IOException", text, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("password", text, StringComparison.Ordinal);
            foreach (var line in text.Split('\n')) Assert.NotNull(JsonNode.Parse(line));
        }
        finally { Directory.Delete(root, true); }
    }
}
