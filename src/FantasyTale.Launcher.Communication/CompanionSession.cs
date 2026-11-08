using System.Collections.Frozen;
using System.Diagnostics;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Communication;

/// <summary>Maintains a launch-scoped connection and restores registrations after fresh authorization.</summary>
public sealed class CompanionSession : IAsyncDisposable
{
    private readonly CompanionIdentity _identity;
    private readonly Func<CancellationToken, ValueTask<CompanionAuthorization?>> _authorize;
    private readonly Func<CompanionAuthorization, CompanionIdentity, CompanionLimits, CancellationToken, Task<CompanionConnection>> _connect;
    private readonly CompanionLimits _limits;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, SessionChannel> _owners = new(StringComparer.Ordinal);
    private readonly TimeSpan _minimumDelay, _maximumDelay;
    private CompanionConnection? _connection;
    private Task? _run, _dispose, _draining;
    private CompanionChannelState _state = CompanionChannelState.Unavailable;

    /// <summary>Creates a dormant session; the authorization callback must supply a fresh one-time grant on every attempt.</summary>
    public CompanionSession(CompanionIdentity identity, Func<CancellationToken, ValueTask<CompanionAuthorization?>> authorize,
        CompanionLimits? limits = null, TimeSpan? minimumReconnectDelay = null, TimeSpan? maximumReconnectDelay = null)
        : this(identity, authorize, limits, minimumReconnectDelay, maximumReconnectDelay,
            (grant, selected, budgets, token) => CompanionConnection.ConnectAsync(grant, selected, budgets, token)) { }

