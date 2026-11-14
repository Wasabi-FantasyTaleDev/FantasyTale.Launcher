using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;

/// <summary>Whether a <see cref="CompanionSceneGate"/> currently lets its owner work, and why.</summary>
/// <param name="Active">True only while the server's latest scene satisfies the owner's condition.</param>
/// <param name="Generation">Increases with every published state.</param>
/// <param name="Reason">A short, log-safe reason such as <c>scene-active</c> or <c>waiting-connection</c>.</param>
/// <param name="Scene">The latest accepted scene, or null when none is known for the current connection.</param>
internal sealed record CompanionSceneState(bool Active, long Generation, string Reason, CompanionScene? Scene);

/// <summary>
/// Lets a controller work only while the server says the player is in a scene it cares about (the plaza, a room, a
/// match), from the Nirinsou scene feed. Each connection starts unknown: the gate subscribes to scene events, then asks
/// once for the current scene; whichever arrived later in the connection's receive order wins. A disconnect, a missing
/// capability, an event from an earlier connection or a malformed scene closes the gate at once. A failed snapshot is
/// not retried; the next scene event supplies the state.
/// </summary>
internal sealed class CompanionSceneGate : IAsyncDisposable
{
    private readonly ICompanionChannel? _channel;
    private readonly Func<CompanionScene, bool> _watch;
    private readonly Action<CompanionSceneState> _changed;
    private readonly object _gate = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly CancellationTokenSource _stop = new();
    private CompanionSceneState _current = new(false, 0, "starting", null);
    private CancellationTokenSource? _snapshot;
    private Task _pending = Task.CompletedTask;
    private string? _epoch;
    private long _connectionRevision = -1, _sequence;
    private CompanionScene? _scene;
    private bool _eligible, _disposed;
    private Task? _disposal;

    /// <summary>Opens the gate whenever <paramref name="watch"/> accepts the latest scene; <paramref name="changed"/> must return promptly.</summary>
    internal CompanionSceneGate(ICompanionChannel? channel, Func<CompanionScene, bool> watch, Action<CompanionSceneState> changed)
    {
        ArgumentNullException.ThrowIfNull(watch);
        ArgumentNullException.ThrowIfNull(changed);
        _channel = channel;
        _watch = watch;
        _changed = changed;
        if (channel is null) { lock (_gate) PublishLocked("companion-unavailable"); return; }
        try
        {
            // The event feed is subscribed before the lifecycle replay can request a snapshot.
            _subscriptions.Add(channel.Subscribe(CompanionScene.Event, 1, OnScene));
            _subscriptions.Add(channel.SubscribeState(OnConnection));
        }
        catch (Exception error) when (error is NotSupportedException or ArgumentException or InvalidOperationException)
        {
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
            lock (_gate) { _eligible = false; PublishLocked("companion-lifecycle-unavailable"); }
        }
    }

    /// <summary>The latest published state.</summary>
    internal CompanionSceneState Current => Volatile.Read(ref _current);

    private void OnConnection(CompanionConnectionSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed || snapshot.Revision <= _connectionRevision) return;
            _connectionRevision = snapshot.Revision;
            var ready = snapshot.State == CompanionChannelState.Connected && !string.IsNullOrEmpty(snapshot.Epoch)
                && snapshot.Capabilities.Contains(CompanionScene.Event) && snapshot.Capabilities.Contains(CompanionScene.Request);
            if (!ready)
            {
                _eligible = false;
                _epoch = snapshot.Epoch;
                ForgetLocked();
                PublishLocked(snapshot.State == CompanionChannelState.Connected ? "capabilities-unavailable" : "waiting-connection");
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
            PublishLocked("waiting-scene");
        }
    }

    private ValueTask OnScene(CompanionEvent message)
    {
        lock (_gate)
        {
            if (_disposed || !_eligible || message.Type != CompanionScene.Event || message.Version != 1 || message.ConnectionEpoch != _epoch)
                return ValueTask.CompletedTask;
            if (message.ReceiveSequence <= 0) { _scene = null; PublishLocked("scene-sequence-unavailable"); return ValueTask.CompletedTask; }
            // Event callbacks and the snapshot reply can run in a different order than their frames arrived.
            if (message.ReceiveSequence <= _sequence) return ValueTask.CompletedTask;
            _sequence = message.ReceiveSequence;
            Apply(message.Payload);
        }
        return ValueTask.CompletedTask;
    }

    private async Task SnapshotAsync(string epoch, CancellationTokenSource cancel)
    {
        CompanionOperationResult result;
        try { result = await _channel!.RequestAsync(CompanionScene.Request, 1, new { }, cancellationToken: cancel.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return; }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or JsonException or TimeoutException)
        { result = new(CompanionOperationStatus.Unavailable); }
        lock (_gate)
        {
            if (_disposed || !_eligible || !ReferenceEquals(_snapshot, cancel) || _epoch != epoch) return;
            if (result.Status != CompanionOperationStatus.Completed || result.ConnectionEpoch != epoch || result.ReceiveSequence <= 0)
            {
                if (_scene is null) PublishLocked("scene-snapshot-unavailable");
                return;
            }
            if (result.ReceiveSequence <= _sequence) return;
            _sequence = result.ReceiveSequence;
            Apply(result.Response);
        }
    }

    private void Apply(object? payload)
    {
        if (!CompanionScene.TryParse(payload, out var scene)) { _scene = null; PublishLocked("invalid-scene"); return; }
        if (_scene == scene) return;
        _scene = scene;
        PublishLocked(_watch(scene) ? "scene-active" : "scene-inactive");
    }

    private void ForgetLocked()
    {
        _scene = null;
        _sequence = 0;
        var previous = _snapshot;
        _snapshot = null;
        previous?.Cancel();
    }

    private void PublishLocked(string reason)
    {
        var active = !_disposed && _eligible && _scene is not null && _watch(_scene);
        var state = new CompanionSceneState(active, checked(_current.Generation + 1), reason, _eligible ? _scene : null);
        Volatile.Write(ref _current, state);
        _changed(state);
    }

    /// <summary>Closes the gate, unsubscribes and cancels a pending snapshot; the shared channel stays open.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null) return new(_disposal);
            _disposed = true;
            _eligible = false;
            PublishLocked("closed");
            _stop.Cancel();
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
            _disposal = FinishAsync(_pending);
            return new(_disposal);
        }
    }

    private async Task FinishAsync(Task pending)
    {
        try { await pending.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}
