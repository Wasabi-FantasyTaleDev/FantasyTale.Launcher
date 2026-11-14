using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class GameNativeUiTests
{
    [Fact]
    public async Task HookContentionRejectsBeforeSubmissionAndKeepsTheStreamHealthy()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var held = peer.Channel.RequestAsync("ping");
        var first = await peer.ReadRequestAsync();
        Assert.Equal("ping", first.String("method"));
        await Assert.ThrowsAsync<HookChannelBusyException>(() => peer.Channel.RequestAsync("status"));
        await Assert.ThrowsAsync<HookChannelBusyException>(() => peer.Channel.InvokeServiceAsync("game.native-ui", "action = \"capabilities\"\n"));
        await peer.RespondAsync(first, "ok", "pong");
        Assert.Equal("pong", await held);

        var after = peer.Channel.RequestAsync("ping");
        var second = await peer.ReadRequestAsync();
        Assert.Equal(2, second.Integer("request_id"));
        Assert.Equal("ping", second.String("method"));
        await peer.RespondAsync(second, "ok", "pong");
        Assert.Equal("pong", await after);
        Assert.Equal(2, peer.Requests);
    }

    [Fact]
    public async Task BusyCapabilityQueriesDoNotCacheUnsupportedAndMutationsAreNotSubmitted()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var held = peer.Channel.RequestAsync("status");
        var first = await peer.ReadRequestAsync();
        await Assert.ThrowsAsync<GameNativeUiBusyException>(() => peer.Client.GetCapabilitiesAsync().AsTask());
        var request = new GameNativeUiRequest("test.admission", "control.setText", "Changed")
        { Target = new() { Id = "control-1" } };
        var rejected = await peer.Client.InvokeAsync(request);
        Assert.Equal("busy", rejected.Status);
        Assert.Equal("channelBusy", rejected.Reason);
        Assert.Equal(request.OperationId, rejected.OperationId);
        await peer.RespondAsync(first, "ok", "status");
        Assert.Equal("status", await held);

        var capabilities = peer.Client.GetCapabilitiesAsync().AsTask();
        var body = await peer.ReplyAsync("ok", AdmissionCapabilities);
        Assert.Equal("capabilities", TomlReader.Parse("native-ui", body).String("action"));
        Assert.True(Assert.Single((await capabilities).Operations).Supported);

        var admitted = peer.Client.InvokeAsync(request).AsTask();
        body = await peer.ReplyAsync("ok", "{\"operationId\":\"test.admission\",\"status\":\"completed\"}");
        Assert.Equal("invoke", TomlReader.Parse("native-ui", body).String("action"));
        Assert.Equal("completed", (await admitted).Status);
        Assert.Equal(3, peer.Requests);
    }

    [Fact]
    public async Task ScopedCapabilityDiscoveryContentionDoesNotSubmitAnUnscopedMutation()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var native = peer.Client.ForOwner("fixture.owner");
        var held = peer.Channel.RequestAsync("ping");
        var first = await peer.ReadRequestAsync();
        var request = new GameNativeUiRequest("test.scoped", "control.setText", "Changed")
        { Target = new() { Id = "control-1" } };
        var rejected = await native.InvokeAsync(request);
        Assert.Equal("busy", rejected.Status);
        Assert.Equal("channelBusy", rejected.Reason);
        await peer.RespondAsync(first, "ok", "pong");
        await held;

        var admitted = native.InvokeAsync(request).AsTask();
        var discovery = await peer.ReplyAsync("ok", AdmissionCapabilities);
        Assert.Equal("capabilities", TomlReader.Parse("native-ui", discovery).String("action"));
        var payload = await peer.ReplyAsync("ok", "{\"operationId\":\"test.scoped\",\"status\":\"completed\"}");
        var invocation = TomlReader.Parse("native-ui", payload);
        Assert.Equal("invoke", invocation.String("action"));
        Assert.Equal("fixture.owner", invocation.String("owner_scope"));
        Assert.Equal("completed", (await admitted).Status);
        Assert.Equal(3, peer.Requests);
    }

    [Fact]
    public async Task BusyResultQueryPreservesQueuedMutationAndRetriesOnlyItsRead()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var request = new GameNativeUiRequest("test.queued", "control.addButton", "Entry")
        { Target = new() { Id = "form-1" }, Definition = new("entry", 1, 2, 100, 25) };
        var operation = peer.Client.InvokeAsync(request).AsTask();
        var payload = await peer.ReplyAsync("ok", "{\"operationId\":\"test.queued\",\"status\":\"queued\"}");
        Assert.Equal("invoke", TomlReader.Parse("native-ui", payload).String("action"));
        Assert.Equal("queued", (await operation).Status);

        var held = peer.Channel.RequestAsync("ping");
        var ping = await peer.ReadRequestAsync();
        await Assert.ThrowsAsync<GameNativeUiBusyException>(() => peer.Client.GetResultAsync(request.OperationId).AsTask());
        await peer.RespondAsync(ping, "ok", "pong");
        await held;

        var result = peer.Client.GetResultAsync(request.OperationId).AsTask();
        payload = await peer.ReplyAsync("ok", "{\"operationId\":\"test.queued\",\"status\":\"completed\"}");
        var query = TomlReader.Parse("native-ui", payload);
        Assert.Equal("result", query.String("action"));
        Assert.Equal(request.OperationId, query.String("operation_id"));
        Assert.Equal("completed", (await result).Status);
        Assert.Equal(3, peer.Requests);
    }

    [Fact]
    public async Task SubmittedServiceFailureRemainsUnknownAndIsNotRetried()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var request = new GameNativeUiRequest("test.failed", "control.setText", "Changed")
        { Target = new() { Id = "control-1" } };
        var operation = peer.Client.InvokeAsync(request).AsTask();
        var payload = await peer.ReplyAsync("service-failed", "");
        Assert.Equal("invoke", TomlReader.Parse("native-ui", payload).String("action"));
        var unknown = await operation;
        Assert.Equal("outcomeUnknown", unknown.Status);
        Assert.Equal("channelInterrupted", unknown.Reason);

        var ping = peer.Channel.RequestAsync("ping");
        Assert.Equal("", await peer.ReplyAsync("ok", "pong"));
        Assert.Equal("pong", await ping);
        Assert.Equal(2, peer.Requests);
    }

    [Fact]
    public async Task CancellationAfterSubmissionRemainsUnknownAndClosesTheStream()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        using var cancellation = new CancellationTokenSource();
        await peer.ConnectAsync();
        var request = new GameNativeUiRequest("test.cancelled", "control.activate", "")
        { Target = new() { Id = "control-1" } };
        var operation = peer.Client.InvokeAsync(request, cancellation.Token).AsTask();
        var submitted = await peer.ReadRequestAsync();
        Assert.Equal("invoke", TomlReader.Parse("native-ui", submitted.String("payload")).String("action"));
        cancellation.Cancel();
        Assert.Equal("outcomeUnknown", (await operation).Status);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => peer.Channel.RequestAsync("ping"));
        Assert.Equal(1, peer.Requests);
    }

    private const string AdmissionCapabilities = """
        {"build":"fixture","operations":[{"kind":"control.setText","supported":true,"ready":true,"features":[]}]}
        """;
}
