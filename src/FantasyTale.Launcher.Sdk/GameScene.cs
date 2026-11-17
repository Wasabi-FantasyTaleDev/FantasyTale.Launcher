using System.Text.Json;

namespace FantasyTale.Launcher.Sdk;

/// <summary>
/// The scene the server reports for the player over Nirinsou (<c>game.session.state</c> and <c>game.session.changed</c>):
/// <c>main_menu</c>, <c>plaza</c>, <c>room_*</c>, <c>match</c>, ... with the flags the server sends with it.
/// </summary>
/// <param name="State">The server's scene name; it follows the server's session phase, not the client's stage.</param>
/// <param name="InMatch">Whether the server counts the player as playing a round.</param>
/// <param name="CanLeaveMatch">Whether the server would let the player leave the round now.</param>
/// <param name="Tutorial">Whether the scene is a tutorial.</param>
/// <param name="Mode">The room's mode id, or null outside rooms.</param>
public sealed record GameCompanionScene(string State, bool InMatch, bool CanLeaveMatch, bool Tutorial, int? Mode)
{
    /// <summary>The scene event the server pushes on every scene change.</summary>
    public const string Event = "game.session.changed";

    /// <summary>The request that returns the current scene.</summary>
    public const string Request = "game.session.state";

    /// <summary>
    /// Reads a scene payload strictly: a nonblank state, the three flags as booleans, an optional integer mode, no
    /// duplicate keys, and a plaza that claims no match, tutorial or mode. Anything else is rejected.
    /// </summary>
    public static bool TryParse(object? payload, out GameCompanionScene scene)
    {
        scene = null!;
        if (!TryObject(payload, out var value) || !value.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(state.GetString()) || !TryBoolean(value, "in_match", out var inMatch)
            || !TryBoolean(value, "can_leave_match", out var canLeave) || !TryBoolean(value, "is_tutorial", out var tutorial)) return false;
        // The server omits the nullable mode outside rooms.
        int? modeId = null;
        if (value.TryGetProperty("mode_id", out var mode) && mode.ValueKind != JsonValueKind.Null)
        {
            if (mode.ValueKind != JsonValueKind.Number || !mode.TryGetInt32(out var number)) return false;
            modeId = number;
        }
        var name = state.GetString()!;
        if (name == "plaza" && (inMatch || canLeave || tutorial || modeId is not null)) return false;
        scene = new(name, inMatch, canLeave, tutorial, modeId);
        return true;
    }

    /// <summary>Reads a payload as one JSON object without duplicate keys.</summary>
    public static bool TryObject(object? payload, out JsonElement value)
    {
        try
        {
            value = payload is JsonElement json ? json : JsonSerializer.SerializeToElement(payload);
            return value.ValueKind == JsonValueKind.Object
                && value.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() == value.EnumerateObject().Count();
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or InvalidOperationException)
        { value = default; return false; }
    }

