using System.Collections.Frozen;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Communication;

/// <summary>Owns one negotiated companion transport and supplies restricted interfaces to plugins.</summary>
public sealed class CompanionConnection : IAsyncDisposable
{
    private readonly WebSocket _socket;
    private readonly CompanionLimits _limits;
    private readonly string _epoch;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _lifetime;
    private readonly SemaphoreSlim _outboundAvailable = new(0);
    private readonly SemaphoreSlim _inboundAvailable = new(0);
    private readonly Queue<Outgoing> _outbound = new();
    private readonly Queue<byte[]> _control = new();
    private readonly Queue<Incoming> _inbound = new();
    private readonly Dictionary<string, PendingRequest> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CompanionChannel> _owners = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _seenOrder = new();
    private readonly IReadOnlyDictionary<string, int> _capabilities;
    private readonly Task[] _loops;
    private int _outboundBytes, _inboundBytes;
    private CompanionChannelState _state;
    private long _stateRevision;
    private readonly CompanionStateObservers _states;
    private string? _heartbeatId;
    private TaskCompletionSource? _heartbeat;
    private Task? _dispose;

    private CompanionConnection(WebSocket socket, CompanionLimits limits, string epoch,
        CompanionChannelState state, IReadOnlyDictionary<string, int> capabilities)
    {
        _socket = socket; _limits = limits; _epoch = epoch; _state = state; _capabilities = capabilities; _lifetime = _stop.Token;
        _states = new(new(epoch, state, capabilities.Keys.ToFrozenSet(StringComparer.Ordinal)), limits.Routes);
        _loops = [Task.Run(SendLoopAsync), Task.Run(ReceiveLoopAsync), Task.Run(DispatchLoopAsync), Task.Run(HeartbeatLoopAsync)];
    }

    /// <summary>Current binding state; transport failure closes this epoch rather than replaying messages.</summary>
    public CompanionChannelState State { get { lock (_gate) return _state; } }

    /// <summary>Reason code for transport termination; never includes message payloads or credentials.</summary>
    public string? FailureCode { get; private set; }

    /// <summary>Finishes when the transport loops and owned callbacks have stopped.</summary>
    public Task Completion => Task.WhenAll(_loops);

    internal CancellationToken StopToken => _lifetime;
    internal string Epoch => _epoch;
    internal int StateObserverLimit => _limits.Routes;
    internal CompanionConnectionSnapshot ConnectionSnapshot => _states.Current;
    internal IDisposable SubscribeState(Action<CompanionConnectionSnapshot> handler) => _states.Subscribe(handler);

    /// <summary>Upgrades a WSS connection using an in-memory grant and negotiates the launch identity.</summary>
    public static async Task<CompanionConnection> ConnectAsync(CompanionAuthorization authorization, CompanionIdentity identity,
        CompanionLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(identity);
        limits ??= new CompanionLimits();
        limits.Validate();
        var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol(CompanionWire.Subprotocol);
        socket.Options.SetRequestHeader("Authorization", "Bearer " + authorization.Ticket);
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.KeepAliveInterval = Timeout.InfiniteTimeSpan;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limits.HandshakeTimeout);
        try
        {
            // Redirects could move the upgrade credential to another endpoint.
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
            using var invoker = new HttpMessageInvoker(handler);
            await socket.ConnectAsync(authorization.Endpoint, invoker, timeout.Token).ConfigureAwait(false);
            return await NegotiateAsync(socket, identity, limits, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is WebSocketException or HttpRequestException or IOException or OperationCanceledException
            or ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            var code = cancellationToken.IsCancellationRequested ? "companion.canceled"
                : socket.HttpStatusCode == HttpStatusCode.Unauthorized ? "companion.unauthorized"
                : socket.HttpStatusCode == HttpStatusCode.Forbidden ? "companion.forbidden"
                : socket.HttpStatusCode == HttpStatusCode.UpgradeRequired ? "companion.protocol-mismatch"
                : error is CompanionConnectionException known ? known.Message
                : error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException ? "companion.invalid-welcome"
                : "companion.connect-failed";
            socket.Dispose();
            throw new CompanionConnectionException(code);
        }
    }

