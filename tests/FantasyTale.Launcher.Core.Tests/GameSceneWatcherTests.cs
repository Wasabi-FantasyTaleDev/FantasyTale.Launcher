using System.Threading.Channels;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class GameSceneWatcherTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(5);
    private static object Scene(string state, int? mode = null) =>
        new { state, in_match = state == "match", can_leave_match = false, is_tutorial = false, mode_id = mode };

    [Fact]
    public async Task WithNeitherReadingTheSceneStaysUnknown()
    {
        await using var watcher = new GameSceneWatcher(null, null, _ => { });
        Assert.Equal(GameSceneSource.None, watcher.Current.Source);
        Assert.False(watcher.NativeSupported);
    }

    [Fact]
    public async Task TheCompanionSnapshotAndLaterEventsSupplyTheScene()
    {
        var channel = new SceneChannel();
        await using var watcher = new GameSceneWatcher(null, channel, _ => { });
        var request = await channel.NextRequest();
        Assert.Equal(GameCompanionScene.Request, request.Type);
        request.Reply(channel, Scene("room_waiting", 3));
        await Until(() => watcher.Current.Companion is { Mode: 3 });
        Assert.Equal(GameSceneSource.Companion, watcher.Current.Source);

        await channel.Emit(Scene("plaza"));
        Assert.Equal("plaza", watcher.Current.Companion!.State);
    }

    [Fact]
    public async Task TheNativeStageWinsAndTheCompanionSceneStaysAlongside()
    {
        var channel = new SceneChannel();
        var native = new StageReader { Stage = new(2, "mainMenu", false) };
        await using var watcher = new GameSceneWatcher(native, channel, _ => { }, Fast);
        (await channel.NextRequest()).Reply(channel, Scene("main_menu"));
        await Until(() => watcher.Current.Stage is not null && watcher.Current.Companion is not null);
        Assert.Equal(GameSceneSource.Native, watcher.Current.Source);
        Assert.Equal("mainMenu", watcher.Current.Stage!.Name);
        Assert.Equal("main_menu", watcher.Current.Companion!.State);

        native.Stage = new(4, "room", false);
        await Until(() => watcher.Current.Stage?.Name == "room");
        Assert.True(watcher.NativeSupported);
    }

    [Fact]
    public async Task AFailingNativeReadFallsBackToTheCompanionUntilItRecovers()
    {
        var channel = new SceneChannel();
        var native = new StageReader { Stage = new(3, "lobby", false) };
        await using var watcher = new GameSceneWatcher(native, channel, _ => { }, Fast);
        (await channel.NextRequest()).Reply(channel, Scene("plaza"));
        await Until(() => watcher.Current.Source == GameSceneSource.Native && watcher.Current.Companion is not null);

        native.Error = new IOException("pipe closed");
        await Until(() => watcher.Current.Source == GameSceneSource.Companion);
        Assert.Null(watcher.Current.Stage);
        Assert.True(watcher.NativeSupported);

        native.Error = null;
        await Until(() => watcher.Current.Source == GameSceneSource.Native);
    }

    [Fact]
    public async Task ABuildWithoutAStageMapIsNotReadAgainAndBusyKeepsTheStage()
    {
        var busy = new StageReader { Stage = new(2, "mainMenu", false) };
        await using (var watcher = new GameSceneWatcher(busy, null, _ => { }, Fast))
        {
            await Until(() => watcher.Current.Source == GameSceneSource.Native);
            busy.Error = new GameNativeUiBusyException();
            var reads = busy.Reads;
            await Until(() => busy.Reads > reads + 3);
            Assert.Equal("mainMenu", watcher.Current.Stage!.Name);
        }

        var unsupported = new StageReader { Error = new NotSupportedException() };
        var channel = new SceneChannel();
        await using var fallback = new GameSceneWatcher(unsupported, channel, _ => { }, Fast);
        (await channel.NextRequest()).Reply(channel, Scene("main_menu"));
        await Until(() => fallback.Current.Source == GameSceneSource.Companion && !fallback.NativeSupported);
        var count = unsupported.Reads;
        await Task.Delay(50);
        Assert.Equal(count, unsupported.Reads);
    }

    [Fact]
    public async Task AnEventReceivedAfterTheSnapshotWasSentWinsOverTheLateReply()
    {
        var channel = new SceneChannel();
        await using var watcher = new GameSceneWatcher(null, channel, _ => { });
        var request = await channel.NextRequest();
        var reply = request.Receive(channel, Scene("room_waiting", 1));
        await channel.Emit(Scene("plaza"));
        request.Complete(reply);
        await Task.Delay(50);
        Assert.Equal("plaza", watcher.Current.Companion!.State);
    }

    [Fact]
    public async Task DisconnectForgetsTheSceneAndOldConnectionEventsAreIgnored()
    {
        var channel = new SceneChannel();
        await using var watcher = new GameSceneWatcher(null, channel, _ => { });
        (await channel.NextRequest()).Reply(channel, Scene("room_waiting"));
        await Until(() => watcher.Current.Companion is not null);

        channel.Transition(CompanionChannelState.Reconnecting, null);
        Assert.Equal(GameSceneSource.None, watcher.Current.Source);
        Assert.Null(watcher.Current.Companion);

        channel.Transition(CompanionChannelState.Connected, "epoch-b");
        var fresh = await channel.NextRequest();
        await channel.Emit(Scene("room_waiting"), epoch: "epoch-a");
        Assert.Null(watcher.Current.Companion);
        fresh.Reply(channel, Scene("room_waiting"));
        await Until(() => watcher.Current.Companion is not null);
    }

    [Fact]
    public async Task MissingCapabilitiesAndMalformedScenesLeaveTheCompanionSceneUnknown()
    {
        var limited = new SceneChannel([GameCompanionScene.Event]);
        await using (var watcher = new GameSceneWatcher(null, limited, _ => { }))
            Assert.Null(watcher.Current.Companion);
        Assert.Empty(limited.Requests);

        var channel = new SceneChannel();
        await using var open = new GameSceneWatcher(null, channel, _ => { });
        (await channel.NextRequest()).Reply(channel, Scene("room_waiting"));
        await Until(() => open.Current.Companion is not null);
        await channel.Emit(new { state = "plaza", in_match = false, can_leave_match = false, is_tutorial = false, mode_id = 3 });
        Assert.Null(open.Current.Companion);
    }

    [Fact]
    public async Task AFailedSnapshotIsNotRetriedAndTheNextEventSuppliesTheScene()
    {
        var channel = new SceneChannel();
        await using var watcher = new GameSceneWatcher(null, channel, _ => { });
        (await channel.NextRequest()).Complete(new CompanionOperationResult(CompanionOperationStatus.Unavailable));
        await Task.Delay(20);
        await channel.Emit(Scene("room_waiting"));
        Assert.Equal("room_waiting", watcher.Current.Companion!.State);
        Assert.Single(channel.Requests);
    }

    [Fact]
    public async Task ChangesArePublishedOnceEachAndDisposalStopsEverything()
    {
        var channel = new SceneChannel();
        var native = new StageReader { Stage = new(2, "mainMenu", false) };
        var published = new List<GameScene>();
        var watcher = new GameSceneWatcher(native, channel, scene => { lock (published) published.Add(scene); }, Fast);
        var request = await channel.NextRequest();
        await Until(() => watcher.Current.Stage is not null);
        var reads = native.Reads;
        await Until(() => native.Reads > reads + 3);
        lock (published) Assert.Single(published, scene => scene.Stage is not null);
        await watcher.DisposeAsync();
        Assert.Equal(GameSceneSource.None, watcher.Current.Source);
        Assert.True(request.Token.IsCancellationRequested);
        Assert.Equal(0, channel.SubscriptionCount);
        lock (published) Assert.Equal(Enumerable.Range(1, published.Count).Select(i => (long)i), published.Select(scene => scene.Generation));
        var stopped = native.Reads;
        await Task.Delay(50);
        Assert.Equal(stopped, native.Reads);
    }

    [Theory]
    [InlineData("{\"state\":\"room_waiting\",\"in_match\":false,\"can_leave_match\":false,\"is_tutorial\":false,\"mode_id\":7}", true)]
    [InlineData("{\"state\":\"main_menu\",\"in_match\":false,\"can_leave_match\":false,\"is_tutorial\":false}", true)]
    [InlineData("{\"state\":\" \",\"in_match\":false,\"can_leave_match\":false,\"is_tutorial\":false}", false)]
    [InlineData("{\"state\":\"match\",\"in_match\":1,\"can_leave_match\":false,\"is_tutorial\":false}", false)]
    [InlineData("{\"state\":\"room\",\"state\":\"plaza\",\"in_match\":false,\"can_leave_match\":false,\"is_tutorial\":false}", false)]
    [InlineData("{\"state\":\"plaza\",\"in_match\":false,\"can_leave_match\":false,\"is_tutorial\":true}", false)]
    public void CompanionScenesAreReadStrictly(string json, bool accepted) =>
        Assert.Equal(accepted, GameCompanionScene.TryParse(System.Text.Json.JsonDocument.Parse(json).RootElement, out _));

    private sealed class StageReader : IGameNativeUi
    {
        private int _reads;
        private volatile GameNativeUiStage? _stage;
        private volatile Exception? _error;
        internal GameNativeUiStage? Stage { get => _stage; set => _stage = value; }
        internal Exception? Error { get => _error; set => _error = value; }
        internal int Reads => Volatile.Read(ref _reads);
        public ValueTask<GameNativeUiStage> GetStageAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reads);
            if (_error is { } error) return ValueTask.FromException<GameNativeUiStage>(error);
            return ValueTask.FromResult(_stage ?? throw new InvalidOperationException("No stage set."));
        }
        public ValueTask<GameNativeUiCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<GameNativeUiResult> InvokeAsync(GameNativeUiRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<GameNativeUiResult> GetResultAsync(string operationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition()) { Assert.True(DateTime.UtcNow < deadline, "The watcher did not reach the expected state."); await Task.Delay(5); }
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
                (capabilities ?? [GameCompanionScene.Event, GameCompanionScene.Request]).ToHashSet(StringComparer.Ordinal));

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
            var message = new CompanionEvent(GameCompanionScene.Event, 1, "event", payload) { ConnectionEpoch = epoch, ReceiveSequence = Next(epoch) };
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
            Assert.Equal((GameCompanionScene.Event, 1), (type, version));
            lock (_gate) _feeds.Add(handler);
            return new Subscription(() => { lock (_gate) _feeds.Remove(handler); });
        }

        public ValueTask<CompanionOperationResult> SendAsync(string type, int version, object? payload = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The scene watcher must not send events.");

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
