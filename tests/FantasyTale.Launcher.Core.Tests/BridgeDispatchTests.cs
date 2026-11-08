using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Host.Bridge;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class BridgeDispatchTests
{
    [Fact]
    public async Task SynchronousBackendAndDiagnosticIoCannotOccupyTheWindowCallback()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackThread = 0; var handlerThread = 0; var traceThread = 0;
        JsonNode? reply = null;
        var bridge = new BridgeRouter(raw => reply = JsonNode.Parse(raw), (_, _) => traceThread = Environment.CurrentManagedThreadId);
        bridge.Add("instances.list", _ =>
        {
            handlerThread = Environment.CurrentManagedThreadId;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Fixture was not released.");
            return 42;
        });
        Task? dispatched = null;
        var callback = Task.Factory.StartNew(() =>
        {
            callbackThread = Environment.CurrentManagedThreadId;
            dispatched = bridge.DispatchAsync("{\"id\":1,\"method\":\"instances.list\"}", default);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await callback.WaitAsync(TimeSpan.FromSeconds(5));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(dispatched); Assert.False(dispatched.IsCompleted);
            Assert.NotEqual(callbackThread, handlerThread); Assert.NotEqual(callbackThread, traceThread);
        }
        finally { release.Set(); }
        await dispatched!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, reply!["result"]!.GetValue<int>());
    }

    [Fact]
    public async Task WindowOperationsRemainAvailableWhenBackendWorkersAndQueueAreFull()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var occupied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replies = new ConcurrentDictionary<long, JsonNode>();
        var active = 0;
        var bridge = new BridgeRouter(raw => { var node = JsonNode.Parse(raw)!; replies[node["id"]!.GetValue<long>()] = node; });
        bridge.Add("store.sync", async (_, _) =>
        {
            var count = Interlocked.Increment(ref active);
            if (count == 8) occupied.TrySetResult();
            try { await release.Task; return true; }
            finally { Interlocked.Decrement(ref active); }
        });
        bridge.AddWindow("window.minimize", _ => true);
        var requests = Enumerable.Range(1, 64).Select(id => bridge.DispatchAsync($"{{\"id\":{id},\"method\":\"store.sync\"}}", default)).ToArray();
        try
        {
            await occupied.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await bridge.DispatchAsync("{\"id\":65,\"method\":\"store.sync\"}", default);
            Assert.Equal("bridge_busy", replies[65]["code"]!.GetValue<string>());
            await bridge.DispatchAsync("{\"id\":66,\"method\":\"window.minimize\"}", default).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(replies[66]["result"]!.GetValue<bool>());
            Assert.Equal(8, Volatile.Read(ref active));
        }
        finally { release.TrySetResult(); await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(10)); }
        await bridge.DispatchAsync("{\"id\":67,\"method\":\"store.sync\"}", default);
        Assert.True(replies[67]["result"]!.GetValue<bool>());
    }

    [Fact]
    public async Task CanceledQueuedRequestNeverEntersItsBackendHandler()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var occupied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var entered = false;
        JsonNode? reply = null;
        var bridge = new BridgeRouter(raw => { var node = JsonNode.Parse(raw)!; if (node["id"]!.GetValue<long>() == 9) reply = node; });
        bridge.Add("slow", async (_, _) => { if (Interlocked.Increment(ref active) == 8) occupied.TrySetResult(); await release.Task; return null; });
        bridge.Add("canceled", _ => { entered = true; return null; });
        var requests = Enumerable.Range(1, 8).Select(id => bridge.DispatchAsync($"{{\"id\":{id},\"method\":\"slow\"}}", default)).ToArray();
        using var cancellation = new CancellationTokenSource();
        Task? canceled = null;
        try
        {
            await occupied.Task.WaitAsync(TimeSpan.FromSeconds(5));
            canceled = bridge.DispatchAsync("{\"id\":9,\"method\":\"canceled\"}", cancellation.Token);
            cancellation.Cancel();
        }
        finally { release.TrySetResult(); await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(10)); }
        await canceled!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(entered); Assert.NotNull(reply!["error"]);
    }

    [Theory]
    [InlineData("{\"id\":1,\"method\":\"missing\"}")]
    [InlineData("{\"id\":1}")]
    public async Task DispatchRetainsErrorRepliesForInvalidRequests(string message)
    {
        JsonNode? reply = null;
        var bridge = new BridgeRouter(raw => reply = JsonNode.Parse(raw));
        await bridge.DispatchAsync(message, default);
        Assert.Equal(1, reply!["id"]!.GetValue<int>()); Assert.NotNull(reply["error"]);
    }
}
