using System.Text.Json;
using System.Threading.Channels;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Communication;

/// <summary>
/// Optional consumer that shows structured companion notifications from the server: as the game's own message box
/// through Game Control Support when that operation is ready, otherwise as a trusted overlay dialog.
/// </summary>
public sealed class CompanionOverlayNotifications : IAsyncDisposable
{
    private const string ShowType = "ui.notification.show";
    private const string ResultType = "ui.notification.result";
    private readonly ICompanionChannel _channel;
    private readonly IOverlayHost _overlay;
    private readonly IGameNativeUi? _native;
    private readonly IDisposable _subscription;
    private readonly Channel<NotificationResult> _results;
    private readonly Task _sender;
    private readonly object _gate = new();
    private readonly Dictionary<string, IOverlayPanel> _panels = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenRegistration> _bindings = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();
    private readonly int _maximumDialogs;
    private readonly TimeSpan _readyTimeout;
    private readonly string _namespace = "companion." + Guid.NewGuid().ToString("N");
    private int _disposed;
    private int _callbacks;
    private Task? _dispose;
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Subscribes an already restricted channel; absence of Overlay is handled by the composing plugin.</summary>
    /// <param name="channel">The channel restricted to the notification routes.</param>
    /// <param name="overlay">The overlay, used when the native message box is unavailable or fails.</param>
    /// <param name="maximumDialogs">Overlay dialogs open at once.</param>
    /// <param name="resultQueueSize">Results waiting for delivery.</param>
    /// <param name="readyTimeout">How long a notification may wait for a presenter.</param>
    /// <param name="native">The launch's native game controls, or null when the client has no hook channel.</param>
    public CompanionOverlayNotifications(ICompanionChannel channel, IOverlayHost overlay, int maximumDialogs = 4,
        int resultQueueSize = 32, TimeSpan? readyTimeout = null, IGameNativeUi? native = null)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(overlay);
        if (!channel.Capabilities.Contains(ShowType) || !channel.Capabilities.Contains(ResultType))
            throw new ArgumentException("The notification consumer requires both negotiated notification routes.", nameof(channel));
        _readyTimeout = readyTimeout ?? TimeSpan.FromSeconds(10);
        if (maximumDialogs is < 1 or > 16 || resultQueueSize is < 1 or > 128
            || _readyTimeout <= TimeSpan.Zero || _readyTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentException("Invalid companion notification budget.");
        _channel = channel; _overlay = overlay; _maximumDialogs = maximumDialogs; _native = native;
        _results = Channel.CreateBounded<NotificationResult>(new BoundedChannelOptions(resultQueueSize) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        _subscription = channel.Subscribe(ShowType, 1, ShowAsync);
        _sender = Task.Run(SendResultsAsync);
    }

    /// <summary>Whether a result could not be queued or delivered; no notification content is retained.</summary>
    public bool DeliveryFailed { get; private set; }

    private async ValueTask ShowAsync(CompanionEvent message, CancellationToken connectionToken)
    {
        lock (_gate) { if (_disposed != 0) return; _callbacks++; }
        try { await ShowCoreAsync(message, connectionToken).ConfigureAwait(false); }
        finally
        {
            lock (_gate) { if (--_callbacks == 0 && _disposed != 0) _drained.TrySetResult(); }
        }
    }

    private async Task ShowCoreAsync(CompanionEvent message, CancellationToken connectionToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, connectionToken);
        lifetime.CancelAfter(_readyTimeout);
        var token = lifetime.Token;
        try
        {
            if (message.Payload is not JsonElement { ValueKind: JsonValueKind.Object } payload) throw new ArgumentException("Structured notification required.");
            var title = payload.GetProperty("title").GetString();
            var text = payload.GetProperty("text").GetString();
            if (title is null || title.Length > 256 || text is null || text.Length > 8192)
                throw new ArgumentException("Invalid notification text.");
            var buttons = new List<OverlayDialogButton>();
            if (payload.TryGetProperty("buttons", out var rawButtons))
            {
                if (rawButtons.ValueKind != JsonValueKind.Array || rawButtons.GetArrayLength() is < 1 or > 4) throw new ArgumentException("Invalid notification buttons.");
                foreach (var button in rawButtons.EnumerateArray())
                {
                    var id = button.GetProperty("id").GetString(); var label = button.GetProperty("label").GetString();
                    if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || string.IsNullOrWhiteSpace(label) || label.Length > 128)
                        throw new ArgumentException("Invalid notification button.");
                    buttons.Add(new(id, label));
                }
                if (buttons.Select(b => b.Name).Distinct(StringComparer.Ordinal).Count() != buttons.Count)
                    throw new ArgumentException("Duplicate notification button.");
            }
            else buttons.Add(new("ok", "确定"));
            // The game's own message box comes first; the overlay dialog is the fallback.
            if (await TryNativeAsync(message.MessageId, title, text, buttons, connectionToken, token).ConfigureAwait(false)) return;
            await _overlay.WaitUntilReadyAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var panelId = _namespace + "." + Guid.NewGuid().ToString("N")[..8];
            IOverlayPanel panel;
            lock (_gate)
            {
                if (_disposed != 0 || token.IsCancellationRequested) return;
                if (_panels.Count >= _maximumDialogs) { QueueResult(message.MessageId, "busy", connectionToken); return; }
                // Register first so mounted/action events cannot race subscription setup.
                panel = _overlay.CreateGameUiPanel(panelId, interactive: true, modal: true);
                _panels.Add(panelId, panel);
                panel.MessageReceived += (_, args) =>
                {
                    if (connectionToken.IsCancellationRequested || Volatile.Read(ref _disposed) != 0) return;
                    if (args.EventName == "game-ui.mounted") QueueResult(message.MessageId, "shown", connectionToken);
                    else if (args.EventName == "game-ui.action")
                    {
                        var action = args.Data.TryGetProperty("name", out var name) ? name.GetString() : null;
                        if (action != "cancel" && !buttons.Any(b => b.Name == action)) return;
                        QueueResult(message.MessageId, "selected", connectionToken, action);
                        RemovePanel(panelId);
                    }
                    else if (args.EventName is "game-ui.close" or "game-ui.error")
                    {
                        QueueResult(message.MessageId, args.EventName == "game-ui.error" ? "failed" : "closed", connectionToken);
                        RemovePanel(panelId);
                    }
                };
                QueueResult(message.MessageId, "queued", connectionToken);
            }
            var registration = connectionToken.Register(() => RemovePanel(panelId));
            var retained = false;
            lock (_gate)
                if (_panels.ContainsKey(panelId)) { _bindings[panelId] = registration; retained = true; }
            if (!retained) { registration.Unregister(); return; }
            try
            {
                panel.Send("game-ui.message-box", new { title, text, buttons = buttons.Select(b => new { name = b.Name, label = b.Label }).ToArray() });
            }
            catch { RemovePanel(panelId); throw; }
        }
        catch (OperationCanceledException) { QueueResult(message.MessageId, "unavailable", connectionToken); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException or IOException or NotSupportedException)
        { QueueResult(message.MessageId, "failed", connectionToken); }
    }

    // Native outcomes that mean the message box never appeared, so the overlay shows it instead.
    private static readonly string[] NotShown = ["unsupported", "notReady", "busy", "failed"];
    private static readonly string[] Settled = ["selected", "dismissed", "closed", "failed", "outcomeUnknown", "operationUnavailable"];

    private async Task<bool> TryNativeAsync(string notificationId, string title, string text, IReadOnlyList<OverlayDialogButton> buttons,
        CancellationToken connectionToken, CancellationToken token)
    {
        if (_native is null) return false;
        try
        {
            // Native button identities are restricted; a server id outside them keeps the overlay dialog.
            foreach (var button in buttons) GameNativeUiRequest.ValidateIdentity(button.Name);
            var capabilities = await _native.GetCapabilitiesAsync(token).ConfigureAwait(false);
            if (!capabilities.Operations.Any(operation => operation.Kind == "messageBox" && operation.Supported && operation.Ready)) return false;
            var operationId = "notification-" + Guid.NewGuid().ToString("N");
            var request = new GameNativeUiRequest(operationId, "messageBox", text)
            {
                Title = title, Icon = "notice", Buttons = [.. buttons.Select(button => new GameNativeUiButton(button.Name, button.Label))],
            };
            request.Validate();
            var submitted = await _native.InvokeAsync(request, token).ConfigureAwait(false);
            if (NotShown.Contains(submitted.Status)) return false;
            QueueResult(notificationId, "queued", connectionToken);
            QueueResult(notificationId, "shown", connectionToken);
            _ = FollowNativeAsync(notificationId, operationId, connectionToken);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or TimeoutException)
        {
            return false;
        }
    }

    // The native modal is the game's own; its outcome is read without presenting it again, until it settles.
    private async Task FollowNativeAsync(string notificationId, string operationId, CancellationToken connectionToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, connectionToken);
        try
        {
            for (var waited = TimeSpan.Zero; waited < TimeSpan.FromMinutes(30); waited += TimeSpan.FromMilliseconds(500))
            {
                await Task.Delay(500, lifetime.Token).ConfigureAwait(false);
                var outcome = await _native!.GetResultAsync(operationId, lifetime.Token).ConfigureAwait(false);
                if (!Settled.Contains(outcome.Status)) continue;
                if (outcome.Status == "selected") QueueResult(notificationId, "selected", connectionToken, outcome.ButtonId);
                else QueueResult(notificationId, outcome.Status == "failed" ? "failed" : "closed", connectionToken);
                return;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or TimeoutException)
        {
            QueueResult(notificationId, "failed", connectionToken);
        }
    }

    private void QueueResult(string notificationId, string phase, CancellationToken epoch, string? action = null)
    {
        if (epoch.IsCancellationRequested || Volatile.Read(ref _disposed) != 0) return;
        if (!_results.Writer.TryWrite(new(new { notification_id = notificationId, phase, action }, epoch))) DeliveryFailed = true;
    }

    private void RemovePanel(string id)
    {
        IOverlayPanel? panel;
        CancellationTokenRegistration binding;
        lock (_gate) { _panels.Remove(id, out panel); _bindings.Remove(id, out binding); }
        binding.Unregister();
        panel?.Dispose();
    }

    private async Task SendResultsAsync()
    {
        try
        {
            await foreach (var result in _results.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                if (result.Epoch.IsCancellationRequested) continue;
                using var delivery = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, result.Epoch);
                try
                {
                    var outcome = await _channel.SendAsync(ResultType, 1, result.Payload, delivery.Token).ConfigureAwait(false);
                    if (outcome.Status != CompanionOperationStatus.Accepted && !delivery.IsCancellationRequested) DeliveryFailed = true;
                }
                catch (OperationCanceledException) when (delivery.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException) { DeliveryFailed = true; }
    }

    /// <summary>Releases the event subscription, closes owned dialogs and stops result delivery.</summary>
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = 1;
            return new(_dispose ??= Task.Run(DisposeCoreAsync));
        }
    }

    private async Task DisposeCoreAsync()
    {
        _subscription.Dispose(); _stop.Cancel(); _results.Writer.TryComplete();
        IOverlayPanel[] panels;
        CancellationTokenRegistration[] bindings;
        lock (_gate)
        {
            panels = _panels.Values.ToArray(); _panels.Clear(); bindings = _bindings.Values.ToArray(); _bindings.Clear();
            if (_callbacks == 0) _drained.TrySetResult();
        }
        foreach (var binding in bindings) binding.Unregister();
        foreach (var panel in panels) panel.Dispose();
        await _sender.ConfigureAwait(false);
        await _drained.Task.ConfigureAwait(false);
        _stop.Dispose();
    }

    private sealed record NotificationResult(object Payload, CancellationToken Epoch);
}