    internal static async Task<CompanionConnection> NegotiateAsync(WebSocket socket, CompanionIdentity identity,
        CompanionLimits limits, CancellationToken token)
    {
        limits.Validate();
        if (socket.SubProtocol != CompanionWire.Subprotocol) throw new CompanionConnectionException("companion.protocol-mismatch");
        CompanionWire.ValidateName(identity.Game);
        CompanionWire.ValidateName(identity.Build);
        if (identity.Locale is not ("zh-CN" or "zh-TW" or "en" or "ko" or "id" or "vi") || identity.Capabilities.Count > limits.Routes)
            throw new ArgumentException("Invalid companion negotiation identity.");
        foreach (var (capability, version) in identity.Capabilities)
        {
            CompanionWire.ValidateName(capability);
            if (version is < 1 or > 65535) throw new ArgumentException("Invalid capability version.");
        }
        var hello = new CompanionEnvelope("hello", NewId(), "companion.hello", 1,
            JsonSerializer.SerializeToElement(new { protocol = 1, game = identity.Game, build = identity.Build,
                launch_id = identity.LaunchId, locale = identity.Locale, capabilities = identity.Capabilities }));
        await socket.SendAsync(CompanionWire.Encode(hello, limits), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
        var welcome = await CompanionWire.ReceiveAsync(socket, limits, token).ConfigureAwait(false);
        if (welcome is not { Kind: "welcome", Type: "companion.welcome", Version: 1, Epoch: not null }
            || welcome.Payload.ValueKind != JsonValueKind.Object)
            throw new CompanionConnectionException("companion.invalid-welcome");
        var payload = welcome.Payload;
        if (payload.GetProperty("protocol").GetInt32() != 1 || payload.GetProperty("build").GetString() != identity.Build
            || payload.GetProperty("launch_id").GetString() != identity.LaunchId)
            throw new CompanionConnectionException("companion.identity-mismatch");
        var state = payload.GetProperty("state").GetString() switch
        {
            "pending_game" => CompanionChannelState.PendingGame,
            "connected" => CompanionChannelState.Connected,
            _ => throw new CompanionConnectionException("companion.invalid-binding-state"),
        };
        var accepted = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var capability in payload.GetProperty("capabilities").EnumerateObject())
        {
            if (!identity.Capabilities.TryGetValue(capability.Name, out var requested) || capability.Value.GetInt32() != requested)
                throw new CompanionConnectionException("companion.capability-mismatch");
            accepted.Add(capability.Name, requested);
        }
        return new CompanionConnection(socket, limits, welcome.Epoch, state, accepted.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>Creates a plugin-owned view with explicit allowed message types and schema versions.</summary>
    public CompanionChannel CreateChannel(string owner, IReadOnlyDictionary<string, int> allowedTypes)
    {
        CompanionWire.ValidateOwner(owner);
        ArgumentNullException.ThrowIfNull(allowedTypes);
        foreach (var (type, version) in allowedTypes)
            if (!_capabilities.TryGetValue(type, out var accepted) || accepted != version)
                throw new ArgumentException("A plugin requested an unnegotiated companion route.", nameof(allowedTypes));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_state == CompanionChannelState.Closed, this);
            if (_owners.Count >= _limits.Routes || _owners.Values.Sum(c => c.Types.Count) + allowedTypes.Count > _limits.Routes)
                throw new InvalidOperationException("Companion owner/route limit reached.");
            if (_owners.ContainsKey(owner)) throw new InvalidOperationException("Companion owner already registered.");
            var channel = new CompanionChannel(this, owner, allowedTypes.ToFrozenDictionary(StringComparer.Ordinal));
            _owners.Add(owner, channel);
            return channel;
        }
    }

