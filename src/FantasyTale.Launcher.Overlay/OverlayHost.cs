using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Overlay;

/// <summary>
/// Concrete <see cref="IOverlayHost"/> implementation.
/// Create one instance, call <see cref="Attach"/> once the game window is known,
/// then pass it to any plugin that needs to register overlay panels.
/// </summary>
public sealed class OverlayHost : IOverlayHost, IDisposable
{
    private const int MaximumPanels = 64;
    private const int MaximumPendingMessages = 256;
    private const int MaximumMessageBytes = 256 * 1024;
    private static readonly Regex PanelIdPattern = new("^[a-z0-9][a-z0-9.-]{0,95}$", RegexOptions.CultureInvariant);
    private static readonly Regex EventNamePattern = new("^[a-z][a-z0-9.-]{0,63}$", RegexOptions.CultureInvariant);

    private OverlayWindow?  _window;
    private readonly Dictionary<string, OverlayPanelHandle> _panels = new(StringComparer.Ordinal);
    private bool            _navigationReady;
    private readonly Queue<string> _pendingMessages = new();
    private readonly object _gate = new();
    private Dispatcher? _dispatcher;
    private int _disposed;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public bool IsReady => _ready.Task.IsCompletedSuccessfully;

    /// <inheritdoc />
    public OverlayBuildContext? BuildContext { get; private set; }

    internal void SetBuildContext(OverlayBuildContext context)
    {
        BuildContext = context;
        PostToPage(JsonSerializer.Serialize(new { type = "build-context", context }, JsonSerializerOptions.Web));
    }

    /// <inheritdoc />
    public Task WaitUntilReadyAsync(CancellationToken cancellationToken = default) => _ready.Task.WaitAsync(cancellationToken);

    internal event EventHandler? Ready;

