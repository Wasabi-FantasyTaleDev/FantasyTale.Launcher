using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Sdk;
using Microsoft.Win32.SafeHandles;

namespace FantasyTale.Launcher.Host.Window;

/// <summary>Owns optional x64 overlay processes for running game clients.</summary>
internal sealed partial class OverlayProcessManager : IAsyncDisposable
{
    private const int ProtocolVersion = 1;
    private const int MaximumHandshakeBytes = 16 * 1024;
    private readonly string _overlayExecutable;
    private readonly HostLifecycleLog? _log;
    private OverlayTestSettings _test;
    private readonly object _gate = new();
    private readonly Dictionary<string, OverlaySession> _sessions = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _runs = [];
    private readonly CancellationTokenSource _stop = new();
    private Task? _dispose;
    private int _disposed;

    /// <summary>Creates a manager that starts the overlay executable beside the launcher.</summary>
    internal OverlayProcessManager(string applicationDirectory, HostLifecycleLog? log = null, OverlayTestSettings? test = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        _overlayExecutable = Path.Combine(applicationDirectory, "overlay", "FantasyTale.Launcher.Overlay.exe");
        _log = log;
        _test = test ?? new(false, null);
    }

    internal OverlayTestSettings TestSettings => Volatile.Read(ref _test);

    internal void SetTestSettings(OverlayTestSettings settings) => Volatile.Write(ref _test, settings);

