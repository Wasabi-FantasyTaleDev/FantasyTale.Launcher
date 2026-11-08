using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;

namespace FantasyTale.Launcher.Core.Hooks;

/// <summary>A bounded request received from a native plugin through the optional overlay broker.</summary>
public sealed record OverlayBrokerRequest(string Owner, string Method, string PanelId, string? Html, string? FileName,
    string? EventName, string? Data);

/// <summary>The result returned to a native overlay request.</summary>
public sealed record OverlayBrokerResponse(string Result, string? Error = null, string? Data = null)
{
    /// <summary>Indicates a successful broker operation.</summary>
    public bool IsSuccess => Result == "ok";
}

/// <summary>Serves the x86 native overlay ABI over a launch-bound, authenticated named pipe.</summary>
internal sealed class OverlayBrokerChannel : IDisposable
{
    private const int Protocol = 1;
    private readonly NativeBrokerServer _server;
    private readonly TaskCompletionSource<Func<OverlayBrokerRequest, OverlayBrokerResponse>?> _handler =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal OverlayBrokerChannel(string launchId) =>
        _server = new("FantasyTale.OverlayBroker." + launchId, (text, token) => HandleAsync(text, token));

    internal string PipeName => _server.PipeName;
    internal Task Completion => _server.Completion;
    internal void BindProcess(int processId) => _server.BindProcess(processId);
    internal void SetHandler(Func<OverlayBrokerRequest, OverlayBrokerResponse>? handler) => _handler.TrySetResult(handler);

    private async ValueTask<string> HandleAsync(string raw, CancellationToken cancellationToken)
    {
        try
        {
            var root = TomlReader.Parse("overlay-broker", raw);
            root.OnlyKeys("protocol", "request_id", "method", "owner", "panel_id", "html", "file_name", "event", "data");
            if (root.Integer("protocol", true, Protocol, Protocol) != Protocol) return Reply(root, "invalid", "unsupported protocol");
            var id = checked((int)root.Integer("request_id", true, 1, int.MaxValue)!.Value);
            var method = root.String("method", true);
            var owner = root.String("owner", true);
            var panel = root.String("panel_id", true);
            if (owner.Length is < 1 or > 128 || panel.Length is < 1 or > 96) return Reply(id, "invalid", "invalid panel owner or id");
            if (method is not ("register" or "unregister" or "show" or "hide" or "send" or "poll")) return Reply(id, "invalid", "unsupported method");
            var request = new OverlayBrokerRequest(owner, method, panel, root.String("html"), root.String("file_name"),
                root.String("event"), root.String("data"));
            var handler = await _handler.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);
            if (handler is null) return Reply(id, "not-found", "overlay is unavailable");
            var result = handler(request);
            return Reply(id, result.Result, result.Error, result.Data);
        }
        catch (Exception error) when (error is DefinitionException or TimeoutException or InvalidOperationException)
        {
            return Reply(0, "invalid", error.Message);
        }
    }

    private static string Reply(TomlReader root, string result, string? error) =>
        Reply((int)(root.Integer("request_id") ?? 0), result, error);

    private static string Reply(int id, string result, string? error, string? data = null) =>
        $"protocol = {Protocol}\nrequest_id = {id}\nresult = {Quote(result)}\nerror = {Quote(error ?? string.Empty)}\n"
        + (data is null ? "" : $"data = {Quote(data)}\n");

    private static string Quote(string value)
    {
        var text = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            if (c == '"' || c == '\\') text.Append('\\').Append(c);
            else if (c == '\n') text.Append("\\n");
            else if (c == '\r') text.Append("\\r");
            else if (c == '\t') text.Append("\\t");
            else if (char.IsControl(c)) text.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
            else text.Append(c);
        }
        return text.Append('"').ToString();
    }

    public void Dispose() { _handler.TrySetResult(null); _server.Dispose(); }
}