    /// <summary>Raised when a registered panel emits an event from its page.</summary>
    internal event EventHandler<OverlayMessageEventArgs>? PanelMessageReceived;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates and shows the overlay window attached to <paramref name="gameHwnd"/>.
    /// Must be called on the WPF dispatcher thread (i.e. from Application.Current.Dispatcher).
    /// </summary>
    /// <param name="gameHwnd">Handle of the game window to follow, or zero for demo mode.</param>
    /// <param name="theme">Native UI theme selected by the client build: v1, v2 or v3.</param>
    /// <param name="resourceRoot">Optional external directory containing native theme resources.</param>
    /// <param name="preview">Whether to display the local native UI integration preview.</param>
    /// <param name="gameProcessId">Optional launch-bound game process id for tracking replacement windows.</param>
    public void Attach(nint gameHwnd, string theme = "v2", string? resourceRoot = null, bool preview = false, uint gameProcessId = 0)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_window is not null) throw new InvalidOperationException("The overlay is already attached.");
        _dispatcher = Application.Current?.Dispatcher ?? throw new InvalidOperationException("A WPF application must be running before attaching the overlay.");
        if (!_dispatcher.CheckAccess()) throw new InvalidOperationException("Attach must run on the WPF dispatcher thread.");
        if (theme is not ("v1" or "v2" or "v3")) throw new ArgumentException("The overlay theme must be v1, v2 or v3.", nameof(theme));
        _window = new OverlayWindow(gameHwnd, theme, resourceRoot, preview, gameProcessId);
        Application.Current.MainWindow = _window;
        _window.NavigationReady  += OnNavigationReady;
        _window.MessageFromPage  += OnMessageFromPage;
        _window.Show();
    }

    /// <inheritdoc />
    public IOverlayPanel RegisterPanel(string panelId, OverlayPanelDescriptor descriptor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(panelId);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (descriptor.Layer is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(descriptor), "Panel layers must be between zero and 10000.");
        if (panelId.Length > 96 || !PanelIdPattern.IsMatch(panelId))
            throw new ArgumentException("Panel ids must be unique lower-case names of at most 96 characters.", nameof(panelId));
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_window is null) throw new InvalidOperationException("Attach the overlay before registering panels.");

        var inline = descriptor.InlineHtml;
        var fileName = descriptor.HtmlFileName;
        if ((inline is null) == (fileName is null))
            throw new ArgumentException("Exactly one of InlineHtml or HtmlFileName must be set.", nameof(descriptor));
        if (inline is { Length: > MaximumMessageBytes } || fileName is { Length: > 128 } ||
            fileName is not null && (Path.GetFileName(fileName) != fileName || !fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new ArgumentException("Panel content exceeds its limit or uses an invalid HTML file name.", nameof(descriptor));
        var handle = new OverlayPanelHandle(panelId, this);
        OverlayLifecycleLog.Write("panel-registered", new { panelId });
        lock (_gate)
        {
            if (_panels.Count >= MaximumPanels) throw new InvalidOperationException("The overlay panel limit has been reached.");
            if (!_panels.TryAdd(panelId, handle)) throw new InvalidOperationException("A panel with this id is already registered.");
        }

        var payload = JsonSerializer.Serialize(new
        {
            type     = "register",
            panelId,
            html     = inline,
            fileName,
            layer = descriptor.Layer, interactive = descriptor.Interactive, modal = descriptor.Modal,
            passThroughOutside = descriptor.PassThroughOutside,
            backdrop = descriptor.Backdrop, backdropColor = descriptor.BackdropColor,
        });
        try { PostToPage(payload); }
        catch { lock (_gate) _panels.Remove(panelId); throw; }
        return handle;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _ready.TrySetCanceled();
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        if (dispatcher.CheckAccess()) CloseWindow();
        else dispatcher.Invoke(CloseWindow);
    }

    private void CloseWindow()
    {
        lock (_gate) _panels.Clear();
        _pendingMessages.Clear();
        if (_window is not null)
        {
            _window.NavigationReady -= OnNavigationReady;
            _window.MessageFromPage -= OnMessageFromPage;
            _window.Close();
            _window = null;
        }
    }

    // ── Message routing ───────────────────────────────────────────────────────

    /// <summary>Posts a JSON message to the overlay page, queuing it until the page is ready.</summary>
    internal void PostToPage(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumMessageBytes)
            throw new ArgumentException("Overlay messages cannot exceed 256 KiB.", nameof(json));
        var dispatcher = _dispatcher ?? throw new InvalidOperationException("Attach the overlay before sending messages.");
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        dispatcher.BeginInvoke(() =>
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (!_navigationReady)
            {
                if (_pendingMessages.Count >= MaximumPendingMessages) throw new InvalidOperationException("The overlay startup message queue is full.");
                _pendingMessages.Enqueue(json);
                return;
            }
            _window?.PostMessage(json);
        });
    }

    private void OnNavigationReady(object? sender, EventArgs e)
    {
        OverlayLifecycleLog.Write("page-ready", new { queued = _pendingMessages.Count });
        _navigationReady = true;
        while (_pendingMessages.TryDequeue(out var msg))
        {
            try { _window?.PostMessage(msg); }
            catch (InvalidOperationException) { /* WebView2 may have closed during dispatcher shutdown. */ }
        }
        _ready.TrySetResult();
        Ready?.Invoke(this, EventArgs.Empty);
    }

    private void OnMessageFromPage(object? sender, string raw)
    {
        // Expected shape: { "type": "event", "panelId": "...", "name": "...", "data": {...} }
        // Drag interactive-mode messages have type "set-interactive" / "unset-interactive" and no panelId.
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl)) return;
            var type = typeEl.GetString();

            // Drag support: page asks the host to toggle click-through so pointer events reach the panel.
            if (type == "set-interactive")
            {
                _dispatcher?.BeginInvoke(() => _window?.ApplyClickThrough(false));
                return;
            }
            if (type == "unset-interactive")
            {
                _dispatcher?.BeginInvoke(() => _window?.ApplyClickThrough(true));
                return;
            }

            if (type != "event") return;
            if (!root.TryGetProperty("panelId", out var panelIdEl) || panelIdEl.GetString() is not string panelId) return;
            if (!root.TryGetProperty("name",    out var nameEl)    || nameEl.GetString()    is not string name) return;
            if (name.Length > 64 || !EventNamePattern.IsMatch(name)) return;
            if (name is "game-ui.mounted" or "game-ui.error") OverlayLifecycleLog.Write(name, new { panelId });
            var data = root.TryGetProperty("data", out var rawData) ? rawData.Clone() : default;

            OverlayPanelHandle? panel;
            lock (_gate) _panels.TryGetValue(panelId, out panel);
            if (panel is not null)
            {
                panel.RaiseMessage(name, data);
                PanelMessageReceived?.Invoke(this, new OverlayMessageEventArgs(panelId, name, data));
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { /* malformed or oversized message from page */ }
    }

    // ── Panel commands ────────────────────────────────────────────────────────

    internal void SendToPanel(string panelId, string eventName, object? data)
    {
        if (eventName.Length > 64 || !EventNamePattern.IsMatch(eventName)) throw new ArgumentException("Invalid overlay event name.", nameof(eventName));
        lock (_gate) ObjectDisposedException.ThrowIf(!_panels.ContainsKey(panelId), this);
        var json = JsonSerializer.Serialize(new
        {
            type    = "send",
            panelId,
            name    = eventName,
            data = data is JsonElement element && element.ValueKind == JsonValueKind.Undefined ? null : data,
        });
        PostToPage(json);
    }

    internal void ShowPanel(string panelId) { EnsurePanel(panelId); PostToPage(JsonSerializer.Serialize(new { type = "show", panelId })); }
    internal void HidePanel(string panelId) { EnsurePanel(panelId); PostToPage(JsonSerializer.Serialize(new { type = "hide", panelId })); }

    internal void UnregisterPanel(string panelId)
    {
        OverlayPanelHandle? panel;
        lock (_gate) _panels.TryGetValue(panelId, out panel);
        panel?.Dispose();
    }

    private void EnsurePanel(string panelId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        lock (_gate) ObjectDisposedException.ThrowIf(!_panels.ContainsKey(panelId), this);
    }

    internal void RemovePanel(OverlayPanelHandle handle)
    {
        lock (_gate)
        {
            if (_panels.TryGetValue(handle.PanelId, out var current) && ReferenceEquals(current, handle))
                _panels.Remove(handle.PanelId);
            else return;
        }
        PostToPage(JsonSerializer.Serialize(new { type = "unregister", panelId = handle.PanelId }));
    }
}

/// <summary>
/// Returned to callers of <see cref="IOverlayHost.RegisterPanel"/>.
/// Dispose to unregister.
/// </summary>
internal sealed class OverlayPanelHandle(string panelId, OverlayHost host) : IOverlayPanel
{
    private int _disposed;

    /// <inheritdoc />
    public string PanelId => panelId;

    /// <inheritdoc />
    public event EventHandler<OverlayMessageEventArgs>? MessageReceived;

    /// <inheritdoc />
    public void Show() => host.ShowPanel(panelId);

    /// <inheritdoc />
    public void Hide() => host.HidePanel(panelId);

    /// <inheritdoc />
    public void Send(string eventName, object? data = null) => host.SendToPanel(panelId, eventName, data);

    /// <inheritdoc />
    public void MountGameUi(string component, object? props = null) =>
        host.SendToPanel(panelId, "game-ui.mount", new { component, props });

    internal void RaiseMessage(string eventName, JsonElement data) =>
        MessageReceived?.Invoke(this, new OverlayMessageEventArgs(panelId, eventName, data));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) host.RemovePanel(this);
    }
}
