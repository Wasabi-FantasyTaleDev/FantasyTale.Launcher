using System.Net.WebSockets;
using System.Text.Json;
using FantasyTale.Launcher.Communication;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Host.Window;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ControllerCompanionTests
{
    [Fact]
    public void ControlledPackagesHaveOneBackendOwnerAndNoRawPageGrants()
    {
        var controlled = Panel("fixture.controlled", "feature-controller");
        var plain = Panel("fixture.plain");
        var instance = Instance(controlled, plain);
        var controllers = LauncherServicePlan.ControllerConsumers(instance, Build, [controlled, plain]);
        var pages = LauncherServicePlan.PanelConsumers(instance, Build, [controlled, plain]);
        Assert.Equal(controlled.Id, Assert.Single(controllers).Key);
        Assert.Equal(plain.Id, Assert.Single(pages).Key);
        Assert.Equal(controlled.CompanionMessages, controllers[controlled.Id]);
        var disabled = instance with { Plugins = instance.Plugins.Select(selection => selection with { Enabled = false }).ToArray() };
        Assert.Empty(LauncherServicePlan.ControllerConsumers(disabled, Build, [controlled, plain]));
    }

    [Theory]
    [InlineData("no-provider")]
    [InlineData("no-factory")]
    [InlineData("no-session")]
    [InlineData("factory-failed")]
    public async Task MissingCompanionCompletesControllerLookupBeforeTheGameExits(string mode)
    {
        var panel = Panel("fixture.controlled", "feature-controller");
        var foundation = Foundation();
        var instance = Instance(panel, foundation);
        IReadOnlyList<ResolvedLauncherService> plan = mode == "no-provider" ? [] : [new(foundation, new Dictionary<string, object>())];
        LauncherServiceManager.CompanionFactory? factory = mode == "no-factory" ? null : (_, _, _, _, _) => mode == "factory-failed"
            ? throw new HttpRequestException("Fixture provider failed.") : Task.FromResult<CompanionSession?>(null);
        await using var manager = new LauncherServiceManager(_ => null, factory);
        var game = new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.StartForClient(instance, Build, plan, game.Task,
            controllerConsumers: LauncherServicePlan.ControllerConsumers(instance, Build, [panel, foundation]));
        Assert.Null(await manager.GetControllerChannelAsync(instance.Id, panel.Id).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(await manager.GetControllerChannelAsync(instance.Id, "unselected.owner"));
        Assert.False(game.Task.IsCompleted);
    }

    [Fact]
    public async Task ControllerReceivesStableGrantedChannelBeforeAuthorizationAndPageSetup()
    {
        await using var pair = await CompanionTests.SocketPair.CreateAsync();
        var panelPackage = Panel("fixture.controlled", "feature-controller");
        var foundation = Foundation();
        var instance = Instance(panelPackage, foundation);
        var authorize = new TaskCompletionSource<CompanionAuthorization?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pageLookups = 0;
        await using var manager = new LauncherServiceManager(_ => null, (_, _, messages, limits, _) =>
            Task.FromResult<CompanionSession?>(new CompanionSession(new("testgame", "kr-812", "launch-1", "zh-CN", messages),
                token => new(authorize.Task.WaitAsync(token)), limits, null, null,
                (_, _, _, _) => CompanionTests.ConnectAsync(pair, types: messages, epoch: "controller-epoch"))),
            (_, _) => { Interlocked.Increment(ref pageLookups); throw new InvalidOperationException("A controlled page cannot get a raw companion bridge."); });
        var game = new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.StartForClient(instance, Build, [new(foundation, new Dictionary<string, object>())], game.Task,
            controllerConsumers: LauncherServicePlan.ControllerConsumers(instance, Build, [panelPackage, foundation]));
        var channel = await manager.GetControllerChannelAsync(instance.Id, panelPackage.Id).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(channel);
        Assert.Same(channel, await manager.GetControllerChannelAsync(instance.Id, panelPackage.Id));
        Assert.False(authorize.Task.IsCompleted);
        Assert.NotEqual(CompanionChannelState.Connected, channel.State);
        var received = new TaskCompletionSource<CompanionEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var state = channel.SubscribeState(snapshot =>
        {
            if (snapshot.State == CompanionChannelState.Connected) connected.TrySetResult();
        });
        var endpoint = new FakePanel();
        await using var lifetime = new PanelBackendLifetime();
        var provider = new FactoryProvider(context =>
        {
            Assert.Same(channel, context.Companion);
            Assert.Equal(0, endpoint.Emitted);
            return new SubscriptionLifetime(context.Companion!.Subscribe("game.feature.changed", 1, message =>
            {
                received.TrySetResult(message);
                return ValueTask.CompletedTask;
            }));
        });
        lifetime.Start(panelPackage, Build, endpoint, null, "en", provider, companion: channel);
        endpoint.Emit("companion.request", new { id = "raw-page", type = "game.feature.get", version = 1 });
        authorize.SetResult(new(new Uri("wss://example.test/channel"), "fixture-grant"));
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<ArgumentException>(() => channel.Subscribe("game.ungranted", 1, _ => ValueTask.CompletedTask));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var message = new CompanionEnvelope("event", "context-one", "game.feature.changed", 1,
            JsonSerializer.SerializeToElement(new { active = true }), Epoch: "controller-epoch");
        await pair.Server.SendAsync(CompanionWire.Encode(message, new()), WebSocketMessageType.Text, true, timeout.Token);
        Assert.Equal("controller-epoch", (await received.Task.WaitAsync(timeout.Token)).ConnectionEpoch);
        await lifetime.DisposeAsync();
        await manager.DisposeAsync();
        Assert.Equal(0, pageLookups);
        Assert.Equal(CompanionChannelState.Closed, channel.ConnectionSnapshot.State);
    }

    private static PluginDefinition Panel(string id, string? controller = null) => PluginCatalog.Parse("plugin.toml", $$"""
        schema = 1
        [plugin]
        id = "{{id}}"
        version = "1.0.0"
        game = "fixture"
        kind = "overlay"
        name = { en = "Fixture panel" }
        [overlay]
        entry = "panel.html"
        {{(controller is null ? "" : "controller = \"" + controller + "\"")}}
        [companion.messages]
        "game.feature.changed" = 1
        "game.feature.get" = 1
        """);

    private static PluginDefinition Foundation() => PluginCatalog.Parse("plugin.toml", """
        schema = 1
        [plugin]
        id = "fixture.companion"
        version = "1.0.0"
        game = "fixture"
        kind = "service"
        name = { en = "Fixture companion" }
        [service]
        provider = "companion"
        """);

    private static InstanceDefinition Instance(params PluginDefinition[] packages) => new("fixture", "Fixture", "fixture", Build.Id, null,
        packages.Select(package => new InstancePlugin(package.Id, package.Version, true, false, new Dictionary<string, object>())).ToArray());

    private static readonly BuildDefinition Build = new("kr-812", "kr", 812, new(new Dictionary<string, string> { ["en"] = "Fixture" }),
        "game.exe", "standin", "", null, null, "hook", [], []);

    private sealed class FactoryProvider(Func<GameOverlayControllerContext, IAsyncDisposable> create) : IGameOverlayControllerProvider
    {
        public IAsyncDisposable StartController(GameOverlayControllerContext context) => create(context);
    }

    private sealed class SubscriptionLifetime(IDisposable subscription) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { subscription.Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class FakePanel : IOverlayPanel
    {
        public string PanelId => "fixture.controlled";
        public event EventHandler<OverlayMessageEventArgs>? MessageReceived;
        internal int Emitted { get; private set; }
        public void Show() { }
        public void Hide() { }
        public void Send(string eventName, object? data = null) { }
        public void MountGameUi(string component, object? props = null) { }
        public void Dispose() { }
        internal void Emit(string name, object data) { Emitted++; MessageReceived?.Invoke(this, new(PanelId, name, JsonSerializer.SerializeToElement(data))); }
    }
}
