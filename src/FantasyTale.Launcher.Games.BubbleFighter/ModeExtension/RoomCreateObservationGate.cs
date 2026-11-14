using System.Text.Json;
using System.Threading.Channels;
using FantasyTale.Launcher.Sdk;
using Scene = FantasyTale.Launcher.Games.BubbleFighter.ModeExtension.CompanionScene;

namespace FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;

internal sealed class RoomCreateObservationGate : IAsyncDisposable
{
    internal const string ContextEvent = "game.room.create.context";
    internal const string ContextRequest = "game.room.create.context.get";
    internal const string SceneEvent = CompanionScene.Event;
    internal const string SceneRequest = CompanionScene.Request;
    private static readonly string[] Required = [ContextEvent, ContextRequest, SceneEvent, SceneRequest];
    private readonly ICompanionChannel? _channel;
    private readonly Action<RoomCreateObservationState> _changed;
    private readonly object _gate = new();
    private readonly List<IDisposable> _subscriptions = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly Task _work;
    private RoomCreateObservationState _current = new(false, 0, "starting");
    private Synchronization? _synchronization;
    private string? _epoch, _contextId;
    private long _connectionRevision = -1, _contextRevision, _contextSequence, _sceneSequence;
    private bool _contextActive, _contextKnown, _sceneKnown, _sceneNeedsSnapshot, _eligible, _disposed;
    private Scene? _scene;
    private Task? _disposal;

    internal RoomCreateObservationGate(ICompanionChannel? channel, bool hasBinding, Action<RoomCreateObservationState> changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        _channel = channel;
        _changed = changed;
        if (!hasBinding || channel is null)
        {
            _work = Task.CompletedTask;
            lock (_gate) PublishLocked(!hasBinding ? "binding-unavailable" : "companion-unavailable");
            return;
        }
        _work = Task.Run(RunAsync);
        try
        {
            // Both feeds are subscribed before the lifecycle replay can schedule either snapshot.
            _subscriptions.Add(channel.Subscribe(ContextEvent, 1, OnContextAsync));
            _subscriptions.Add(channel.Subscribe(SceneEvent, 1, OnSceneAsync));
            _subscriptions.Add(channel.SubscribeState(OnConnection));
        }
        catch (Exception error) when (error is NotSupportedException or ArgumentException or InvalidOperationException)
        {
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
            lock (_gate)
            {
                _eligible = false;
                InvalidateSynchronizationLocked();
                PublishLocked("companion-lifecycle-unavailable");
            }
            _stop.Cancel();
        }
    }

    internal RoomCreateObservationState Current => Volatile.Read(ref _current);

