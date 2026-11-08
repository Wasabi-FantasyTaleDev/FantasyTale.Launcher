using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using FantasyTale.Launcher.Core.Hooks;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class HookChannelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposingTheChannelCancelsPendingConnectionOrReply(bool connected)
    {
        if (!OperatingSystem.IsWindows()) return;
        var id = Guid.NewGuid().ToString("N");
        using var channel = new HookChannel(id);
        channel.BindProcess(Environment.ProcessId);
        using var peer = new NamedPipeClientStream(".", "FantasyTale.Hook." + id, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        if (connected) await peer.ConnectAsync(timeout.Token);
        var request = channel.RequestAsync("ping", timeout.Token);
        if (connected)
        {
            var header = new byte[4];
            await peer.ReadExactlyAsync(header, timeout.Token);
            var payload = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
            await peer.ReadExactlyAsync(payload, timeout.Token);
            Assert.Contains("method = \"ping\"", Encoding.UTF8.GetString(payload), StringComparison.Ordinal);
        }
        channel.Dispose();
        var error = await Record.ExceptionAsync(() => request.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(error is OperationCanceledException or ObjectDisposedException or IOException, error?.ToString());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => channel.RequestAsync("ping", timeout.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingStatusKeepsTheChannelButMismatchedResponsesCloseIt(bool mismatch)
    {
        if (!OperatingSystem.IsWindows()) return;
        var id = Guid.NewGuid().ToString("N");
        using var channel = new HookChannel(id);
        channel.BindProcess(Environment.ProcessId);
        using var peer = new NamedPipeClientStream(".", "FantasyTale.Hook." + id, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await peer.ConnectAsync(timeout.Token);
        async Task Reply(string result, string body, int sequence)
        {
            var header = new byte[4];
            await peer.ReadExactlyAsync(header, timeout.Token);
            var request = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
            await peer.ReadExactlyAsync(request, timeout.Token);
            Assert.Contains($"request_id = {sequence}", Encoding.UTF8.GetString(request), StringComparison.Ordinal);
            var bytes = Encoding.UTF8.GetBytes($"protocol = 1\nrequest_id = {(mismatch ? sequence + 1 : sequence)}\nresult = \"{result}\"\nbody = \"{body}\"\n");
            BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
            await peer.WriteAsync(header, timeout.Token);
            await peer.WriteAsync(bytes, timeout.Token);
        }
        var pending = channel.RequestAsync("status", timeout.Token);
        await Reply("not-ready", "", 1);
        if (mismatch)
        {
            await Assert.ThrowsAsync<IOException>(() => pending);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => channel.RequestAsync("ping", timeout.Token));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
            var ping = channel.RequestAsync("ping", timeout.Token);
            await Reply("ok", "pong", 2);
            Assert.Equal("pong", await ping);
            var stop = channel.RequestAsync("stop", timeout.Token);
            await Reply("ok", "stopping", 3);
            Assert.Equal("stopping", await stop);
        }
    }
}
