using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FantasyTale.Launcher.Core.Hooks;

// Shared framing and process authentication; application protocols never receive pipe handles.
internal sealed class NativeBrokerServer : IDisposable
{
    private const int MaximumConnections = 64;
    private const int MaximumFrameBytes = 256 * 1024;
    private readonly object _gate = new();
    private readonly HashSet<NamedPipeServerStream> _pipes = [];
    private readonly HashSet<Task> _clients = [];
    private readonly SemaphoreSlim _slots = new(MaximumConnections, MaximumConnections);
    private readonly CancellationTokenSource _closed = new();
    private readonly TaskCompletionSource<int> _bound = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<string, CancellationToken, ValueTask<string>> _handle;
    private int _disposed;

    internal NativeBrokerServer(string pipeName, Func<string, CancellationToken, ValueTask<string>> handle)
    {
        PipeName = pipeName; _handle = handle;
        var first = CreatePipe(first: true);
        Completion = Task.Run(() => AcceptAsync(first));
    }

    internal string PipeName { get; }
    internal Task Completion { get; }

    internal void BindProcess(int processId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_bound.TrySetResult(processId) && _bound.Task.GetAwaiter().GetResult() != processId)
            throw new InvalidOperationException("The native broker is already bound to a different process.");
    }

    private NamedPipeServerStream CreatePipe(bool first = false)
    {
        var options = PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
        if (first) options |= PipeOptions.FirstPipeInstance;
        var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, MaximumConnections,
            PipeTransmissionMode.Byte, options, 65536, 65536);
        lock (_gate)
        {
            if (_disposed != 0) { pipe.Dispose(); throw new ObjectDisposedException(nameof(NativeBrokerServer)); }
            _pipes.Add(pipe);
        }
        return pipe;
    }

    private async Task AcceptAsync(NamedPipeServerStream first)
    {
        NamedPipeServerStream? listener = first;
        try
        {
            while (!_closed.IsCancellationRequested)
            {
                await _slots.WaitAsync(_closed.Token).ConfigureAwait(false);
                var transferred = false;
                try
                {
                    listener ??= CreatePipe();
                    await listener.WaitForConnectionAsync(_closed.Token).ConfigureAwait(false);
                    var pipe = listener;
                    var client = Task.Run(() => ServeTrackedAsync(pipe));
                    lock (_gate) _clients.Add(client);
                    _ = client.ContinueWith(completed =>
                    {
                        _ = completed.Exception;
                        lock (_gate) _clients.Remove(completed);
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    transferred = true;
                    listener = null;
                }
                finally { if (!transferred) _slots.Release(); }
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            _closed.Cancel();
            NamedPipeServerStream[] pipes;
            Task[] clients;
            lock (_gate) { pipes = _pipes.ToArray(); clients = _clients.ToArray(); }
            foreach (var pipe in pipes) pipe.Dispose();
            try { await Task.WhenAll(clients).ConfigureAwait(false); }
            finally { _slots.Dispose(); }
        }
    }

    private async Task ServeTrackedAsync(NamedPipeServerStream pipe)
    {
        try
        {
            // A client may connect during Process.Start, before the caller can bind the returned PID.
            var expected = await _bound.Task.WaitAsync(TimeSpan.FromSeconds(10), _closed.Token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) || pid != expected)
                throw new IOException("The native broker was connected by a different process.");
            while (!_closed.IsCancellationRequested)
            {
                var request = await ReadAsync(pipe, _closed.Token).ConfigureAwait(false);
                var response = await _handle(request, _closed.Token).ConfigureAwait(false);
                await WriteAsync(pipe, response, _closed.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ArgumentException
            or ObjectDisposedException or TimeoutException or InvalidOperationException) { }
        finally
        {
            pipe.Dispose();
            lock (_gate) _pipes.Remove(pipe);
            _slots.Release();
        }
    }

    private static async Task<string> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaximumFrameBytes) throw new IOException("Invalid native broker frame size.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static async Task WriteAsync(Stream stream, string text, CancellationToken cancellationToken)
    {
        var bytes = new UTF8Encoding(false, true).GetBytes(text);
        if (bytes.Length is < 1 or > MaximumFrameBytes) throw new IOException("Native broker response is too large.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _closed.Cancel();
        NamedPipeServerStream[] pipes;
        lock (_gate) pipes = _pipes.ToArray();
        foreach (var pipe in pipes) pipe.Dispose();
        _ = Completion.ContinueWith(t => { _ = t.Exception; _closed.Dispose(); }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out int processId);
}
