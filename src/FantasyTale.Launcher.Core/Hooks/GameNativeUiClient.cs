using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Hooks;

/// <summary>Connects typed native UI operations to one process-authenticated plugin service without fallback or replay.</summary>
public sealed class GameNativeUiClient(HookChannel channel, string build, string? ownerScope = null) : IGameNativeUi, IGameNativeUiScopes
{
    private const string Service = "game.native-ui";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Kinds = ["messageBox", "notification", "announcement.show", "announcement.close", "chat",
        "control.inspect", "control.setText", "control.addButton", "control.remove", "control.events", "control.activate", "control.setEnabled"];
    private readonly string? _ownerScope = ValidateScope(ownerScope);
    private int _scopesSupported = -1;

    /// <summary>Creates a transport whose native results, control handles and button events belong to the supplied trusted owner.</summary>
    public IGameNativeUi ForOwner(string owner)
    {
        GameNativeUiRequest.ValidateIdentity(owner);
        return new GameNativeUiClient(channel, build, _ownerScope is null ? owner : _ownerScope + "." + owner);
    }

    private static string? ValidateScope(string? scope)
    {
        if (scope is not null) GameNativeUiRequest.ValidateIdentity(scope);
        return scope;
    }

    /// <inheritdoc />
    public async ValueTask<GameNativeUiCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var capabilities = await ExchangeAsync<GameNativeUiCapabilities>("action = \"capabilities\"\n", cancellationToken, scoped: false).ConfigureAwait(false);
            if (capabilities.Build != build || capabilities.Operations is null)
                throw new InvalidDataException("The native UI provider returned capabilities for another build or omitted its operations.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operation in capabilities.Operations)
                if (operation is null || !Kinds.Contains(operation.Kind) || !seen.Add(operation.Kind)
                    || operation.Features is null || operation.Features.Any(string.IsNullOrWhiteSpace) || operation.Ready && !operation.Supported)
                    throw new InvalidDataException("The native UI provider returned invalid or conflicting capabilities.");
            _scopesSupported = capabilities.Operations.Any(operation => GameNativeUiRequest.IsControlOperation(operation.Kind)) ? 1 : 0;
            return capabilities;
        }
        catch (HookChannelBusyException) { throw new GameNativeUiBusyException(); }
        catch (NotSupportedException)
        {
            _scopesSupported = 0;
            return new(build, Kinds
                .Select(kind => new GameNativeUiCapability(kind, false, false, [], "providerUnavailable")).ToArray());
        }
    }

    /// <inheritdoc />
    public async ValueTask<GameNativeUiResult> InvokeAsync(GameNativeUiRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        var text = new StringBuilder("action = \"invoke\"\n");
        void Line(string key, string value) => text.Append(key).Append(" = ").Append(SessionWriter.Quote(value)).Append('\n');
        Line("operation_id", request.OperationId); Line("kind", request.Kind);
        if (!GameNativeUiRequest.IsControlOperation(request.Kind) || request.Kind is "control.setText" or "control.addButton")
            Line("text", request.Text);
        if (!GameNativeUiRequest.IsControlOperation(request.Kind))
        {
            Line("title", request.Title); Line("icon", request.Icon); Line("chat_style", request.ChatStyle);
        }
        if (request.ChatCaption is not null) Line("chat_caption", request.ChatCaption);
        if (request.AnnouncementId is not null) Line("announcement_id", request.AnnouncementId);
        if (request.AfterSequence is { } cursor) text.Append("after_sequence = ").Append(cursor.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        if (request.MaximumLength is { } length) text.Append("maximum_length = ").Append(length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        if (request.Enabled is { } enabled) text.Append("enabled = ").Append(enabled ? "true" : "false").Append('\n');
        foreach (var button in request.Buttons) { text.Append("[[buttons]]\n"); Line("id", button.Id); Line("label", button.Label); }
        foreach (var run in request.Runs) { text.Append("[[runs]]\n"); Line("text", run.Text); Line("color", run.Color); }
        if (request.Target is { } target)
        {
            text.Append("[target]\n");
            if (target.Form is not null) Line("form", target.Form);
            if (target.Name is not null) Line("name", target.Name);
            if (target.ArrayIndex is { } arrayIndex) text.Append("array_index = ").Append(arrayIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            if (target.Text is not null) Line("text", target.Text);
            if (target.Id is not null) Line("id", target.Id);
        }
        if (request.Definition is { } definition)
        {
            text.Append("[definition]\n");
            Line("name", definition.Name);
            if (definition.Template is not null) Line("template", definition.Template);
            foreach (var pair in new[] { ("x", definition.X), ("y", definition.Y), ("width", definition.Width), ("height", definition.Height) })
                text.Append(pair.Item1).Append(" = ").Append(pair.Item2.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }
        try
        {
            var result = await ExchangeAsync<GameNativeUiResult>(text.ToString(), cancellationToken).ConfigureAwait(false);
            ValidateResult(result);
            if (result.OperationId != request.OperationId || result.ButtonId is not null && !request.Buttons.Any(button => button.Id == result.ButtonId))
                throw new InvalidDataException("The native UI provider returned a mismatched operation or button identity.");
            return result;
        }
        catch (Exception error) when (error is HookChannelBusyException or GameNativeUiBusyException)
        { return new(request.OperationId, "busy", Reason: "channelBusy"); }
        catch (NotSupportedException) { return new(request.OperationId, "unsupported", Reason: "providerUnavailable"); }
        catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or JsonException or InvalidOperationException)
        { return new(request.OperationId, "outcomeUnknown", Reason: "channelInterrupted"); }
    }

    /// <inheritdoc />
    public async ValueTask<GameNativeUiResult> GetResultAsync(string operationId, CancellationToken cancellationToken = default)
    {
        GameNativeUiRequest.ValidateIdentity(operationId);
        try
        {
            var result = await ExchangeAsync<GameNativeUiResult>($"action = \"result\"\noperation_id = {SessionWriter.Quote(operationId)}\n", cancellationToken).ConfigureAwait(false);
            ValidateResult(result);
            if (result.OperationId != operationId) throw new InvalidDataException("The native UI provider returned another operation's result.");
            return result;
        }
        catch (HookChannelBusyException) { throw new GameNativeUiBusyException(); }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<GameNativeUiForm>> GetFormsAsync(string resource, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        if (!resource.EndsWith(".ui", StringComparison.OrdinalIgnoreCase) || resource.Contains("..", StringComparison.Ordinal)
            || resource.Contains(':', StringComparison.Ordinal) || resource.StartsWith('/') || resource.Contains('\0'))
            throw new ArgumentException("A form resource is a relative .ui path.", nameof(resource));
        FormsReply reply;
        try
        {
            reply = await ExchangeAsync<FormsReply>("action = \"forms\"\nresource = " + SessionWriter.Quote(resource) + "\n",
                cancellationToken, scoped: false).ConfigureAwait(false);
        }
        catch (HookChannelBusyException) { throw new GameNativeUiBusyException(); }
        if (reply.Status == "unsupported") throw new NotSupportedException("The native UI provider does not track forms in this build.");
        if (reply.Status != "completed" || reply.Forms is null
            || reply.Forms.Any(form => form is null || form.Resource is null || form.Sequence <= 0 || form.Width < 0 || form.Height < 0
                || form.RootWidth < 0 || form.RootHeight < 0))
            throw new InvalidDataException("The native UI provider returned an invalid form read.");
        return reply.Forms;
    }

    private sealed record FormsReply(string Status, GameNativeUiForm[]? Forms);

    private static void ValidateResult(GameNativeUiResult result)
    {
        if (result.Status is not ("queued" or "shown" or "selected" or "dismissed" or "closed" or "inserted"
            or "unsupported" or "notReady" or "busy" or "failed" or "outcomeUnknown" or "completed" or "ambiguous"))
            throw new InvalidDataException("The native UI provider returned an unknown result status.");
        if (result.Status == "selected" ? string.IsNullOrWhiteSpace(result.ButtonId) : result.ButtonId is not null)
            throw new InvalidDataException("Only a selected native UI result must include a button identity.");
        if (result.Controls is null || result.Events is null || result.Cursor < 0)
            throw new InvalidDataException("The native UI provider returned invalid control metadata.");
        var controls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var control in result.Controls)
        {
            if (control is null || string.IsNullOrEmpty(control.Id) || !controls.Add(control.Id)
                || control.Form is null || control.Name is null || control.Text is null || control.Text.Contains('\0')
                || control.Kind is not ("form" or "button" or "edit" or "label" or "control")
                || control.ArrayIndex < 0 || control.MaximumLength < 0 || control.Width < 0 || control.Height < 0
                || (control.SelectionValue is null) != (control.Selected is null))
                throw new InvalidDataException("The native UI provider returned an invalid control snapshot.");
            GameNativeUiRequest.ValidateIdentity(control.Id);
        }
        long sequence = 0;
        foreach (var controlEvent in result.Events)
        {
            if (controlEvent is null || controlEvent.Sequence <= sequence || result.Cursor is null || controlEvent.Sequence > result.Cursor
                || controlEvent.Event != "click" || string.IsNullOrEmpty(controlEvent.ControlId))
                throw new InvalidDataException("The native UI provider returned invalid or unordered control events.");
            GameNativeUiRequest.ValidateIdentity(controlEvent.ControlId);
            sequence = controlEvent.Sequence;
        }
    }

    private async Task<T> ExchangeAsync<T>(string payload, CancellationToken cancellationToken, bool scoped = true)
    {
        if (scoped && _ownerScope is not null)
        {
            if (_scopesSupported < 0) await GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
            if (_scopesSupported == 1) payload = "owner_scope = " + SessionWriter.Quote(_ownerScope) + "\n" + payload;
        }
        var response = await channel.InvokeServiceAsync(Service, payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(response, Json) ?? throw new InvalidDataException("The native UI provider returned an empty response.");
    }
}
