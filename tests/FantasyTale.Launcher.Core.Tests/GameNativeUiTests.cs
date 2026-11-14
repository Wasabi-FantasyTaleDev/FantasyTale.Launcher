using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class GameNativeUiTests
{
    [Fact]
    public void ChatRetainsOrderedColorsAndOnlyExplicitBannerClosureRequiresOwnership()
    {
        var chat = new GameNativeUiRequest("test.chat", "chat", "")
        {
            ChatStyle = "system",
            Runs = [new("first", "#4DA6FFFF"), new("second", "#FFFFFFFF")]
        };
        chat.Validate();
        Assert.Equal("second", chat.Runs[1].Text);
        Assert.Throws<ArgumentException>(() => (chat with { Text = "ambiguous body" }).Validate());
        Assert.Throws<ArgumentException>(() => (chat with { Runs = [new("text", "red")] }).Validate());
        Assert.Throws<ArgumentException>(() => (chat with { Runs = [new("text", "#FFGGFFFF")] }).Validate());
        (chat with { Runs = [new("text", "#FFFFFF")], ChatCaption = "" }).Validate();
        Assert.Throws<ArgumentException>(() => (chat with { ChatCaption = "bad\0caption" }).Validate());
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("test.notice", "notification", "text") { ChatCaption = "wrong operation" }.Validate());
        new GameNativeUiRequest("test.banner", "announcement.show", "banner").Validate();
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("test.close", "announcement.close", "").Validate());
        new GameNativeUiRequest("test.banner", "announcement.show", "banner") { AnnouncementId = "owned.banner" }.Validate();
        new GameNativeUiRequest("test.close", "announcement.close", "") { AnnouncementId = "owned.banner" }.Validate();
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("test.close", "announcement.close", "unexpected") { AnnouncementId = "owned.banner" }.Validate());
    }

    [Fact]
    public void MessageButtonsHaveStableUniqueIdsAndOtherOperationsCannotBorrowThem()
    {
        var request = new GameNativeUiRequest("test.message", "messageBox", "body")
        { Title = "title", Icon = "warning", Buttons = [new("accept", "Accept"), new("reject", "Cancel")] };
        request.Validate();
        Assert.Throws<ArgumentException>(() => (request with { Buttons = [new("accept", "A"), new("accept", "B")] }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Buttons = [new("bad id", "A")] }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Icon = "invented" }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Kind = "notification" }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Text = "text\0suffix" }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { OperationId = "../file" }).Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankButtonLabelsSurviveValidationAndTransport(string label)
    {
        var request = new GameNativeUiRequest("test.blank", "messageBox", "body") { Buttons = [new("ok", label)] };
        request.Validate();
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var operation = peer.Client.InvokeAsync(request).AsTask();
        var payload = await peer.ReplyAsync("ok", "{\"operationId\":\"test.blank\",\"status\":\"queued\"}");
        Assert.Equal(label, TomlReader.Parse("native-ui", payload).Tables("buttons")[0].String("label"));
        Assert.Equal("queued", (await operation).Status);
        Assert.Throws<ArgumentException>(() => (request with { Buttons = [new("ok", null!)] }).Validate());
        Assert.Throws<ArgumentException>(() => (request with { Buttons = [new("ok", "bad\0label")] }).Validate());
    }

    [Fact]
    public async Task NotificationTransportPreservesLineBreaks()
    {
        var request = new GameNativeUiRequest("test.notice", "notification", "First line\n第二行\r\nThird line");
        request.Validate();
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var operation = peer.Client.InvokeAsync(request).AsTask();
        var payload = await peer.ReplyAsync("ok", "{\"operationId\":\"test.notice\",\"status\":\"shown\"}");
        Assert.Equal(request.Text, TomlReader.Parse("native-ui", payload).String("text"));
        Assert.Equal("shown", (await operation).Status);
    }

    [Fact]
    public async Task MissingProviderDoesNotBreakPingOrFabricateNativeSuccess()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var capabilities = peer.Client.GetCapabilitiesAsync().AsTask();
        await peer.ReplyAsync("service-unavailable", "");
        var result = await capabilities;
        Assert.Equal("fixture", result.Build);
        Assert.Equal(12, result.Operations.Count);
        Assert.All(result.Operations, operation => { Assert.False(operation.Supported); Assert.False(operation.Ready); });
        var ping = peer.Channel.RequestAsync("ping");
        await peer.ReplyAsync("ok", "pong");
        Assert.Equal("pong", await ping);
    }

    [Fact]
    public async Task ChatPayloadPreservesUnicodeQuotesAndRunBoundaries()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var request = new GameNativeUiRequest("test.chat", "chat", "")
        { ChatCaption = "Custom \"source\" 来源", Runs = [new("Unicode \"line\"\n第二行", "#4DA6FFFF"), new(" tail", "#FFFFFFFF")] };
        var operation = peer.Client.InvokeAsync(request).AsTask();
        var payload = await peer.ReplyAsync("ok", JsonSerializer.Serialize(new { operationId = "test.chat", status = "inserted" }));
        var parsed = TomlReader.Parse("native-ui", payload);
        Assert.Equal("invoke", parsed.String("action"));
        Assert.Equal("chat", parsed.String("kind"));
        Assert.Equal(request.ChatCaption, parsed.String("chat_caption"));
        Assert.Equal("Unicode \"line\"\n第二行", parsed.Tables("runs")[0].String("text"));
        Assert.Equal("#FFFFFFFF", parsed.Tables("runs")[1].String("color"));
        Assert.Equal("inserted", (await operation).Status);
    }

    [Theory]
    [InlineData("other.operation", null)]
    [InlineData("test.dialog", "other-button")]
    public async Task InvalidResultsAreUncertainAndNeverReplayTheOperation(string id, string? button)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var request = new GameNativeUiRequest("test.dialog", "messageBox", "body") { Buttons = [new("ok", "OK")] };
        var operation = peer.Client.InvokeAsync(request).AsTask();
        await peer.ReplyAsync("ok", JsonSerializer.Serialize(new { operationId = id, status = "selected", buttonId = button }));
        Assert.Equal("outcomeUnknown", (await operation).Status);
        var ping = peer.Channel.RequestAsync("ping");
        Assert.Equal("", await peer.ReplyAsync("ok", "pong"));
        Assert.Equal("pong", await ping);
        Assert.Equal(2, peer.Requests);
    }

    [Fact]
    public async Task ButtonPollingDoesNotSubmitAnotherDialog()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var request = new GameNativeUiRequest("test.dialog", "messageBox", "body") { Buttons = [new("ok", "OK")] };
        var show = peer.Client.InvokeAsync(request).AsTask();
        await peer.ReplyAsync("ok", "{\"operationId\":\"test.dialog\",\"status\":\"queued\"}");
        Assert.Equal("queued", (await show).Status);
        var poll = peer.Client.GetResultAsync("test.dialog").AsTask();
        var payload = await peer.ReplyAsync("ok", "{\"operationId\":\"test.dialog\",\"status\":\"selected\",\"buttonId\":\"ok\"}");
        Assert.Equal("result", TomlReader.Parse("native-ui", payload).String("action"));
        Assert.Equal("ok", (await poll).ButtonId);
        Assert.Equal(2, peer.Requests);
    }

    [Theory]
    [InlineData("selected", null)]
    [InlineData("shown", "ok")]
    [InlineData("invented", null)]
    public async Task MalformedOutcomesNeverBecomeSuccessfulPresentations(string status, string? buttonId)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var request = new GameNativeUiRequest("test.dialog", "messageBox", "body") { Buttons = [new("ok", "OK")] };
        var operation = peer.Client.InvokeAsync(request).AsTask();
        await peer.ReplyAsync("ok", JsonSerializer.Serialize(new { operationId = request.OperationId, status, buttonId }));
        Assert.Equal("outcomeUnknown", (await operation).Status);
    }

    [Theory]
    [InlineData("{\"build\":\"other\",\"operations\":[]}")]
    [InlineData("{\"build\":\"fixture\",\"operations\":null}")]
    [InlineData("{\"build\":\"fixture\",\"operations\":[{\"kind\":\"chat\",\"supported\":false,\"ready\":true,\"features\":[]}]}")]
    [InlineData("{\"build\":\"fixture\",\"operations\":[{\"kind\":\"invented\",\"supported\":true,\"ready\":true,\"features\":[]}]}")]
    [InlineData("{\"build\":\"fixture\",\"operations\":[{\"kind\":\"chat\",\"features\":[]},{\"kind\":\"chat\",\"features\":[]}]}")]
    public async Task InvalidCapabilitiesAreRejectedWithoutBreakingTheChannel(string response)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new NativePeer();
        await peer.ConnectAsync();
        var capabilities = peer.Client.GetCapabilitiesAsync().AsTask();
        await peer.ReplyAsync("ok", response);
        await Assert.ThrowsAsync<InvalidDataException>(() => capabilities);
        var ping = peer.Channel.RequestAsync("ping");
        await peer.ReplyAsync("ok", "pong");
        Assert.Equal("pong", await ping);
    }

    private sealed class NativePeer : IDisposable
    {
        private readonly NamedPipeClientStream _peer;
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(10));
        internal HookChannel Channel { get; }
        internal GameNativeUiClient Client { get; }
        internal int Requests { get; private set; }

        internal NativePeer()
        {
            var launch = Guid.NewGuid().ToString("N");
            Channel = new HookChannel(launch);
            Channel.BindProcess(Environment.ProcessId);
            Client = new(Channel, "fixture");
            _peer = new(".", "FantasyTale.Hook." + launch, PipeDirection.InOut, PipeOptions.Asynchronous);
        }
        internal Task ConnectAsync() => _peer.ConnectAsync(_timeout.Token);
        internal async Task<string> ReplyAsync(string result, string body)
        {
            var request = await ReadRequestAsync();
            await RespondAsync(request, result, body);
            return request.String("payload");
        }
        internal async Task<TomlReader> ReadRequestAsync()
        {
            var header = new byte[4];
            await _peer.ReadExactlyAsync(header, _timeout.Token);
            var request = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
            await _peer.ReadExactlyAsync(request, _timeout.Token);
            var table = TomlReader.Parse("control", Encoding.UTF8.GetString(request));
            Requests++;
            Assert.Equal(Requests, table.Integer("request_id"));
            if (table.String("method") == "service") Assert.Equal("game.native-ui", table.String("service"));
            return table;
        }
        internal async Task RespondAsync(TomlReader request, string result, string body)
        {
            var response = Encoding.UTF8.GetBytes($"protocol = 1\nrequest_id = {request.Integer("request_id")}\nresult = {SessionWriter.Quote(result)}\nbody = {SessionWriter.Quote(body)}\n");
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, response.Length);
            await _peer.WriteAsync(header, _timeout.Token);
            await _peer.WriteAsync(response, _timeout.Token);
        }
        public void Dispose() { Channel.Dispose(); _peer.Dispose(); _timeout.Dispose(); }
    }
}
