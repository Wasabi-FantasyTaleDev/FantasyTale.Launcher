using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Host.Window;
using FantasyTale.Launcher.Sdk;
using System.Net.WebSockets;
using System.Text.Json;
using FantasyTale.Launcher.Communication;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class LauncherServicePluginTests
{
    private static string Repository
    {
        get
        {
            var directory = TestRepository.StartDirectory();
            while (!File.Exists(Path.Combine(directory!.FullName, "AGENTS.md"))) directory = directory.Parent;
            return directory.FullName;
        }
    }

    private static (GameDefinition Game, BuildDefinition Build, IReadOnlyList<PluginDefinition> Packages) Catalog()
    {
        var game = GameCatalog.Load(Path.Combine(Repository, "games")).Single(g => g.Id == "bubblefighter");
        var packages = PluginCatalog.Load(Path.Combine(game.Folder, "plugins"));
        var foundation = packages.Single(p => p.Service?.Provider == "companion");
        var notifications = foundation with
        {
            Id = "test.notification-provider",
            Service = new("overlay-notifications", foundation.Service!.Messages),
            Options = foundation.Options.Where(option => option.Key == "max_dialogs").ToArray(),
        };
        return (game, game.Builds.Single(b => b.Id == "kr-812"), packages.Append(notifications).ToArray());
    }

    private static InstanceDefinition Instance(BuildDefinition build, params PluginDefinition[] enabled) =>
        new("service-test", "Services", "bubblefighter", build.Id, null,
            enabled.Select(p => new InstancePlugin(p.Id, p.Version, true, false, new Dictionary<string, object>())).ToArray());

    [Fact]
    public void FoundationsAreOptionalDataRowsAndStayOutOfTheNativeSession()
    {
        var (game, build, packages) = Catalog();
        var foundation = packages.Single(p => p.Service?.Provider == "companion");
        var notifications = packages.Single(p => p.Service?.Provider == "overlay-notifications");
        Assert.Null(typeof(PluginDefinition).GetProperty("RequiredOn"));
        Assert.Null(typeof(PluginDefinition).GetProperty("WarnOn"));
        Assert.Contains(PluginResolver.Defaults(game, build, packages), p => p.Id == foundation.Id && p.Enabled);
        Assert.Contains(PluginResolver.Defaults(game, build, packages), p => p.Id == "bubblefighter.fantasy.native-ui-bridge" && p.Enabled);
        var instance = Instance(build, foundation, notifications);
        Assert.Empty(PluginResolver.Resolve(instance, build, packages));
        var plan = LauncherServicePlan.Resolve(instance, build, packages);
        Assert.Equal(2, plan.Count);
        Assert.Equal(20000L, plan.Single(p => p.Plugin.Id == foundation.Id).Options["heartbeat_interval_ms"]);
        Assert.Equal(4L, plan.Single(p => p.Plugin.Id == notifications.Id).Options["max_dialogs"]);
        Assert.Equal(1, notifications.Service!.Messages["ui.notification.show"]);
        Assert.Empty(LauncherServicePlan.Resolve(Instance(build), build, packages));
    }

    [Theory]
    [InlineData("[service]\nprovider = \"companion\"", "[service]\nprovider = \"arbitrary-library\"")]
    [InlineData("[service]\nprovider = \"companion\"", "")]
    [InlineData("kind = \"service\"", "kind = \"overlay\"")]
    [InlineData("provider = \"companion\"", "provider = \"companion\"\nendpoint = \"wss://example.test\"")]
    [InlineData("provider = \"companion\"", "provider = \"companion\"\n[service.messages]\n\"unsafe.type\" = 1")]
    public void ManifestCannotLoadArbitraryCodeAddressesOrFoundationApplicationRoutes(string before, string after)
    {
        const string manifest = """
            schema = 1
            [plugin]
            id = "test.service"
            version = "1.0.0"
            game = "test"
            kind = "service"
            name = { en = "Service" }
            [service]
            provider = "companion"
            """;
        var directory = Directory.CreateTempSubdirectory("ft-service-manifest-");
        try
        {
            var path = Path.Combine(directory.FullName, "plugin.toml");
            var normalizedManifest = manifest.ReplaceLineEndings("\n");
            Assert.Contains(before, normalizedManifest, StringComparison.Ordinal);
            File.WriteAllText(path, normalizedManifest.Replace(before, after, StringComparison.Ordinal));
            Assert.Throws<DefinitionException>(() => PluginCatalog.LoadFile(path));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void DuplicateProvidersAndCrossGameSelectionsAreRejectedBeforeActivation()
    {
        var (_, build, packages) = Catalog();
        var foundation = packages.Single(p => p.Service?.Provider == "companion");
        var other = foundation with { Id = "other.communication" };
        Assert.Throws<DefinitionException>(() => LauncherServicePlan.Resolve(Instance(build, foundation, other), build, [foundation, other]));
        Assert.Throws<DefinitionException>(() => LauncherServicePlan.Resolve(Instance(build, foundation) with { Game = "other" }, build, packages));
    }

    [Fact]
    public void ProviderSpecificSettingsCannotSilentlyFallBackOnInvalidMetadata()
    {
        var (_, build, packages) = Catalog();
        var foundation = packages.Single(p => p.Service?.Provider == "companion");
        var option = foundation.Options.Single(o => o.Key == "heartbeat_interval_ms");
        var badType = foundation with { Options = [option with { Type = "bool", Default = true }] };
        var unknown = foundation with { Options = [option with { Key = "unused_option" }] };
        var invalid = foundation with { Options = [option with { Minimum = 0, Default = 0L }] };
        foreach (var plugin in new[] { badType, unknown, invalid })
            Assert.Throws<DefinitionException>(() => LauncherServicePlan.Resolve(Instance(build, plugin), build, [plugin]));
    }

    [Fact]
    public async Task MissingServerAuthorizationIsObservableAndDoesNotInvokeNetworkOrOverlay()
    {
        var (_, build, packages) = Catalog();
        var foundation = packages.Single(p => p.Service?.Provider == "companion");
        var notifications = packages.Single(p => p.Service?.Provider == "overlay-notifications");
        var instance = Instance(build, foundation, notifications);
        var lookups = 0;
        await using var manager = new LauncherServiceManager(_ => { Interlocked.Increment(ref lookups); return null; });
        var completion = new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.StartForClient(instance, build, LauncherServicePlan.Resolve(instance, build, packages), completion.Task);
        await WaitAsync(() => manager.Status(instance.Id).GetValueOrDefault(foundation.Id) == "server-unavailable");
        Assert.Equal("dependency-missing", manager.Status(instance.Id)[notifications.Id]);
        Assert.Equal(0, lookups);
        completion.SetResult(new(instance.Id, 0));
        await WaitAsync(() => manager.Status(instance.Id).Count == 0);
    }

    [Fact]
    public async Task OptionalConsumerDoesNotImplicitlyEnableFoundation()
    {
        var (_, build, packages) = Catalog();
        var notifications = packages.Single(p => p.Service?.Provider == "overlay-notifications");
        var instance = Instance(build, notifications);
        var factories = 0;
        await using var manager = new LauncherServiceManager(_ => null, (_, _, _, _, _) =>
        {
            Interlocked.Increment(ref factories);
            return Task.FromResult<Communication.CompanionSession?>(null);
        });
        var completion = new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.StartForClient(instance, build, LauncherServicePlan.Resolve(instance, build, packages), completion.Task);
        await WaitAsync(() => manager.Status(instance.Id).GetValueOrDefault(notifications.Id) == "dependency-missing");
        Assert.Equal(0, factories);
        await manager.DisposeAsync();
        Assert.Empty(manager.Status(instance.Id));
    }

    [Fact]
    public async Task FactoryReceivesSelectedOptionsAndOnlyDeclaredConsumerMessageGrants()
    {
        var (_, build, packages) = Catalog();
        var foundation = packages.Single(p => p.Service?.Provider == "companion");
        var notifications = packages.Single(p => p.Service?.Provider == "overlay-notifications");
        var instance = Instance(build, foundation, notifications);
        instance = instance with { Plugins = instance.Plugins.Select(p => p.Id == foundation.Id
            ? p with { Options = new Dictionary<string, object> { ["heartbeat_interval_ms"] = 30000L } } : p).ToArray() };
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var manager = new LauncherServiceManager(_ => null, (_, selected, messages, limits, _) =>
        {
            Assert.Equal(build.Id, selected.Id);
            Assert.Equal(TimeSpan.FromSeconds(30), limits.HeartbeatInterval);
            Assert.Collection(messages.Keys.Order(StringComparer.Ordinal),
                type => Assert.Equal("ui.notification.result", type), type => Assert.Equal("ui.notification.show", type));
            observed.SetResult();
            return Task.FromResult<Communication.CompanionSession?>(null);
        });
        var completion = new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.StartForClient(instance, build, LauncherServicePlan.Resolve(instance, build, packages), completion.Task);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        completion.SetResult(new(instance.Id, 0));
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthorizationFailureStaysObservableUntilGameExitOrHostShutdown(bool shutdown)
    {
        var (_, build, packages) = Catalog();
        var foundation = packages.Single(p => p.Service?.Provider == "companion");
        var instance = Instance(build, foundation);
        var attempts = 0;
        await using var manager = new LauncherServiceManager(_ => throw new InvalidOperationException("Overlay must stay dormant."),
            (_, _, _, _, _) =>
            {
                Interlocked.Increment(ref attempts);
                throw new HttpRequestException("Private authorization provider detail.");
            });
        var completion = new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.StartForClient(instance, build, LauncherServicePlan.Resolve(instance, build, packages), completion.Task);
        await WaitAsync(() => manager.Status(instance.Id).GetValueOrDefault(foundation.Id) == "failed");
        Assert.Equal(1, attempts);
        Assert.Single(manager.Status(instance.Id));
        Assert.False(completion.Task.IsCompleted);
        if (shutdown) await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        else completion.SetResult(new(instance.Id, 0));
        await WaitAsync(() => manager.Status(instance.Id).Count == 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualCompositionDeliversNotificationAndStopsWithGameLifetime(bool separateProvider)
    {
        var (_, build, packages) = Catalog();
        var foundation = packages.Single(p => p.Service?.Provider == "companion");
        var notification = separateProvider ? packages.Single(p => p.Service?.Provider == "overlay-notifications") : foundation;
        var instance = separateProvider ? Instance(build, foundation, notification) : Instance(build, foundation);
        await using var pair = await CompanionTests.SocketPair.CreateAsync();
        using var overlay = new NotificationOverlay();
        await using var manager = new LauncherServiceManager(_ => overlay, (_, _, messages, limits, _) =>
        {
            var session = new CompanionSession(new("testgame", "kr-812", "launch-1", "zh-CN", messages),
                _ => ValueTask.FromResult<CompanionAuthorization?>(new(new Uri("wss://example.test/channel"), "test-only")),
                limits, null, null, (_, _, _, _) => CompanionTests.ConnectAsync(pair, types: messages));
            return Task.FromResult<CompanionSession?>(session);
        });
        var completion = new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.StartForClient(instance, build, LauncherServicePlan.Resolve(instance, build, packages), completion.Task);
        await WaitAsync(() => manager.Status(instance.Id).GetValueOrDefault(notification.Id) == (separateProvider ? "ready" : "connected"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var wire = new CompanionEnvelope("event", "manager-notification", "ui.notification.show", 1,
            JsonSerializer.SerializeToElement(new { title = "提示", text = "Managed service notification" }), Epoch: "epoch-1");
        await pair.Server.SendAsync(CompanionWire.Encode(wire, new()), WebSocketMessageType.Text, true, timeout.Token);
        var reply = await CompanionWire.ReceiveAsync(pair.Server, new(), timeout.Token);
        Assert.Equal("queued", reply!.Payload.GetProperty("phase").GetString());
        await WaitAsync(() => overlay.Panel?.Payload is not null);
        overlay.Panel!.Emit("game-ui.mounted", "{}");
        reply = await CompanionWire.ReceiveAsync(pair.Server, new(), timeout.Token);
        Assert.Equal("shown", reply!.Payload.GetProperty("phase").GetString());
        overlay.Panel.Emit("game-ui.action", "{\"name\":\"ok\"}");
        reply = await CompanionWire.ReceiveAsync(pair.Server, new(), timeout.Token);
        Assert.Equal("selected", reply!.Payload.GetProperty("phase").GetString());
        Assert.Equal("manager-notification", reply.Payload.GetProperty("notification_id").GetString());
        Assert.True(overlay.Panel.Disposed);
        completion.SetResult(new(instance.Id, 0));
        await WaitAsync(() => manager.Status(instance.Id).Count == 0);
        Assert.NotEqual(WebSocketState.Open, pair.Client.State);
    }

    [Fact]
    public async Task ReconnectClosesOldNotificationAndRejectsItsLateAction()
    {
        var (_, _, packages) = Catalog();
        var messages = packages.Single(p => p.Service?.Provider == "overlay-notifications").Service!.Messages;
        await using var first = await CompanionTests.SocketPair.CreateAsync();
        await using var second = await CompanionTests.SocketPair.CreateAsync();
        var attempts = 0;
        await using var session = new CompanionSession(new("testgame", "kr-812", "launch-1", "zh-CN", messages),
            _ => ValueTask.FromResult<CompanionAuthorization?>(new(new Uri("wss://example.test/channel"), "test-only")),
            null, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(30),
            (_, _, _, _) => CompanionTests.ConnectAsync(Interlocked.Increment(ref attempts) == 1 ? first : second, types: messages));
        _ = session.Start();
        using var channel = session.CreateChannel("test.notifications", messages);
        await WaitAsync(() => channel.State == CompanionChannelState.Connected);
        using var overlay = new NotificationOverlay();
        await using var notifications = new CompanionOverlayNotifications(channel, overlay);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        async Task NotifyAsync(WebSocket socket, string id)
        {
            var wire = new CompanionEnvelope("event", id, "ui.notification.show", 1,
                JsonSerializer.SerializeToElement(new { title = "提示", text = "通知" }), Epoch: "epoch-1");
            await socket.SendAsync(CompanionWire.Encode(wire, new()), WebSocketMessageType.Text, true, timeout.Token);
            var reply = await CompanionWire.ReceiveAsync(socket, new(), timeout.Token);
            Assert.Equal(id, reply!.Payload.GetProperty("notification_id").GetString());
            Assert.Equal("queued", reply.Payload.GetProperty("phase").GetString());
        }
        await NotifyAsync(first.Server, "old-notification");
        await WaitAsync(() => overlay.Panel?.Payload is not null);
        var oldPanel = overlay.Panel!;
        first.Server.Abort();
        await WaitAsync(() => attempts == 2 && channel.State == CompanionChannelState.Connected);
        Assert.True(oldPanel.Disposed);
        oldPanel.Emit("game-ui.action", "{\"name\":\"ok\"}");
        // The first reply on the fresh socket must belong to its own notification, never an old action.
        await NotifyAsync(second.Server, "fresh-notification");
        await WaitAsync(() => overlay.Panel != oldPanel && overlay.Panel?.Payload is not null);
        overlay.Panel!.Emit("game-ui.mounted", "{}");
        var mounted = await CompanionWire.ReceiveAsync(second.Server, new(), timeout.Token);
        Assert.Equal("fresh-notification", mounted!.Payload.GetProperty("notification_id").GetString());
        Assert.Equal("shown", mounted.Payload.GetProperty("phase").GetString());
    }

    private sealed class NotificationOverlay : IOverlayHost, IDisposable
    {
        internal NotificationPanel? Panel;
        public Task WaitUntilReadyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public IOverlayPanel RegisterPanel(string panelId, OverlayPanelDescriptor descriptor) => Panel = new(panelId);
        public void Dispose() => Panel?.Dispose();
    }

    private sealed class NotificationPanel(string id) : IOverlayPanel
    {
        internal bool Disposed;
        internal object? Payload;
        public string PanelId => id;
        public event EventHandler<OverlayMessageEventArgs>? MessageReceived;
        public void Show() { }
        public void Hide() { }
        public void Send(string eventName, object? data = null) => Payload = data;
        public void MountGameUi(string component, object? props = null) { }
        public void Dispose() => Disposed = true;
        internal void Emit(string name, string data)
        {
            using var document = JsonDocument.Parse(data);
            MessageReceived?.Invoke(this, new(id, name, document.RootElement.Clone()));
        }
    }
}
