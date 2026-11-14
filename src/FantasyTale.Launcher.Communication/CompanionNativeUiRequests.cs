using System.Text.Json;
using System.Threading.Channels;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Communication;

/// <summary>
/// Shows the game's own interface on the server's request (<c>ui.native.invoke</c>): a message box, notification, banner
/// or local chat line, or live control inspection and mutation through Game Control Support. When a presentation cannot show and the request allows it, a
/// message box or notification is drawn as an overlay dialog instead. Every outcome goes back as <c>ui.native.result</c>
/// with the presenter that showed it.
/// </summary>
public sealed class CompanionNativeUiRequests : IAsyncDisposable
{
    /// <summary>The server's request type.</summary>
    public const string InvokeType = "ui.native.invoke";
    /// <summary>The launcher's result type.</summary>
    public const string ResultType = "ui.native.result";
    private static readonly string[] NotShown = ["unsupported", "notReady", "busy", "failed"];
    private static readonly string[] Settled = ["selected", "dismissed", "closed", "failed", "outcomeUnknown", "operationUnavailable", "completed", "ambiguous"];
    private readonly ICompanionChannel _channel;
    private readonly IGameNativeUi? _native;
    private readonly IOverlayHost? _overlay;
    private readonly IDisposable _subscription;
    private readonly Channel<object> _results = Channel.CreateBounded<object>(new BoundedChannelOptions(64) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _sender;
    private readonly string _namespace = "native-ui." + Guid.NewGuid().ToString("N")[..8];
    private int _overlayDialogs;

    /// <summary>Subscribes the restricted channel.</summary>
    /// <param name="channel">A channel granted exactly the request and result types.</param>
    /// <param name="native">The launch's native game controls, or null when the client has no hook channel.</param>
    /// <param name="overlay">The overlay for the fallback, or null when there is none.</param>
    public CompanionNativeUiRequests(ICompanionChannel channel, IGameNativeUi? native, IOverlayHost? overlay)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (!channel.Capabilities.Contains(InvokeType) || !channel.Capabilities.Contains(ResultType))
            throw new ArgumentException("The native UI consumer requires both negotiated routes.", nameof(channel));
        _channel = channel; _native = native is IGameNativeUiScopes scopes ? scopes.ForOwner("companion") : native; _overlay = overlay;
        _subscription = channel.Subscribe(InvokeType, 1, OnInvokeAsync);
        _sender = Task.Run(SendResultsAsync);
    }

    /// <summary>Whether a result could not be queued or delivered.</summary>
    public bool DeliveryFailed { get; private set; }

    private ValueTask OnInvokeAsync(CompanionEvent message, CancellationToken connectionToken)
    {
        _ = HandleAsync(message, connectionToken);
        return ValueTask.CompletedTask;
    }

