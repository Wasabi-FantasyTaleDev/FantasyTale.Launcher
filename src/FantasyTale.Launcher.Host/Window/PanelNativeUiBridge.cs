using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Window;

/// <summary>
/// Lets an overlay package's entry page use the game's own controls through Game Control Support (<see cref="IGameNativeUi"/>):
/// a page asks for capabilities, submits an operation and reads its result, and falls back to its own overlay drawing
/// only when the native operation is unsupported or not ready. Operation identities are prefixed with the package id,
/// so packages never share a native result.
/// </summary>
internal sealed class PanelNativeUiBridge : IDisposable
{
    private const int MaximumOpenCalls = 16;
    private readonly IOverlayPanel _panel;
    private readonly IGameNativeUi? _native;
    private readonly string _prefix;
    private readonly CancellationTokenSource _stop = new();
    private int _open;

    /// <summary>Starts answering the panel's native UI requests.</summary>
    /// <param name="panel">The package's entry panel.</param>
    /// <param name="packageId">The package identity, used to keep its operations apart.</param>
    /// <param name="native">The launch's native UI client, or null when the client has no hook channel.</param>
    internal PanelNativeUiBridge(IOverlayPanel panel, string packageId, IGameNativeUi? native)
    {
        _panel = panel;
        _native = native is IGameNativeUiScopes scopes ? scopes.ForOwner(packageId) : native;
        _prefix = packageId + ".";
        _panel.MessageReceived += OnMessage;
    }

    private void OnMessage(object? sender, OverlayMessageEventArgs args)
    {
        if (args.EventName != "native-ui.request" || args.Data is not { ValueKind: JsonValueKind.Object } data) return;
        var id = data.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (id is null || id.Length is 0 or > 32 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) return;
        if (Interlocked.Increment(ref _open) > MaximumOpenCalls)
        {
            Interlocked.Decrement(ref _open);
            Reply(id, "busy", null);
            return;
        }
        _ = HandleAsync(id, data.Clone());
    }

    private async Task HandleAsync(string id, JsonElement data)
    {
        try
        {
            if (_native is null) { Reply(id, "unavailable", null); return; }
            var action = data.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
            switch (action)
            {
                case "capabilities":
                    Reply(id, "completed", await _native.GetCapabilitiesAsync(_stop.Token).ConfigureAwait(false));
                    break;
                case "invoke":
                {
                    var request = data.GetProperty("request").Deserialize<GameNativeUiRequest>(JsonOptions)
                        ?? throw new ArgumentException("A native UI request is required.");
                    request = request with { OperationId = _prefix + request.OperationId };
                    request.Validate();
                    Reply(id, "completed", Own(await _native.InvokeAsync(request, _stop.Token).ConfigureAwait(false)));
                    break;
                }
                case "result":
                {
                    var operation = data.GetProperty("operation_id").GetString() ?? throw new ArgumentException("An operation id is required.");
                    GameNativeUiRequest.ValidateIdentity(operation);
                    Reply(id, "completed", Own(await _native.GetResultAsync(_prefix + operation, _stop.Token).ConfigureAwait(false)));
                    break;
                }
                default:
                    Reply(id, "failed", null);
                    break;
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (OperationCanceledException) { Reply(id, "failed", null); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or JsonException
            or KeyNotFoundException or TimeoutException)
        {
            Reply(id, "failed", null);
        }
        finally { Interlocked.Decrement(ref _open); }
    }

    // The page sees its own operation identity, without the package prefix.
    private GameNativeUiResult Own(GameNativeUiResult result) => result.OperationId.StartsWith(_prefix, StringComparison.Ordinal)
        ? result with { OperationId = result.OperationId[_prefix.Length..] } : result;

    private void Reply(string id, string status, object? result)
    {
        if (_stop.IsCancellationRequested) return;
        // Camel-case, as the page writes its requests.
        var body = result is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(result, result.GetType(), JsonOptions);
        try { _panel.Send("native-ui.response", new { id, status, result = body }); }
        catch (ObjectDisposedException) { }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Stops answering; operations already submitted keep their native outcome.</summary>
    public void Dispose()
    {
        _panel.MessageReceived -= OnMessage;
        _stop.Cancel();
        _stop.Dispose();
    }
}
