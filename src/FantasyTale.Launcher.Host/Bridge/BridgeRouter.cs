using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Servers;

namespace FantasyTale.Launcher.Host.Bridge;

/// <summary>
/// Request/response messaging between the web frontend and the host. The frontend posts
/// <c>{"id": n, "method": "games.list", "params": {...}}</c>; the host answers with
/// <c>{"id": n, "result": ...}</c> or <c>{"id": n, "error": "..."}</c>. The host may also push
/// <c>{"event": "name", "data": ...}</c> messages. Methods are the only way the frontend reaches
/// the file system, processes or the network.
/// </summary>
public sealed class BridgeRouter
{
    private readonly Dictionary<string, Func<JsonNode?, CancellationToken, Task<object?>>> _methods = new(StringComparer.Ordinal);
    private readonly Action<string> _send;
    private readonly Action<string, string>? _trace;
    private readonly HashSet<string> _windowMethods = new(StringComparer.Ordinal);
    private readonly Channel<byte> _pending = Channel.CreateBounded<byte>(64);
    private readonly Channel<byte> _windowPending = Channel.CreateBounded<byte>(16);
    private readonly Channel<byte> _workers = Channel.CreateBounded<byte>(8);

    /// <summary>Creates a reply router with an optional diagnostic sink receiving only registered method names and outcome codes.</summary>
    public BridgeRouter(Action<string> send, Action<string, string>? trace = null) { _send = send; _trace = trace; }

    /// <summary>Serializer settings shared by every message.</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>Registers a method. Names are <c>area.action</c>, for example <c>window.minimize</c>.</summary>
    /// <exception cref="InvalidOperationException">The name is already registered.</exception>
    public void Add(string method, Func<JsonNode?, CancellationToken, Task<object?>> handler)
    {
        if (!_methods.TryAdd(method, handler)) throw new InvalidOperationException($"Bridge method '{method}' is registered twice.");
    }

    /// <summary>Registers a synchronous method.</summary>
    public void Add(string method, Func<JsonNode?, object?> handler) => Add(method, (p, _) => Task.FromResult(handler(p)));

    /// <summary>Registers a window operation that must remain available while backend requests wait for I/O. The handler marshals native calls to the window thread.</summary>
    public void AddWindow(string method, Func<JsonNode?, CancellationToken, Task<object?>> handler)
    {
        Add(method, handler);
        _windowMethods.Add(method);
    }

    /// <summary>Registers a synchronous window operation with capacity independent of backend work.</summary>
    public void AddWindow(string method, Func<JsonNode?, object?> handler) => AddWindow(method, (p, _) => Task.FromResult(handler(p)));

    /// <summary>Schedules handlers and their diagnostic I/O outside the window thread. Excess requests receive a retryable error instead of growing an unbounded queue.</summary>
    public Task DispatchAsync(string message, CancellationToken cancellationToken)
    {
        long? id = null;
        var windowOperation = false;
        try
        {
            var node = JsonNode.Parse(message);
            id = node?["id"]?.GetValue<long>();
            windowOperation = node?["method"]?.GetValue<string>() is { } method && _windowMethods.Contains(method);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            // The regular router supplies the same malformed-request response on a worker.
        }
        var pending = windowOperation ? _windowPending : _pending;
        if (!pending.Writer.TryWrite(0))
        {
            if (id is not null) _send(JsonSerializer.Serialize(new { id, error = "The launcher is busy. Try this operation again.", code = "bridge_busy" }, Json));
            return Task.CompletedTask;
        }
        return Task.Run(async () =>
        {
            var entered = false;
            try
            {
                if (!windowOperation)
                {
                    try { await _workers.Writer.WriteAsync(0, cancellationToken).ConfigureAwait(false); entered = true; }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                }
                await HandleAsync(message, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (entered) _workers.Reader.TryRead(out _);
                pending.Reader.TryRead(out _);
            }
        }, CancellationToken.None);
    }

    /// <summary>Pushes an event to the frontend.</summary>
    public void Emit(string name, object? data) =>
        _send(JsonSerializer.Serialize(new { @event = name, data }, Json));

    /// <summary>Handles one raw message from the frontend. Never throws; failures become error replies.</summary>
    public async Task HandleAsync(string message, CancellationToken cancellationToken)
    {
        long? id = null;
        string? registeredMethod = null;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var node = JsonNode.Parse(message) ?? throw new FormatException("empty message");
            id = node["id"]?.GetValue<long>();
            var method = node["method"]?.GetValue<string>() ?? throw new FormatException("missing method");
            if (!_methods.TryGetValue(method, out var handler)) throw new InvalidOperationException($"unknown method '{method}'");
            registeredMethod = method;
            Record(method, "requested");
            cancellationToken.ThrowIfCancellationRequested();
            var result = await handler(node["params"], cancellationToken).ConfigureAwait(false);
            Record(method, "completed");
            if (id is not null) _send(JsonSerializer.Serialize(new { id, result }, Json));
        }
#pragma warning disable CA1031 // A bridge failure must reach the frontend as an error reply, whatever its type.
        catch (Exception e)
#pragma warning restore CA1031
        {
            if (registeredMethod is not null)
                Record(registeredMethod, e.Message == "The launcher is shutting down." ? "failed-shutting-down" : "failed-" + e.GetType().Name);
            if (id is not null)
            {
                if (e is FantasyTale.Launcher.Core.Instances.InstanceBusyException busy)
                    _send(JsonSerializer.Serialize(new { id, error = busy.Message, code = "instance_in_use",
                        instanceFailure = new { operation = registeredMethod, busy.InstanceId, busy.ClientPath, busy.LockPath,
                            osError = busy.HResult & 0xffff, kind = busy.InstanceId is null ? "registration-lease" : "client-lease" } }, Json));
                else if (e is LauncherUpgradeRequiredException required)
                    _send(JsonSerializer.Serialize(new { id, error = e.Message, code = "launcher_update_required",
                        minimumLauncherVersion = required.MinimumVersion, currentLauncherVersion = required.CurrentVersion, subject = required.Subject }, Json));
                else if (e is FantasyTale.Launcher.Sdk.GameResourceConflictException conflict)
                    _send(JsonSerializer.Serialize(new { id, error = e.Message, code = "resource_conflict", conflictReport = conflict.Report }, Json));
                else if (e is ServerFailureException failure)
                    _send(JsonSerializer.Serialize(new { id, error = e.Message, code = failure.Code,
                        httpStatus = (int?)failure.StatusCode, failure.TransportCode, failure.Reason,
                        failure.MaintenanceStart, failure.MaintenanceEnd, failure.MaintenanceDescription }, Json));
                else _send(JsonSerializer.Serialize(new { id, error = e.Message }, Json));
            }
        }
        finally
        {
            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (registeredMethod is not null && elapsed >= 250)
                Record(registeredMethod, "elapsed-" + ((long)elapsed).ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");
        }
    }

    private void Record(string method, string outcome)
    {
        try { _trace?.Invoke(method, outcome); }
#pragma warning disable CA1031 // Optional diagnostics must not prevent a window operation or its error reply.
        catch (Exception error)
#pragma warning restore CA1031
        { System.Diagnostics.Trace.WriteLine("Bridge recording unavailable: " + error.GetType().Name); }
    }
}
