using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FantasyTale.Launcher.Sdk;
using Microsoft.Web.WebView2.Core;

namespace FantasyTale.Launcher.Overlay;

/// <summary>
/// Transparent WPF window following a game window and hosting a WebView2 overlay.
/// Attach it to a game window by passing the game's HWND on construction;
/// pass <see cref="IntPtr.Zero"/> for free-floating demo mode.
/// </summary>
public partial class OverlayWindow : Window
{
    // Win32 constants
    private const int GwlExstyle      = -20;
    private const int GclStyle        = -26;
    private const int CsOwnDc         = 0x0020;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered     = 0x00080000;
    private const int WsExToolwindow  = 0x00000080;
    private const int WsExNoactivate  = 0x08000000;
    private const int WsExTopmost     = 0x00000008;
    private const uint GwHwndPrev     = 3;

    private const uint SwpNoactivate = 0x0010;
    private const uint SwpNomove     = 0x0002;
    private const uint SwpNosize     = 0x0001;

    private static readonly nint HwndTopmost   = new(-1);
    private static readonly nint HwndNotopmost = new(-2);

    private nint _gameHwnd;
    private readonly uint _gamePid;
    private readonly string _theme;
    private readonly string? _resourceRoot;
    private readonly bool _preview;
    private readonly string? _locale;
    private nint          _hwnd;
    private int           _originalExStyle;
    private bool          _styleCaptured;

    // Click-through is on by default: the overlay is visible but mouse events pass to the game.
    private bool _clickThrough = true;
    private bool _pageReady;
    private bool _webViewInitializationStarted;
    private bool _closed;
    private bool _panelsVisible;
    private bool _panelsInteractive;
    private bool _pageBusy;
    private long _browserActivity;
    private bool _suspending;
    private bool _modalInput;
    private bool _passThroughOutside;
    private bool _pointerCaptured;
    private readonly bool _preserveGameActivation;
    private Rect[] _inputRegions = [];
    private DispatcherTimer? _inputTimer;
    private HwndSource? _source;
    private (int Width, int Height, uint Dpi)? _viewport;
    private readonly HashSet<long> _pendingPageCommands = [];
    private long _pageCommandId;

    // WinEvent hook for following the game window when it moves or resizes.
    private nint          _winEventHook;
    private WinEventProc? _winEventProc; // must be a field — GC would collect a local delegate
    private DispatcherTimer? _followTimer;
    private nint _lastSelected = new(-1);
    private bool? _lastVisible;
    private nint _minimizingWindow;
    private nint _lastForeground = new(-1);