    /// <summary>Starts an overlay only when the selected instance contains an available overlay package.</summary>
    internal void StartForClient(InstanceDefinition instance, BuildDefinition build, IReadOnlyList<PluginDefinition> packages,
        RunningClient client, string? locale = null, IGameOverlayControllerProvider? controllerProvider = null,
        Func<string, CancellationToken, Task<ICompanionChannel?>>? controllerCompanion = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(client);
        if (!IsEnabledForClient(instance, build, packages))
        {
            WriteLog("overlay-not-enabled");
            return;
        }
        WriteLog("overlay-start-requested");
        var panels = packages.Where(package => package.Kind == "overlay" && package.Overlay?.Entry is { Length: > 0 }
            && PluginCatalog.IsAvailable(package, build)
            && instance.Plugins.Any(selection => selection.Id == package.Id && selection.Version == package.Version && selection.Enabled)).ToArray();
        WriteLog("overlay-entry-count", panels.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var packageFolders = PluginRequirements.OverlayPackageFolders(instance, build, packages, locale);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            var work = Task.Run(() => RunSessionAsync(instance.Id, build, panels, packageFolders, client.ProcessId,
                client.Control, client.Completion, TestSettings, locale, controllerProvider, controllerCompanion));
            _runs.Add(work);
            _ = work.ContinueWith(completed =>
            {
                lock (_gate) _runs.Remove(completed);
                if (completed.IsFaulted) WriteLog("overlay-session-failed", completed.Exception!.GetType().Name);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    /// <summary>Returns whether an installed and enabled overlay package applies to a build.</summary>
    internal static bool IsEnabled(InstanceDefinition instance, BuildDefinition build, IReadOnlyList<PluginDefinition> packages) =>
        packages.Any(package => package.Kind == "overlay" && package.Overlay is not null
            && PluginCatalog.IsAvailable(package, build)
            && instance.Plugins.Any(selection => selection.Id == package.Id && selection.Version == package.Version && selection.Enabled));

    internal bool IsEnabledForClient(InstanceDefinition instance, BuildDefinition build, IReadOnlyList<PluginDefinition> packages) =>
        TestSettings.StartupMessage || IsEnabled(instance, build, packages);

    internal IOverlayHost? GetHost(string instanceId)
    {
        lock (_gate) return _sessions.TryGetValue(instanceId, out var session) && session.IsReady ? session : null;
    }

    /// <summary>Returns the panel an enabled overlay package's entry page was registered on, once the overlay is ready.</summary>
    internal IOverlayPanel? GetEntryPanel(string instanceId, string packageId)
    {
        lock (_gate) return _sessions.TryGetValue(instanceId, out var session) && session.IsReady
            && session.EntryPanels.TryGetValue(packageId, out var panel) ? panel : null;
    }

    private async Task RunSessionAsync(string instanceId, BuildDefinition build, IReadOnlyList<PluginDefinition> panels,
        IReadOnlyDictionary<string, string> packageFolders, int gameProcessId, HookChannel? control,
        Task<LaunchResult> gameCompletion, OverlayTestSettings test, string? locale,
        IGameOverlayControllerProvider? controllerProvider,
        Func<string, CancellationToken, Task<ICompanionChannel?>>? controllerCompanion)
    {
        OverlaySession? session = null;
        Process? overlay = null;
        NamedPipeServerStream? pipe = null;
        IOverlayPanel? startupTest = null;
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stopRegistration = _stop.Token.Register(() => stopped.TrySetResult());
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var profile = build.Overlay ?? new OverlayBuildProfile(build.BuildDate is { Year: <= 2012 } ? "v1" : "v2", [], []);
            var buildContext = new OverlayBuildContext(build.Id, build.Branch, build.BuildDate?.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), build.ProtocolVersion, profile);
            if (!File.Exists(_overlayExecutable))
            {
                WriteLog("overlay-executable-missing");
                return;
            }
            // The external renderer does not depend on an injected hook reporting readiness.
            // File-only clients may never report native readiness at all.
            using var game = Process.GetProcessById(gameProcessId);
            var hwnd = await WaitForMainWindowAsync(game, gameCompletion, _stop.Token).ConfigureAwait(false);
            if (hwnd == nint.Zero) return;
            if (gameCompletion.IsCompleted || Volatile.Read(ref _disposed) != 0) return;

            var launchId = Guid.NewGuid().ToString("N");
            var pipeName = "FantasyTale.Overlay." + launchId;
            pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance, 65536, 65536);
            overlay = new Process
            {
                StartInfo = new ProcessStartInfo(_overlayExecutable)
                {
                    WorkingDirectory = Path.GetDirectoryName(_overlayExecutable)!,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            overlay.StartInfo.ArgumentList.Add("--game-pid");
            overlay.StartInfo.ArgumentList.Add(gameProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            overlay.StartInfo.ArgumentList.Add("--game-hwnd");
            overlay.StartInfo.ArgumentList.Add(hwnd.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture));
            overlay.StartInfo.ArgumentList.Add("--launch-id");
            overlay.StartInfo.ArgumentList.Add(launchId);
            overlay.StartInfo.ArgumentList.Add("--pipe");
            overlay.StartInfo.ArgumentList.Add(pipeName);
            overlay.StartInfo.ArgumentList.Add("--protocol");
            overlay.StartInfo.ArgumentList.Add(ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            overlay.StartInfo.ArgumentList.Add("--theme");
            overlay.StartInfo.ArgumentList.Add(profile.Theme);
            if (locale is "zh-CN" or "zh-TW" or "ko" or "en" or "id" or "vi" or "th")
            {
                overlay.StartInfo.ArgumentList.Add("--locale");
                overlay.StartInfo.ArgumentList.Add(locale);
            }
            if (test.ResourceRoot is { } resourceRoot)
            {
                overlay.StartInfo.ArgumentList.Add("--resource-root");
                overlay.StartInfo.ArgumentList.Add(resourceRoot);
            }
            if (!overlay.Start()) return;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var peerPid) || peerPid != overlay.Id)
                throw new IOException("The overlay pipe was connected by a different process.");
            var hello = await ReadFrameAsync(pipe, timeout.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(hello);
            var root = document.RootElement;
            if (root.GetProperty("type").GetString() != "hello" || root.GetProperty("protocol").GetInt32() != ProtocolVersion
                || root.GetProperty("launchId").GetString() != launchId || root.GetProperty("gamePid").GetInt32() != gameProcessId)
                throw new IOException("The overlay handshake did not match the game session.");
            await WriteFrameAsync(pipe, JsonSerializer.Serialize(new { type = "ready", protocol = ProtocolVersion }), timeout.Token).ConfigureAwait(false);

            session = new OverlaySession(instanceId, overlay, pipe, buildContext);
            WriteLog("overlay-ready");
            control?.SetOverlayHandler(session.HandleNative);
            session.StartReader();
            await session.SendContextAsync().ConfigureAwait(false);
            // Folders are registered before any page exists, so a page can fetch package files from its first script.
            if (packageFolders.Count > 0) await session.SendPackageRootsAsync(packageFolders).ConfigureAwait(false);
            OverlaySession? previous;
            lock (_gate)
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                _sessions.Remove(instanceId, out previous);
                _sessions.Add(instanceId, session);
            }
            if (previous is not null) await previous.DisposeAsync().ConfigureAwait(false);
            foreach (var package in panels)
            {
                var companion = package.Overlay?.Controller is null || controllerCompanion is null ? null
                    : await controllerCompanion(package.Id, _stop.Token).ConfigureAwait(false);
                var entry = Path.Combine(package.Folder, package.Overlay!.Entry!);
                for (var path = entry; path is not null; path = Path.GetDirectoryName(path))
                    if (Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Overlay entries cannot traverse filesystem links.");
                using var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length > MaximumHandshakeBytes) throw new IOException("An overlay entry exceeds 16 KiB.");
                using var reader = new StreamReader(input, new UTF8Encoding(false, true));
                var panel = session.RegisterPanel(package.Id,
                    new OverlayPanelDescriptor { InlineHtml = await reader.ReadToEndAsync().ConfigureAwait(false) }, prepared =>
                    {
                        // Subscribe before dispatching the entry HTML so a cached page cannot outrun its controller.
                        try
                        {
                            session.Backends.Start(package, build, prepared, control is null ? null : new GameNativeUiClient(control, build.Id),
                                locale, controllerProvider, (name, detail) => WriteLog(name, detail), companion);
                        }
                        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or JsonException)
                        {
                            WriteLog("overlay-controller-start-failed", package.Id + "-" + error.GetType().Name);
                            throw;
                        }
                        prepared.MessageReceived += (_, args) =>
                        {
                            if (args.EventName is "game-ui.mounted" or "game-ui.error") WriteLog(args.EventName, package.Id);
                        };
                    });
                session.EntryPanels[package.Id] = panel;
                WriteLog("overlay-entry-prepared", package.Id);
            }
            if (test.StartupMessage)
            {
                using var readinessTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                readinessTimeout.CancelAfter(TimeSpan.FromSeconds(45));
                var rendererReady = session.WaitUntilReadyAsync(readinessTimeout.Token);
                if (await Task.WhenAny(rendererReady, gameCompletion).ConfigureAwait(false) == gameCompletion) return;
                await rendererReady.ConfigureAwait(false);
                startupTest = session.CreateGameUiPanel("launcher.overlay-test", interactive: true, modal: true,
                    passThroughOutside: test.PassThroughOutside);
                startupTest.MessageReceived += (_, args) =>
                {
                    if (args.EventName is "game-ui.mounted" or "game-ui.error") WriteLog(args.EventName, "launcher.overlay-test");
                };
                startupTest.Send("game-ui.message-box", new
                {
                    title = "Overlay Test", text = "Overlay Test", buttons = new[] { new { name = "ok", label = "OK" } },
                });
                WriteLog("overlay-test-requested");
            }
            await Task.WhenAny(gameCompletion, overlay.WaitForExitAsync(), session.ConnectionCompletion, stopped.Task).ConfigureAwait(false);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or OperationCanceledException or TimeoutException or JsonException or KeyNotFoundException)
        {
            // An optional overlay must never prevent the game from starting or finishing.
            WriteLog("overlay-failed", error.GetType().Name);
        }
        finally
        {
            lock (_gate)
            {
                if (_sessions.TryGetValue(instanceId, out var current) && ReferenceEquals(current, session)) _sessions.Remove(instanceId);
            }
            try
            {
                if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) { WriteLog("overlay-controller-stop-failed", error.GetType().Name); }
            finally { startupTest?.Dispose(); }
            if (session is null && overlay is not null)
            {
                try { if (!overlay.HasExited) overlay.Kill(entireProcessTree: true); }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                finally { overlay.Dispose(); }
            }
            if (session is null)
            {
                pipe?.Dispose();
                control?.SetOverlayHandler(_ => new OverlayBrokerResponse("not-found", "overlay is unavailable"));
            }
            WriteLog("overlay-ended");
        }
    }

    private void WriteLog(string name, string? detail = null)
    {
        try { _log?.Write(name, detail); }
        catch (ArgumentException) { }
    }

    private static async Task<nint> WaitForMainWindowAsync(Process process, Task<LaunchResult> completion, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested && !completion.IsCompleted)
        {
            try
            {
                process.Refresh();
                if (process.HasExited) return nint.Zero;

                // Process.MainWindowHandle is zero when the game runs elevated and the
                // launcher does not. Use EnumWindows to enumerate by PID instead.
                var hwnd = FindAnyVisibleWindowByPid((uint)process.Id);
                if (hwnd != nint.Zero) return hwnd;
            }
            catch (InvalidOperationException) { return nint.Zero; }
            await Task.Delay(100, timeout.Token).ConfigureAwait(false);
        }
        return nint.Zero;
    }

