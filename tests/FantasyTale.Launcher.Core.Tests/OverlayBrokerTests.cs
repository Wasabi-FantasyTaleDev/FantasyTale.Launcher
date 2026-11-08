using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class OverlayBrokerTests
{
    [Fact]
    public async Task IndependentNativeOwnersCanKeepConcurrentConnectionsAndReconnect()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var broker = CreateBroker();
        broker.BindProcess(Environment.ProcessId);
        broker.SetHandler(request => new("ok", Data: request.Owner + ":" + request.EventName));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var first = await ConnectAsync(broker, timeout.Token);
        using var second = await ConnectAsync(broker, timeout.Token);
        async Task ExchangeAsync(Stream pipe, string owner)
        {
            for (var id = 1; id <= 4; id++)
            {
                var reply = await RequestAsync(pipe, owner, id, timeout.Token);
                Assert.Equal(id, reply.Integer("request_id"));
                Assert.Equal("ok", reply.String("result"));
                Assert.Equal(owner + ":update", reply.String("data"));
            }
        }
        await Task.WhenAll(ExchangeAsync(first, "first.plugin"), ExchangeAsync(second, "second.plugin"));
        first.Dispose();
        using var replacement = await ConnectAsync(broker, timeout.Token);
        await Task.WhenAll(ExchangeAsync(replacement, "replacement.plugin"), ExchangeAsync(second, "second.plugin"));
        broker.Dispose();
        await broker.Completion.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task MalformedConnectionDoesNotDisableHealthyOrFutureConnections()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var broker = CreateBroker();
        broker.BindProcess(Environment.ProcessId);
        broker.SetHandler(_ => new("ok"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var healthy = await ConnectAsync(broker, timeout.Token);
        using var malformed = await ConnectAsync(broker, timeout.Token);
        await WriteAsync(malformed, [0xc0, 0xaf], timeout.Token);
        Assert.Equal(0, await malformed.ReadAsync(new byte[1], timeout.Token));
        Assert.Equal("ok", (await RequestAsync(healthy, "healthy.plugin", 1, timeout.Token)).String("result"));
        using var later = await ConnectAsync(broker, timeout.Token);
        Assert.Equal("ok", (await RequestAsync(later, "later.plugin", 1, timeout.Token)).String("result"));
        broker.Dispose();
        await broker.Completion.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task EarlyRequestWaitsForProcessBindingAndWrongProcessIsRejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var broker = CreateBroker();
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        broker.SetHandler(_ => { invoked.TrySetResult(); return new("ok"); });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var early = await ConnectAsync(broker, timeout.Token);
        var reply = RequestAsync(early, "early.plugin", 1, timeout.Token);
        // The server must not dispatch before it knows which process owns this launch.
        Assert.NotSame(invoked.Task, await Task.WhenAny(invoked.Task, Task.Delay(100, timeout.Token)));
        broker.BindProcess(Environment.ProcessId);
        Assert.Equal("ok", (await reply).String("result"));
        Assert.True(invoked.Task.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => broker.BindProcess(int.MaxValue));
        broker.Dispose();
        await broker.Completion.WaitAsync(timeout.Token);

        using var other = CreateBroker();
        other.BindProcess(int.MaxValue);
        other.SetHandler(_ => throw new InvalidOperationException("A different process must never dispatch."));
        using var rejected = await ConnectAsync(other, timeout.Token);
        Assert.Equal(0, await rejected.ReadAsync(new byte[1], timeout.Token));
        other.Dispose();
        await other.Completion.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task ConnectionBudgetRecoversAfterAnOwnerDisconnects()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var broker = CreateBroker();
        broker.BindProcess(Environment.ProcessId);
        broker.SetHandler(_ => new("ok"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var clients = new List<NamedPipeClientStream>();
        try
        {
            for (var i = 0; i < 64; i++)
            {
                var client = await ConnectAsync(broker, timeout.Token);
                clients.Add(client);
                Assert.Equal("ok", (await RequestAsync(client, "bounded.plugin", i + 1, timeout.Token)).String("result"));
            }
            using var atCapacity = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            atCapacity.CancelAfter(100);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConnectAsync(broker, atCapacity.Token));
            clients[0].Dispose();
            using var replacement = await ConnectAsync(broker, timeout.Token);
            Assert.Equal("ok", (await RequestAsync(replacement, "replacement.plugin", 1, timeout.Token)).String("result"));
            broker.Dispose();
            await broker.Completion.WaitAsync(timeout.Token);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }

    private static OverlayBrokerChannel CreateBroker() => new(Guid.NewGuid().ToString("N"));

    private static async Task<NamedPipeClientStream> ConnectAsync(OverlayBrokerChannel broker, CancellationToken cancellationToken)
    {
        var client = new NamedPipeClientStream(".", broker.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await client.ConnectAsync(cancellationToken); return client; }
        catch { client.Dispose(); throw; }
    }

    private static async Task<TomlReader> RequestAsync(Stream pipe, string owner, int id, CancellationToken cancellationToken)
    {
        var request = $"protocol = 1\nrequest_id = {id}\nmethod = \"send\"\nowner = \"{owner}\"\npanel_id = \"status\"\nevent = \"update\"\ndata = \"{{}}\"\n";
        await WriteAsync(pipe, Encoding.UTF8.GetBytes(request), cancellationToken);
        var header = new byte[4];
        await pipe.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        Assert.InRange(length, 1, 256 * 1024);
        var bytes = new byte[length];
        await pipe.ReadExactlyAsync(bytes, cancellationToken);
        return TomlReader.Parse("test-overlay-broker", Encoding.UTF8.GetString(bytes));
    }

    private static async Task WriteAsync(Stream pipe, byte[] bytes, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await pipe.WriteAsync(header, cancellationToken);
        await pipe.WriteAsync(bytes, cancellationToken);
        await pipe.FlushAsync(cancellationToken);
    }
}