    private void OnConnection(CompanionConnectionSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_disposed || snapshot.Revision <= _connectionRevision) return;
            _connectionRevision = snapshot.Revision;
            var ready = snapshot.State == CompanionChannelState.Connected && !string.IsNullOrEmpty(snapshot.Epoch)
                && Required.All(snapshot.Capabilities.Contains);
            if (!ready)
            {
                _eligible = false;
                _epoch = snapshot.Epoch;
                ClearEvidenceLocked();
                InvalidateSynchronizationLocked();
                PublishLocked(snapshot.State == CompanionChannelState.Connected ? "capabilities-unavailable" : "waiting-connection");
                return;
            }
            if (_eligible && _epoch == snapshot.Epoch) return;
            _epoch = snapshot.Epoch;
            _eligible = true;
            ClearEvidenceLocked();
            QueueSynchronizationLocked();
            PublishLocked("waiting-context");
        }
    }

    private ValueTask OnContextAsync(CompanionEvent message)
    {
        lock (_gate)
        {
            if (!AcceptEventLocked(message, ContextEvent)) return ValueTask.CompletedTask;
            if (!AcceptEventSequenceLocked(message.ReceiveSequence, context: true)) return ValueTask.CompletedTask;
            if (!TryContext(message.Payload, out var context))
            {
                _contextKnown = false;
                PublishLocked("invalid-context");
                return ValueTask.CompletedTask;
            }
            ApplyContextLocked(context);
        }
        return ValueTask.CompletedTask;
    }

    private ValueTask OnSceneAsync(CompanionEvent message)
    {
        lock (_gate)
        {
            if (!AcceptEventLocked(message, SceneEvent)) return ValueTask.CompletedTask;
            if (!AcceptEventSequenceLocked(message.ReceiveSequence, context: false)) return ValueTask.CompletedTask;
            if (!TryScene(message.Payload, out var scene))
            {
                _sceneKnown = false;
                PublishLocked("invalid-scene");
                return ValueTask.CompletedTask;
            }
            var resynchronize = _sceneNeedsSnapshot && _contextKnown && _contextActive && scene.State == "plaza"
                && _synchronization is { SceneRequestFinished: true };
            if (resynchronize) QueueSynchronizationLocked();
            ApplySceneLocked(scene, resynchronize);
        }
        return ValueTask.CompletedTask;
    }

    private bool AcceptEventLocked(CompanionEvent message, string type) => !_disposed && _eligible
        && message.Type == type && message.Version == 1 && message.ConnectionEpoch == _epoch;

    private bool AcceptEventSequenceLocked(long sequence, bool context)
    {
        if (sequence <= 0)
        {
            if (context) _contextKnown = false;
            else _sceneKnown = false;
            PublishLocked(context ? "context-sequence-unavailable" : "scene-sequence-unavailable");
            return false;
        }
        // Event callbacks and response continuations can run in a different order than their frames arrived.
        if (sequence <= (context ? _contextSequence : _sceneSequence)) return false;
        if (context) _contextSequence = sequence;
        else _sceneSequence = sequence;
        return true;
    }

    private void ApplyContextLocked(Context context)
    {
        if (_contextId == context.Id)
        {
            if (context.Revision < _contextRevision) return;
            if (context.Revision == _contextRevision)
            {
                if (context.Active != _contextActive)
                {
                    _contextKnown = false;
                    PublishLocked("conflicting-context-revision");
                }
                return;
            }
        }
        else if (_contextId is not null)
        {
            // The scene feed has no context identity; a new login cannot reuse an earlier plaza observation.
            _sceneKnown = false;
            _sceneNeedsSnapshot = true;
            _scene = null;
            _sceneSequence = Math.Max(_sceneSequence, _contextSequence);
            QueueSynchronizationLocked();
        }
        _contextId = context.Id;
        _contextRevision = context.Revision;
        _contextActive = context.Active;
        _contextKnown = true;
        PublishLocked(EvidenceReasonLocked());
    }

    private void ApplySceneLocked(Scene scene, bool refreshed = false)
    {
        if (!refreshed && _sceneKnown && _scene == scene) return;
        _scene = scene;
        _sceneKnown = true;
        PublishLocked(EvidenceReasonLocked());
    }

    private string EvidenceReasonLocked() => !_contextKnown ? "waiting-context"
        : !_contextActive ? "context-inactive"
        : _sceneNeedsSnapshot ? "waiting-scene-synchronization"
        : !_sceneKnown ? "waiting-scene"
        : _scene!.State != "plaza" ? "outside-plaza" : "plaza-active";

    private void ClearEvidenceLocked()
    {
        _contextId = null;
        _contextRevision = 0;
        _contextKnown = false;
        _contextActive = false;
        _sceneKnown = false;
        _sceneNeedsSnapshot = false;
        _scene = null;
        _contextSequence = 0;
        _sceneSequence = 0;
    }

    private void QueueSynchronizationLocked()
    {
        InvalidateSynchronizationLocked();
        _synchronization = new(_epoch!, Math.Max(_contextSequence, _sceneSequence), _stop.Token);
        _wake.Writer.TryWrite(true);
    }

    private void InvalidateSynchronizationLocked()
    {
        var previous = _synchronization;
        _synchronization = null;
        previous?.Cancel();
    }

    private void PublishLocked(string reason)
    {
        var active = !_disposed && _eligible && _contextKnown && _contextActive && _sceneKnown
            && !_sceneNeedsSnapshot && _scene!.State == "plaza";
        var state = new RoomCreateObservationState(active, checked(_current.Generation + 1), reason);
        Volatile.Write(ref _current, state);
        // The controller callback only revokes/refreshes authorization and wakes its actor; delivery stays ordered.
        _changed(state);
    }

    private async Task RunAsync()
    {
        try
        {
            while (await _wake.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                while (_wake.Reader.TryRead(out _)) { }
                Synchronization? work;
                lock (_gate)
                {
                    work = _synchronization;
                    if (_disposed || !_eligible || work is null || work.Started) continue;
                    work.Started = true;
                }
                await SynchronizeAsync(work).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task SynchronizeAsync(Synchronization work)
    {
        try
        {
            await SnapshotAsync(work, context: true).ConfigureAwait(false);
            lock (_gate) if (!IsCurrentLocked(work)) return;
            await SnapshotAsync(work, context: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (work.Token.IsCancellationRequested || _stop.IsCancellationRequested) { }
    }

    private async Task SnapshotAsync(Synchronization work, bool context)
    {
        lock (_gate) if (!IsCurrentLocked(work)) return;
        CompanionOperationResult result;
        try
        {
            result = await _channel!.RequestAsync(context ? ContextRequest : SceneRequest, 1, new { },
                cancellationToken: work.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or JsonException or TimeoutException)
        {
            result = new(CompanionOperationStatus.Unavailable);
        }
        lock (_gate)
        {
            if (!IsCurrentLocked(work)) return;
            // Mark the terminal request before publishing its result so a later plaza event can recover a failed sync.
            if (!context) work.SceneRequestFinished = true;
            if (result.Status != CompanionOperationStatus.Completed || result.ConnectionEpoch != work.Epoch
                || result.ReceiveSequence <= work.MinimumReceiveSequence)
            {
                if (context ? !_contextKnown : !_sceneKnown || _sceneNeedsSnapshot)
                    PublishLocked(context ? "context-snapshot-unavailable" : "scene-snapshot-unavailable");
                return;
            }
            if (context)
            {
                if (result.ReceiveSequence <= _contextSequence) return;
                _contextSequence = result.ReceiveSequence;
                if (TryContext(result.Response, out var value)) ApplyContextLocked(value);
                else { _contextKnown = false; PublishLocked("invalid-context"); }
            }
            else
            {
                var newerEvidence = result.ReceiveSequence <= _sceneSequence;
                if (!newerEvidence) _sceneSequence = result.ReceiveSequence;
                if (TryScene(result.Response, out var value))
                {
                    // Independent scene events cannot establish the new login identity without a fresh round trip.
                    var refreshed = _sceneNeedsSnapshot;
                    _sceneNeedsSnapshot = false;
                    if (!newerEvidence) ApplySceneLocked(value, refreshed);
                    else if (refreshed) PublishLocked(EvidenceReasonLocked());
                }
                else if (!newerEvidence) { _sceneKnown = false; PublishLocked("invalid-scene"); }
            }
        }
    }

    private bool IsCurrentLocked(Synchronization work) => !_disposed && _eligible
        && ReferenceEquals(_synchronization, work) && work.Epoch == _epoch && !work.Token.IsCancellationRequested;

    private static bool TryContext(object? payload, out Context context)
    {
        context = null!;
        if (!CompanionScene.TryObject(payload, out var value) || !value.TryGetProperty("context_id", out var id)
            || id.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id.GetString()) || id.GetString()!.Contains('\0')
            || !value.TryGetProperty("revision", out var revision) || revision.ValueKind != JsonValueKind.Number
            || !revision.TryGetInt64(out var number) || number <= 0 || !CompanionScene.TryBoolean(value, "active", out var active)) return false;
        context = new(id.GetString()!, number, active);
        return true;
    }

    private static bool TryScene(object? payload, out Scene scene) => CompanionScene.TryParse(payload, out scene);

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is not null) return new(_disposal);
            _disposed = true;
            _eligible = false;
            InvalidateSynchronizationLocked();
            PublishLocked("closed");
            _wake.Writer.TryComplete();
            _stop.Cancel();
            foreach (var subscription in _subscriptions) subscription.Dispose();
            _subscriptions.Clear();
            _disposal = FinishDisposalAsync();
            return new(_disposal);
        }
    }

    private async Task FinishDisposalAsync()
    {
        try { await _work.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }

    private sealed class Synchronization
    {
        private readonly CancellationTokenSource _cancel;
        internal string Epoch { get; }
        internal long MinimumReceiveSequence { get; }
        internal CancellationToken Token { get; }
        internal bool Started { get; set; }
        internal bool SceneRequestFinished { get; set; }
        private bool _canceled;

        internal Synchronization(string epoch, long minimumReceiveSequence, CancellationToken stop)
        {
            Epoch = epoch;
            MinimumReceiveSequence = minimumReceiveSequence;
            _cancel = CancellationTokenSource.CreateLinkedTokenSource(stop);
            Token = _cancel.Token;
        }

        internal void Cancel()
        {
            if (_canceled) return;
            _canceled = true;
            _cancel.Cancel();
            _cancel.Dispose();
        }
    }

    private sealed record Context(string Id, long Revision, bool Active);
}

internal sealed record RoomCreateObservationState(bool Active, long Generation, string Reason);
