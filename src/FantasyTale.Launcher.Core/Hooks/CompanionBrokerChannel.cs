using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;

namespace FantasyTale.Launcher.Core.Hooks;

/// <summary>A credential-free operation from a runtime-supplied native plugin owner.</summary>
public sealed record CompanionBrokerRequest(string Owner, string Method, string Type = "", int Version = 1,
    string? JsonData = null, int TimeoutMilliseconds = 0, string? OperationId = null);

/// <summary>A local broker result; successful operations carry a structured channel outcome as JSON.</summary>
public sealed record CompanionBrokerResponse(string Result, string? Data = null);

// Uses the same bounded framing as Overlay, with an independent pipe and no renderer dependency.
internal sealed class CompanionBrokerChannel : IDisposable
{
    private readonly NativeBrokerServer _server;
    private readonly TaskCompletionSource<Func<CompanionBrokerRequest, CancellationToken, ValueTask<CompanionBrokerResponse>>?> _handler =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal CompanionBrokerChannel(string launchId) =>
        _server = new("FantasyTale.CompanionBroker." + launchId, HandleAsync);

    internal string PipeName => _server.PipeName;
    internal Task Completion => _server.Completion;
    internal void BindProcess(int processId) => _server.BindProcess(processId);
    internal void SetHandler(Func<CompanionBrokerRequest, CancellationToken, ValueTask<CompanionBrokerResponse>>? handler) => _handler.TrySetResult(handler);

    private async ValueTask<string> HandleAsync(string raw, CancellationToken cancellationToken)
    {
        var id = 0;
        try
        {
            var root = TomlReader.Parse("companion-broker", raw);
            root.OnlyKeys("protocol", "request_id", "owner", "method", "type", "version", "data", "timeout_ms", "operation_id");
            if (root.Integer("protocol", true, 1, 1) != 1) return Reply(id, new("invalid"));
            id = (int)root.Integer("request_id", true, 1, int.MaxValue)!.Value;
            var owner = root.String("owner", true);
            PluginCatalog.ValidateId("companion-broker", "owner", owner);
            var method = root.String("method", true);
            if (method is not ("state" or "subscribe" or "unsubscribe" or "send" or "request" or "poll")) return Reply(id, new("invalid"));
            var type = root.String("type");
            if (method is "subscribe" or "unsubscribe" or "send" or "request")
            {
                PluginCatalog.ValidateId("companion-broker", "type", type);
                if (!char.IsAsciiLetterLower(type[0])) return Reply(id, new("invalid"));
            }
            var version = (int)(root.Integer("version", false, 1, 65535) ?? 1);
            var timeout = (int)(root.Integer("timeout_ms", false, 0, 300000) ?? 0);
            var operation = root.String("operation_id");
            if (operation.Length > 128 || operation.Any(char.IsControl) || timeout is > 0 and < 100) return Reply(id, new("invalid"));
            var data = root.String("data");
            if (data.Length > 65536 || Encoding.UTF8.GetByteCount(data) > 65536) return Reply(id, new("invalid"));
            if (data.Length > 0)
            {
                using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 16 });
            }
            var handler = await _handler.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            if (handler is null) return Reply(id, new("not-found"));
            var reply = await handler(new(owner, method, type, version, data.Length == 0 ? null : data,
                timeout, operation.Length == 0 ? null : operation), cancellationToken).ConfigureAwait(false);
            return Reply(id, reply);
        }
        catch (Exception error) when (error is DefinitionException or ArgumentException or InvalidOperationException or JsonException or TimeoutException)
        {
            return Reply(id, new("invalid"));
        }
    }

    private static string Reply(int id, CompanionBrokerResponse response)
    {
        if (response.Data is { } data && Encoding.UTF8.GetByteCount(data) > 128 * 1024)
            response = new("failed");
        return $"protocol = 1\nrequest_id = {id}\nresult = {SessionWriter.Quote(response.Result)}\n"
            + (response.Data is null ? "" : "data = " + SessionWriter.Quote(response.Data) + "\n");
    }

    public void Dispose() { _handler.TrySetResult(null); _server.Dispose(); }
}