    internal CompanionSession(CompanionIdentity identity, Func<CancellationToken, ValueTask<CompanionAuthorization?>> authorize,
        CompanionLimits? limits, TimeSpan? minimumReconnectDelay, TimeSpan? maximumReconnectDelay,
        Func<CompanionAuthorization, CompanionIdentity, CompanionLimits, CancellationToken, Task<CompanionConnection>> connect)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(authorize);
        ArgumentNullException.ThrowIfNull(connect);
        _limits = limits ?? new CompanionLimits(); _limits.Validate();
        _identity = identity with { Capabilities = identity.Capabilities.ToFrozenDictionary(StringComparer.Ordinal) };
        _authorize = authorize; _connect = connect;
        _minimumDelay = minimumReconnectDelay ?? TimeSpan.FromMilliseconds(500);
        _maximumDelay = maximumReconnectDelay ?? TimeSpan.FromSeconds(30);
        if (_minimumDelay <= TimeSpan.Zero || _maximumDelay < _minimumDelay || _maximumDelay > TimeSpan.FromMinutes(5))
            throw new ArgumentException("Invalid companion reconnect budget.");
    }

    /// <summary>Current session state, reflecting the negotiated game binding when connected.</summary>
    public CompanionChannelState State { get { lock (_gate) return _connection?.State ?? _state; } }

    /// <summary>Starts one owned connection loop; repeated starts return the same lifetime task.</summary>
    public Task Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_dispose is not null, this);
            return _run ??= Task.Run(RunAsync);
        }
    }

    /// <summary>Returns a stable restricted view that keeps subscriptions across connection epochs.</summary>
    public ICompanionChannel CreateChannel(string owner, IReadOnlyDictionary<string, int> allowedTypes)
    {
        CompanionWire.ValidateOwner(owner);
        ArgumentNullException.ThrowIfNull(allowedTypes);
        foreach (var (type, version) in allowedTypes)
            if (!_identity.Capabilities.TryGetValue(type, out var requested) || requested != version)
                throw new ArgumentException("A plugin requested a companion type outside this session's capability set.");
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_dispose is not null, this);
            if (_owners.Count >= _limits.Routes || _owners.Values.Sum(c => c.Types.Count) + allowedTypes.Count > _limits.Routes)
                throw new InvalidOperationException("Companion session route limit reached.");
            var channel = new SessionChannel(this, owner, allowedTypes.ToFrozenDictionary(StringComparer.Ordinal));
            if (!_owners.TryAdd(owner, channel)) throw new InvalidOperationException("Companion owner already registered.");
            channel.Bind(_connection);
            return channel;
        }
    }

    private async Task RunAsync()
    {
        var delay = _minimumDelay;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                SetState(CompanionChannelState.Connecting);
                CompanionConnection? connection = null;
                Stopwatch? connectedTime = null;
                try
                {
                    // Tokens are obtained here, never copied into a durable retry queue or plugin view.
                    CompanionAuthorization? authorization;
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
                    {
                        timeout.CancelAfter(_limits.HandshakeTimeout);
                        authorization = await _authorize(timeout.Token).ConfigureAwait(false);
                    }
                    if (authorization is null) { SetState(CompanionChannelState.Unavailable); return; }
                    connection = await _connect(authorization, _identity, _limits, _stop.Token).ConfigureAwait(false);
                    connectedTime = Stopwatch.StartNew();
                    SessionChannel[] toBind;
                    lock (_gate)
                    {
                        if (!_stop.IsCancellationRequested)
                        {
                            _connection = connection;
                            toBind = _owners.Values.ToArray();
                        }
                        else toBind = [];
                    }
                    foreach (var owner in toBind) owner.Bind(connection);
                    await connection.Completion.WaitAsync(_stop.Token).ConfigureAwait(false);
                    if (connection.FailureCode is not ("companion.peer-closed" or "companion.send-ended" or "companion.receive-ended" or "companion.heartbeat-ended"))
                    { SetState(CompanionChannelState.Unavailable); return; }
                }
                catch (CompanionConnectionException error) when (error.Message is "companion.unauthorized" or "companion.forbidden" or "companion.protocol-mismatch"
                    or "companion.identity-mismatch" or "companion.capability-mismatch" or "companion.invalid-welcome")
                { SetState(CompanionChannelState.Unavailable); return; }
                catch (CompanionConnectionException) { }
                catch (OperationCanceledException) when (!_stop.IsCancellationRequested) { }
                catch (HttpRequestException) { }
                finally
                {
                    SessionChannel[] toUnbind;
                    lock (_gate)
                    {
                        _connection = null;
                        toUnbind = _owners.Values.ToArray();
                    }
                    foreach (var owner in toUnbind) owner.Bind(null);
                    if (connection is not null)
                    {
                        try { await connection.DisposeAsync().ConfigureAwait(false); }
                        catch (CompanionConnectionException error) when (error.Message == "companion.shutdown-pending")
                        {
                            // A plugin that ignores cancellation still owns its work; do not overlap a new epoch.
                            _draining = DrainAsync(connection);
                        }
                    }
                }
                if (_draining is not null) { SetState(CompanionChannelState.Unavailable); return; }
                if (_stop.IsCancellationRequested) break;
                SetState(CompanionChannelState.Reconnecting);
                var basis = connectedTime?.Elapsed >= TimeSpan.FromSeconds(30) ? _minimumDelay : delay;
                var milliseconds = Math.Min(_maximumDelay.TotalMilliseconds, basis.TotalMilliseconds * (0.8 + Random.Shared.NextDouble() * 0.4));
                await Task.Delay(TimeSpan.FromMilliseconds(milliseconds), _stop.Token).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(Math.Min(_maximumDelay.TotalMilliseconds, basis.TotalMilliseconds * 2));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or OperationCanceledException or InvalidOperationException or ArgumentException)
        {
            // Provider failure ends this attempt without leaking HTTP exceptions or authorization data.
            SetState(CompanionChannelState.Unavailable);
        }
        finally { if (_stop.IsCancellationRequested) SetState(CompanionChannelState.Closed); }
    }

    private void SetState(CompanionChannelState state) { lock (_gate) _state = state; }

    private void RemoveOwner(string owner)
    {
        lock (_gate) _owners.Remove(owner);
    }

    /// <summary>Ends the game session, cancels reconnect and waits for the owned connection loop.</summary>
    public ValueTask DisposeAsync()
    {
        Task cleanup;
        lock (_gate) cleanup = _dispose ??= Task.Run(DisposeCoreAsync);
        return new(WaitForDisposeAsync(cleanup));
    }

    private async Task WaitForDisposeAsync(Task cleanup)
    {
        try { await cleanup.WaitAsync(_limits.CloseTimeout).ConfigureAwait(false); }
        catch (TimeoutException) { throw new CompanionConnectionException("companion.shutdown-pending"); }
    }

    private static async Task DrainAsync(CompanionConnection connection)
    {
        await connection.Completion.ConfigureAwait(false);
        await connection.DisposeAsync().ConfigureAwait(false);
    }

    private async Task DisposeCoreAsync()
    {
        _stop.Cancel();
        Task? running;
        lock (_gate) running = _run;
        if (running is not null) await running.ConfigureAwait(false);
        if (_draining is not null) await _draining.ConfigureAwait(false);
        SessionChannel[] owners;
        lock (_gate) { owners = _owners.Values.ToArray(); _owners.Clear(); _state = CompanionChannelState.Closed; }
        foreach (var owner in owners) owner.Bind(null);
        foreach (var owner in owners) owner.Dispose();
        _stop.Dispose();
    }

    private sealed class SessionChannel(CompanionSession session, string owner, IReadOnlyDictionary<string, int> types) : ICompanionChannel
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Registration> _handlers = new(StringComparer.Ordinal);
        private CompanionChannel? _channel;
        private bool _disposed;
        internal IReadOnlyDictionary<string, int> Types { get; } = types;
        public CompanionChannelState State
        {
            get
            {
                CompanionChannel? channel;
                lock (_gate) { if (_disposed) return CompanionChannelState.Closed; channel = _channel; }
                if (channel is not null) return channel.State;
                var state = session.State;
                return state == CompanionChannelState.Connected ? CompanionChannelState.Unavailable : state;
            }
        }
        public IReadOnlySet<string> Capabilities { get { lock (_gate) return _channel?.Capabilities ?? FrozenSet<string>.Empty; } }

        internal void Bind(CompanionConnection? connection)
        {
            CompanionChannel? previous;
            lock (_gate)
            {
                previous = _channel; _channel = null;
                if (connection is not null && !_disposed)
                {
                    try
                    {
                        _channel = connection.CreateChannel(owner, Types);
                        foreach (var registration in _handlers.Values) registration.Bind(_channel);
                    }
                    catch (ArgumentException) { _channel = null; }
                    catch (InvalidOperationException) when (connection.State == CompanionChannelState.Closed) { _channel = null; }
                }
            }
            previous?.Dispose();
        }

        public ValueTask<CompanionOperationResult> SendAsync(string type, int version, object? payload = null, CancellationToken cancellationToken = default)
        {
            CompanionChannel? channel;
            lock (_gate) { Validate(type, version); channel = _channel; }
            return channel?.SendAsync(type, version, payload, cancellationToken) ?? ValueTask.FromResult(new CompanionOperationResult(CompanionOperationStatus.Unavailable));
        }

        public ValueTask<CompanionOperationResult> RequestAsync(string type, int version, object? payload = null,
            CompanionRequestOptions? options = null, CancellationToken cancellationToken = default)
        {
            CompanionChannel? channel;
            lock (_gate) { Validate(type, version); channel = _channel; }
            return channel?.RequestAsync(type, version, payload, options, cancellationToken) ?? ValueTask.FromResult(new CompanionOperationResult(CompanionOperationStatus.Unavailable));
        }

        public IDisposable Subscribe(string type, int version, Func<CompanionEvent, ValueTask> handler) =>
            Subscribe(type, version, (message, _) => handler(message));

        public IDisposable Subscribe(string type, int version, Func<CompanionEvent, CancellationToken, ValueTask> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            lock (_gate)
            {
                Validate(type, version);
                var registration = new Registration(this, type, version, handler);
                if (!_handlers.TryAdd(type, registration)) throw new InvalidOperationException("The companion route already has a handler.");
                if (_channel is not null) registration.Bind(_channel);
                return registration;
            }
        }

        private void Validate(string type, int version)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!Types.TryGetValue(type, out var expected) || expected != version) throw new ArgumentException("Companion type not granted.");
        }

        public void Dispose()
        {
            CompanionChannel? channel;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true; channel = _channel; _channel = null;
                foreach (var registration in _handlers.Values) registration.Unbind();
                _handlers.Clear();
            }
            session.RemoveOwner(owner);
            channel?.Dispose();
        }

        private sealed class Registration(SessionChannel owner, string type, int version,
            Func<CompanionEvent, CancellationToken, ValueTask> handler) : IDisposable
        {
            private IDisposable? _current;
            internal void Bind(CompanionChannel channel) { _current?.Dispose(); _current = channel.Subscribe(type, version, handler); }
            internal void Unbind() { _current?.Dispose(); _current = null; }
            public void Dispose()
            {
                lock (owner._gate)
                {
                    if (!owner._handlers.TryGetValue(type, out var current) || !ReferenceEquals(current, this)) return;
                    owner._handlers.Remove(type); _current?.Dispose(); _current = null;
                }
            }
        }
    }
}
