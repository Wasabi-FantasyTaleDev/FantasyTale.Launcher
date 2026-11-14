using System.Collections.Concurrent;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Host.Window;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

/// <summary>A feature package's page reaches the server only through the types its manifest grants.</summary>
public sealed class PanelCompanionBridgeTests
{
    private static readonly Dictionary<string, int> Grants = new(StringComparer.Ordinal) { ["game.feature.changed"] = 1, ["game.feature.get"] = 1 };

    [Fact]
    public async Task APageRequestReachesTheServerAndItsReplyComesBack()
    {
        var channel = new FakeChannel();
        var panel = new FakePanel();
        using var bridge = new PanelCompanionBridge(panel, channel, Grants);
        panel.Emit("companion.request", new { id = "r1", type = "game.feature.get", version = 1, payload = new { page = 2 } });
        var reply = await panel.WaitAsync("companion.response");
        Assert.Equal("r1", reply.GetProperty("id").GetString());
        Assert.Equal("completed", reply.GetProperty("status").GetString());
        Assert.Equal(2, reply.GetProperty("response").GetProperty("echo").GetProperty("page").GetInt32());
        Assert.Equal(("game.feature.get", 1), Assert.Single(channel.Requests));
    }

    [Theory]
    [InlineData("game.other.type", 1)]
    [InlineData("game.feature.get", 2)]
    public async Task APageCannotUseATypeOrSchemaItsManifestDidNotGrant(string type, int version)
    {
        var channel = new FakeChannel();
        var panel = new FakePanel();
        using var bridge = new PanelCompanionBridge(panel, channel, Grants);
        panel.Emit("companion.request", new { id = "r2", type, version, payload = new { } });
        Assert.Equal("not_granted", (await panel.WaitAsync("companion.response")).GetProperty("status").GetString());
        Assert.Empty(channel.Requests);
    }

    [Fact]
    public async Task ServerEventsAndTheGrantedCapabilitiesReachThePage()
    {
        var channel = new FakeChannel();
        var panel = new FakePanel();
        using var bridge = new PanelCompanionBridge(panel, channel, Grants);
        var state = await panel.WaitAsync("companion.state");
        Assert.Equal("connected", state.GetProperty("state").GetString());
        // The server also offers a type this package was not granted; the page is not told about it.
        Assert.Equal(["game.feature.changed", "game.feature.get"], state.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()));
        await channel.EmitAsync("game.feature.changed", new { count = 3 });
        var message = await panel.WaitAsync("companion.event");
        Assert.Equal("game.feature.changed", message.GetProperty("type").GetString());
        Assert.Equal(3, message.GetProperty("payload").GetProperty("count").GetInt32());
    }

    [Fact]
    public void AnOverlayPackageDeclaresItsGrantsAndOnlyWithAnEntryPage()
    {
        var directory = Directory.CreateTempSubdirectory("ft-panel-consumer-");
        try
        {
            var path = Path.Combine(directory.FullName, "plugin.toml");
            File.WriteAllText(Path.Combine(directory.FullName, "feature.html"), "<div data-game-ui-root></div>");
            File.WriteAllText(path, Manifest("entry = \"feature.html\""));
            var package = PluginCatalog.LoadFile(path);
            Assert.Equal(Grants.OrderBy(x => x.Key), package.CompanionMessages.OrderBy(x => x.Key));
            File.WriteAllText(path, Manifest("entry = \"\""));
            Assert.Throws<DefinitionException>(() => PluginCatalog.LoadFile(path));
        }
        finally { directory.Delete(true); }
    }

    private static string Manifest(string entry) => $"""
        schema = 1

        [plugin]
        id = "test.feature-panel"
        version = "1.0.0"
        game = "bubblefighter"
        kind = "overlay"
        name = {"{"} en = "Feature" {"}"}

        [compatibility]
        builds = ["*"]

        [overlay]
        {entry}

        [companion.messages]
        "game.feature.changed" = 1
        "game.feature.get" = 1
        """;

    private sealed class FakeChannel : ICompanionChannel
    {
        private readonly ConcurrentDictionary<string, Func<CompanionEvent, CancellationToken, ValueTask>> _handlers = new(StringComparer.Ordinal);
        internal ConcurrentQueue<(string Type, int Version)> Requests { get; } = new();
        public CompanionChannelState State => CompanionChannelState.Connected;
        public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>(StringComparer.Ordinal) { "game.feature.changed", "game.feature.get", "game.unrelated" };
        public ValueTask<CompanionOperationResult> SendAsync(string type, int version, object? payload = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CompanionOperationResult(CompanionOperationStatus.Accepted));
        public ValueTask<CompanionOperationResult> RequestAsync(string type, int version, object? payload = null,
            CompanionRequestOptions? options = null, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue((type, version));
            return ValueTask.FromResult(new CompanionOperationResult(CompanionOperationStatus.Completed, "q1",
                JsonSerializer.SerializeToElement(new { echo = payload })));
        }
        public IDisposable Subscribe(string type, int version, Func<CompanionEvent, ValueTask> handler) => Subscribe(type, version, (message, _) => handler(message));
        public IDisposable Subscribe(string type, int version, Func<CompanionEvent, CancellationToken, ValueTask> handler)
        {
            _handlers[type] = handler;
            return new Release(() => _handlers.TryRemove(type, out _));
        }
        internal async Task EmitAsync(string type, object payload) =>
            await _handlers[type](new(type, 1, "m1", JsonSerializer.SerializeToElement(payload)), CancellationToken.None);
        public void Dispose() { }
    }

    private sealed class Release(Action action) : IDisposable { public void Dispose() => action(); }

    private sealed class FakePanel : IOverlayPanel
    {
        private readonly ConcurrentQueue<(string Name, JsonElement Data)> _sent = new();
        public string PanelId => "test.feature-panel";
        public event EventHandler<OverlayMessageEventArgs>? MessageReceived;
        public void Show() { }
        public void Hide() { }
        public void Send(string eventName, object? data = null) => _sent.Enqueue((eventName, JsonSerializer.SerializeToElement(data)));
        public void MountGameUi(string component, object? props = null) { }
        public void Dispose() { }
        internal void Emit(string name, object data) => MessageReceived?.Invoke(this, new(PanelId, name, JsonSerializer.SerializeToElement(data)));
        internal async Task<JsonElement> WaitAsync(string name)
        {
            for (var i = 0; i < 300; i++)
            {
                var match = _sent.FirstOrDefault(x => x.Name == name);
                if (match.Name is not null) return match.Data;
                await Task.Delay(10);
            }
            throw new TimeoutException(name + " was not sent.");
        }
    }
}