    /// <summary>Initialises the overlay window.</summary>
    /// <param name="gameHwnd">Handle of the game window to track, or zero for demo mode.</param>
    /// <param name="theme">Native UI theme selected by the client build: v1, v2 or v3.</param>
    /// <param name="resourceRoot">Optional external directory containing native theme resources.</param>
    /// <param name="preview">Whether to show a native UI component for local integration testing.</param>
    /// <param name="gameProcessId">Optional launch-bound process identity, retained if the initial window was destroyed.</param>
    /// <param name="locale">Launcher UI language handed to overlay panels, or null for the overlay default.</param>
    public OverlayWindow(nint gameHwnd, string theme = "v2", string? resourceRoot = null, bool preview = false, uint gameProcessId = 0,
        string? locale = null)
    {
        _locale = locale is null || OverlayLocales.IsKnown(locale) ? locale : throw new ArgumentException("Unsupported overlay locale.", nameof(locale));
        _gameHwnd = gameHwnd;
        _gamePid = gameProcessId;
        if (_gamePid == 0 && gameHwnd != IntPtr.Zero) _ = GetWindowThreadProcessId(gameHwnd, out _gamePid);
        _preserveGameActivation = _gamePid != 0;
        _theme = theme is "v1" or "v2" or "v3" ? theme : throw new ArgumentException("The overlay theme must be v1, v2 or v3.", nameof(theme));
        _resourceRoot = resourceRoot;
        _preview = preview;
        _panelsVisible = preview;
        InitializeComponent();
        OverlayLifecycleLog.Write("window-created", new { gamePid = _gamePid, initialHwnd = gameHwnd.ToInt64() });
        OverlayLifecycleLog.Write("activation-policy", new { preserveGameActivation = _preserveGameActivation });
        // Panel registration and navigation may finish while the splash is still
        // visible. Keep their visual root dormant until a render window exists.
        if (_gamePid != 0) Root.Visibility = Visibility.Hidden;
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(OnWindowMessage);

        // Capture baseline extended style once so the click-through toggle is always reversible.
        _originalExStyle = (int)GetWindowLongPtr(_hwnd, GwlExstyle).ToInt64();
        _styleCaptured   = true;

        // Keep the overlay out of Alt+Tab and prevent it stealing focus on show.
        SetWindowLongPtr(_hwnd, GwlExstyle,
            (nint)(_originalExStyle | WsExToolwindow | WsExNoactivate | WsExLayered | WsExTransparent));

        ApplyClickThrough(true);

        if (_gamePid != 0)
        {
            StartFollowing();
        }
        else
        {
            // Demo mode: float in the top-right corner of the primary screen.
            ShowInTaskbar = true;
            Left   = SystemParameters.PrimaryScreenWidth  - 660;
            Top    = 40;
            Width  = 640;
            Height = 480;
        }

        QueueWebViewInitialization();
    }

    // ── WebView2 ──────────────────────────────────────────────────────────────

    private static CoreWebView2Environment? _sharedEnv;
    private string _webViewInitializationStage = "environment";

