using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using FantasyTale.Launcher.Communication;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Host.Window;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

[Collection("native hook runtime")]
public sealed class NativeCompanionTests
{
    private static readonly Dictionary<string, int> Routes = new(StringComparer.Ordinal) { ["test.echo"] = 1 };
    private static Dictionary<string, IReadOnlyDictionary<string, int>> Grants => new(StringComparer.Ordinal) { ["test.native"] = Routes };
    private static string Repository
    {
        get
        {
            var directory = TestRepository.StartDirectory();
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
            return directory!.FullName;
        }
    }

    [Fact]
    public async Task RouterRejectsUnknownOwnersAndSchemasAndKeepsEpochQueuesBounded()
    {
        using var epoch = new CancellationTokenSource();
        using var next = new CancellationTokenSource();
        var channel = new TestChannel();
        using var router = new NativeCompanionRouter(Grants, (_, _) => channel);
        var badOwner = await router.HandleAsync(new("other.plugin", "state"), default);
        var badSchema = await router.HandleAsync(new("test.native", "send", "test.echo", 2, "{}"), default);
        Assert.Equal("invalid", badOwner.Result);
        Assert.Equal("invalid", badSchema.Result);
        Assert.Equal("ok", (await router.HandleAsync(new("test.native", "subscribe", "test.echo"), default)).Result);
        for (var i = 0; i < 33; i++) await channel.EmitAsync("old-" + i, epoch.Token);
        var overflow = await router.HandleAsync(new("test.native", "poll"), default);
        Assert.Contains("companion.events-overflow", overflow.Data);
        epoch.Cancel();
        Assert.Equal("not-found", (await router.HandleAsync(new("test.native", "poll"), default)).Result);
        await channel.EmitAsync("fresh", next.Token);
        var current = await router.HandleAsync(new("test.native", "poll"), default);
        Assert.Contains("fresh", current.Data);
        Assert.DoesNotContain("old-", current.Data);
        await router.HandleAsync(new("test.native", "unsubscribe", "test.echo"), default);
        Assert.Null(channel.Handler);
        router.Dispose();
        Assert.True(channel.Disposed);
        Assert.Equal("not-found", (await router.HandleAsync(new("test.native", "state"), default)).Result);
    }