    /// <summary>Reads one required JSON boolean property.</summary>
    public static bool TryBoolean(JsonElement value, string name, out bool result)
    {
        result = false;
        if (!value.TryGetProperty(name, out var property) || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        result = property.GetBoolean();
        return true;
    }
}

/// <summary>Which reading a <see cref="GameScene"/> rests on.</summary>
public enum GameSceneSource
{
    /// <summary>Neither the client nor the server has reported a scene.</summary>
    None,
    /// <summary>The client's own stage manager, read through the native provider.</summary>
    Native,
    /// <summary>The server's scene feed over Nirinsou, used where the native read is unavailable.</summary>
    Companion,
}

/// <summary>
/// The player's scene from the best available reading, plus every reading known now. The two readings name scenes in
/// their own words and are not mapped onto each other: a server <c>room_*</c> scene is not always a client <c>room</c>
/// stage (the server keeps a room phase for one-seat scenes such as the clan stage), so compare each reading in its own
/// vocabulary.
/// </summary>
/// <param name="Source">Native when the client's stage is known, otherwise Companion when the server's scene is known.</param>
/// <param name="Stage">The client's stage, or null when this build has no stage map or the read is failing.</param>
/// <param name="Companion">The server's scene and its flags (such as the room mode), or null when unknown.</param>
/// <param name="Generation">Increases with every published scene.</param>
public sealed record GameScene(GameSceneSource Source, GameNativeUiStage? Stage, GameCompanionScene? Companion, long Generation);

/// <summary>
/// Follows the player's scene from two readings: the client's stage manager through <see cref="IGameNativeUi.GetStageAsync"/>
/// where the build has a stage map (immediate, and known before login and inside developer tools), and the server's
/// Nirinsou scene feed as the fallback. The native read is polled; a build without a stage map is not read again, and a
/// failing read leaves the stage unknown until it succeeds. The companion feed starts unknown on each connection,
/// subscribes to scene events, then asks once for the current scene; whichever frame arrived later in the connection's
/// receive order wins, and a disconnect, an earlier connection's event or a malformed scene forgets it.
/// </summary>
public sealed class GameSceneWatcher : IAsyncDisposable
{
    /// <summary>The default pause between native stage reads.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(500);

    private readonly IGameNativeUi? _native;
    private readonly ICompanionChannel? _channel;
    private readonly Action<GameScene> _changed;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly CancellationTokenSource _stop = new();
    private GameScene _current = new(GameSceneSource.None, null, null, 0);
    private GameNativeUiStage? _stage;
    private GameCompanionScene? _scene;
    private CancellationTokenSource? _snapshot;
    private Task _pending = Task.CompletedTask;
    private readonly Task _polling;
    private string? _epoch;
    private long _connectionRevision = -1, _sequence;
    private bool _eligible, _disposed, _nativeSupported;
    private Task? _disposal;

    /// <summary>
    /// Starts following the scene. Either reading may be null. <paramref name="changed"/> runs on every published change
    /// and must return promptly.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The interval is not positive.</exception>
    public GameSceneWatcher(IGameNativeUi? native, ICompanionChannel? companion, Action<GameScene> changed, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(changed);
        _interval = interval ?? DefaultInterval;
        if (_interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), "The interval must be positive.");
        _native = native;
        _channel = companion;
        _changed = changed;
        _nativeSupported = native is not null;
        if (companion is not null)
        {
            try
            {
                // The event feed is subscribed before the lifecycle replay can request a snapshot.
                _subscriptions.Add(companion.Subscribe(GameCompanionScene.Event, 1, OnScene));
                _subscriptions.Add(companion.SubscribeState(OnConnection));
            }
            catch (Exception error) when (error is NotSupportedException or ArgumentException or InvalidOperationException)
            {
                foreach (var subscription in _subscriptions) subscription.Dispose();
                _subscriptions.Clear();
                lock (_gate) { _eligible = false; _scene = null; }
            }
        }
        _polling = native is null ? Task.CompletedTask : Task.Run(PollAsync);
    }

    /// <summary>The latest published scene.</summary>
    public GameScene Current => Volatile.Read(ref _current);

    /// <summary>False once the native provider said this build has no stage map, or when no provider was given.</summary>
    public bool NativeSupported { get { lock (_gate) return _nativeSupported; } }

