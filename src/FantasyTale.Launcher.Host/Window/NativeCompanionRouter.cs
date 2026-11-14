using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Window;

// Installed package grants are authoritative; neither native requests nor page content can expand them.
internal sealed class NativeCompanionRouter : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly object _gate = new();
    private readonly Dictionary<string, Owner> _owners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, int>> _grants;
    private readonly Func<string, IReadOnlyDictionary<string, int>, ICompanionChannel?> _channelFactory;
    private readonly CancellationTokenSource _stop = new();
    private int _disposed;

    internal NativeCompanionRouter(IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> grants,
        Func<string, IReadOnlyDictionary<string, int>, ICompanionChannel?> channelFactory)
    {
        _grants = grants.ToDictionary(p => p.Key,
            p => (IReadOnlyDictionary<string, int>)p.Value.ToDictionary(StringComparer.Ordinal), StringComparer.Ordinal);
        _channelFactory = channelFactory;
        if (grants.Count > 64 || grants.Values.Sum(row => row.Count) > 128) throw new ArgumentException("Native companion grant budget exceeded.");
    }

    internal async ValueTask<CompanionBrokerResponse> HandleAsync(CompanionBrokerRequest request, CancellationToken cancellationToken)
    {
        if (!_grants.TryGetValue(request.Owner, out var grants)) return new("invalid");
        if (request.Method is not ("state" or "poll") && (!grants.TryGetValue(request.Type, out var version) || version != request.Version)) return new("invalid");
        Owner? owner;
        lock (_gate)
        {
            if (_disposed != 0) return new("not-found");
            if (!_owners.TryGetValue(request.Owner, out owner))
            {
                var channel = _channelFactory(request.Owner, grants);
                if (channel is not null) _owners.Add(request.Owner, owner = new(channel));
            }
        }
        if (owner is null)
            return request.Method == "state" ? Json(new { state = "unavailable", capabilities = Array.Empty<string>() })
                : request.Method is "send" or "request" ? Json(new { status = "unavailable" }) : new("not-found");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, cancellationToken);
        try
        {
            switch (request.Method)
            {
                case "state":
                    return Json(new
                    {
                        state = StateName(owner.Channel.State),
                        capabilities = owner.Channel.Capabilities.Order(StringComparer.Ordinal).ToArray(),
                    });
                case "subscribe": owner.Subscribe(request.Type, request.Version); return new("ok");
                case "unsubscribe": owner.Unsubscribe(request.Type); return new("ok");
                case "poll": return owner.Take();
                case "send":
                case "request":
                    object? payload = null;
                    if (request.JsonData is not null)
                    {
                        using var document = JsonDocument.Parse(request.JsonData, new JsonDocumentOptions { MaxDepth = 16 });
                        payload = document.RootElement.Clone();
                    }
                    var result = request.Method == "send"
                        ? await owner.Channel.SendAsync(request.Type, request.Version, payload, lifetime.Token).ConfigureAwait(false)
                        : await owner.Channel.RequestAsync(request.Type, request.Version, payload,
                            new() { TimeoutMilliseconds = request.TimeoutMilliseconds, OperationId = request.OperationId }, lifetime.Token).ConfigureAwait(false);
                    return Json(new { status = OutcomeName(result.Status), request_id = result.RequestId, response = result.Response });
                default: return new("invalid");
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return Json(new { status = "canceled" }); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or JsonException) { return new("failed"); }
    }

    private static CompanionBrokerResponse Json(object value) => new("ok", JsonSerializer.Serialize(value, JsonOptions));

    internal static string StateName(CompanionChannelState state) => state switch
    {
        CompanionChannelState.PendingGame => "pending_game",
        _ => state.ToString().ToLowerInvariant(),
    };

    internal static string OutcomeName(CompanionOperationStatus status) => status switch
    {
        CompanionOperationStatus.NotBound => "not_bound",
        CompanionOperationStatus.OutcomeUnknown => "outcome_unknown",
        _ => status.ToString().ToLowerInvariant(),
    };

    public void Dispose()
    {
        Owner[] owners;
        lock (_gate)
        {
            if (_disposed != 0) return;
            _disposed = 1; owners = _owners.Values.ToArray(); _owners.Clear();
        }
        _stop.Cancel();
        foreach (var owner in owners) owner.Dispose();
        // Pending operations own linked tokens until their awaited channel calls return.
    }

    private sealed class Owner(ICompanionChannel channel) : IDisposable
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, IDisposable> _subscriptions = new(StringComparer.Ordinal);
        private readonly Queue<QueuedEvent> _events = new();
        private int _bytes;
        private bool _disposed;
        private CancellationToken? _overflow;
        internal ICompanionChannel Channel => channel;

        internal void Subscribe(string type, int version)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_subscriptions.ContainsKey(type)) return;
                _subscriptions.Add(type, channel.Subscribe(type, version, (message, epoch) =>
                {
                    var json = JsonSerializer.Serialize(new { type = message.Type, version = message.Version, id = message.MessageId, payload = message.Payload }, JsonOptions);
                    var bytes = Encoding.UTF8.GetByteCount(json);
                    lock (_gate)
                    {
                        if (_disposed || epoch.IsCancellationRequested) return ValueTask.CompletedTask;
                        RemoveStale();
                        if (_events.Count >= 32 || bytes > 128 * 1024 || _bytes + bytes > 128 * 1024) _overflow = epoch;
                        else { _events.Enqueue(new(type, json, bytes, epoch)); _bytes += bytes; }
                    }
                    return ValueTask.CompletedTask;
                }));
            }
        }

        internal void Unsubscribe(string type)
        {
            IDisposable? subscription;
            lock (_gate)
            {
                _subscriptions.Remove(type, out subscription);
                var retained = _events.Where(e => e.Type != type).ToArray();
                _events.Clear(); _bytes = 0;
                foreach (var item in retained) { _events.Enqueue(item); _bytes += item.Bytes; }
            }
            subscription?.Dispose();
        }

        private void RemoveStale()
        {
            if (_overflow is { IsCancellationRequested: true }) _overflow = null;
            if (!_events.Any(e => e.Epoch.IsCancellationRequested)) return;
            var retained = _events.Where(e => !e.Epoch.IsCancellationRequested).ToArray();
            _events.Clear(); _bytes = 0;
            foreach (var item in retained) { _events.Enqueue(item); _bytes += item.Bytes; }
        }

        internal CompanionBrokerResponse Take()
        {
            lock (_gate)
            {
                RemoveStale();
                if (_overflow is not null) { _overflow = null; return Json(new { error = "companion.events-overflow" }); }
                if (!_events.TryDequeue(out var item)) return new("not-found");
                _bytes -= item.Bytes;
                return new("ok", item.Json);
            }
        }

        public void Dispose()
        {
            IDisposable[] subscriptions;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                subscriptions = _subscriptions.Values.ToArray(); _subscriptions.Clear(); _events.Clear(); _bytes = 0;
            }
            foreach (var subscription in subscriptions) subscription.Dispose();
            channel.Dispose();
        }

        private sealed record QueuedEvent(string Type, string Json, int Bytes, CancellationToken Epoch);
    }
}
