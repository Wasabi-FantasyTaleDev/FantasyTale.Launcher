using System.Threading.Channels;
using FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class CompanionSceneGateTests
{
    private static object Scene(string state, int? mode = null) =>
        new { state, in_match = state == "match", can_leave_match = false, is_tutorial = false, mode_id = mode };
    private static readonly Func<CompanionScene, bool> InRoom = scene => scene.State.StartsWith("room_", StringComparison.Ordinal);

    [Fact]
    public async Task WithoutAChannelTheGateStaysClosedAndSubscribesNothing()
    {
        await using var gate = new CompanionSceneGate(null, InRoom, _ => { });
        Assert.Equal((false, "companion-unavailable"), (gate.Current.Active, gate.Current.Reason));
    }

    [Fact]
    public async Task TheSnapshotOpensTheGateOnlyForWatchedScenes()
    {
        var channel = new SceneChannel();
        await using var gate = new CompanionSceneGate(channel, InRoom, _ => { });
        Assert.Equal("waiting-scene", gate.Current.Reason);
        var request = await channel.NextRequest();
        Assert.Equal(CompanionScene.Request, request.Type);
        request.Reply(channel, Scene("room_waiting", 3));
        await Until(() => gate.Current.Active);
        Assert.Equal(3, gate.Current.Scene!.Mode);

        await channel.Emit(Scene("plaza"));
        Assert.Equal((false, "scene-inactive"), (gate.Current.Active, gate.Current.Reason));
        await channel.Emit(Scene("room_countdown", 3));
        Assert.True(gate.Current.Active);
    }

    [Fact]
    public async Task AnEventReceivedAfterTheSnapshotWasSentWinsOverTheLateReply()
    {
        var channel = new SceneChannel();
        await using var gate = new CompanionSceneGate(channel, InRoom, _ => { });
        var request = await channel.NextRequest();
        var reply = request.Receive(channel, Scene("room_waiting", 1));
        await channel.Emit(Scene("plaza"));
        request.Complete(reply);
        await Task.Delay(50);
        Assert.False(gate.Current.Active);
        Assert.Equal("plaza", gate.Current.Scene!.State);
    }

    [Fact]
    public async Task DisconnectClosesAtOnceAndOldConnectionEventsAreIgnored()
    {
        var channel = new SceneChannel();
        await using var gate = new CompanionSceneGate(channel, InRoom, _ => { });
        (await channel.NextRequest()).Reply(channel, Scene("room_waiting"));
        await Until(() => gate.Current.Active);

        channel.Transition(CompanionChannelState.Reconnecting, null);
        Assert.Equal((false, "waiting-connection"), (gate.Current.Active, gate.Current.Reason));
        Assert.Null(gate.Current.Scene);

        channel.Transition(CompanionChannelState.Connected, "epoch-b");
        var fresh = await channel.NextRequest();
        await channel.Emit(Scene("room_waiting"), epoch: "epoch-a");
        Assert.False(gate.Current.Active);
        fresh.Reply(channel, Scene("room_waiting"));
        await Until(() => gate.Current.Active);
    }

    [Fact]
    public async Task MissingCapabilitiesAndMalformedScenesKeepTheGateClosed()
    {
        var limited = new SceneChannel([CompanionScene.Event]);
        await using (var gate = new CompanionSceneGate(limited, InRoom, _ => { }))
            Assert.Equal("capabilities-unavailable", gate.Current.Reason);
        Assert.Empty(limited.Requests);

        var channel = new SceneChannel();
        await using var open = new CompanionSceneGate(channel, InRoom, _ => { });
        (await channel.NextRequest()).Reply(channel, Scene("room_waiting"));
        await Until(() => open.Current.Active);
        await channel.Emit(new { state = "plaza", in_match = false, can_leave_match = false, is_tutorial = false, mode_id = 3 });
        Assert.Equal((false, "invalid-scene"), (open.Current.Active, open.Current.Reason));
    }

    [Fact]
    public async Task AFailedSnapshotIsNotRetriedAndTheNextEventSuppliesTheScene()
    {
        var channel = new SceneChannel();
        await using var gate = new CompanionSceneGate(channel, InRoom, _ => { });
        (await channel.NextRequest()).Complete(new CompanionOperationResult(CompanionOperationStatus.Unavailable));
        await Until(() => gate.Current.Reason == "scene-snapshot-unavailable");
        await channel.Emit(Scene("room_waiting"));
        Assert.True(gate.Current.Active);
        Assert.Single(channel.Requests);
    }

    [Fact]
    public async Task DisposalClosesUnsubscribesAndCancelsThePendingSnapshot()
    {
        var channel = new SceneChannel();
        var gate = new CompanionSceneGate(channel, InRoom, _ => { });
        var request = await channel.NextRequest();
        await gate.DisposeAsync();
        Assert.Equal((false, "closed"), (gate.Current.Active, gate.Current.Reason));
        Assert.True(request.Token.IsCancellationRequested);
        Assert.Equal(0, channel.SubscriptionCount);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition()) { Assert.True(DateTime.UtcNow < deadline, "The gate did not reach the expected state."); await Task.Delay(5); }
    }

    private sealed class SceneChannel : ICompanionChannel
    {
        private readonly object _gate = new();
        private readonly List<Func<CompanionEvent, ValueTask>> _feeds = [];
        private readonly List<Action<CompanionConnectionSnapshot>> _observers = [];
        private readonly Channel<Pending> _pending = Channel.CreateUnbounded<Pending>();
        private readonly Dictionary<string, long> _sequences = new(StringComparer.Ordinal);
        private CompanionConnectionSnapshot _snapshot;

        internal SceneChannel(IEnumerable<string>? capabilities = null) =>
            _snapshot = new("epoch-a", CompanionChannelState.Connected,
                (capabilities ?? [CompanionScene.Event, CompanionScene.Request]).ToHashSet(StringComparer.Ordinal));

        internal List<Pending> Requests { get; } = [];
        internal int SubscriptionCount { get { lock (_gate) return _feeds.Count + _observers.Count; } }
        public CompanionChannelState State => _snapshot.State;
        public IReadOnlySet<string> Capabilities => _snapshot.Capabilities;
        public CompanionConnectionSnapshot ConnectionSnapshot { get { lock (_gate) return _snapshot; } }

        internal long Next(string? epoch)
        {
            lock (_gate) { var key = epoch ?? ""; var value = _sequences.GetValueOrDefault(key) + 1; _sequences[key] = value; return value; }
        }

        internal void Transition(CompanionChannelState state, string? epoch)
        {
            Action<CompanionConnectionSnapshot>[] observers;
            CompanionConnectionSnapshot snapshot;
            lock (_gate) { snapshot = _snapshot = _snapshot with { State = state, Epoch = epoch, Revision = _snapshot.Revision + 1 }; observers = [.. _observers]; }
            foreach (var observer in observers) observer(snapshot);
        }

        internal async ValueTask Emit(object payload, string? epoch = null)
        {
            epoch ??= ConnectionSnapshot.Epoch;
            Func<CompanionEvent, ValueTask>[] feeds;
            lock (_gate) feeds = [.. _feeds];
            var message = new CompanionEvent(CompanionScene.Event, 1, "event", payload) { ConnectionEpoch = epoch, ReceiveSequence = Next(epoch) };
            foreach (var feed in feeds) await feed(message);
        }

        internal async Task<Pending> NextRequest() => await _pending.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        public IDisposable SubscribeState(Action<CompanionConnectionSnapshot> handler)
        {
            lock (_gate) _observers.Add(handler);
            handler(ConnectionSnapshot);
            return new Subscription(() => { lock (_gate) _observers.Remove(handler); });
        }

        public IDisposable Subscribe(string type, int version, Func<CompanionEvent, ValueTask> handler)
        {
            Assert.Equal((CompanionScene.Event, 1), (type, version));
            lock (_gate) _feeds.Add(handler);
            return new Subscription(() => { lock (_gate) _feeds.Remove(handler); });
        }

        public ValueTask<CompanionOperationResult> SendAsync(string type, int version, object? payload = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The scene gate must not send events.");

        public async ValueTask<CompanionOperationResult> RequestAsync(string type, int version, object? payload = null,
            CompanionRequestOptions? options = null, CancellationToken cancellationToken = default)
        {
            var request = new Pending(type, ConnectionSnapshot.Epoch, cancellationToken);
            lock (_gate) Requests.Add(request);
            _pending.Writer.TryWrite(request);
            return await request.Result.WaitAsync(cancellationToken);
        }

        public void Dispose() { }
    }

    private sealed class Pending(string type, string? epoch, CancellationToken token)
    {
        private readonly TaskCompletionSource<CompanionOperationResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Type { get; } = type;
        internal CancellationToken Token { get; } = token;
        internal Task<CompanionOperationResult> Result => _result.Task;
        internal CompanionOperationResult Receive(SceneChannel channel, object payload) =>
            new(CompanionOperationStatus.Completed, Response: payload) { ConnectionEpoch = epoch, ReceiveSequence = channel.Next(epoch) };
        internal void Reply(SceneChannel channel, object payload) => Complete(Receive(channel, payload));
        internal void Complete(CompanionOperationResult result) => _result.TrySetResult(result);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
