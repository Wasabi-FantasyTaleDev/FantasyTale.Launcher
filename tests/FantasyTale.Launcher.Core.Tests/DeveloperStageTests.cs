using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Host.Window;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class DeveloperStageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-developer-stages-").FullName;

    [Fact]
    public void StageOpenAcceptsOnlyAStageAndOnlyItAcceptsOne()
    {
        new GameNativeUiRequest("stage.a", "stage.open", "") { Stage = "character" }.Validate();
        new GameNativeUiRequest("stage.b", "stage.open", "") { Stage = "mainMenu" }.Validate();
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("stage.c", "stage.open", "").Validate());
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("stage.c", "stage.open", "") { Stage = "../tools" }.Validate());
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("stage.c", "stage.open", "text") { Stage = "character" }.Validate());
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("stage.c", "stage.open", "") { Stage = "character", Title = "x" }.Validate());
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("stage.c", "stage.open", "")
        { Stage = "character", Target = new GameNativeUiControlTarget { Name = "btn" } }.Validate());
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("note", "notification", "hi") { Stage = "character" }.Validate());
        Assert.Throws<ArgumentException>(() => new GameNativeUiRequest("edit", "control.inspect", "") { Stage = "character" }.Validate());
    }

    [Fact]
    public async Task StageOpenSendsOnlyItsStageAndAcceptsAnOpenedResult()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new Peer();
        await peer.ConnectAsync();
        var operation = peer.Client.InvokeAsync(new GameNativeUiRequest("stage.a", "stage.open", "") { Stage = "character" }).AsTask();
        var payload = TomlReader.Parse("native-ui", await peer.ReplyAsync("{\"operationId\":\"stage.a\",\"status\":\"opened\"}"));
        // The native provider refuses any presentation field beside a stage.open identity.
        payload.OnlyKeys("action", "operation_id", "kind", "stage");
        Assert.Equal("stage.open", payload.String("kind"));
        Assert.Equal("character", payload.String("stage"));
        Assert.Equal("opened", (await operation).Status);
    }

    [Fact]
    public async Task ProvidersMayOfferStageOpen()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new Peer();
        await peer.ConnectAsync();
        var capabilities = peer.Client.GetCapabilitiesAsync().AsTask();
        await peer.ReplyAsync("{\"build\":\"fixture\",\"operations\":[{\"kind\":\"stage.open\",\"supported\":true,\"ready\":true,"
            + "\"features\":[\"tool:character\",\"tool:effect\",\"mainMenu\"]}]}");
        var stage = Assert.Single((await capabilities).Operations);
        Assert.Equal(["character", "effect"], DeveloperStageMenu.Tools(stage));
    }

    [Fact]
    public async Task TheStageReadIsAQueuelessActionAndRejectsMalformedReplies()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var peer = new Peer();
        await peer.ConnectAsync();
        var read = peer.Client.GetStageAsync().AsTask();
        var payload = TomlReader.Parse("native-ui", await peer.ReplyAsync("{\"status\":\"completed\",\"id\":3,\"name\":\"lobby\",\"switching\":false}"));
        payload.OnlyKeys("action");
        Assert.Equal("stage", payload.String("action"));
        Assert.Equal(new GameNativeUiStage(3, "lobby", false), await read);
        var unsupported = peer.Client.GetStageAsync().AsTask();
        await peer.ReplyAsync("{\"status\":\"unsupported\",\"reason\":\"adapterUnavailable\"}");
        await Assert.ThrowsAsync<NotSupportedException>(() => unsupported);
        var malformed = peer.Client.GetStageAsync().AsTask();
        await peer.ReplyAsync("{\"status\":\"completed\",\"id\":3,\"name\":\"<lobby>\",\"switching\":false}");
        await Assert.ThrowsAsync<InvalidDataException>(() => malformed);
    }

    [Theory]
    [InlineData("mainMenu", true)]
    [InlineData("tool", true)]
    [InlineData("lobby", false)]
    [InlineData("room", false)]
    [InlineData(null, false)]
    public void TheButtonShowsOnlyOnTheMainMenuAndInToolStages(string? stage, bool shown) =>
        Assert.Equal(shown, DeveloperStageMenu.Shows(stage));

    [Fact]
    public void ToolListIgnoresFeaturesThatAreNotToolKeys()
    {
        var capability = new GameNativeUiCapability("stage.open", true, true,
            ["mainMenu", "tool:ui_editor", "tool:", "tool:bad key", "tool:\"><script>", "tool:ui_editor", "tool:costume_test"]);
        Assert.Equal(["ui_editor", "costume_test"], DeveloperStageMenu.Tools(capability));
    }

    [Fact]
    public void MenuListsEachToolAndTheWayBack()
    {
        var html = DeveloperStageMenu.MenuHtml(["character", "unnamed_tool"]);
        Assert.Contains("data-overlay-action=\"stage:character\">Character tool<", html);
        Assert.Contains("data-overlay-action=\"stage:unnamed_tool\">unnamed_tool<", html);
        Assert.Contains("data-overlay-action=\"stage:mainMenu\"", html);
        Assert.Contains("data-overlay-action=\"close\"", html);
        Assert.Contains("data-overlay-action=\"open\"", DeveloperStageMenu.ButtonHtml);
    }

    [Theory]
    [InlineData("opened", null, false)]
    [InlineData("notReady", "notMainMenu", true)]
    [InlineData("notReady", "stageSwitchPending", true)]
    [InlineData("failed", "unexpectedStageMethod", true)]
    public void RefusalsAreExplained(string status, string? reason, bool explained) =>
        Assert.Equal(explained, DeveloperStageMenu.Explain(new("stage.a", status, Reason: reason)) is not null);

    [Fact]
    public void DeveloperStagesAreOffUntilSwitchedOnAndPersistBesideTheOverlayTest()
    {
        Assert.False(OverlayTestSettings.Load(_root).DeveloperStages);
        var enabled = new OverlayTestSettings(false, null, DeveloperStages: true);
        enabled.Save(_root);
        Assert.Equal(enabled, OverlayTestSettings.Load(_root));
        File.WriteAllText(Path.Combine(_root, "overlay-test.toml"), "developer_stages = 'yes'");
        Assert.Throws<InvalidDataException>(() => OverlayTestSettings.Load(_root));
    }

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class Peer : IDisposable
    {
        private readonly NamedPipeClientStream _peer;
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(10));
        internal HookChannel Channel { get; }
        internal GameNativeUiClient Client { get; }

        internal Peer()
        {
            var launch = Guid.NewGuid().ToString("N");
            Channel = new HookChannel(launch);
            Channel.BindProcess(Environment.ProcessId);
            Client = new(Channel, "fixture");
            _peer = new(".", "FantasyTale.Hook." + launch, PipeDirection.InOut, PipeOptions.Asynchronous);
        }

        internal Task ConnectAsync() => _peer.ConnectAsync(_timeout.Token);

        internal async Task<string> ReplyAsync(string body)
        {
            var header = new byte[4];
            await _peer.ReadExactlyAsync(header, _timeout.Token);
            var bytes = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
            await _peer.ReadExactlyAsync(bytes, _timeout.Token);
            var request = TomlReader.Parse("control", Encoding.UTF8.GetString(bytes));
            var response = Encoding.UTF8.GetBytes($"protocol = 1\nrequest_id = {request.Integer("request_id")}\nresult = \"ok\"\nbody = {SessionWriter.Quote(body)}\n");
            BinaryPrimitives.WriteInt32LittleEndian(header, response.Length);
            await _peer.WriteAsync(header, _timeout.Token);
            await _peer.WriteAsync(response, _timeout.Token);
            return request.String("payload");
        }

        public void Dispose() { Channel.Dispose(); _peer.Dispose(); _timeout.Dispose(); }
    }
}