    private async Task PollAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    var stage = await _native!.GetStageAsync(_stop.Token).ConfigureAwait(false);
                    lock (_gate) { if (!_disposed) { _stage = stage; PublishLocked(); } }
                }
                catch (GameNativeUiBusyException) { }
                catch (NotSupportedException)
                {
                    lock (_gate) { _nativeSupported = false; _stage = null; if (!_disposed) PublishLocked(); }
                    return;
                }
                catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException or TimeoutException)
                {
                    lock (_gate) { _stage = null; if (!_disposed) PublishLocked(); }
                }
                await Task.Delay(_interval, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) { }
    }

    private void OnConnection(CompanionConnectionSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed || snapshot.Revision <= _connectionRevision) return;
            _connectionRevision = snapshot.Revision;
            var ready = snapshot.State == CompanionChannelState.Connected && !string.IsNullOrEmpty(snapshot.Epoch)
                && snapshot.Capabilities.Contains(GameCompanionScene.Event) && snapshot.Capabilities.Contains(GameCompanionScene.Request);
            if (!ready)
            {
                _eligible = false;
                _epoch = snapshot.Epoch;
                ForgetLocked();
                PublishLocked();
                return;
            }
            if (_eligible && _epoch == snapshot.Epoch) return;
            _epoch = snapshot.Epoch;
            _eligible = true;
            ForgetLocked();
            var cancel = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            _snapshot = cancel;
            var epoch = _epoch!;
            _pending = Task.Run(() => SnapshotAsync(epoch, cancel));
            PublishLocked();
        }
    }

    private ValueTask OnScene(CompanionEvent message)
    {
        lock (_gate)
        {
            if (_disposed || !_eligible || message.Type != GameCompanionScene.Event || message.Version != 1 || message.ConnectionEpoch != _epoch)
                return ValueTask.CompletedTask;
            if (message.ReceiveSequence <= 0) { _scene = null; PublishLocked(); return ValueTask.CompletedTask; }
            // Event callbacks and the snapshot reply can run in a different order than their frames arrived.
            if (message.ReceiveSequence <= _sequence) return ValueTask.CompletedTask;
            _sequence = message.ReceiveSequence;
            ApplyLocked(message.Payload);
        }
        return ValueTask.CompletedTask;
    }

    private async Task SnapshotAsync(string epoch, CancellationTokenSource cancel)
    {
        CompanionOperationResult result;
        try { result = await _channel!.RequestAsync(GameCompanionScene.Request, 1, new { }, cancellationToken: cancel.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return; }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or JsonException or TimeoutException)
        { result = new(CompanionOperationStatus.Unavailable); }
        lock (_gate)
        {
            // A failed snapshot is not retried; the next scene event supplies the scene.
            if (_disposed || !_eligible || !ReferenceEquals(_snapshot, cancel) || _epoch != epoch) return;
            if (result.Status != CompanionOperationStatus.Completed || result.ConnectionEpoch != epoch || result.ReceiveSequence <= 0) return;
            if (result.ReceiveSequence <= _sequence) return;
            _sequence = result.ReceiveSequence;
            ApplyLocked(result.Response);
        }
    }

    private void ApplyLocked(object? payload)
    {
        _scene = GameCompanionScene.TryParse(payload, out var scene) ? scene : null;
        PublishLocked();
    }

    private void ForgetLocked()
    {
        _scene = null;
        _sequence = 0;
        var previous = _snapshot;
        _snapshot = null;
        previous?.Cancel();
    }

    private void PublishLocked()
    {
        var stage = _disposed ? null : _stage;
        var scene = _disposed || !_eligible ? null : _scene;
        var source = stage is not null ? GameSceneSource.Native : scene is not null ? GameSceneSource.Companion : GameSceneSource.None;
        var current = _current;
        if (current.Source == source && current.Stage == stage && current.Companion == scene) return;
        var next = new GameScene(source, stage, scene, checked(current.Generation + 1));
        Volatile.Write(ref _current, next);
        _changed(next);
    }

    /// <summary>Stops reading, unsubscribes and cancels a pending snapshot; the shared channel and provider stay open.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null) return new(_disposal);
            _disposed = true;
            _eligible = false;
            PublishLocked();
            _stop.Cancel();
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
            _disposal = FinishAsync(_pending, _polling);
            return new(_disposal);
        }
    }

    private async Task FinishAsync(Task pending, Task polling)
    {
        try { await Task.WhenAll(pending, polling).ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}
