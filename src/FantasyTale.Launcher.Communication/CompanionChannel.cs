using System.Collections.Frozen;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Communication;

/// <summary>A plugin-owned companion view that cannot access another owner's registrations.</summary>
public sealed class CompanionChannel : ICompanionChannel, IDisposable
{
    private readonly CompanionConnection _connection;
    private readonly object _gate = new();
    private readonly Dictionary<string, Subscription> _handlers = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop;
    private readonly CancellationToken _token;
    private readonly CompanionStateObservers _states;
    private readonly IDisposable _stateSubscription;
    private int _disposed;
    private int _active;
    private bool _cancellationComplete;

    internal CompanionChannel(CompanionConnection connection, string owner, IReadOnlyDictionary<string, int> types)
    {
        _connection = connection; Owner = owner; Types = types;
        Capabilities = types.Keys.ToFrozenSet(StringComparer.Ordinal);
        _stop = CancellationTokenSource.CreateLinkedTokenSource(connection.StopToken);
        _token = _stop.Token;
        var initial = connection.ConnectionSnapshot;
        _states = new(initial with { Capabilities = Capabilities }, connection.StateObserverLimit, () => HandlerFailed = true);
        _stateSubscription = connection.SubscribeState(snapshot => _states.Update(snapshot.Epoch, snapshot.State,
            snapshot.State == CompanionChannelState.Closed ? FrozenSet<string>.Empty : Capabilities, snapshot.Revision));
    }

    internal string Owner { get; }
    internal IReadOnlyDictionary<string, int> Types { get; }
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    internal CancellationToken Token => _token;

    /// <inheritdoc />
    public CompanionChannelState State => IsDisposed ? CompanionChannelState.Closed : _connection.State;
    /// <inheritdoc />
    public IReadOnlySet<string> Capabilities { get; }

    /// <inheritdoc />
    public CompanionConnectionSnapshot ConnectionSnapshot => _states.Current;

    /// <inheritdoc />
    public IDisposable SubscribeState(Action<CompanionConnectionSnapshot> handler) => _states.Subscribe(handler);

    /// <summary>Whether this owner's most recent callback failed; exception text is never retained.</summary>
    public bool HandlerFailed { get; private set; }

    /// <inheritdoc />
    public ValueTask<CompanionOperationResult> SendAsync(string type, int version, object? payload = null, CancellationToken cancellationToken = default) =>
        _connection.SendAsync(this, type, version, payload, null, false, cancellationToken);

    /// <inheritdoc />
    public ValueTask<CompanionOperationResult> RequestAsync(string type, int version, object? payload = null,
        CompanionRequestOptions? options = null, CancellationToken cancellationToken = default) =>
        _connection.SendAsync(this, type, version, payload, options, true, cancellationToken);

    /// <inheritdoc />
    public IDisposable Subscribe(string type, int version, Func<CompanionEvent, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Subscribe(type, version, (message, _) => handler(message));
    }

    /// <inheritdoc />
    public IDisposable Subscribe(string type, int version, Func<CompanionEvent, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ValidateRoute(type, version);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var subscription = new Subscription(this, type, handler);
            if (!_handlers.TryAdd(type, subscription)) throw new InvalidOperationException("The companion route already has an owner handler.");
            return subscription;
        }
    }

    internal void ValidateRoute(string type, int version)
    {
        if (!Types.TryGetValue(type, out var accepted) || accepted != version)
            throw new ArgumentException("The companion route is not granted to this plugin.");
    }

    internal async ValueTask DispatchAsync(CompanionEnvelope envelope, long receiveSequence)
    {
        Subscription? handler;
        lock (_gate)
        {
            _handlers.TryGetValue(envelope.Type, out handler);
            if (IsDisposed || handler is null || handler.IsDisposed || Token.IsCancellationRequested) return;
            _active++;
        }
        try
        {
            await handler.Handler(new CompanionEvent(envelope.Type, envelope.Version, envelope.Id, envelope.Payload.Clone())
                { ConnectionEpoch = _connection.Epoch, ReceiveSequence = receiveSequence }, Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (Token.IsCancellationRequested) { }
        catch (Exception) { HandlerFailed = true; }
        finally
        {
            lock (_gate)
            {
                _active--;
                if (_active == 0 && _cancellationComplete) _stop.Dispose();
            }
        }
    }

    /// <summary>Revokes routes, cancels owner work and prevents queued sends from reaching the network.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_gate) _handlers.Clear();
        _stateSubscription.Dispose();
        _states.Update(_connection.Epoch, CompanionChannelState.Closed, FrozenSet<string>.Empty, long.MaxValue);
        _connection.RemoveOwner(this);
        try { _stop.Cancel(); }
        catch (AggregateException) { HandlerFailed = true; }
        finally
        {
            lock (_gate)
            {
                _cancellationComplete = true;
                if (_active == 0) _stop.Dispose();
            }
        }
    }


    private sealed class Subscription(CompanionChannel owner, string type, Func<CompanionEvent, CancellationToken, ValueTask> handler) : IDisposable
    {
        private int _disposed;
        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        internal Func<CompanionEvent, CancellationToken, ValueTask> Handler { get; } = handler;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._gate)
                if (owner._handlers.TryGetValue(type, out var current) && ReferenceEquals(current, this)) owner._handlers.Remove(type);
        }
    }
}