    [Fact]
    public void NativeManifestGrantsAreOptionalAndConflictBeforeLaunch()
    {
        var directory = Directory.CreateTempSubdirectory("ft-native-companion-");
        try
        {
            const string text = """
                schema = 1
                [plugin]
                id = "test.native"
                version = "1.0.0"
                game = "bubblefighter"
                kind = "native"
                runtime = ">=3.3.3"
                name = { en = "Native consumer" }
                [native]
                library = "test.dll"
                [companion.messages]
                "test.echo" = 1
                """;
            var file = Path.Combine(directory.FullName, "plugin.toml");
            File.WriteAllText(file, text);
            var plugin = PluginCatalog.LoadFile(file);
            Assert.Equal(1, plugin.CompanionMessages["test.echo"]);
            var game = GameCatalog.Load(Path.Combine(Repository, "games")).Single(g => g.Id == "bubblefighter");
            var build = game.Builds.Single(b => b.Id == "kr-812");
            var instance = new InstanceDefinition("test-native", "Native", game.Id, build.Id, null,
                [new(plugin.Id, plugin.Version, true, false, new Dictionary<string, object>())]);
            var consumers = LauncherServicePlan.NativeConsumers(instance, build, [plugin]);
            Assert.Equal(1, LauncherServicePlan.MessageGrants([], consumers)["test.echo"]);
            var conflicting = new Dictionary<string, IReadOnlyDictionary<string, int>>(consumers)
            { ["test.other"] = new Dictionary<string, int> { ["test.echo"] = 2 } };
            Assert.Throws<DefinitionException>(() => LauncherServicePlan.MessageGrants([], conflicting));
            foreach (var invalid in new[]
            {
                text.Replace("\"test.echo\" = 1", "\"test.echo\" = 0", StringComparison.Ordinal),
                text.Replace("\"test.echo\" = 1", "\"test.echo\" = 1.0", StringComparison.Ordinal),
                text.Replace("\"test.echo\" = 1", "\"test.echo\" = true", StringComparison.Ordinal),
                text.Replace("\"test.echo\" = 1", "\"Test.echo\" = 1", StringComparison.Ordinal),
                text.Replace("[companion.messages]", "[companion]\nendpoint = \"wss://example.test\"\n[companion.messages]", StringComparison.Ordinal),
            })
            {
                File.WriteAllText(file, invalid);
                Assert.Throws<DefinitionException>(() => PluginCatalog.LoadFile(file));
            }
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void FailedOptionalBrokerConstructionReleasesTheMainControlPipe()
    {
        if (!OperatingSystem.IsWindows()) return;
        var launch = Guid.NewGuid().ToString("N");
        using var occupied = new CompanionBrokerChannel(launch);
        Assert.Throws<UnauthorizedAccessException>(() => new HookChannel(launch, enableCompanionBroker: true));
        // The failed constructor must not leave its already-created control pipe reserved.
        using var recovered = new HookChannel(launch);
    }

    [Fact]
    public async Task X86RuntimeClientUsesActualManagerPipeAndBidirectionalWebSocketWithoutOverlay()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var pair = await CompanionTests.SocketPair.CreateAsync();
        var game = GameCatalog.Load(Path.Combine(Repository, "games")).Single(g => g.Id == "bubblefighter");
        var build = game.Builds.Single(b => b.Id == "kr-812");
        var foundation = PluginCatalog.Load(Path.Combine(game.Folder, "plugins")).Single(p => p.Service?.Provider == "companion");
        var instance = new InstanceDefinition("test-native", "Native", game.Id, build.Id, null,
            [new(foundation.Id, foundation.Version, true, false, new Dictionary<string, object>())]);
        var launchId = Guid.NewGuid().ToString("N");
        using var control = new HookChannel(launchId, enableCompanionBroker: true);
        await using var manager = new LauncherServiceManager(_ => throw new InvalidOperationException("Overlay must not be consulted."),
            (_, _, messages, limits, _) => Task.FromResult<CompanionSession?>(new(new("testgame", build.Id, "launch-1", "zh-CN", messages),
                _ => ValueTask.FromResult<CompanionAuthorization?>(new(new Uri("wss://example.test/channel"), "test-only")),
                limits, null, null, (_, _, _, _) => CompanionTests.ConnectAsync(pair, types: messages))));
        var gameEnded = new TaskCompletionSource<LaunchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.StartForClient(instance, build, LauncherServicePlan.Resolve(instance, build, [foundation]), gameEnded.Task, Grants, control);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (manager.Status(instance.Id).GetValueOrDefault(foundation.Id) != "connected") await Task.Delay(10, timeout.Token);
        async Task ServerAsync()
        {
            var sent = await CompanionWire.ReceiveAsync(pair.Server, new(), timeout.Token);
            Assert.Equal("event", sent!.Kind);
            Assert.Equal("中文 한국어", sent.Payload.GetProperty("text").GetString());
            await pair.Server.SendAsync(CompanionWire.Encode(new("event", "native-notice", "test.echo", 1,
                JsonSerializer.SerializeToElement(new { notice = "通知" }), Epoch: "epoch-1"), new()), WebSocketMessageType.Text, true, timeout.Token);
            var request = await CompanionWire.ReceiveAsync(pair.Server, new(), timeout.Token);
            Assert.Equal("request", request!.Kind);
            await pair.Server.SendAsync(CompanionWire.Encode(new("response", "native-response", "test.echo", 1,
                JsonSerializer.SerializeToElement(new { acknowledged = true }), request.Id, "epoch-1"), new()), WebSocketMessageType.Text, true, timeout.Token);
        }
        var server = ServerAsync();
        var start = new ProcessStartInfo(Path.Combine(Repository, "native", "out", "Release", "fthook_tests.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--companion-probe");
        start.ArgumentList.Add("FantasyTale.CompanionBroker." + launchId);
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var probe = Process.Start(start)!;
        control.BindProcess(probe.Id);
        try
        {
            await Task.WhenAll(server, probe.WaitForExitAsync(timeout.Token));
            Assert.Equal(0, probe.ExitCode);
            Assert.Contains("companion-probe: passed", await probe.StandardOutput.ReadToEndAsync(timeout.Token));
        }
        finally
        {
            if (!probe.HasExited) { probe.Kill(); await probe.WaitForExitAsync(); }
            gameEnded.TrySetResult(new(instance.Id, 0));
        }
        await manager.DisposeAsync();
        Assert.Empty(manager.Status(instance.Id));
        Assert.NotEqual(WebSocketState.Open, pair.Client.State);
    }

    private sealed class TestChannel : ICompanionChannel
    {
        internal Func<CompanionEvent, CancellationToken, ValueTask>? Handler;
        internal bool Disposed;
        public CompanionChannelState State => CompanionChannelState.Connected;
        public IReadOnlySet<string> Capabilities { get; } = new HashSet<string> { "test.echo" };
        public ValueTask<CompanionOperationResult> SendAsync(string type, int version, object? payload = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CompanionOperationResult(CompanionOperationStatus.Accepted));
        public ValueTask<CompanionOperationResult> RequestAsync(string type, int version, object? payload = null,
            CompanionRequestOptions? options = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CompanionOperationResult(CompanionOperationStatus.Completed));
        public IDisposable Subscribe(string type, int version, Func<CompanionEvent, ValueTask> handler) => Subscribe(type, version, (message, _) => handler(message));
        public IDisposable Subscribe(string type, int version, Func<CompanionEvent, CancellationToken, ValueTask> handler)
        { Handler = handler; return new Subscription(() => Handler = null); }
        internal ValueTask EmitAsync(string id, CancellationToken token) => Handler!(new("test.echo", 1, id, null), token);
        public void Dispose() { Disposed = true; Handler = null; }
        private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    }
}
