using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using FantasyTale.Launcher.Communication;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class CompanionLifecycleTests
{
    private static readonly IReadOnlyDictionary<string, int> Grants = new Dictionary<string, int> { ["game.feature.changed"] = 1 };

    [Fact]
    public async Task BindingStateIsDeliveredBeforeEventsWithTheSameCapturedEpoch()
    {
        await using var pair = await CompanionTests.SocketPair.CreateAsync();
        var negotiated = new Dictionary<string, int>(Grants) { ["game.other.changed"] = 1 };
        await using var connection = await CompanionTests.ConnectAsync(pair, "pending_game", types: negotiated, epoch: "binding-epoch");
        using var channel = connection.CreateChannel("fixture.controller", Grants);
        var order = new ConcurrentQueue<string>();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var state = channel.SubscribeState(snapshot =>
        {
            Assert.Equal("binding-epoch", snapshot.Epoch);
            order.Enqueue(snapshot.State.ToString());
            if (snapshot.State == CompanionChannelState.Connected) connected.TrySetResult();
            if (snapshot.State == CompanionChannelState.Closed) closed.TrySetResult();
        });
        Assert.Equal(CompanionChannelState.PendingGame, channel.ConnectionSnapshot.State);
        Assert.Equal(["game.feature.changed"], channel.ConnectionSnapshot.Capabilities);
        var delivered = new TaskCompletionSource<CompanionEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var events = channel.Subscribe("game.feature.changed", 1, message =>
        {
            order.Enqueue("event");
            delivered.TrySetResult(message);
            return ValueTask.CompletedTask;
        });
        await SendAsync(pair.Server, new("state", "bound", "companion.state", 1,
            JsonSerializer.SerializeToElement(new { state = "connected" }), Epoch: "binding-epoch"));
        await SendAsync(pair.Server, Event("first-event", "binding-epoch"));
        var message = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("binding-epoch", message.ConnectionEpoch);
        Assert.Equal(2, message.ReceiveSequence);
        Assert.Equal(["PendingGame", "Connected", "event"], order.ToArray());
        pair.Server.Abort();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CompanionChannelState.Closed, channel.ConnectionSnapshot.State);
        Assert.Empty(channel.ConnectionSnapshot.Capabilities);
        Assert.Equal("binding-epoch", message.ConnectionEpoch);
    }

    [Fact]
    public async Task StableOwnerPushesDisconnectAndNewEpochWithoutPolling()
    {
        await using var first = await CompanionTests.SocketPair.CreateAsync();
        await using var second = await CompanionTests.SocketPair.CreateAsync();
        var attempts = 0;
        var permitSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = new CompanionSession(new("testgame", "kr-812", "launch-1", "zh-CN", Grants),
            _ => ValueTask.FromResult<CompanionAuthorization?>(new(new Uri("wss://example.test/channel"), "fixture-grant")),
            null, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(30), async (_, _, _, token) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                if (attempt == 1) return await CompanionTests.ConnectAsync(first, types: Grants, epoch: "connection-one");
                await permitSecond.Task.WaitAsync(token);
                return await CompanionTests.ConnectAsync(second, types: Grants, epoch: "connection-two");
            });
        using var channel = session.CreateChannel("fixture.controller", Grants);
        var snapshots = new ConcurrentQueue<CompanionConnectionSnapshot>();
        var firstConnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondConnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var state = channel.SubscribeState(snapshot =>
        {
            snapshots.Enqueue(snapshot);
            if (snapshot.Epoch == "connection-one" && snapshot.State == CompanionChannelState.Connected) firstConnected.TrySetResult();
            if (snapshot.Epoch == "connection-one" && snapshot.State == CompanionChannelState.Closed) disconnected.TrySetResult();
            if (snapshot.Epoch == "connection-two" && snapshot.State == CompanionChannelState.Connected) secondConnected.TrySetResult();
        });
        Assert.Equal(CompanionChannelState.Unavailable, Assert.Single(snapshots).State);
        var firstEvent = new TaskCompletionSource<CompanionEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEvent = new TaskCompletionSource<CompanionEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var events = channel.Subscribe("game.feature.changed", 1, message =>
        {
            (message.ConnectionEpoch == "connection-one" ? firstEvent : secondEvent).TrySetResult(message);
            return ValueTask.CompletedTask;
        });
        _ = session.Start();
        await firstConnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await SendAsync(first.Server, Event("event-one", "connection-one"));
        var before = await firstEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        first.Server.Abort();
        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(CompanionChannelState.Connected, channel.ConnectionSnapshot.State);
        permitSecond.SetResult();
        await secondConnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await SendAsync(second.Server, Event("event-two", "connection-two"));
        var after = await secondEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("connection-one", before.ConnectionEpoch);
        Assert.Equal("connection-two", after.ConnectionEpoch);
        Assert.Equal(1, before.ReceiveSequence);
        Assert.Equal(1, after.ReceiveSequence);
        Assert.Equal("connection-two", channel.ConnectionSnapshot.Epoch);
        var revisions = snapshots.Select(snapshot => snapshot.Revision).ToArray();
        Assert.True(revisions.Zip(revisions.Skip(1)).All(pair => pair.First < pair.Second));
    }

    [Fact]
    public async Task StateReplayAndRacingTransitionsStayOrderedAndCallbacksHoldNoObserverLock()
    {
        var observers = new CompanionStateObservers(new(null, CompanionChannelState.Unavailable, new HashSet<string>()), 2);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var received = new ConcurrentQueue<CompanionConnectionSnapshot>();
        var callbackReadSucceeded = false;
        var subscribing = Task.Run(() => observers.Subscribe(snapshot =>
        {
            if (snapshot.Revision == 0)
            {
                callbackReadSucceeded = Task.Run(() => observers.Current).Wait(TimeSpan.FromSeconds(2));
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }
            received.Enqueue(snapshot);
        }));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            observers.Update("one", CompanionChannelState.Connected, new HashSet<string> { "game.feature.changed" }, 1);
            observers.Update("one", CompanionChannelState.Closed, new HashSet<string>(), 2);
            observers.Update("one", CompanionChannelState.Connected, new HashSet<string>(), 1);
        }
        finally { release.Set(); }
        using var subscription = await subscribing;
        Assert.True(callbackReadSucceeded);
        Assert.Equal([CompanionChannelState.Unavailable, CompanionChannelState.Closed], received.Select(snapshot => snapshot.State));
        Assert.Equal([0L, 2L], received.Select(snapshot => snapshot.Revision));
        subscription.Dispose();
        observers.Update("two", CompanionChannelState.Connected, new HashSet<string>(), 3);
        Assert.Equal(2, received.Count);
    }

    [Fact]
    public void StateSubscriptionBudgetRecoversAfterDisposal()
    {
        var observers = new CompanionStateObservers(new(null, CompanionChannelState.Unavailable, new HashSet<string>()), 1);
        using var first = observers.Subscribe(_ => { });
        Assert.Throws<InvalidOperationException>(() => observers.Subscribe(_ => { }));
        first.Dispose();
        var seen = 0;
        using var replacement = observers.Subscribe(_ => seen++);
        observers.Update("one", CompanionChannelState.Connected, new HashSet<string>(), 1);
        Assert.Equal(2, seen);
    }

    private static CompanionEnvelope Event(string id, string epoch) => new("event", id, "game.feature.changed", 1,
        JsonSerializer.SerializeToElement(new { value = id }), Epoch: epoch);

    private static async Task SendAsync(WebSocket socket, CompanionEnvelope envelope)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.SendAsync(CompanionWire.Encode(envelope, new()), WebSocketMessageType.Text, true, timeout.Token);
    }
}