    private static nint FindAnyVisibleWindowByPid(uint pid)
    {
        nint found = nint.Zero;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var windowPid);
            if (windowPid != pid || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
            if (!GetClientRect(hwnd, out var rect) || rect.Right <= 0 || rect.Bottom <= 0) return true;
            found = hwnd;
            return false; // stop enumeration
        }, nint.Zero);
        return found;
    }

    private static async Task<string> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > MaximumHandshakeBytes) throw new IOException("Invalid overlay handshake size.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static async Task WriteFrameAsync(Stream stream, string body, CancellationToken cancellationToken)
    {
        var bytes = new UTF8Encoding(false, true).GetBytes(body);
        if (bytes.Length > MaximumHandshakeBytes) throw new IOException("Overlay handshake is too large.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = 1;
            return new(_dispose ??= Task.Run(DisposeCoreAsync));
        }
    }

    private async Task DisposeCoreAsync()
    {
        _stop.Cancel();
        Task[] runs;
        lock (_gate) runs = _runs.ToArray();
        try { await Task.WhenAll(runs).ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }

    private sealed class OverlaySession(string instanceId, Process process, Stream pipe, OverlayBuildContext buildContext) : IOverlayHost, IAsyncDisposable
    {
        private const int MaximumFrameBytes = 256 * 1024;
        private readonly object _gate = new();
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Dictionary<string, TaskCompletionSource<OverlayResponse>> _pending = new(StringComparer.Ordinal);
        private readonly Dictionary<string, OverlayPanelProxy> _panels = new(StringComparer.Ordinal);
        internal PanelBackendLifetime Backends { get; } = new();
        private Task? _dispose;

        // Entry pages by package id, so a package's companion grants can be bridged to its own panel.
        internal System.Collections.Concurrent.ConcurrentDictionary<string, IOverlayPanel> EntryPanels { get; } = new(StringComparer.Ordinal);
        private Task? _reader;
        private Task? _dispatcher;
        private long _requestNumber;
        private int _disposed;
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Channel<(OverlayPanelProxy Panel, string Name, JsonElement Data)> _events =
            Channel.CreateBounded<(OverlayPanelProxy, string, JsonElement)>(32);

        public bool IsReady => _ready.Task.IsCompletedSuccessfully;
        public OverlayBuildContext BuildContext => buildContext;
        public Task WaitUntilReadyAsync(CancellationToken cancellationToken = default) => _ready.Task.WaitAsync(cancellationToken);
        internal Task ConnectionCompletion => _reader ?? throw new InvalidOperationException("The overlay reader has not started.");

        internal Task SendContextAsync() => SendCommandAsync(new { type = "build-context", context = buildContext });

        internal Task SendPackageRootsAsync(IReadOnlyDictionary<string, string> roots) => SendCommandAsync(new { type = "package-roots", roots });

        internal IOverlayHost Host => this;

        internal void StartReader()
        {
            _reader = Task.Run(ReadLoopAsync);
            _dispatcher = Task.Run(DispatchEventsAsync);
        }

        private async Task DispatchEventsAsync()
        {
            try
            {
                await foreach (var entry in _events.Reader.ReadAllAsync(_shutdown.Token).ConfigureAwait(false))
                {
                    try { entry.Panel.Raise(entry.Name, entry.Data); }
                    catch (Exception) { entry.Panel.Events.Add("overlay.handler-error", default); }
                }
            }
            catch (OperationCanceledException) { }
        }

        public IOverlayPanel RegisterPanel(string panelId, OverlayPanelDescriptor descriptor) => RegisterPanel(panelId, descriptor, null);

        internal IOverlayPanel RegisterPanel(string panelId, OverlayPanelDescriptor descriptor, Action<IOverlayPanel>? prepare)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(panelId);
            ArgumentNullException.ThrowIfNull(descriptor);
            var panel = new OverlayPanelProxy(panelId, this);
            lock (_gate)
            {
                if (_panels.Count >= 64) throw new InvalidOperationException("The overlay panel limit has been reached.");
                if (!_panels.TryAdd(panelId, panel)) throw new InvalidOperationException("A panel with this id is already registered.");
            }
            try
            {
                prepare?.Invoke(panel);
                var request = new Dictionary<string, object?>
                {
                    ["type"] = "register", ["panelId"] = panelId,
                    ["html"] = descriptor.InlineHtml, ["fileName"] = descriptor.HtmlFileName,
                    ["layer"] = descriptor.Layer, ["interactive"] = descriptor.Interactive, ["modal"] = descriptor.Modal,
                    ["passThroughOutside"] = descriptor.PassThroughOutside,
                    ["backdrop"] = descriptor.Backdrop, ["backdropColor"] = descriptor.BackdropColor,
                };
                SendCommandAsync(request).GetAwaiter().GetResult();
                return panel;
            }
            catch
            {
                lock (_gate) _panels.Remove(panelId);
                throw;
            }
        }

        internal void ShowPanel(string panelId) => SendCommandAsync(new { type = "show", panelId }).GetAwaiter().GetResult();
        internal void HidePanel(string panelId) => SendCommandAsync(new { type = "hide", panelId }).GetAwaiter().GetResult();
        internal void SendPanel(string panelId, string eventName, object? data) =>
            SendCommandAsync(new { type = "send", panelId, name = eventName, data }).GetAwaiter().GetResult();

        internal OverlayBrokerResponse HandleNative(OverlayBrokerRequest request)
        {
            try
            {
                var panelId = request.Owner + "." + request.PanelId;
                if (panelId.Length > 96) return new("invalid", "the panel id is too long");
                switch (request.Method)
                {
                    case "register":
                        if ((request.Html is null) == (request.FileName is null)) return new("invalid", "exactly one HTML source is required");
                        RegisterPanel(panelId, new OverlayPanelDescriptor { InlineHtml = request.Html, HtmlFileName = request.FileName });
                        return new("ok");
                    case "unregister":
                        OverlayPanelProxy? panel;
                        lock (_gate) _panels.TryGetValue(panelId, out panel);
                        panel?.Dispose();
                        return new("ok");
                    case "poll":
                        OverlayPanelProxy? polled;
                        lock (_gate) _panels.TryGetValue(panelId, out polled);
                        var next = polled?.Events.Take();
                        return next is null ? new("not-found") : new("ok", Data: next);
                    case "show": ShowPanel(panelId); return new("ok");
                    case "hide": HidePanel(panelId); return new("ok");
                    case "send":
                        if (request.EventName is null) return new("invalid", "event name is required");
                        object? data = null;
                        if (request.Data is { } json)
                        {
                            using var document = JsonDocument.Parse(json);
                            data = document.RootElement.Clone();
                        }
                        SendPanel(panelId, request.EventName, data);
                        return new("ok");
                    default: return new("invalid", "unsupported method");
                }
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or JsonException or IOException or TimeoutException)
            {
                return new("failed", error.Message);
            }
        }

        private void RemovePanel(OverlayPanelProxy panel)
        {
            lock (_gate)
            {
                if (!_panels.Remove(panel.PanelId)) return;
            }
            try { SendCommandAsync(new { type = "unregister", panelId = panel.PanelId }).GetAwaiter().GetResult(); }
            catch (Exception error) when (error is IOException or InvalidOperationException or TimeoutException or OperationCanceledException) { }
        }

        private async Task SendCommandAsync(object command)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var requestId = Interlocked.Increment(ref _requestNumber).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var tcs = new TaskCompletionSource<OverlayResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate) _pending.Add(requestId, tcs);
            try
            {
                var payload = JsonSerializer.SerializeToNode(command)?.AsObject() ?? throw new InvalidOperationException("Invalid overlay command.");
                payload["requestId"] = requestId;
                await WriteFrameAsync(JsonSerializer.Serialize(payload), _shutdown.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var response = await tcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!response.Ok) throw new InvalidOperationException(response.Error ?? "The overlay rejected the command.");
            }
            catch (OperationCanceledException) when (!_shutdown.IsCancellationRequested)
            {
                throw new TimeoutException("The overlay did not respond to the command.");
            }
            finally
            {
                lock (_gate) _pending.Remove(requestId);
            }
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (!_shutdown.IsCancellationRequested)
                {
                    var raw = await ReadFrameAsync(pipe, _shutdown.Token).ConfigureAwait(false);
                    using var document = JsonDocument.Parse(raw);
                    var root = document.RootElement;
                    var type = root.GetProperty("type").GetString();
                    if (type == "renderer-ready") _ready.TrySetResult();
                    else if (type == "response")
                    {
                        var id = root.GetProperty("requestId").GetString();
                        if (id is null) continue;
                        TaskCompletionSource<OverlayResponse>? completion;
                        lock (_gate) _pending.TryGetValue(id, out completion);
                        completion?.TrySetResult(new OverlayResponse(root.GetProperty("ok").GetBoolean(),
                            root.TryGetProperty("error", out var error) ? error.GetString() : null));
                    }
                    else if (type == "panel-event")
                    {
                        var id = root.GetProperty("panelId").GetString();
                        var name = root.GetProperty("name").GetString();
                        if (id is null || name is null) continue;
                        var data = root.TryGetProperty("data", out var value) ? value.Clone() : default;
                        OverlayPanelProxy? panel;
                        lock (_gate) _panels.TryGetValue(id, out panel);
                        if (panel is not null)
                        {
                            panel.Events.Add(name, data);
                            if (!_events.Writer.TryWrite((panel, name, data))) throw new IOException("The overlay event queue is full.");
                        }
                    }
                }
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or JsonException or InvalidOperationException)
            {
                _ready.TrySetException(new IOException("The overlay connection closed before readiness.", error));
                lock (_gate)
                {
                    foreach (var pending in _pending.Values) pending.TrySetException(new IOException("The overlay connection closed.", error));
                    _pending.Clear();
                }
            }
        }

        private async Task WriteFrameAsync(string body, CancellationToken cancellationToken)
        {
            var bytes = new UTF8Encoding(false, true).GetBytes(body);
            if (bytes.Length is < 1 or > MaximumFrameBytes) throw new IOException("Overlay command is too large.");
            var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await pipe.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                await pipe.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally { _writeGate.Release(); }
        }

        private static async Task<string> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
        {
            var header = new byte[4]; await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length is < 1 or > MaximumFrameBytes) throw new IOException("Invalid overlay message size.");
            var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
            return new UTF8Encoding(false, true).GetString(bytes);
        }

        public ValueTask DisposeAsync()
        {
            lock (_gate) return new(_dispose ??= Task.Run(DisposeCoreAsync));
        }

        private async Task DisposeCoreAsync()
        {
            try { await Backends.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                Interlocked.Exchange(ref _disposed, 1);
                _ready.TrySetCanceled();
                _events.Writer.TryComplete();
                _shutdown.Cancel();
                OverlayPanelProxy[] panels;
                lock (_gate) { panels = _panels.Values.ToArray(); _panels.Clear(); }
                foreach (var panel in panels) panel.Dispose();
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                process.Dispose();
                pipe.Dispose();
                if (_reader is not null) await _reader.ConfigureAwait(false);
                if (_dispatcher is not null) await _dispatcher.ConfigureAwait(false);
                _writeGate.Dispose();
                _shutdown.Dispose();
                _ = instanceId;
            }
        }

        private readonly record struct OverlayResponse(bool Ok, string? Error);

        private sealed class OverlayPanelProxy(string panelId, OverlaySession session) : IOverlayPanel
        {
            private int _disposed;

            public string PanelId => panelId;
            public event EventHandler<OverlayMessageEventArgs>? MessageReceived;
            internal OverlayEventQueue Events { get; } = new();
            public void Show() => session.ShowPanel(panelId);
            public void Hide() => session.HidePanel(panelId);
            public void Send(string eventName, object? data = null) => session.SendPanel(panelId, eventName, data);
            public void MountGameUi(string component, object? props = null) => session.SendPanel(panelId, "game-ui.mount", new { component, props });
            internal void Raise(string eventName, JsonElement data)
            {
                if (Volatile.Read(ref _disposed) == 0)
                    MessageReceived?.Invoke(this, new OverlayMessageEventArgs(panelId, eventName, data));
            }
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0) session.RemovePanel(this);
            }
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out int processId);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint window, out RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
}