    internal async ValueTask<CompanionOperationResult> SendAsync(CompanionChannel owner, string type, int version, object? payload,
        CompanionRequestOptions? options, bool request, CancellationToken token)
    {
        owner.ValidateRoute(type, version);
        if (options?.TimeoutMilliseconds is < 0 or > 300000) throw new ArgumentOutOfRangeException(nameof(options));
        var id = NewId();
        var envelope = new CompanionEnvelope(request ? "request" : "event", id, type, version,
            CompanionWire.EncodePayload(payload, _limits),
            Epoch: _epoch, OperationId: options?.OperationId);
        var bytes = CompanionWire.Encode(envelope, _limits);
        PendingRequest? pending = null;
        lock (_gate)
        {
            if (token.IsCancellationRequested) return new(CompanionOperationStatus.Canceled);
            if (owner.IsDisposed || _state == CompanionChannelState.Closed) return new(CompanionOperationStatus.Unavailable);
            if (_state != CompanionChannelState.Connected) return new(CompanionOperationStatus.NotBound);
            if (_outbound.Count >= _limits.QueueMessages || _outboundBytes + bytes.Length > _limits.QueueBytes
            || request && _pending.Count >= _limits.PendingRequests) return new(CompanionOperationStatus.Busy);
            if (request) _pending.Add(id, pending = new PendingRequest(owner, envelope));
            _outbound.Enqueue(new Outgoing(owner, envelope, bytes, pending, token));
            _outboundBytes += bytes.Length;
            _outboundAvailable.Release();
        }
        if (!request) return new(CompanionOperationStatus.Accepted, id);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token, owner.Token, _lifetime);
        wait.CancelAfter(options?.TimeoutMilliseconds is > 0 ? TimeSpan.FromMilliseconds(options.TimeoutMilliseconds) : _limits.RequestTimeout);
        try { return await pending!.Completion.Task.WaitAsync(wait.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _pending.Remove(id);
                if (pending!.Completion.Task.IsCompletedSuccessfully) return pending.Completion.Task.Result;
                return new(pending.Sending ? CompanionOperationStatus.OutcomeUnknown : CompanionOperationStatus.Canceled, id);
            }
        }
        finally { lock (_gate) _pending.Remove(id); }
    }

    private async Task SendLoopAsync()
    {
        try
        {
            while (true)
            {
                await _outboundAvailable.WaitAsync(_stop.Token).ConfigureAwait(false);
                byte[]? bytes = null;
                lock (_gate)
                {
                    if (_control.TryDequeue(out var control)) bytes = control;
                    else if (_outbound.TryDequeue(out var outgoing))
                    {
                        _outboundBytes -= outgoing.Bytes.Length;
                        if (!outgoing.Owner.IsDisposed && !outgoing.Token.IsCancellationRequested
                            && (outgoing.Pending is null || _pending.ContainsKey(outgoing.Envelope.Id)))
                        {
                            if (outgoing.Pending is not null) outgoing.Pending.Sending = true;
                            bytes = outgoing.Bytes;
                        }
                    }
                }
                if (bytes is not null)
                    await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or WebSocketException or OperationCanceledException or ObjectDisposedException)
        { Stop("companion.send-ended"); }
    }

    private async Task ReceiveLoopAsync()
    {
        var window = Stopwatch.StartNew();
        var count = 0;
        long receiveSequence = 0;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var message = await CompanionWire.ReceiveAsync(_socket, _limits, _stop.Token).ConfigureAwait(false);
                if (message is null) { Stop("companion.peer-closed"); return; }
                if (window.Elapsed >= TimeSpan.FromSeconds(1)) { window.Restart(); count = 0; }
                if (++count > _limits.MessagesPerSecond) throw new IOException("companion.rate-limit");
                if (message.Epoch != _epoch) throw new IOException("companion.stale-epoch");
                long stateRevision = -1;
                lock (_gate)
                {
                    if (!_seenIds.Add(message.Id)) throw new IOException("companion.duplicate-id");
                    _seenOrder.Enqueue(message.Id);
                    if (_seenOrder.Count > 1024) _seenIds.Remove(_seenOrder.Dequeue());
                    if (receiveSequence == long.MaxValue) throw new IOException("companion.receive-sequence-exhausted");
                    receiveSequence++;
                    if (message.Kind == "heartbeat_ack")
                    {
                        if (message.Type != "companion.heartbeat" || message.Version != 1 || message.ReplyTo != _heartbeatId)
                            throw new IOException("companion.invalid-heartbeat");
                        _heartbeat?.TrySetResult();
                    }
                    else if (message.Kind == "heartbeat")
                    {
                        if (message.Type != "companion.heartbeat" || message.Version != 1) throw new IOException("companion.invalid-heartbeat");
                        QueueControl(new("heartbeat_ack", NewId(), "companion.heartbeat", 1,
                            JsonSerializer.SerializeToElement<object?>(null), message.Id, _epoch));
                    }
                    else if (message.Kind == "state")
                    {
                        if (message.Type != "companion.state" || message.Version != 1
                            || message.Payload.GetProperty("state").GetString() != "connected" || _state != CompanionChannelState.PendingGame)
                            throw new IOException("companion.invalid-binding-state");
                        _state = CompanionChannelState.Connected;
                        stateRevision = ++_stateRevision;
                    }
                    else if (message.Kind is "response" or "error")
                    {
                        if (_pending.TryGetValue(message.ReplyTo!, out var pending))
                        {
                            if (pending.Envelope.Type != message.Type || pending.Envelope.Version != message.Version || !pending.Sending)
                                throw new IOException("companion.response-mismatch");
                            pending.Completion.TrySetResult(new(message.Kind == "response" ? CompanionOperationStatus.Completed
                                : CompanionOperationStatus.Rejected, pending.Envelope.Id, message.Payload.Clone())
                                { ConnectionEpoch = _epoch, ReceiveSequence = receiveSequence });
                        }
                    }
                    else if (message.Kind == "event")
                    {
                        if (_state != CompanionChannelState.Connected) throw new IOException("companion.not-bound");
                        if (!_capabilities.TryGetValue(message.Type, out var version) || version != message.Version)
                            throw new IOException("companion.unnegotiated-type");
                        var bytes = message.ReceivedBytes;
                        if (_inbound.Count >= _limits.QueueMessages || _inboundBytes + bytes > _limits.QueueBytes)
                            throw new IOException("companion.slow-consumer");
                        _inbound.Enqueue(new(message, bytes, receiveSequence)); _inboundBytes += bytes;
                        _inboundAvailable.Release();
                    }
                    else throw new IOException("companion.unexpected-kind");
                }
                if (stateRevision >= 0) _states.Update(_epoch, CompanionChannelState.Connected,
                    _capabilities.Keys.ToFrozenSet(StringComparer.Ordinal), stateRevision);
            }
        }
        catch (Exception error) when (error is IOException or WebSocketException or OperationCanceledException or ObjectDisposedException
            or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException)
        { Stop(error is IOException && error.Message.StartsWith("companion.", StringComparison.Ordinal) ? error.Message : "companion.receive-ended"); }
    }

    private async Task DispatchLoopAsync()
    {
        try
        {
            while (true)
            {
                await _inboundAvailable.WaitAsync(_stop.Token).ConfigureAwait(false);
                Incoming incoming;
                CompanionChannel[] owners;
                lock (_gate)
                {
                    if (!_inbound.TryDequeue(out incoming!)) continue;
                    _inboundBytes -= incoming.Bytes;
                    owners = _owners.Values.Where(c => c.Types.TryGetValue(incoming.Envelope.Type, out var version)
                        && version == incoming.Envelope.Version).ToArray();
                }
                foreach (var owner in owners) await owner.DispatchAsync(incoming.Envelope, incoming.ReceiveSequence).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task HeartbeatLoopAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(_limits.HeartbeatInterval, _stop.Token).ConfigureAwait(false);
                Task ack;
                lock (_gate)
                {
                    _heartbeatId = NewId();
                    _heartbeat = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    ack = _heartbeat.Task;
                    QueueControl(new("heartbeat", _heartbeatId, "companion.heartbeat", 1,
                        JsonSerializer.SerializeToElement<object?>(null), Epoch: _epoch));
                }
                await ack.WaitAsync(_limits.HeartbeatTimeout, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException or IOException)
        { Stop("companion.heartbeat-ended"); }
    }

    private void QueueControl(CompanionEnvelope envelope)
    {
        if (_control.Count >= 8) throw new IOException("companion.control-overflow");
        _control.Enqueue(CompanionWire.Encode(envelope, _limits)); _outboundAvailable.Release();
    }

    internal void RemoveOwner(CompanionChannel owner)
    {
        lock (_gate)
        {
            if (_owners.TryGetValue(owner.Owner, out var registered) && ReferenceEquals(registered, owner)) _owners.Remove(owner.Owner);
            foreach (var request in _pending.Values.Where(p => p.Owner == owner))
                request.Completion.TrySetResult(new(request.Sending ? CompanionOperationStatus.OutcomeUnknown : CompanionOperationStatus.Canceled,
                    request.Envelope.Id));
        }
    }

    private void Stop(string reason)
    {
        long revision;
        lock (_gate)
        {
            if (_state == CompanionChannelState.Closed) return;
            _state = CompanionChannelState.Closed; FailureCode = reason;
            revision = ++_stateRevision;
            foreach (var request in _pending.Values)
                request.Completion.TrySetResult(new(request.Sending ? CompanionOperationStatus.OutcomeUnknown : CompanionOperationStatus.Canceled,
                    request.Envelope.Id));
            _outbound.Clear(); _control.Clear(); _inbound.Clear(); _outboundBytes = _inboundBytes = 0;
        }
        _states.Update(_epoch, CompanionChannelState.Closed, FrozenSet<string>.Empty, revision);
        try { _stop.Cancel(); }
        catch (AggregateException) { FailureCode = "companion.cancellation-handler-failed"; }
        finally { _socket.Abort(); }
    }

    /// <summary>Cancels this epoch and waits for owned loops before releasing the transport.</summary>
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

    private async Task DisposeCoreAsync()
    {
        Stop("companion.closed");
        // Cancellation is cooperative; ownership remains until handlers really finish.
        await Task.WhenAll(_loops).ConfigureAwait(false);
        CompanionChannel[] owners;
        lock (_gate) { owners = _owners.Values.ToArray(); _owners.Clear(); }
        foreach (var owner in owners) owner.Dispose();
        _socket.Dispose(); _stop.Dispose(); _outboundAvailable.Dispose(); _inboundAvailable.Dispose();
    }

    private static string NewId() => Guid.NewGuid().ToString("N");
    private sealed record Outgoing(CompanionChannel Owner, CompanionEnvelope Envelope, byte[] Bytes, PendingRequest? Pending, CancellationToken Token);
    private sealed record Incoming(CompanionEnvelope Envelope, int Bytes, long ReceiveSequence);
    private sealed class PendingRequest(CompanionChannel owner, CompanionEnvelope envelope)
    {
        internal CompanionChannel Owner { get; } = owner;
        internal CompanionEnvelope Envelope { get; } = envelope;
        internal bool Sending { get; set; }
        internal TaskCompletionSource<CompanionOperationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
