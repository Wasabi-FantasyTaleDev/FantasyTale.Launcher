using System.Collections.Concurrent;
using System.Text.Json;
using FantasyTale.Launcher.Communication;
using FantasyTale.Launcher.Host.Window;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

/// <summary>Server notifications and package pages use the game's own controls first and the overlay only as the fallback.</summary>
public sealed class NativeFirstUiTests
{
    [Fact]
    public async Task AReadyNativeMessageBoxShowsTheServerNotificationAndReportsTheSelection()
    {
        var channel = new FakeChannel();
        using var overlay = new FakeOverlay();
        var native = new FakeNative(ready: true) { Outcome = new("", "selected", "ok") };
        await using var notifications = new CompanionOverlayNotifications(channel, overlay, native: native);
        await channel.NotifyAsync("n1", new { title = "公告", text = "服务器维护", buttons = new[] { new { id = "ok", label = "确定" } } });
        await WaitAsync(() => channel.Results.Count >= 3);
        Assert.Equal(["queued", "shown", "selected"], channel.Results.Select(result => result.Phase));
        Assert.Equal("ok", channel.Results.Last().Action);
        var request = Assert.Single(native.Requests);
        Assert.Equal(("messageBox", "公告", "服务器维护"), (request.Kind, request.Title, request.Text));
        Assert.Null(overlay.Panel);
    }

    [Theory]
    [InlineData(false, "selected")]
    [InlineData(true, "notReady")]
    public async Task TheOverlayDialogIsTheFallbackWhenTheNativeBoxCannotShow(bool ready, string invokeStatus)
    {
        var channel = new FakeChannel();
        using var overlay = new FakeOverlay();
        var native = new FakeNative(ready) { InvokeStatus = invokeStatus };
        await using var notifications = new CompanionOverlayNotifications(channel, overlay, native: native);
        await channel.NotifyAsync("n2", new { title = "公告", text = "通知" });
        await WaitAsync(() => overlay.Panel?.Sent is not null);
        Assert.Equal("game-ui.message-box", overlay.Panel!.Sent);
    }

    [Fact]
    public async Task APackagePageGetsItsOwnNativeOperationsWithoutSeeingThePackagePrefix()
    {
        var panel = new FakePanel();
        var native = new FakeNative(ready: true) { Outcome = new("", "selected", "confirm") };
        using var bridge = new PanelNativeUiBridge(panel, "bubblefighter.fantasy.banpick", native);
        panel.Emit("native-ui.request", new
        {
            id = "n1", action = "invoke",
            request = new { operationId = "confirm-1", kind = "messageBox", title = "开局确认", text = "?", icon = "question",
                buttons = new[] { new { id = "confirm", label = "开始" } } },
        });
        var reply = await panel.WaitAsync("native-ui.response");
        Assert.Equal("completed", reply.GetProperty("status").GetString());
        Assert.Equal("confirm-1", reply.GetProperty("result").GetProperty("operationId").GetString());
        Assert.Equal("bubblefighter.fantasy.banpick.confirm-1", Assert.Single(native.Requests).OperationId);
    }

