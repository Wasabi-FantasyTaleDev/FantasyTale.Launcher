using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Hooks;

/// <summary>Connects typed native UI operations to one process-authenticated plugin service without fallback or replay.</summary>
public sealed class GameNativeUiClient(HookChannel channel, string build) : IGameNativeUi
{
    private const string Service = "game.native-ui";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Kinds = ["messageBox", "notification", "announcement.show", "announcement.close", "chat"];

    /// <inheritdoc />
    public async ValueTask<GameNativeUiCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var capabilities = await ExchangeAsync<GameNativeUiCapabilities>("action = \"capabilities\"\n", cancellationToken).ConfigureAwait(false);
            if (capabilities.Build != build || capabilities.Operations is null)
                throw new InvalidDataException("The native UI provider returned capabilities for another build or omitted its operations.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operation in capabilities.Operations)
                if (operation is null || !Kinds.Contains(operation.Kind) || !seen.Add(operation.Kind)
                    || operation.Features is null || operation.Features.Any(string.IsNullOrWhiteSpace) || operation.Ready && !operation.Supported)
                    throw new InvalidDataException("The native UI provider returned invalid or conflicting capabilities.");
            return capabilities;
        }
        catch (NotSupportedException)
        {
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
        Line("operation_id", request.OperationId); Line("kind", request.Kind); Line("text", request.Text);
        Line("title", request.Title); Line("icon", request.Icon); Line("chat_style", request.ChatStyle);
        if (request.ChatCaption is not null) Line("chat_caption", request.ChatCaption);
        if (request.AnnouncementId is not null) Line("announcement_id", request.AnnouncementId);
        foreach (var button in request.Buttons) { text.Append("[[buttons]]\n"); Line("id", button.Id); Line("label", button.Label); }
        foreach (var run in request.Runs) { text.Append("[[runs]]\n"); Line("text", run.Text); Line("color", run.Color); }
        try
        {
            var result = await ExchangeAsync<GameNativeUiResult>(text.ToString(), cancellationToken).ConfigureAwait(false);
            ValidateResult(result);
            if (result.OperationId != request.OperationId || result.ButtonId is not null && !request.Buttons.Any(button => button.Id == result.ButtonId))
                throw new InvalidDataException("The native UI provider returned a mismatched operation or button identity.");
            return result;
        }
        catch (NotSupportedException) { return new(request.OperationId, "unsupported", Reason: "providerUnavailable"); }
        catch (Exception error) when (error is IOException or InvalidDataException or OperationCanceledException or JsonException or InvalidOperationException)
        { return new(request.OperationId, "outcomeUnknown", Reason: "channelInterrupted"); }
    }

    /// <inheritdoc />
    public async ValueTask<GameNativeUiResult> GetResultAsync(string operationId, CancellationToken cancellationToken = default)
    {
        GameNativeUiRequest.ValidateIdentity(operationId);
        var result = await ExchangeAsync<GameNativeUiResult>($"action = \"result\"\noperation_id = {SessionWriter.Quote(operationId)}\n", cancellationToken).ConfigureAwait(false);
        ValidateResult(result);
        if (result.OperationId != operationId) throw new InvalidDataException("The native UI provider returned another operation's result.");
        return result;
    }

    private static void ValidateResult(GameNativeUiResult result)
    {
        if (result.Status is not ("queued" or "shown" or "selected" or "dismissed" or "closed" or "inserted"
            or "unsupported" or "notReady" or "busy" or "failed" or "outcomeUnknown"))
            throw new InvalidDataException("The native UI provider returned an unknown result status.");
        if (result.Status == "selected" ? string.IsNullOrWhiteSpace(result.ButtonId) : result.ButtonId is not null)
            throw new InvalidDataException("Only a selected native UI result must include a button identity.");
    }

    private async Task<T> ExchangeAsync<T>(string payload, CancellationToken cancellationToken)
    {
        var response = await channel.InvokeServiceAsync(Service, payload, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(response, Json) ?? throw new InvalidDataException("The native UI provider returned an empty response.");
    }
}
