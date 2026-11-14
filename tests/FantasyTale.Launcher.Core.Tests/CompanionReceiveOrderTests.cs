using System.Net.WebSockets;
using System.Text.Json;
using FantasyTale.Launcher.Communication;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class CompanionReceiveOrderTests
{
    private static readonly IReadOnlyDictionary<string, int> Grants = new Dictionary<string, int>
    { ["game.feature.changed"] = 1, ["game.feature.get"] = 1 };

    [Theory]
    [InlineData("response", CompanionOperationStatus.Completed)]
    [InlineData("error", CompanionOperationStatus.Rejected)]
    public async Task ResponseAndDelayedEventRetainWireOrderWhenCallbacksArriveInReverse(string kind, CompanionOperationStatus status)
    {
        await using var pair = await CompanionTests.SocketPair.CreateAsync();
        await using var connection = await CompanionTests.ConnectAsync(pair, types: Grants, epoch: "ordered-epoch");
        using var channel = connection.CreateChannel("fixture.controller", Grants);
        var held = new TaskCompletionSource<CompanionEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<CompanionEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = channel.Subscribe("game.feature.changed", 1, async (message, token) =>
        {
            if (message.MessageId == "hold-dispatch")
            {
                held.TrySetResult(message);
                await release.Task.WaitAsync(token);
            }
            else delayed.TrySetResult(message);
        });
        try
        {
            await SendAsync(pair.Server, Event("hold-dispatch", "ordered-epoch"));
            var first = await held.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var request = channel.RequestAsync("game.feature.get", 1, new { }).AsTask();
            var outbound = await ReceiveAsync(pair.Server);
            Assert.Equal("request", outbound.Kind);
            await SendAsync(pair.Server, Event("older-event", "ordered-epoch"));
            await SendAsync(pair.Server, new(kind, "newer-response", outbound.Type, outbound.Version,
                JsonSerializer.SerializeToElement(new { state = "current" }), outbound.Id, "ordered-epoch"));

            var response = await request.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(status, response.Status);
            Assert.False(delayed.Task.IsCompleted);
            Assert.Equal("ordered-epoch", response.ConnectionEpoch);
            Assert.Equal(3, response.ReceiveSequence);

            release.TrySetResult();
            var earlier = await delayed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("ordered-epoch", earlier.ConnectionEpoch);
            Assert.Equal(1, first.ReceiveSequence);
            Assert.Equal(2, earlier.ReceiveSequence);
            Assert.True(earlier.ReceiveSequence < response.ReceiveSequence);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task EventHandlerCanAwaitARequestWithoutBlockingItsResponse()
    {
        await using var pair = await CompanionTests.SocketPair.CreateAsync();
        await using var connection = await CompanionTests.ConnectAsync(pair, types: Grants, epoch: "request-epoch");
        using var channel = connection.CreateChannel("fixture.controller", Grants);
        var completed = new TaskCompletionSource<(CompanionEvent Event, CompanionOperationResult Response)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = channel.Subscribe("game.feature.changed", 1, async (message, token) =>
        {
            var response = await channel.RequestAsync("game.feature.get", 1, new { }, cancellationToken: token);
            completed.TrySetResult((message, response));
        });
        await SendAsync(pair.Server, Event("request-from-handler", "request-epoch"));
        var request = await ReceiveAsync(pair.Server);
        Assert.Equal("request", request.Kind);
        await SendAsync(pair.Server, new("response", "handler-response", request.Type, request.Version,
            JsonSerializer.SerializeToElement(new { ready = true }), request.Id, "request-epoch"));

        var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CompanionOperationStatus.Completed, result.Response.Status);
        Assert.Equal("request-epoch", result.Event.ConnectionEpoch);
        Assert.Equal(result.Event.ConnectionEpoch, result.Response.ConnectionEpoch);
        Assert.Equal(1, result.Event.ReceiveSequence);
        Assert.Equal(2, result.Response.ReceiveSequence);
    }

    [Fact]
    public async Task LocalOutcomesDoNotInventReceivePositions()
    {
        await using var pair = await CompanionTests.SocketPair.CreateAsync();
        await using var connection = await CompanionTests.ConnectAsync(pair, "pending_game", types: Grants, epoch: "pending-epoch");
        using var channel = connection.CreateChannel("fixture.controller", Grants);
        var result = await channel.RequestAsync("game.feature.get", 1, new { });
        Assert.Equal(CompanionOperationStatus.NotBound, result.Status);
        Assert.Null(result.ConnectionEpoch);
        Assert.Equal(0, result.ReceiveSequence);
    }

    private static CompanionEnvelope Event(string id, string epoch) => new("event", id, "game.feature.changed", 1,
        JsonSerializer.SerializeToElement(new { value = id }), Epoch: epoch);

    private static async Task SendAsync(WebSocket socket, CompanionEnvelope envelope)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await socket.SendAsync(CompanionWire.Encode(envelope, new()), WebSocketMessageType.Text, true, timeout.Token);
    }

    private static async Task<CompanionEnvelope> ReceiveAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await CompanionWire.ReceiveAsync(socket, new(), timeout.Token) ?? throw new IOException("Unexpected fixture close.");
    }
}
