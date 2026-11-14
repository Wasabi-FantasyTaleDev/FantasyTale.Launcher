using System.Text.Json;
using System.Text.RegularExpressions;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Window;

// Connects one overlay package's entry panel to its own companion channel. The installed manifest's grants are
// authoritative: the page can only name types it was granted, at the granted schema, and never widen them. Nothing
// here knows any feature, so a new feature is a server plugin plus a package, with no launcher change.
internal sealed partial class PanelCompanionBridge : IDisposable
{
    internal const int MaximumInFlight = 32;
    private readonly IOverlayPanel _panel;
    private readonly ICompanionChannel _channel;
    private readonly IReadOnlyDictionary<string, int> _grants;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private string? _published;
    private int _inFlight;
    private int _disposed;

    internal PanelCompanionBridge(IOverlayPanel panel, ICompanionChannel channel, IReadOnlyDictionary<string, int> grants)
    {
        _panel = panel; _channel = channel; _grants = grants;
        _panel.MessageReceived += OnMessage;
        try
        {
            foreach (var (type, version) in grants)
                _subscriptions.Add(channel.Subscribe(type, version, (message, _) =>
                {
                    Deliver("companion.event", new { type = message.Type, version = message.Version, message_id = message.MessageId, payload = message.Payload });
                    return ValueTask.CompletedTask;
                }));
        }
        catch { Dispose(); throw; }
        Refresh();
    }

    /// <summary>Publishes the channel state to the page when it changed: connection state and the granted types the server offers.</summary>
    internal void Refresh()
    {
        var state = new
        {
            state = NativeCompanionRouter.StateName(_channel.State),
            capabilities = _channel.Capabilities.Where(_grants.ContainsKey).Order(StringComparer.Ordinal).ToArray(),
        };
        var text = JsonSerializer.Serialize(state);
        lock (_gate)
        {
            if (_disposed != 0 || text == _published) return;
            _published = text;
        }
        Deliver("companion.state", state);
    }

    private void OnMessage(object? sender, OverlayMessageEventArgs args)
    {
        if (args.EventName == "companion.state") { lock (_gate) _published = null; Refresh(); return; }
        if (args.EventName is not ("companion.request" or "companion.send")) return;
        var data = args.Data;
        var id = Text(data, "id");
        if (id is null || !RequestId().IsMatch(id)) return;
        var type = Text(data, "type");
        var version = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("version", out var v) && v.TryGetInt32(out var number) ? number : 0;
        if (type is null || !_grants.TryGetValue(type, out var granted) || granted != version)
        {
            Deliver("companion.response", new { id, status = "not_granted" });
            return;
        }
        if (Interlocked.Increment(ref _inFlight) > MaximumInFlight)
        {
            Interlocked.Decrement(ref _inFlight);
            Deliver("companion.response", new { id, status = "busy" });
            return;
        }
        var payload = data.TryGetProperty("payload", out var p) && p.ValueKind != JsonValueKind.Null ? p.Clone() : (JsonElement?)null;
        var timeout = data.TryGetProperty("timeout_ms", out var t) && t.TryGetInt32(out var ms) && ms is >= 0 and <= 300000 ? ms : 0;
        var request = args.EventName == "companion.request";
        var token = _stop.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = request
                    ? await _channel.RequestAsync(type, version, payload, new() { TimeoutMilliseconds = timeout }, token).ConfigureAwait(false)
                    : await _channel.SendAsync(type, version, payload, token).ConfigureAwait(false);
                Deliver("companion.response", new { id, status = NativeCompanionRouter.OutcomeName(result.Status), request_id = result.RequestId, response = result.Response });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or JsonException)
            {
                Deliver("companion.response", new { id, status = "failed" });
            }
            finally { Interlocked.Decrement(ref _inFlight); }
        });
    }

    // A closed or failing panel must not break the channel; the page simply misses what it cannot receive.
    private void Deliver(string name, object data)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try { _panel.Send(name, data); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or ObjectDisposedException) { }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex RequestId();

    public void Dispose()
    {
        lock (_gate) { if (_disposed != 0) return; _disposed = 1; }
        _panel.MessageReceived -= OnMessage;
        _stop.Cancel();
        // The source is not disposed: requests still in flight observe its cancellation through their copied token.
        foreach (var subscription in _subscriptions) subscription.Dispose();
    }
}