    [Fact]
    public async Task APageWithoutAHookChannelIsToldTheNativeControlsAreUnavailable()
    {
        var panel = new FakePanel();
        using var bridge = new PanelNativeUiBridge(panel, "test.package", null);
        panel.Emit("native-ui.request", new { id = "n1", action = "capabilities" });
        Assert.Equal("unavailable", (await panel.WaitAsync("native-ui.response")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task AServerRequestedMessageBoxIsNativeAndReportsThePlayersAnswer()
    {
        var channel = new NativeChannel();
        using var overlay = new FakeOverlay();
        var native = new FakeNative(ready: true) { Outcome = new("", "selected", "yes") };
        await using var requests = new CompanionNativeUiRequests(channel, native, overlay);
        await channel.InvokeAsync(new { operation_id = "op1", kind = "messageBox", title = "Test", text = "Continue?", icon = "question",
            buttons = new[] { new { id = "yes", label = "Yes" }, new { id = "no", label = "No" } } });
        await WaitAsync(() => channel.Results.Count >= 2);
        Assert.Equal([("shown", "native", null), ("selected", "native", "yes")], channel.Results.Select(r => (r.Status, r.Presenter, r.Button)));
        Assert.Equal("server-op1", Assert.Single(native.Requests).OperationId);
        Assert.Null(overlay.Panel);
    }

    [Fact]
    public async Task AServerRequestFallsBackToTheOverlayOnlyWhenAllowed()
    {
        var channel = new NativeChannel();
        using var overlay = new FakeOverlay();
        await using var requests = new CompanionNativeUiRequests(channel, new FakeNative(ready: false), overlay);
        await channel.InvokeAsync(new { operation_id = "op2", kind = "notification", text = "Hello" });
        await WaitAsync(() => overlay.Panel?.Sent is not null);
        Assert.Equal("game-ui.message-box", overlay.Panel!.Sent);
        await channel.InvokeAsync(new { operation_id = "op3", kind = "chat", text = "", runs = new[] { new { text = "hi", color = "#FFFFFF" } }, fallback = false });
        await WaitAsync(() => channel.Results.Any(r => r.Status == "unsupported"));
    }

    /// <summary>A launch without an overlay can reject a requested fallback after native capability detection.</summary>
    [Fact]
    public async Task AnUnavailableDeferredOverlayReturnsAnUnsupportedResult()
    {
        var channel = new NativeChannel();
        using var overlay = new FakeOverlay { Unavailable = true };
        await using var requests = new CompanionNativeUiRequests(channel, new FakeNative(false), overlay);
        await channel.InvokeAsync(new { operation_id = "no-overlay", kind = "notification", text = "Notice" });
        await WaitAsync(() => !channel.Results.IsEmpty);
        Assert.Equal("unsupported", Assert.Single(channel.Results).Status);
        Assert.Null(overlay.Panel);
    }

    [Fact]
    public async Task ABrokenHookChannelFallsBackToTheOverlay()
    {
        var channel = new NativeChannel();
        using var overlay = new FakeOverlay();
        await using var requests = new CompanionNativeUiRequests(channel, new FakeNative(ready: true) { Broken = true }, overlay);
        await channel.InvokeAsync(new { operation_id = "op5", kind = "messageBox", title = "T", text = "?", icon = "question",
            buttons = new[] { new { id = "ok", label = "OK" } } });
        await WaitAsync(() => overlay.Panel?.Sent is not null);
        Assert.Equal("game-ui.message-box", overlay.Panel!.Sent);
        Assert.DoesNotContain(channel.Results, result => result.Status == "failed");
    }

    [Fact]
    public async Task ANativeNotificationIsReportedShownOnceTheGameTookIt()
    {
        var channel = new NativeChannel();
        await using var requests = new CompanionNativeUiRequests(channel, new FakeNative(ready: true, kind: "notification"), null);
        await channel.InvokeAsync(new { operation_id = "op4", kind = "notification", text = "Server restarts soon" });
        await WaitAsync(() => !channel.Results.IsEmpty);
        Assert.Equal(("shown", "native"), (channel.Results.Single().Status, channel.Results.Single().Presenter));
    }

    private sealed record NativeResult(string Status, string Presenter, string? Button);

    /// <summary>A hook-internal polling deadline produces a response even when the owning overlay remains active.</summary>
    [Fact]
    public async Task APanelResultDeadlineDoesNotLeaveTheCallerWaitingWithoutAReply()
    {
        var panel = new FakePanel();
        var native = new FakeNative(true) { ResultCanceled = true };
        using var bridge = new PanelNativeUiBridge(panel, "test.package", native);
        panel.Emit("native-ui.request", new { id = "poll", action = "result", operation_id = "operation" });
        Assert.Equal("failed", (await panel.WaitAsync("native-ui.response")).GetProperty("status").GetString());
    }

    /// <summary>A control mutation whose hook polling times out is reported as uncertain and is not replayed.</summary>
    [Fact]
    public async Task ACompanionControlDeadlineReportsAnUnknownOutcome()
    {
        var channel = new NativeChannel();
        var native = new FakeNative(true, "control.setText") { ResultCanceled = true };
        await using var requests = new CompanionNativeUiRequests(channel, native, null);
        await channel.InvokeAsync(new { operation_id = "deadline", kind = "control.setText", text = "New", target = new { id = "control-1" } });
        await WaitAsync(() => !channel.Results.IsEmpty);
        Assert.Equal("outcomeUnknown", Assert.Single(channel.Results).Status);
        Assert.Single(native.Requests);
    }

    /// <summary>An interrupted control submission cannot be reported as unavailable because it may have executed.</summary>
    [Theory]
    [InlineData("io")]
    [InlineData("timeout")]
    [InlineData("deadline")]
    public async Task ACompanionControlSubmissionFailureReportsAnUnknownOutcome(string failure)
    {
        var channel = new NativeChannel();
        using var overlay = new FakeOverlay();
        var native = new FakeNative(true, "control.activate")
        {
            InvokeError = failure switch
            {
                "io" => new IOException("The submitted reply was interrupted."),
                "timeout" => new TimeoutException("The submitted reply timed out."),
                _ => new OperationCanceledException("The internal deadline expired."),
            },
        };
        await using var requests = new CompanionNativeUiRequests(channel, native, overlay);
        await channel.InvokeAsync(new { operation_id = "submit", kind = "control.activate", target = new { id = "control-1" } });
        await WaitAsync(() => !channel.Results.IsEmpty);
        Assert.Equal("outcomeUnknown", Assert.Single(channel.Results).Status);
        Assert.Single(native.Requests);
        Assert.Null(overlay.Panel);
    }

    /// <summary>Companion callers receive terminal control metadata without an incorrect presentation-only outcome.</summary>
    [Theory]
    [InlineData("control.inspect", "completed")]
    [InlineData("control.setText", "ambiguous")]
    [InlineData("control.events", "completed")]
    public async Task ControlResultsWaitForExecutionAndForwardSnapshotsAndEvents(string kind, string status)
    {
        var channel = new NativeChannel();
        using var overlay = new FakeOverlay();
        var native = new FakeNative(ready: true, kind: kind)
        {
            Outcome = new("", status)
            {
                Controls = [new("control-1", "Ui/DlgCreateRoom.ui", "edtRoomTitle", "edit", "Room", true)],
                Events = [new(7, "control-2", "click")], Cursor = 7,
            },
        };
        await using var requests = new CompanionNativeUiRequests(channel, native, overlay);
        await channel.InvokeAsync(new { operation_id = "control", kind, text = "", target = kind == "control.events" ? null : new { name = "edtRoomTitle" } });
        await WaitAsync(() => !channel.Results.IsEmpty);
        Assert.Equal((status, "native"), (channel.Results.Single().Status, channel.Results.Single().Presenter));
        var result = Assert.Single(channel.Payloads);
        Assert.Equal("Room", result.GetProperty("controls")[0].GetProperty("text").GetString());
        Assert.Equal("control-2", result.GetProperty("events")[0].GetProperty("control_id").GetString());
        Assert.Equal(7, result.GetProperty("cursor").GetInt64());
        Assert.Null(overlay.Panel);
    }

    /// <summary>Malformed control-only input produces a correlated failure instead of an unobserved task exception.</summary>
    [Fact]
    public async Task MalformedControlCursorReturnsFailureInsteadOfEscapingTheSubscription()
    {
        var channel = new NativeChannel();
        await using var requests = new CompanionNativeUiRequests(channel, new FakeNative(true, "control.events"), null);
        await channel.InvokeAsync(new { operation_id = "bad-cursor", kind = "control.events", after_sequence = "invalid" });
        await WaitAsync(() => !channel.Results.IsEmpty);
        Assert.Equal("failed", Assert.Single(channel.Results).Status);
    }

    /// <summary>Companion selectors use the same snake-case convention as their outer message and returned snapshots.</summary>
    [Fact]
    public async Task CompanionArraySelectorsUseTheWireNamingPolicy()
    {
        var channel = new NativeChannel();
        var native = new FakeNative(true, "control.activate") { Outcome = new("", "completed") };
        await using var requests = new CompanionNativeUiRequests(channel, native, null);
        await channel.InvokeAsync(new { operation_id = "array", kind = "control.activate", target = new { name = "btnGameMode", array_index = 0 } });
        await WaitAsync(() => !channel.Results.IsEmpty);
        Assert.Equal(0, Assert.Single(native.Requests).Target!.ArrayIndex);
        Assert.Equal("completed", Assert.Single(channel.Results).Status);
    }

    private sealed class NativeChannel : ICompanionChannel
    {
        private static readonly JsonSerializerOptions WireJson = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        private Func<CompanionEvent, CancellationToken, ValueTask>? _invoke;
        internal ConcurrentQueue<NativeResult> Results { get; } = new();
        internal ConcurrentQueue<JsonElement> Payloads { get; } = new();
        public CompanionChannelState State => CompanionChannelState.Connected;
        public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>(StringComparer.Ordinal) { "ui.native.invoke", "ui.native.result" };
        public ValueTask<CompanionOperationResult> SendAsync(string type, int version, object? payload = null, CancellationToken cancellationToken = default)
        {
            var element = JsonSerializer.SerializeToElement(payload, WireJson);
            Payloads.Enqueue(element);
            Results.Enqueue(new(element.GetProperty("status").GetString()!, element.GetProperty("presenter").GetString()!,
                element.GetProperty("button_id").ValueKind == JsonValueKind.String ? element.GetProperty("button_id").GetString() : null));
            return ValueTask.FromResult(new CompanionOperationResult(CompanionOperationStatus.Accepted));
        }
        public ValueTask<CompanionOperationResult> RequestAsync(string type, int version, object? payload = null,
            CompanionRequestOptions? options = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CompanionOperationResult(CompanionOperationStatus.Rejected));
        public IDisposable Subscribe(string type, int version, Func<CompanionEvent, ValueTask> handler) => Subscribe(type, version, (message, _) => handler(message));
        public IDisposable Subscribe(string type, int version, Func<CompanionEvent, CancellationToken, ValueTask> handler)
        {
            _invoke = handler;
            return new Release();
        }
        internal async Task InvokeAsync(object payload) =>
            await _invoke!(new("ui.native.invoke", 1, Guid.NewGuid().ToString("N"), JsonSerializer.SerializeToElement(payload)), CancellationToken.None);
        public void Dispose() { }
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(20);
        Assert.True(condition());
    }

    private sealed class FakeNative(bool ready, string kind = "messageBox") : IGameNativeUi
    {
        internal ConcurrentQueue<GameNativeUiRequest> Requests { get; } = new();
        internal string InvokeStatus { get; init; } = "queued";
        internal GameNativeUiResult Outcome { get; init; } = new("", "shown");
        internal bool Broken { get; init; }
        internal bool ResultCanceled { get; init; }
        internal Exception? InvokeError { get; init; }
        public ValueTask<GameNativeUiCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default) =>
            Broken ? throw new IOException("The hook channel closed.")
                : ValueTask.FromResult(new GameNativeUiCapabilities("kr-812", [new(kind, true, ready, [])]));
        public ValueTask<GameNativeUiResult> InvokeAsync(GameNativeUiRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);
            if (InvokeError is not null) throw InvokeError;
            return ValueTask.FromResult(new GameNativeUiResult(request.OperationId, InvokeStatus));
        }
        public ValueTask<GameNativeUiResult> GetResultAsync(string operationId, CancellationToken cancellationToken = default) =>
            ResultCanceled ? throw new OperationCanceledException("The hook polling deadline expired.")
                : ValueTask.FromResult(Outcome with { OperationId = operationId });
    }

    private sealed record Result(string Phase, string? Action);

    private sealed class FakeChannel : ICompanionChannel
    {
        private Func<CompanionEvent, CancellationToken, ValueTask>? _show;
        internal ConcurrentQueue<Result> Results { get; } = new();
        public CompanionChannelState State => CompanionChannelState.Connected;
        public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>(StringComparer.Ordinal) { "ui.notification.show", "ui.notification.result" };
        public ValueTask<CompanionOperationResult> SendAsync(string type, int version, object? payload = null, CancellationToken cancellationToken = default)
        {
            var element = JsonSerializer.SerializeToElement(payload);
            Results.Enqueue(new(element.GetProperty("phase").GetString()!,
                element.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.String ? action.GetString() : null));
            return ValueTask.FromResult(new CompanionOperationResult(CompanionOperationStatus.Accepted));
        }
        public ValueTask<CompanionOperationResult> RequestAsync(string type, int version, object? payload = null,
            CompanionRequestOptions? options = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CompanionOperationResult(CompanionOperationStatus.Rejected));
        public IDisposable Subscribe(string type, int version, Func<CompanionEvent, ValueTask> handler) => Subscribe(type, version, (message, _) => handler(message));
        public IDisposable Subscribe(string type, int version, Func<CompanionEvent, CancellationToken, ValueTask> handler)
        {
            _show = handler;
            return new Release();
        }
        internal async Task NotifyAsync(string id, object payload) =>
            await _show!(new("ui.notification.show", 1, id, JsonSerializer.SerializeToElement(payload)), CancellationToken.None);
        public void Dispose() { }
    }

    private sealed class Release : IDisposable { public void Dispose() { } }

    private sealed class FakeOverlay : IOverlayHost, IDisposable
    {
        internal FakePanel? Panel;
        internal bool Unavailable { get; init; }
        public Task WaitUntilReadyAsync(CancellationToken cancellationToken = default) =>
            Unavailable ? throw new NotSupportedException("This launch has no overlay.") : Task.CompletedTask;
        public IOverlayPanel RegisterPanel(string panelId, OverlayPanelDescriptor descriptor) => Panel = new FakePanel();
        public void Dispose() => Panel?.Dispose();
    }

    private sealed class FakePanel : IOverlayPanel
    {
        private readonly ConcurrentQueue<(string Name, JsonElement Data)> _sent = new();
        internal string? Sent;
        public string PanelId => "test.panel";
        public event EventHandler<OverlayMessageEventArgs>? MessageReceived;
        public void Show() { }
        public void Hide() { }
        public void Send(string eventName, object? data = null)
        {
            Sent = eventName;
            _sent.Enqueue((eventName, JsonSerializer.SerializeToElement(data)));
        }
        public void MountGameUi(string component, object? props = null) { }
        public void Dispose() { }
        internal void Emit(string name, object data) => MessageReceived?.Invoke(this, new(PanelId, name, JsonSerializer.SerializeToElement(data)));
        internal async Task<JsonElement> WaitAsync(string name)
        {
            for (var i = 0; i < 300; i++)
            {
                var match = _sent.FirstOrDefault(entry => entry.Name == name);
                if (match.Name is not null) return match.Data;
                await Task.Delay(10);
            }
            throw new TimeoutException(name + " was not sent.");
        }
    }
}
