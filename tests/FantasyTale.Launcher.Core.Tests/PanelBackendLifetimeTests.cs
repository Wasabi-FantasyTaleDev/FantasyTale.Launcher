using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Host.Window;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class PanelBackendLifetimeTests
{
    [Fact]
    public void ControllerDeclarationIsOptionalAndRequiresAPresentationEntry()
    {
        Assert.Null(Package().Overlay!.Controller);
        Assert.Equal("feature-controller", Package("feature-controller").Overlay!.Controller);
        Assert.Throws<DefinitionException>(() => PluginCatalog.Parse("plugin.toml",
            Manifest("feature-controller").Replace("entry = \"panel.html\"", "entry = \"\"", StringComparison.Ordinal)));
        Assert.Throws<DefinitionException>(() => PluginCatalog.Parse("plugin.toml",
            Manifest("feature-controller").Replace("kind = \"overlay\"", "kind = \"patch\"", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../controller")]
    [InlineData("C:/controller")]
    [InlineData("feature controller")]
    [InlineData(".controller")]
    public void ControllerDeclarationRejectsPathsAndMalformedIdentities(string controller) =>
        Assert.Throws<DefinitionException>(() => Package(controller));

    [Fact]
    public async Task ControllerStartsWithoutPageActivityAndReceivesOnlyItsNativeOwner()
    {
        var panel = new FakePanel();
        var native = new ScopedNative();
        var provider = new ObservingProvider();
        Action<string, string> record = (_, _) => { };
        await using var lifetime = new PanelBackendLifetime();
        lifetime.Start(Package("feature-controller"), Build, panel, native, "ko", provider, record);

        await provider.Observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("fixture.panel", native.Owner);
        Assert.Same(native.Scoped, provider.Context!.NativeUi);
        Assert.Equal("ko", provider.Context.Locale);
        Assert.Same(record, provider.Context.Record);
        Assert.False(panel.Shown);
        Assert.Empty(panel.Messages);

        panel.Emit("native-ui.request", new { id = "page1", action = "capabilities" });
        Assert.Equal(1, native.Scoped.CapabilityReads);
        Assert.Empty(panel.Messages);
        await lifetime.DisposeAsync();
        Assert.True(provider.Stopped);
    }

    [Fact]
    public async Task DeclaredControllerNeverFallsBackToPageNativeRequests()
    {
        await using var lifetime = new PanelBackendLifetime();
        var panel = new FakePanel();
        var native = new ScopedNative();
        var package = Package("feature-controller");
        Assert.Throws<InvalidOperationException>(() => lifetime.Start(package, Build, panel, native, "en", null));
        Assert.Throws<InvalidOperationException>(() => lifetime.Start(package, Build, panel, native, "en",
            new FactoryProvider(_ => throw new InvalidOperationException("Controller unavailable."))));
        panel.Emit("native-ui.request", new { id = "page1", action = "capabilities" });
        Assert.Equal(0, native.Scoped.CapabilityReads);
        Assert.Empty(panel.Messages);
    }

    [Fact]
    public async Task ExistingPanelsRetainTheirScopedNativeBridgeAndReleaseIt()
    {
        var lifetime = new PanelBackendLifetime();
        var panel = new FakePanel();
        var native = new ScopedNative();
        lifetime.Start(Package(), Build, panel, native, "en", null);
        panel.Emit("native-ui.request", new { id = "page1", action = "capabilities" });
        Assert.Equal("fixture.panel", native.Owner);
        Assert.Equal(1, native.Scoped.CapabilityReads);
        Assert.Equal("native-ui.response", Assert.Single(panel.Messages));
        await lifetime.DisposeAsync();
        panel.Emit("native-ui.request", new { id = "page2", action = "capabilities" });
        Assert.Equal(1, native.Scoped.CapabilityReads);
        Assert.Single(panel.Messages);
    }

    [Fact]
    public async Task TeardownAwaitsControllersInReverseOrderAndRefusesNewStarts()
    {
        var lifetime = new PanelBackendLifetime();
        var first = new HeldLifetime();
        var second = new HeldLifetime();
        var providers = new Queue<IAsyncDisposable>([first, second]);
        var provider = new FactoryProvider(_ => providers.Dequeue());
        lifetime.Start(Package("first-controller"), Build, new FakePanel(), null, null, provider);
        lifetime.Start(Package("second-controller"), Build, new FakePanel(), null, null, provider);
        var stopping = lifetime.DisposeAsync().AsTask();
        Assert.Same(stopping, lifetime.DisposeAsync().AsTask());
        await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(first.Started.Task.IsCompleted);
        Assert.False(stopping.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => lifetime.Start(Package(), Build, new FakePanel(), null, null, null));
        second.Finish.SetResult();
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stopping.IsCompleted);
        first.Finish.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task FailedControllerCleanupDoesNotSkipEarlierControllers()
    {
        var lifetime = new PanelBackendLifetime();
        var first = new HeldLifetime();
        first.Finish.SetResult();
        var second = new HeldLifetime();
        second.Finish.SetException(new IOException("Cleanup failed."));
        var providers = new Queue<IAsyncDisposable>([first, second]);
        var provider = new FactoryProvider(_ => providers.Dequeue());
        lifetime.Start(Package("first-controller"), Build, new FakePanel(), null, null, provider);
        lifetime.Start(Package("second-controller"), Build, new FakePanel(), null, null, provider);
        var error = await Assert.ThrowsAsync<AggregateException>(() => lifetime.DisposeAsync().AsTask());
        Assert.IsType<IOException>(Assert.Single(error.InnerExceptions));
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    private static PluginDefinition Package(string? controller = null) => PluginCatalog.Parse("plugin.toml", Manifest(controller));

    private static string Manifest(string? controller) => """
        schema = 1
        [plugin]
        id = "fixture.panel"
        version = "1.0.0"
        game = "fixture"
        kind = "overlay"
        name = { en = "Fixture panel" }
        [overlay]
        entry = "panel.html"
        """ + (controller is null ? "" : "\ncontroller = " + JsonSerializer.Serialize(controller));

    private static readonly BuildDefinition Build = new("fixture-1", "fixture", 1, new(new Dictionary<string, string> { ["en"] = "Fixture" }),
        "game.exe", "standin", "", null, null, "hook", [], []);

    private sealed class FactoryProvider(Func<GameOverlayControllerContext, IAsyncDisposable> start) : IGameOverlayControllerProvider
    {
        public IAsyncDisposable StartController(GameOverlayControllerContext context) => start(context);
    }

    private sealed class ObservingProvider : IGameOverlayControllerProvider, IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private Task? _work;
        internal TaskCompletionSource Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal GameOverlayControllerContext? Context { get; private set; }
        internal bool Stopped { get; private set; }

        public IAsyncDisposable StartController(GameOverlayControllerContext context)
        {
            Context = context;
            _work = Task.Run(async () =>
            {
                await context.NativeUi!.GetCapabilitiesAsync(_stop.Token);
                Observed.SetResult();
                try { await Task.Delay(Timeout.Infinite, _stop.Token); }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
                Stopped = true;
            });
            return this;
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _work!;
            _stop.Dispose();
        }
    }

    private sealed class HeldLifetime : IAsyncDisposable
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        public async ValueTask DisposeAsync() { Calls++; Started.SetResult(); await Finish.Task; }
    }

    private sealed class FakePanel : IOverlayPanel
    {
        public string PanelId => "fixture.panel";
        public event EventHandler<OverlayMessageEventArgs>? MessageReceived;
        internal bool Shown { get; private set; }
        internal List<string> Messages { get; } = [];
        public void Show() => Shown = true;
        public void Hide() => Shown = false;
        public void Send(string eventName, object? data = null) => Messages.Add(eventName);
        public void MountGameUi(string component, object? props = null) { }
        public void Dispose() { }
        internal void Emit(string name, object data) => MessageReceived?.Invoke(this, new(PanelId, name, JsonSerializer.SerializeToElement(data)));
    }

    private sealed class ScopedNative : IGameNativeUi, IGameNativeUiScopes
    {
        internal FakeNative Scoped { get; } = new();
        internal string? Owner { get; private set; }
        public IGameNativeUi ForOwner(string owner) { Owner = owner; return Scoped; }
        public ValueTask<GameNativeUiCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unscoped native read.");
        public ValueTask<GameNativeUiResult> InvokeAsync(GameNativeUiRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unscoped native invocation.");
        public ValueTask<GameNativeUiResult> GetResultAsync(string operationId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unscoped native result.");
    }

    private sealed class FakeNative : IGameNativeUi
    {
        internal int CapabilityReads { get; private set; }
        public ValueTask<GameNativeUiCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
        {
            CapabilityReads++;
            return ValueTask.FromResult(new GameNativeUiCapabilities("fixture-1", []));
        }
        public ValueTask<GameNativeUiResult> InvokeAsync(GameNativeUiRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GameNativeUiResult(request.OperationId, "completed"));
        public ValueTask<GameNativeUiResult> GetResultAsync(string operationId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GameNativeUiResult(operationId, "completed"));
    }
}