    private void QueueWebViewInitialization()
    {
        if (_webViewInitializationStarted || _closed || (_gamePid != 0 && !_gameSurfaceAvailable)) return;
        // A hidden splash-bound WPF host can initialize WebView2 with an unpainted
        // native surface. Create its controller only after the render HWND is shown
        // and WPF has arranged the final client bounds, as in a late attachment.
        _webViewInitializationStarted = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => _ = InitializeVisibleWebViewAsync()));
    }

    private async Task InitializeVisibleWebViewAsync()
    {
        if (_closed) return;
        if (_gamePid != 0 && !_gameSurfaceAvailable)
        {
            _webViewInitializationStarted = false;
            return;
        }
        Root.UpdateLayout();
        OverlayLifecycleLog.Write("browser-initializing", new { hwnd = _gameHwnd.ToInt64(), rootVisible = Root.IsVisible,
            width = ActualWidth, height = ActualHeight });
        try
        {
            await InitWebViewAsync();
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException
            or System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException)
        {
            OverlayLifecycleLog.Write("browser-initialization-failed", new { errorType = error.GetType().Name,
                hresult = error.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture),
                stage = _webViewInitializationStage });
            Close();
        }
    }

    /// <summary>Fires once after the overlay page finishes its first navigation.</summary>
    public event EventHandler? NavigationReady;

    /// <summary>Fires when the overlay page posts a message to the host.</summary>
    public event EventHandler<string>? MessageFromPage;

    /// <summary>Posts a raw JSON string to the overlay page's message handler.</summary>
    public void PostMessage(string json)
    {
        if (_pendingPageCommands.Count >= 256) throw new InvalidOperationException("The overlay page command queue is full.");
        var command = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject()
            ?? throw new ArgumentException("Invalid overlay page command.", nameof(json));
        var id = ++_pageCommandId;
        command["wakeId"] = id;
        _pendingPageCommands.Add(id);
        WakeBrowser();
        WebView.CoreWebView2.PostWebMessageAsString(command.ToJsonString());
    }

    private void WakeBrowser()
    {
        _browserActivity++;
        WebView.CoreWebView2?.Resume();
    }

    private async Task SuspendWhenIdleAsync()
    {
        var activity = ++_browserActivity;
        await Task.Delay(750);
        if (_closed || !_pageReady || _pageBusy || _pendingPageCommands.Count != 0 || _lastVisible == true || activity != _browserActivity || _suspending
            || WebView.CoreWebView2 is null) return;
        _suspending = true;
        try
        {
            var suspended = await WebView.CoreWebView2.TrySuspendAsync();
            if (_closed) return;
            if (activity != _browserActivity || _pendingPageCommands.Count != 0 || _lastVisible == true) WebView.CoreWebView2.Resume();
            else OverlayLifecycleLog.Write("browser-idle", new { suspended });
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException or COMException)
        {
            if (!_closed) OverlayLifecycleLog.Write("browser-idle-unavailable", new { errorType = error.GetType().Name });
        }
        finally { _suspending = false; }
    }

    /// <summary>Folders of the launch's overlay and assets packages that pages may read through the package host.</summary>
    internal OverlayPackageFiles PackageFiles { get; } = new();

    private void OnPackageFileRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        var environment = WebView.CoreWebView2.Environment;
        var file = new OverlayPackageFile(404, null, "", false);
        Stream? body = null;
        try
        {
            file = PackageFiles.Resolve(args.Request.Method, args.Request.Uri);
            // The browser pulls the stream as it needs bytes, so a large file is never held in memory.
            if (file.Status == 200)
                body = new FileStream(file.Path!, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            file = new OverlayPackageFile(404, null, "", false);
            OverlayLifecycleLog.Write("package-file-unavailable", new { errorType = error.GetType().Name });
        }
        var reason = file.Status switch { 200 => "OK", 400 => "Bad Request", 403 => "Forbidden", 405 => "Method Not Allowed", _ => "Not Found" };
        args.Response = environment.CreateWebResourceResponse(body, file.Status, reason, file.Headers("https://overlay.fantasytale.local"));
    }

    private async Task InitWebViewAsync()
    {
        // Overlay windows in this process share one browser environment.
        _sharedEnv ??= await CoreWebView2Environment.CreateAsync(
            userDataFolder: Path.Combine(AppContext.BaseDirectory, "OverlayWebData"));

        var opts = _sharedEnv.CreateCoreWebView2ControllerOptions();
        opts.ProfileName = "Overlay";

        _webViewInitializationStage = "controller";
        await WebView.EnsureCoreWebView2Async(_sharedEnv, opts);

        _webViewInitializationStage = "configuration";
        // Set transparent background as early as possible after controller creation.
        WebView.DefaultBackgroundColor = System.Drawing.Color.Transparent;

        var settings = WebView.CoreWebView2.Settings;
        settings.IsWebMessageEnabled              = true;
        settings.AreDefaultScriptDialogsEnabled   = false;
        settings.AreDevToolsEnabled               = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsZoomControlEnabled             = false;
        ApplyNativeScale();

        // Serve the overlay page from a virtual host so ES modules and fetch work normally.
        var overlayDir = Path.Combine(AppContext.BaseDirectory, "overlay");
        WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "overlay.fantasytale.local", overlayDir,
            CoreWebView2HostResourceAccessKind.Allow);
        if (_resourceRoot is not null)
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "resources.fantasytale.local", _resourceRoot,
                CoreWebView2HostResourceAccessKind.Allow);

        // WebView2 crash: immediately revert to click-through so a crashed renderer
        // does not leave an invisible window that swallows mouse input.
        WebView.CoreWebView2.ProcessFailed += (_, _) =>
            Dispatcher.BeginInvoke(() => ApplyClickThrough(true));

        WebView.CoreWebView2.NavigationCompleted += (_, args) =>
        {
            OverlayLifecycleLog.Write("navigation-completed", new { args.IsSuccess, status = args.WebErrorStatus.ToString() });
        };

        // Package files are answered here, never fetched: every request for the virtual host gets a response from the registered folders.
        WebView.CoreWebView2.AddWebResourceRequestedFilter("https://" + OverlayPackageFiles.Host + "/*", CoreWebView2WebResourceContext.All);
        WebView.CoreWebView2.WebResourceRequested += OnPackageFileRequested;

        var document = "https://overlay.fantasytale.local/index.html?theme=" + _theme
            + (_resourceRoot is null ? "" : "&resources=external") + (_locale is null ? "" : "&locale=" + _locale) + (_preview ? "&preview=game-ui" : "");
        var policy = new OverlayWebViewPolicy(WebView.CoreWebView2, document,
            category => OverlayLifecycleLog.Write("browser-rejected-" + category));
        WebView.CoreWebView2.WebMessageReceived += (_, args) =>
        {
            if (!policy.TryReadMessage(args, out var raw)) return;
            try
            {
                using var message = System.Text.Json.JsonDocument.Parse(raw);
                if (message.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && message.RootElement.TryGetProperty("type", out var type) && type.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    if (type.GetString() == "command-completed")
                    {
                        _pendingPageCommands.Remove(message.RootElement.GetProperty("wakeId").GetInt64());
                        if (_lastVisible != true) _ = SuspendWhenIdleAsync();
                        return;
                    }
                    if (type.GetString() == "presentation-state")
                    {
                        _panelsVisible = message.RootElement.GetProperty("visible").GetBoolean();
                        _pageBusy = message.RootElement.TryGetProperty("busy", out var busy) && busy.GetBoolean();
                        _panelsInteractive = message.RootElement.GetProperty("interactive").GetBoolean();
                        _modalInput = message.RootElement.TryGetProperty("modal", out var modal) && modal.GetBoolean();
                        _passThroughOutside = message.RootElement.TryGetProperty("passThroughOutside", out var outside) && outside.GetBoolean();
                        _pointerCaptured = message.RootElement.TryGetProperty("capturePointer", out var capture) && capture.GetBoolean();
                        _inputRegions = message.RootElement.TryGetProperty("regions", out var regions)
                            ? regions.EnumerateArray().Take(64).Select(r => new Rect(r.GetProperty("x").GetDouble(),
                                r.GetProperty("y").GetDouble(), Math.Max(0, r.GetProperty("width").GetDouble()),
                                Math.Max(0, r.GetProperty("height").GetDouble()))).ToArray() : [];
                        if (_gamePid == 0) SetSurfaceVisible(_panelsVisible || !_pageReady);
                        else SynchronizeGameWindow();
                        UpdateInputRouting();
                        if (_lastVisible != true) _ = SuspendWhenIdleAsync();
                        return;
                    }
                    if (type.GetString() != "page-ready") { MessageFromPage?.Invoke(this, raw); return; }
                    if (_pageReady) return;
                    // Navigation completion does not establish that the module's message
                    // listener is installed. Flush commands only after its explicit signal.
                    _pageReady = true;
                    PublishViewport();
                    NavigationReady?.Invoke(this, EventArgs.Empty);
                    if (_gamePid == 0) SetSurfaceVisible(_panelsVisible);
                    else SynchronizeGameWindow();
                    OverlayLifecycleLog.Write("renderer-ready");
                    _ = RecordVisualStateAsync();
                    return;
                }
            }
            catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or ArgumentException) { return; }
            MessageFromPage?.Invoke(this, raw);
        };

        WebView.CoreWebView2.Navigate(document);
    }

    private async Task RecordVisualStateAsync()
    {
        // Vue mounting starts asynchronous resource loads. Record geometry after
        // navigation without collecting panel content or application event data.
        await Task.Delay(2000);
        if (_closed || _lastVisible != true || WebView.CoreWebView2 is null) return;
        try
        {
            var state = await WebView.CoreWebView2.ExecuteScriptAsync("""
                JSON.stringify({
                  viewport: [innerWidth, innerHeight], fonts: document.fonts.status,
                  panels: [...document.querySelectorAll('.ft-panel')].slice(0, 16).map(p => ({
                    visible: p.classList.contains('visible'),
                    bounds: [p.offsetWidth, p.offsetHeight],
                    surfaces: p.shadowRoot?.querySelectorAll('.bf-window,.bf-form').length ?? 0,
                    alerts: p.shadowRoot?.querySelectorAll('[role=alert]').length ?? 0,
                    brokenImages: [...(p.shadowRoot?.querySelectorAll('img') ?? [])].filter(i => i.complete && !i.naturalWidth).length
                  }))
                })
                """);
            OverlayLifecycleLog.Write("visual-state", new { windowVisible = IsVisible, rootVisible = Root.IsVisible,
                width = ActualWidth, height = ActualHeight, page = System.Text.Json.JsonSerializer.Deserialize<string>(state) });
        }
        catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException)
        {
            OverlayLifecycleLog.Write("visual-state-unavailable", new { errorType = error.GetType().Name });
        }
    }

    // ── Positioning ──────────────────────────────────────────────────────────

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int WmMouseActivate = 0x0021;
        const int MaNoactivate = 3;
        if (message == WmMouseActivate && _preserveGameActivation)
        {
            // Mouse controls do not need foreground ownership. Keep the game
            // active in every display mode, including exclusive fullscreen.
            handled = true;
            return MaNoactivate;
        }
        const int WmDpiChanged = 0x02E0;
        const int WmDisplayChange = 0x007E;
        if (message is WmDpiChanged or WmDisplayChange)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_closed) return;
                if (_gamePid != 0) SynchronizeGameWindow();
                UpdateInputRouting();
            }));
        return nint.Zero;
    }

    private void Reposition()
    {
        if (_gameHwnd == IntPtr.Zero || _hwnd == IntPtr.Zero) return;
        if (!GetClientRect(_gameHwnd, out RECT client) || client.Right <= 0 || client.Bottom <= 0) return;
        var origin = new POINT();
        if (!ClientToScreen(_gameHwnd, ref origin)) return;

        // Legacy games report 96 DPI even on scaled displays. WPF bounds use the
        // overlay's device scale, not the DPI awareness of the target process.
        double dpi = GetDpiForWindow(_hwnd) / 96.0;
        if (dpi <= 0) dpi = 1.0;
        Left   = origin.X          / dpi;
        Top    = origin.Y          / dpi;
        Width  = client.Right      / dpi;
        Height = client.Bottom     / dpi;
        var viewport = (client.Right, client.Bottom, GetDpiForWindow(_hwnd));
        if (_viewport != viewport)
        {
            _viewport = viewport;
            ApplyNativeScale();
            PublishViewport();
        }
    }

    /// <summary>
    /// Makes one CSS pixel one physical pixel, which is one game pixel: legacy clients draw their UI one to one in client
    /// pixels and ignore display scaling, so every overlay page then matches the native dialogs' size. Without this a
    /// 150% display draws each page half as large again as the game around it.
    /// </summary>
    private void ApplyNativeScale()
    {
        if (_hwnd == IntPtr.Zero || WebView.CoreWebView2 is null) return;
        var dpi = Math.Max(1, GetDpiForWindow(_hwnd)) / 96.0;
        var zoom = 1 / dpi;
        if (Math.Abs(WebView.ZoomFactor - zoom) > 0.0001) WebView.ZoomFactor = zoom;
    }

    private void PublishViewport()
    {
        if (!_pageReady || _viewport is not { } viewport) return;
        PostMessage(System.Text.Json.JsonSerializer.Serialize(new
        {
            type = "viewport", pixelWidth = viewport.Width, pixelHeight = viewport.Height, dpi = viewport.Dpi,
        }));
    }

    private void SynchronizeGameWindow()
    {
        if (_gamePid == 0) return;
        nint selected = IntPtr.Zero;
        long largestArea = 0;
        // cn-324 sub_87D8B0 registers the render class with CS_OWNDC (style 40 = 0x28);
        // sub_795920 registers the splash without it (style 8). Use CS_OWNDC to skip the
        // splash window and attach only to the actual game render surface.
        _ = EnumWindows((candidate, _param) =>
        {
            _ = GetWindowThreadProcessId(candidate, out var pid);
            if (pid != _gamePid || candidate == _minimizingWindow
                || !IsWindowVisible(candidate) || IsIconic(candidate) || IsWindowCloaked(candidate)
                || (GetClassLongPtr(candidate, GclStyle).ToInt64() & CsOwnDc) == 0
                || !GetClientRect(candidate, out var rect)) return true;
            var area = (long)rect.Right * rect.Bottom;
            if (rect.Right >= 320 && rect.Bottom >= 200 && area > largestArea)
            {
                selected = candidate;
                largestArea = area;
            }
            return true;
        }, IntPtr.Zero);

        if (selected != _lastSelected)
        {
            _lastSelected = selected;
            OverlayLifecycleLog.Write("render-window-selected", new { hwnd = selected.ToInt64(), gamePid = _gamePid });
        }

        if (selected != IntPtr.Zero && selected != _gameHwnd)
        {
            // Keep tracking the render HWND when the client replaces its splash window.
            _gameHwnd = selected;
        }

        _gameSurfaceAvailable = selected != IntPtr.Zero;
        if (_gameSurfaceAvailable && (!_pageReady || _panelsVisible)) Reposition();

        var visible = _gameSurfaceAvailable && (!_pageReady || _panelsVisible);
        SetSurfaceVisible(visible);
        QueueWebViewInitialization();
        if (_followTimer is not null)
            _followTimer.Interval = TimeSpan.FromMilliseconds(visible ? 100 : 1000);
        if (visible) UpdateZOrder();
    }

    private bool _gameSurfaceAvailable;

    private void SetSurfaceVisible(bool visible)
    {
        if (_lastVisible == visible) return;
        _lastVisible = visible;
        // Hide the native browser host as well as the WPF surface. Retain the page
        // and registered panels so restoring the game does not reload their state.
        WebView.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        Root.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        if (!visible)
        {
            Topmost = false;
            ApplyClickThrough(true);
            _inputTimer?.Stop();
            _ = SuspendWhenIdleAsync();
        }
        else
        {
            if (_gamePid == 0) Topmost = true;
            WakeBrowser();
            UpdateInputRouting();
            QueueWebViewInitialization();
            if (_pageReady) _ = RecordVisualStateAsync();
        }
        OverlayLifecycleLog.Write("surface-visibility", new
        {
            visible, hwnd = _gameHwnd.ToInt64(),
            gameIconic = IsIconic(_gameHwnd), nativeVisible = IsWindowVisible(_hwnd),
        });
    }

    // ── Window-follow via WinEvent hook ──────────────────────────────────────

    private void StartFollowing()
    {
        _winEventProc = OnWinEvent;

        // Foreground changes must include other processes. The callback filters
        // game location/minimize events; out-of-context delivery avoids re-entry.
        _winEventHook = SetWinEventHook(
            0x0003,  // EVENT_SYSTEM_FOREGROUND — any window comes to front
            0x800B,  // EVENT_OBJECT_LOCATIONCHANGE — covers MINIMIZESTART/END too
            IntPtr.Zero, _winEventProc,
            0, 0,    // all processes: we need FOREGROUND events from other apps too
            0x0000);
        // Elevated legacy windows can miss out-of-context location events. Keep
        // a bounded fallback observer and stop it when this surface is disposed.
        _followTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _followTimer.Tick += (_, _) => SynchronizeGameWindow();
        _followTimer.Start();
        SynchronizeGameWindow();
    }

    private void OnWinEvent(nint hook, uint ev, nint hwnd,
                            int idObject, int idChild, uint thread, uint time)
    {
        const uint EventSystemForeground    = 0x0003;
        const uint EventSystemMinimizeStart = 0x0016;
        const uint EventSystemMinimizeEnd   = 0x0017;
        const uint EventObjectLocationChange = 0x800B;

        switch (ev)
        {
            case EventSystemForeground:
                Dispatcher.BeginInvoke(() =>
                {
                    if (hwnd == _minimizingWindow && !IsIconic(hwnd)) _minimizingWindow = IntPtr.Zero;
                    SynchronizeGameWindow();
                });
                break;

            case EventSystemMinimizeStart when IsOwnedGameWindow(hwnd):
                Dispatcher.BeginInvoke(() =>
                {
                    // MINIMIZESTART precedes IsIconic becoming true. Latch this
                    // transition so the observer cannot show the surface mid-animation.
                    _minimizingWindow = hwnd;
                    OverlayLifecycleLog.Write("game-minimize-start", new { hwnd = hwnd.ToInt64() });
                    SynchronizeGameWindow();
                });
                break;

            case EventSystemMinimizeEnd when IsOwnedGameWindow(hwnd):
                Dispatcher.BeginInvoke(() =>
                {
                    if (_minimizingWindow == hwnd) _minimizingWindow = IntPtr.Zero;
                    OverlayLifecycleLog.Write("game-minimize-end", new { hwnd = hwnd.ToInt64() });
                    SynchronizeGameWindow();
                });
                break;

            case EventObjectLocationChange when IsOwnedGameWindow(hwnd) && idObject == 0:
                Dispatcher.BeginInvoke(SynchronizeGameWindow);
                break;
        }
    }

    private bool IsOwnedGameWindow(nint hwnd)
    {
        if (hwnd == IntPtr.Zero || _gamePid == 0) return false;
        _ = GetWindowThreadProcessId(hwnd, out var pid);
        return pid == _gamePid;
    }

    private static bool IsWindowCloaked(nint hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) != 0) return false;
        return cloaked != 0;
    }

    private void UpdateZOrder()
    {
        if (_hwnd == IntPtr.Zero || _gameHwnd == IntPtr.Zero || _lastVisible != true) return;
        var foreground = GetForegroundWindow();
        var gameIsForeground = foreground == _hwnd || IsOwnedGameWindow(foreground);
        if (_lastForeground != foreground)
        {
            _lastForeground = foreground;
            OverlayLifecycleLog.Write("foreground-changed", new { hwnd = foreground.ToInt64(), gameIsForeground });
        }
        const uint flags = SwpNoactivate | SwpNomove | SwpNosize;
        // Use WPF's property so its managed state agrees with the native window.
        // Changing Topmost only on a transition avoids repeatedly raising the window.
        if (Topmost != gameIsForeground) Topmost = gameIsForeground;
        if (gameIsForeground) return;

        // Hidden helper windows can sit between visible windows in the z-order.
        // Stay immediately above the visible game instead of raising to HWND_TOP.
        var previous = GetWindow(_gameHwnd, GwHwndPrev);
        for (var count = 0; previous != IntPtr.Zero && !IsWindowVisible(previous) && count < 256; count++)
            previous = GetWindow(previous, GwHwndPrev);
        if (previous == _hwnd) return;
        if (previous != IntPtr.Zero
            && (GetWindowLongPtr(previous, GwlExstyle).ToInt64() & WsExTopmost) != 0)
            previous = IntPtr.Zero;
        SetWindowPos(_hwnd, previous, 0, 0, 0, 0, flags);
    }

    // ── Click-through toggle ──────────────────────────────────────────────────

    private void UpdateInputRouting()
    {
        var enabled = _panelsInteractive && _lastVisible == true;
        if (!enabled)
        {
            _inputTimer?.Stop();
            ApplyClickThrough(true);
            return;
        }
        // Modal priority among panels is independent of game input outside their
        // bounds. A drag retains capture even when outside clicks pass through.
        if ((_modalInput && !_passThroughOutside) || _pointerCaptured)
        {
            _inputTimer?.Stop();
            ApplyClickThrough(false);
            return;
        }
        _inputTimer ??= CreateInputTimer();
        _inputTimer.Start();
        RoutePointer();
    }

    private DispatcherTimer CreateInputTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => RoutePointer();
        return timer;
    }

    private void RoutePointer()
    {
        if (!GetCursorPos(out var cursor) || !GetWindowRect(_hwnd, out var rect)) return;
        // Input regions are page (CSS) pixels: device-independent pixels divided by the page zoom.
        var scale = Math.Max(1, GetDpiForWindow(_hwnd)) / 96.0 * (WebView.CoreWebView2 is null ? 1 : WebView.ZoomFactor);
        var point = new Point((cursor.X - rect.Left) / scale, (cursor.Y - rect.Top) / scale);
        ApplyClickThrough(!_inputRegions.Any(region => region.Contains(point)));
    }

    /// <summary>
    /// Switches click-through on or off.  When off the overlay is interactive and the
    /// background alpha rises to 1/255 so the layered-window hit-test covers the whole surface.
    /// </summary>
    public void ApplyClickThrough(bool through)
    {
        if (_clickThrough == through && _styleCaptured && _clickThroughApplied) return;
        var wasInteractive = !_clickThrough;
        _clickThrough = through;
        if (!_styleCaptured) return;
        _clickThroughApplied = true;

        var current = (int)GetWindowLongPtr(_hwnd, GwlExstyle).ToInt64() & ~(WsExNoactivate | WsExTransparent | WsExToolwindow);
        if (_gamePid != 0) current |= WsExToolwindow;
        int want = through
            ? (current | WsExLayered | WsExNoactivate | WsExTransparent)
            : (current | WsExLayered | (_preserveGameActivation ? WsExNoactivate : 0));
        SetWindowLongPtr(_hwnd, GwlExstyle, (nint)want);

        // alpha=1: invisible to the eye but makes the entire surface hittable.
        // alpha=0: layered-window hit-test treats the pixel as absent; clicks pass through.
        Root.Background = new SolidColorBrush(through
            ? Color.FromArgb(0, 0, 0, 0)
            : Color.FromArgb(1, 0, 0, 0));

        if (!through && !_preserveGameActivation && _modalInput && (IsOwnedGameWindow(GetForegroundWindow()) || GetForegroundWindow() == _hwnd))
        {
            SetForegroundWindow(_hwnd);
            Activate();
        }
        else if (through && wasInteractive && _gameHwnd != IntPtr.Zero && GetForegroundWindow() == _hwnd)
        {
            SetForegroundWindow(_gameHwnd);
        }
    }

    private bool _clickThroughApplied;

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _followTimer?.Stop();
        _inputTimer?.Stop();
        _source?.RemoveHook(OnWindowMessage);
        if (_winEventHook != IntPtr.Zero)
            UnhookWinEvent(_winEventHook);
        WebView.Dispose();
        base.OnClosed(e);
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT  { public int Left, Top, Right, Bottom; }
    private delegate void WinEventProc(nint h, uint ev, nint hwnd,
                                       int idObj, int idChild, uint tid, uint time);
    private delegate bool EnumWindowsProc(nint hwnd, nint parameter);

    [DllImport("user32.dll")] private static extern bool GetClientRect(nint h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint h, ref POINT p);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetWindow(nint h, uint command);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint h);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint h, int attribute, out int value, int valueSize);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint h, nint after,
        int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(
        uint min, uint max, nint hmod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint h);

    // x64 host: use the Ptr variants; the game is x86 but that does not affect the host side.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint h, int i, nint v);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern nint GetClassLongPtr(nint h, int i);

    private const int DwmwaCloaked = 14;
}