    private async Task HandleAsync(CompanionEvent message, CancellationToken connectionToken)
    {
        string? operationId = null;
        try
        {
            if (message.Payload is not JsonElement { ValueKind: JsonValueKind.Object } payload) return;
            operationId = Text(payload, "operation_id") ?? throw new ArgumentException("An operation id is required.");
            GameNativeUiRequest.ValidateIdentity(operationId);
            var fallback = !payload.TryGetProperty("fallback", out var allowed) || allowed.ValueKind != JsonValueKind.False;
            var request = new GameNativeUiRequest("server-" + operationId, Text(payload, "kind") ?? "", Text(payload, "text") ?? "")
            {
                Title = Text(payload, "title") ?? "", Icon = Text(payload, "icon") ?? "notice",
                Buttons = payload.TryGetProperty("buttons", out var buttons) && buttons.ValueKind == JsonValueKind.Array
                    ? [.. buttons.EnumerateArray().Take(4).Select(button => new GameNativeUiButton(Text(button, "id") ?? "", Text(button, "label") ?? ""))] : [],
                AnnouncementId = Text(payload, "announcement_id"), ChatStyle = Text(payload, "chat_style") ?? "system",
                ChatCaption = Text(payload, "chat_caption"),
                Target = payload.TryGetProperty("target", out var target) && target.ValueKind != JsonValueKind.Null
                    ? target.Deserialize<GameNativeUiControlTarget>(NativeJson) : null,
                Definition = payload.TryGetProperty("definition", out var definition) && definition.ValueKind != JsonValueKind.Null
                    ? definition.Deserialize<GameNativeUiControlDefinition>(NativeJson) : null,
                AfterSequence = payload.TryGetProperty("after_sequence", out var cursor) ? cursor.GetInt64() : null,
                MaximumLength = payload.TryGetProperty("maximum_length", out var maximumLength) ? maximumLength.GetInt32() : null,
                Runs = payload.TryGetProperty("runs", out var runs) && runs.ValueKind == JsonValueKind.Array
                    ? [.. runs.EnumerateArray().Take(32).Select(run => new GameNativeUiTextRun(Text(run, "text") ?? "", Text(run, "color") ?? ""))] : [],
            };
            request.Validate();
            if (await TryNativeAsync(operationId, request, connectionToken).ConfigureAwait(false)) return;
            if (fallback && request.Kind is "messageBox" or "notification" && _overlay is not null)
                await ShowOverlayAsync(operationId, request, connectionToken).ConfigureAwait(false);
            else Queue(operationId, "unsupported", "", null, connectionToken);
        }
        catch (OperationCanceledException) when (connectionToken.IsCancellationRequested || _stop.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            if (operationId is not null) Queue(operationId, "outcomeUnknown", "native", null, connectionToken);
        }
        catch (NotSupportedException)
        {
            if (operationId is not null) Queue(operationId, "unsupported", "", null, connectionToken);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or TimeoutException or KeyNotFoundException or JsonException or FormatException or OverflowException)
        {
            if (operationId is not null) Queue(operationId, "failed", "", null, connectionToken);
        }
    }

