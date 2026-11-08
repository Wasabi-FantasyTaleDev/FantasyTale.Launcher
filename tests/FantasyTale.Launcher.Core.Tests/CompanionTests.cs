using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Communication;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class CompanionTests
{
    private static readonly CompanionLimits Limits = new() { HeartbeatInterval = TimeSpan.FromSeconds(30) };
    private static readonly Dictionary<string, int> Types = new(StringComparer.Ordinal) { ["ui.notification"] = 1 };
    private static readonly JsonSerializerOptions UnicodeOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public async Task MixedUnicodeSurvivesByteSizedWebSocketFragments()
    {
        await using var pair = await SocketPair.CreateAsync();
        const string text = "中文 한국어 Bahasa Indonesia English \U00020000 e\u0301";
        var envelope = Message("event", "notice", new { text });
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            kind = envelope.Kind, id = envelope.Id, type = envelope.Type, version = envelope.Version,
            payload = new { text }, epoch = "epoch-1",
        }, UnicodeOptions));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var receive = CompanionWire.ReceiveAsync(pair.Client, Limits, timeout.Token);
        for (var i = 0; i < bytes.Length; i++)
            await pair.Server.SendAsync(bytes.AsMemory(i, 1), WebSocketMessageType.Text, i == bytes.Length - 1, timeout.Token);
        var decoded = await receive;
        Assert.Equal(text, decoded!.Payload.GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("{\"kind\":\"event\",\"kind\":\"request\"}")]
    [InlineData("{\"kind\":\"event\",\"id\":42}")]
    [InlineData("{\"kind\":\"invalid\",\"id\":\"secret-value\"}")]
    public void InvalidWireNeverEchoesRemoteContent(string text)
    {
        var error = Assert.ThrowsAny<IOException>(() => CompanionWire.Decode(Encoding.UTF8.GetBytes(text), Limits));
        Assert.StartsWith("companion.", error.Message);
        Assert.DoesNotContain("secret-value", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void OutboundPayloadAndEnvelopeHaveStrictByteBudgets()
    {
        var limits = Limits with { MessageBytes = 256 };
        Assert.Throws<IOException>(() => CompanionWire.EncodePayload(new { text = new string('中', 300) }, limits));
        Assert.Throws<IOException>(() => CompanionWire.Encode(Message("event", "a", new { text = new string('a', 255) }), limits));
        Assert.Throws<IOException>(() => CompanionWire.Decode([0xc0, 0xaf], Limits));
    }

    [Fact]
    public async Task OversizedFragmentedMessageFailsBeforeFullBuffering()
    {
        await using var pair = await SocketPair.CreateAsync();
        var limits = Limits with { MessageBytes = 256 };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var receive = CompanionWire.ReceiveAsync(pair.Client, limits, timeout.Token);
        await pair.Server.SendAsync(new byte[200], WebSocketMessageType.Text, false, timeout.Token);
        await pair.Server.SendAsync(new byte[100], WebSocketMessageType.Text, false, timeout.Token);
        var error = await Assert.ThrowsAnyAsync<IOException>(() => receive);
        Assert.Equal("companion.message-too-large", error.Message);
    }

    [Fact]
    public async Task PluginCallbackCanRequestAndReceiveResponseWithoutBlockingReader()
    {
        await using var pair = await SocketPair.CreateAsync();
        await using var connection = await ConnectAsync(pair);
        using var plugin = connection.CreateChannel("test.plugin", Types);
        var completed = new TaskCompletionSource<CompanionOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = plugin.Subscribe("ui.notification", 1, async (_, token) =>
            completed.TrySetResult(await plugin.RequestAsync("ui.notification", 1, new { selected = "ok" }, cancellationToken: token)));
        await SendAsync(pair.Server, Message("event", "notification", new { text = "提示" }));
        var request = await ReceiveAsync(pair.Server);
        Assert.Equal("request", request.Kind);
        await SendAsync(pair.Server, Message("response", "reply", new { acknowledged = true }, request.Id));
        var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CompanionOperationStatus.Completed, result.Status);
        Assert.True(Assert.IsType<JsonElement>(result.Response).GetProperty("acknowledged").GetBoolean());
        Assert.Throws<ArgumentException>(() => plugin.Subscribe("forbidden", 1, _ => ValueTask.CompletedTask));
        Assert.Throws<ArgumentException>(() => connection.CreateChannel("other.plugin", new Dictionary<string, int> { ["ui.notification"] = 2 }));
    }

    [Fact]
    public async Task PendingGameRejectsBusinessMessagesUntilServerBindsLaunch()
    {
        await using var pair = await SocketPair.CreateAsync();
        await using var connection = await ConnectAsync(pair, "pending_game");
        using var plugin = connection.CreateChannel("test.plugin", Types);
        Assert.Equal(CompanionOperationStatus.NotBound, (await plugin.SendAsync("ui.notification", 1)).Status);
        await SendAsync(pair.Server, new("state", "bound", "companion.state", 1,
            JsonSerializer.SerializeToElement(new { state = "connected" }), Epoch: "epoch-1"));
        await WaitAsync(() => connection.State == CompanionChannelState.Connected);
        Assert.Equal(CompanionOperationStatus.Accepted, (await plugin.SendAsync("ui.notification", 1, new { text = "已绑定" })).Status);
        Assert.Equal("已绑定", (await ReceiveAsync(pair.Server)).Payload.GetProperty("text").GetString());
    }

    [Fact]
    public async Task DisconnectAfterRequestSendIsExplicitlyUncertain()
    {
        await using var pair = await SocketPair.CreateAsync();
        await using var connection = await ConnectAsync(pair);
        using var plugin = connection.CreateChannel("test.plugin", Types);
        var result = plugin.RequestAsync("ui.notification", 1, new { action = "apply" }).AsTask();
        Assert.Equal("request", (await ReceiveAsync(pair.Server)).Kind);
        pair.Server.Abort();
        Assert.Equal(CompanionOperationStatus.OutcomeUnknown, (await result.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        await WaitAsync(() => connection.State == CompanionChannelState.Closed);
        Assert.Equal(CompanionOperationStatus.Unavailable, (await plugin.SendAsync("ui.notification", 1)).Status);
    }

    [Fact]
    public async Task OwnerDisposalCancelsCallbackAndDoesNotRemoveOtherOwners()
    {
        await using var pair = await SocketPair.CreateAsync();
        await using var connection = await ConnectAsync(pair);
        using var first = connection.CreateChannel("1.first.plugin", Types);
        using var second = connection.CreateChannel("second.plugin", Types);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var a = first.Subscribe("ui.notification", 1, async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        using var b = second.Subscribe("ui.notification", 1, _ => { delivered.TrySetResult(); return ValueTask.CompletedTask; });
        await SendAsync(pair.Server, Message("event", "event-a", new { text = "test" }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        first.Dispose();
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CompanionChannelState.Closed, first.State);
        Assert.Equal(CompanionChannelState.Connected, second.State);
        Assert.False(first.HandlerFailed);
    }

    [Fact]
    public async Task SlowConsumerClosesInsteadOfGrowingQueue()
    {
        await using var pair = await SocketPair.CreateAsync();
        await using var connection = await ConnectAsync(pair, limits: Limits with { QueueMessages = 1 });
        using var plugin = connection.CreateChannel("slow.plugin", Types);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = plugin.Subscribe("ui.notification", 1, async (_, token) =>
        {
            entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        await SendAsync(pair.Server, Message("event", "first", null));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await SendAsync(pair.Server, Message("event", "second", null));
        await SendAsync(pair.Server, Message("event", "third", null));
        await WaitAsync(() => connection.State == CompanionChannelState.Closed);
        Assert.Equal("companion.slow-consumer", connection.FailureCode);
    }

    [Fact]
    public async Task OldEpochCannotDeliverEvents()
    {
        await using var pair = await SocketPair.CreateAsync();
        await using var connection = await ConnectAsync(pair);
        await SendAsync(pair.Server, Message("event", "stale", null) with { Epoch = "old-epoch" });
        await WaitAsync(() => connection.State == CompanionChannelState.Closed);
        Assert.Equal("companion.stale-epoch", connection.FailureCode);
    }

    [Fact]
    public async Task HeartbeatTimeoutEndsEpochWithoutBusyPolling()
    {
        await using var pair = await SocketPair.CreateAsync();
        await using var connection = await ConnectAsync(pair, limits: Limits with
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(20), HeartbeatTimeout = TimeSpan.FromMilliseconds(80),
        });
        Assert.Equal("heartbeat", (await ReceiveAsync(pair.Server)).Kind);
        await WaitAsync(() => connection.State == CompanionChannelState.Closed);
        Assert.Equal("companion.heartbeat-ended", connection.FailureCode);
    }

    [Fact]
    public void GrantsRejectCredentialInUrlAndNeverPrintTicket()
    {
        const string secret = "private-ticket";
        Assert.Throws<ArgumentException>(() => new CompanionAuthorization(new Uri("ws://localhost/channel"), secret));
        Assert.Throws<ArgumentException>(() => new CompanionAuthorization(new Uri("wss://example.test/channel?ticket=bad"), secret));
        Assert.DoesNotContain(secret, new CompanionAuthorization(new Uri("wss://example.test/channel"), secret).ToString());
        // Plain ws only for a server whose launcher API is plain http, and never with credentials in the URL.
        _ = new CompanionAuthorization(new Uri("ws://localhost/channel"), secret, allowPlainText: true);
        Assert.Throws<ArgumentException>(() => new CompanionAuthorization(new Uri("ws://localhost/channel?ticket=bad"), secret, allowPlainText: true));
        Assert.Throws<ArgumentException>(() => new CompanionAuthorization(new Uri("http://localhost/channel"), secret, allowPlainText: true));
    }

    [Fact]
    public async Task ReconnectUsesFreshGrantRestoresSubscriptionsAndDoesNotReplayRequests()
    {
        await using var firstPair = await SocketPair.CreateAsync();
        await using var secondPair = await SocketPair.CreateAsync();
        var attempts = 0;
        var grants = 0;
        await using var session = new CompanionSession(new("testgame", "kr-812", "launch-1", "zh-CN", Types),
            _ => ValueTask.FromResult<CompanionAuthorization?>(new(new Uri("wss://example.test/channel"), "test-grant-" + Interlocked.Increment(ref grants))),
            Limits, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(30),
            (grant, _, _, _) =>
            {
                var attempt = Interlocked.Increment(ref attempts);
                Assert.Equal("test-grant-" + attempt, grant.Ticket);
                return ConnectAsync(attempt == 1 ? firstPair : secondPair);
            });
        using var plugin = session.CreateChannel("1.test.plugin", Types);
        var calls = 0;
        using var subscription = plugin.Subscribe("ui.notification", 1, _ => { Interlocked.Increment(ref calls); return ValueTask.CompletedTask; });
        _ = session.Start();
        await WaitAsync(() => plugin.State == CompanionChannelState.Connected);
        await SendAsync(firstPair.Server, Message("event", "before", null));
        await WaitAsync(() => calls == 1);
        var oldRequest = plugin.RequestAsync("ui.notification", 1, new { action = "old" }).AsTask();
        Assert.Equal("request", (await ReceiveAsync(firstPair.Server)).Kind);
        firstPair.Server.Abort();
        Assert.Equal(CompanionOperationStatus.OutcomeUnknown, (await oldRequest.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        await WaitAsync(() => attempts == 2 && plugin.State == CompanionChannelState.Connected);
        await SendAsync(secondPair.Server, Message("event", "after", null));
        await WaitAsync(() => calls == 2);
        await plugin.SendAsync("ui.notification", 1, new { action = "new" });
        var outgoing = await ReceiveAsync(secondPair.Server);
        Assert.Equal("event", outgoing.Kind);
        Assert.Equal("new", outgoing.Payload.GetProperty("action").GetString());
        Assert.Equal(2, grants);
    }

    [Fact]
    public async Task MissingCapabilityOrAuthorizationRejectionDoesNotRetryForever()
    {
        var identity = new CompanionIdentity("testgame", "kr-812", "launch-1", "zh-CN", Types);
        var attempts = 0;
        await using (var unavailable = new CompanionSession(identity, _ => ValueTask.FromResult<CompanionAuthorization?>(null)))
        {
            await unavailable.Start().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(CompanionChannelState.Unavailable, unavailable.State);
        }
        await using var rejected = new CompanionSession(identity,
            _ => ValueTask.FromResult<CompanionAuthorization?>(new(new Uri("wss://example.test/channel"), "test-only")),
            Limits, null, null, (_, _, _, _) =>
            {
                Interlocked.Increment(ref attempts);
                throw new CompanionConnectionException("companion.unauthorized");
            });
        await rejected.Start().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, attempts);
        Assert.Equal(CompanionChannelState.Unavailable, rejected.State);
    }

    [Fact]
    public async Task DisposeRetainsOwnershipUntilUncooperativeCallbackActuallyEnds()
    {
        await using var pair = await SocketPair.CreateAsync();
        var connection = await ConnectAsync(pair, limits: Limits with { CloseTimeout = TimeSpan.FromMilliseconds(30) });
        using var plugin = connection.CreateChannel("test.plugin", Types);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = plugin.Subscribe("ui.notification", 1, async _ => { entered.TrySetResult(); await release.Task; });
        await SendAsync(pair.Server, Message("event", "test", null));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var error = await Assert.ThrowsAsync<CompanionConnectionException>(() => connection.DisposeAsync().AsTask());
            Assert.Equal("companion.shutdown-pending", error.Message);
            Assert.False(connection.Completion.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await connection.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await connection.DisposeAsync();
    }

    private static CompanionEnvelope Message(string kind, string id, object? payload, string? replyTo = null) =>
        new(kind, id, "ui.notification", 1, JsonSerializer.SerializeToElement(payload), replyTo, "epoch-1");

    internal static async Task<CompanionConnection> ConnectAsync(SocketPair pair, string state = "connected", CompanionLimits? limits = null,
        IReadOnlyDictionary<string, int>? types = null)
    {
        limits ??= Limits;
        types ??= Types;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connecting = CompanionConnection.NegotiateAsync(pair.Client,
            new("testgame", "kr-812", "launch-1", "zh-CN", types), limits, timeout.Token);
        Assert.Equal("hello", (await ReceiveAsync(pair.Server)).Kind);
        await SendAsync(pair.Server, new("welcome", "welcome-1", "companion.welcome", 1,
            JsonSerializer.SerializeToElement(new { protocol = 1, build = "kr-812", launch_id = "launch-1", state, capabilities = types }), Epoch: "epoch-1"));
        return await connecting;
    }

    private static async Task SendAsync(WebSocket socket, CompanionEnvelope message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.SendAsync(CompanionWire.Encode(message, Limits), WebSocketMessageType.Text, true, timeout.Token);
    }

    private static async Task<CompanionEnvelope> ReceiveAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await CompanionWire.ReceiveAsync(socket, Limits, timeout.Token) ?? throw new IOException("Unexpected close.");
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    internal sealed class SocketPair(TcpClient client, TcpClient server) : IAsyncDisposable
    {
        internal WebSocket Client { get; } = WebSocket.CreateFromStream(client.GetStream(), false, CompanionWire.Subprotocol, Timeout.InfiniteTimeSpan);
        internal WebSocket Server { get; } = WebSocket.CreateFromStream(server.GetStream(), true, CompanionWire.Subprotocol, Timeout.InfiniteTimeSpan);

        internal static async Task<SocketPair> CreateAsync()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            return new(client, await listener.AcceptTcpClientAsync());
        }

        public ValueTask DisposeAsync()
        {
            Client.Abort(); Server.Abort(); Client.Dispose(); Server.Dispose(); client.Dispose(); server.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
