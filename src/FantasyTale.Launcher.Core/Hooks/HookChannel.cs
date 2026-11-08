using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using Microsoft.Win32.SafeHandles;

namespace FantasyTale.Launcher.Core.Hooks;

/// <summary>A local, process-authenticated channel with one bounded request in flight and no credential-bearing commands.</summary>
public sealed partial class HookChannel : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _closed = new();
    private readonly SemaphoreSlim _request = new(1, 1);
    private readonly Task _connection;
    private int _sequence;
    private int _disposed;
    private int _clientPid;
    private readonly OverlayBrokerChannel? _overlay;
    private readonly CompanionBrokerChannel? _companion;

    internal HookChannel(string launchId, bool enableOverlayBroker = false, bool enableCompanionBroker = false)
    {
        _pipe = new NamedPipeServerStream("FantasyTale.Hook." + launchId, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance, 65536, 65536);
        _connection = _pipe.WaitForConnectionAsync(_closed.Token);
        try
        {
            if (enableOverlayBroker) _overlay = new OverlayBrokerChannel(launchId);
            if (enableCompanionBroker) _companion = new CompanionBrokerChannel(launchId);
        }
        catch
        {
            _closed.Cancel(); _pipe.Dispose(); _overlay?.Dispose();
            _ = _connection.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            throw;
        }
    }

    /// <summary>Gets the optional broker pipe name written to the native session.</summary>
    internal string? OverlayPipeName => _overlay?.PipeName;

    /// <summary>Installs the launcher-side handler for native overlay requests.</summary>
    public void SetOverlayHandler(Func<OverlayBrokerRequest, OverlayBrokerResponse>? handler) => _overlay?.SetHandler(handler);

    internal void BindProcess(int processId) { _clientPid = processId; _companion?.BindProcess(processId); }

    /// <summary>Installs an optional owner-restricted companion handler, independently of the Overlay renderer.</summary>
    public void SetCompanionHandler(Func<CompanionBrokerRequest, CancellationToken, ValueTask<CompanionBrokerResponse>>? handler) =>
        _companion?.SetHandler(handler);

    /// <summary>Binds the optional overlay broker to the launched game process.</summary>
    public void BindOverlayProcess(int processId) => _overlay?.BindProcess(processId);

    /// <summary>Sends ping, status or an explicitly authorized stop using framed UTF-8 TOML. Invalid peers close the channel.</summary>
    public async Task<string> RequestAsync(string method, CancellationToken cancellationToken = default)
    {
        if (method is not ("ping" or "status" or "stop")) throw new ArgumentException("Unsupported hook command.", nameof(method));
        return await ExchangeAsync(method, null, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Invokes an owner-qualified local plugin service; absent providers do not invalidate the control channel.</summary>
    public Task<string> InvokeServiceAsync(string service, string payload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(service) || service.Length > 192 || service.Contains('\0'))
            throw new ArgumentException("Invalid native service name.", nameof(service));
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Contains('\0')) throw new ArgumentException("Native service data cannot contain NUL.", nameof(payload));
        return ExchangeAsync("service", service, payload, cancellationToken);
    }

    private async Task<string> ExchangeAsync(string method, string? service, string? payload, CancellationToken cancellationToken)
    {
        var extra = service is null ? "" : $"service = {SessionWriter.Quote(service)}\npayload = {SessionWriter.Quote(payload!)}\n";
        // Validate frame size before taking the channel; user input cannot break a healthy stream.
        if (Encoding.UTF8.GetByteCount(extra) > 65000) throw new ArgumentException("Native service request exceeds the control frame budget.");
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!await _request.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw new InvalidOperationException("A hook request is already in progress.");
        string body;
        bool pending;
        string result;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await _connection.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows() || !GetNamedPipeClientProcessId(_pipe.SafePipeHandle, out var pid) || pid != _clientPid)
                throw new IOException("The hook pipe is connected to a different process.");
            var id = checked(++_sequence);
            var message = Encoding.UTF8.GetBytes(FormattableString.Invariant($"protocol = 1\nrequest_id = {id}\nmethod = \"{method}\"\n{extra}"));
            var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, message.Length);
            await _pipe.WriteAsync(header, deadline.Token).ConfigureAwait(false);
            await _pipe.WriteAsync(message, deadline.Token).ConfigureAwait(false);
            await _pipe.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length is < 1 or > 65536) throw new IOException("Invalid hook response size.");
            var buffer = new byte[length];
            await _pipe.ReadExactlyAsync(buffer, deadline.Token).ConfigureAwait(false);
            var reply = TomlReader.Parse("hook-control", new UTF8Encoding(false, true).GetString(buffer));
            reply.OnlyKeys("protocol", "request_id", "result", "body");
            if (reply.Integer("protocol", true) != 1 || reply.Integer("request_id", true) != id) throw new IOException("Mismatched hook response.");
            result = reply.String("result", true);
            body = reply.String("body", true);
            pending = method == "status" && result == "not-ready" && body.Length == 0;
            if (result != "ok" && !pending && !(method == "service" && result is "service-unavailable" or "service-failed"))
                throw new IOException("The hook returned an invalid command result.");
        }
        catch { Dispose(); throw; }
        finally { _request.Release(); }
        // A complete status response before initialization does not invalidate the framed stream.
        if (pending) throw new InvalidOperationException("The hook status is not ready yet. Retry after initialization.");
        if (result == "service-unavailable") throw new NotSupportedException("The native plugin service is unavailable.");
        if (result == "service-failed") throw new InvalidOperationException("The native plugin service could not complete the request; its outcome may be unknown.");
        return body;
    }

    /// <summary>Closes the pipe; it never ends the client process or unloads native plugins.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _closed.Cancel(); _pipe.Dispose(); _overlay?.Dispose(); _companion?.Dispose();
        // Observe cancellation even when no control request was made during this launch.
        _ = _connection.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out int processId);
}