    private async Task<bool> TryNativeAsync(string operationId, GameNativeUiRequest request, CancellationToken connectionToken)
    {
        if (_native is null) return false;
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, connectionToken);
        GameNativeUiResult submitted;
        var invoking = false;
        try
        {
            var capabilities = await _native.GetCapabilitiesAsync(lifetime.Token).ConfigureAwait(false);
            if (!capabilities.Operations.Any(operation => operation.Kind == request.Kind && operation.Supported && operation.Ready)) return false;
            invoking = true;
            submitted = await _native.InvokeAsync(request, lifetime.Token).ConfigureAwait(false);
            if (!GameNativeUiRequest.IsControlOperation(request.Kind) && NotShown.Contains(submitted.Status)) return false;
        }
        // An interrupted submission may already have changed a control; report uncertainty instead of unavailability.
        catch (Exception error) when (invoking && GameNativeUiRequest.IsControlOperation(request.Kind)
            && error is IOException or InvalidOperationException or TimeoutException)
        {
            QueueControl(operationId, new(request.OperationId, "outcomeUnknown", Reason: "channelInterrupted"), connectionToken);
            return true;
        }
        // Nothing was shown yet, so a broken hook channel falls back instead of failing the request.
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException) { return false; }
        if (GameNativeUiRequest.IsControlOperation(request.Kind))
        {
            // Queue admission is not proof that the game-thread mutation ran; never replay it as a presentation.
            var outcome = submitted;
            try
            {
                for (var attempt = 0; outcome.Status == "queued" && attempt < 40; attempt++)
                {
                    await Task.Delay(250, lifetime.Token).ConfigureAwait(false);
                    outcome = await _native.GetResultAsync(request.OperationId, lifetime.Token).ConfigureAwait(false);
                }
                if (outcome.Status == "queued") outcome = new(request.OperationId, "outcomeUnknown", Reason: "resultTimeout");
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException)
            { outcome = new(request.OperationId, "outcomeUnknown", Reason: "channelInterrupted"); }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
            { outcome = new(request.OperationId, "outcomeUnknown", Reason: "channelInterrupted"); }
            QueueControl(operationId, outcome, connectionToken);
            return true;
        }
        if (request.Kind != "messageBox")
        {
            // A notification, banner or chat line is done once the game took it.
            Queue(operationId, "shown", "native", null, connectionToken);
            return true;
        }
        Queue(operationId, "shown", "native", null, connectionToken);
        // The native modal is the game's own; its outcome is read without presenting it again.
        try
        {
            for (var waited = TimeSpan.Zero; waited < TimeSpan.FromMinutes(30); waited += TimeSpan.FromMilliseconds(500))
            {
                await Task.Delay(500, lifetime.Token).ConfigureAwait(false);
                var outcome = await _native.GetResultAsync(request.OperationId, lifetime.Token).ConfigureAwait(false);
                if (!Settled.Contains(outcome.Status)) continue;
                Queue(operationId, outcome.Status, "native", outcome.Status == "selected" ? outcome.ButtonId : null, connectionToken);
                return true;
            }
        }
        // The box is already on screen; showing an overlay copy would ask twice, so the answer is unknown.
        catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException) { }
        Queue(operationId, "outcomeUnknown", "native", null, connectionToken);
        return true;
    }

    private async Task ShowOverlayAsync(string operationId, GameNativeUiRequest request, CancellationToken connectionToken)
    {
        if (Interlocked.Increment(ref _overlayDialogs) > 4)
        {
            Interlocked.Decrement(ref _overlayDialogs);
            Queue(operationId, "busy", "", null, connectionToken);
            return;
        }
        IOverlayPanel? panel = null;
        try
        {
            await _overlay!.WaitUntilReadyAsync(connectionToken).ConfigureAwait(false);
            var buttons = request.Kind == "messageBox" ? request.Buttons : [new GameNativeUiButton("ok", "OK")];
            var done = new TaskCompletionSource<(string Status, string? Button)>(TaskCreationOptions.RunContinuationsAsynchronously);
            panel = _overlay.CreateGameUiPanel(_namespace + "." + Guid.NewGuid().ToString("N")[..8], interactive: true, modal: true);
            panel.MessageReceived += (_, args) =>
            {
                if (args.EventName == "game-ui.mounted") Queue(operationId, "shown", "overlay", null, connectionToken);
                else if (args.EventName == "game-ui.action")
                {
                    var name = args.Data.TryGetProperty("name", out var value) ? value.GetString() : null;
                    done.TrySetResult(buttons.Any(button => button.Id == name) ? ("selected", name) : ("closed", null));
                }
                else if (args.EventName is "game-ui.close" or "game-ui.error")
                    done.TrySetResult((args.EventName == "game-ui.error" ? "failed" : "closed", null));
            };
            panel.Send("game-ui.message-box", new
            {
                title = request.Kind == "messageBox" ? request.Title : "", text = request.Text,
                buttons = buttons.Select(button => new { name = button.Id, label = button.Label }).ToArray(),
            });
            using var registration = connectionToken.Register(() => done.TrySetCanceled());
            var (status, button) = await done.Task.ConfigureAwait(false);
            // A notification has no answer to report beyond being shown.
            if (request.Kind == "messageBox") Queue(operationId, status, "overlay", button, connectionToken);
        }
        finally
        {
            panel?.Dispose();
            Interlocked.Decrement(ref _overlayDialogs);
        }
    }

    private void Queue(string operationId, string status, string presenter, string? button, CancellationToken epoch)
    {
        if (epoch.IsCancellationRequested || _stop.IsCancellationRequested) return;
        if (!_results.Writer.TryWrite(new { operation_id = operationId, status, presenter, button_id = button })) DeliveryFailed = true;
    }

    private void QueueControl(string operationId, GameNativeUiResult result, CancellationToken epoch)
    {
        if (epoch.IsCancellationRequested || _stop.IsCancellationRequested) return;
        if (!_results.Writer.TryWrite(new
        {
            operation_id = operationId, status = result.Status, presenter = "native", button_id = (string?)null, reason = result.Reason,
            controls = result.Controls, events = result.Events, cursor = result.Cursor,
        })) DeliveryFailed = true;
    }

    private async Task SendResultsAsync()
    {
        try
        {
            await foreach (var result in _results.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                var outcome = await _channel.SendAsync(ResultType, 1, result, _stop.Token).ConfigureAwait(false);
                if (outcome.Status != CompanionOperationStatus.Accepted) DeliveryFailed = true;
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException) { DeliveryFailed = true; }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static readonly JsonSerializerOptions NativeJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Stops the subscription and result delivery; native modals already shown stay the game's.</summary>
    public async ValueTask DisposeAsync()
    {
        _subscription.Dispose();
        _stop.Cancel();
        _results.Writer.TryComplete();
        try { await _sender.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        _stop.Dispose();
    }
}
