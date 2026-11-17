using System.Net;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Window;

/// <summary>
/// Offers a client build's developer tool stages from a small overlay button while the Developer page switch is on.
/// The list comes from the native provider's stage.open features, so a build without verified stages shows nothing.
/// </summary>
internal sealed class DeveloperStageMenu : IDisposable
{
    internal const string ButtonPanelId = "launcher.developer-stages.button";
    internal const string MenuPanelId = "launcher.developer-stages.menu";
    internal const string NoticePanelId = "launcher.developer-stages.notice";
    private const string OpenAction = "open";
    private const string CloseAction = "close";
    private const string StagePrefix = "stage:";
    private static readonly TimeSpan CapabilityInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StageInterval = TimeSpan.FromMilliseconds(500);
    private const int CapabilityAttempts = 30;
    private const int ResultAttempts = 20;

    // Display names for the tool stages the client builds name; an unnamed key is shown as it is.
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["ui_editor"] = "UI editor", ["ui_scheme_tester"] = "UI scheme tester", ["character"] = "Character tool",
        ["effect"] = "Effect tool", ["model_viewer"] = "Model viewer", ["level_event_viewer"] = "Level event viewer",
        ["spot_editor"] = "Spot editor", ["costume_test"] = "Costume test", ["costume_play"] = "Costume play",
        ["shop_item_view"] = "Shop item viewer",
    };

    private readonly IOverlayHost _host;
    private readonly IGameNativeUi _nativeUi;
    private readonly Action<string> _log;
    private readonly IOverlayPanel _button;
    private readonly IOverlayPanel _menu;
    private readonly object _gate = new();
    private IOverlayPanel? _notice;
    private GameSceneWatcher? _watcher;
    private string? _place;
    private int _busy;
    private int _disposed;

    private DeveloperStageMenu(IOverlayHost host, IGameNativeUi nativeUi, IReadOnlyList<string> tools, Action<string> log)
    {
        _host = host;
        _nativeUi = nativeUi;
        _log = log;
        _button = host.RegisterPanel(ButtonPanelId, new OverlayPanelDescriptor { Interactive = true, PassThroughOutside = true, Layer = 9000, InlineHtml = ButtonHtml });
        try
        {
            _menu = host.RegisterPanel(MenuPanelId, new OverlayPanelDescriptor
            {
                Interactive = true, Modal = true, PassThroughOutside = true, Layer = 9001, InlineHtml = MenuHtml(tools),
            });
        }
        catch { _button.Dispose(); throw; }
        _button.MessageReceived += (_, args) => { if (Action(args) == OpenAction && Shows(Volatile.Read(ref _place))) _menu.Show(); };
        _menu.MessageReceived += (_, args) =>
        {
            var action = Action(args);
            if (action is null) return;
            _menu.Hide();
            if (action.StartsWith(StagePrefix, StringComparison.Ordinal)) _ = OpenAsync(action[StagePrefix.Length..]);
        };
        // The button stays hidden until the first stage read places the game on the main menu or in a tool stage.
    }

    /// <summary>Whether the button belongs on screen: on the main menu, where tools open, and in a tool stage, to return.</summary>
    internal static bool Shows(string? stage) => stage is "mainMenu" or "tool";

    // Follows the game's stage: the button shows on the main menu and in tool stages and hides everywhere else, and
    // it is shown again on every return there, so a stage change never leaves it missing. A pending switch keeps the
    // current placement; an unknown stage (a failing read) hides nothing until the read recovers.
    private void FollowStage(Task gameCompletion)
    {
        var watcher = new GameSceneWatcher(_nativeUi, null, OnScene, StageInterval);
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0) { _ = watcher.DisposeAsync().AsTask(); return; }
            _watcher = watcher;
        }
        _ = gameCompletion.ContinueWith(_ => StopFollowing(), TaskScheduler.Default);
    }

    private void OnScene(GameScene scene)
    {
        if (Volatile.Read(ref _disposed) != 0 || scene.Source != GameSceneSource.Native || scene.Stage is not { Switching: false } stage) return;
        var place = stage.Name;
        if (Interlocked.Exchange(ref _place, place) == place) return;
        if (Shows(place)) _button.Show();
        else
        {
            _menu.Hide();
            _button.Hide();
        }
    }

    private void StopFollowing()
    {
        GameSceneWatcher? watcher;
        lock (_gate) { watcher = _watcher; _watcher = null; }
        if (watcher is null) return;
        if (!watcher.NativeSupported) _log("stage-read-unsupported");
        _ = watcher.DisposeAsync().AsTask();
    }

    /// <summary>
    /// Waits for the overlay renderer and the native provider, then shows the button when the build offers tool stages.
    /// Returns null when the game ends first or the build has none.
    /// </summary>
    internal static async Task<DeveloperStageMenu?> StartAsync(IOverlayHost host, IGameNativeUi nativeUi, Task gameCompletion,
        Action<string> log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(nativeUi);
        // The renderer is ready only once the game creates its render window, after the player's login; wait for it or
        // the game's end, never a fixed deadline. An optional menu that cannot start must not end the overlay.
        var renderer = host.WaitUntilReadyAsync(cancellationToken);
        if (await Task.WhenAny(renderer, gameCompletion).ConfigureAwait(false) == gameCompletion) return null;
        try { await renderer.ConfigureAwait(false); }
        catch (Exception error) when (error is OperationCanceledException or IOException) { log("renderer-unavailable"); return null; }
        // The native provider loads with the client, so its capabilities can lag the overlay by several seconds.
        for (var attempt = 0; attempt < CapabilityAttempts && !gameCompletion.IsCompleted; attempt++)
        {
            GameNativeUiCapability? stage = null;
            try { stage = (await nativeUi.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false)).Operations.FirstOrDefault(item => item.Kind == "stage.open"); }
            catch (Exception error) when (error is GameNativeUiBusyException or InvalidDataException or IOException or JsonException) { }
            if (stage is { Supported: true })
            {
                var tools = Tools(stage);
                if (tools.Count == 0) { log("no-tools"); return null; }
                log("offered-" + tools.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var menu = new DeveloperStageMenu(host, nativeUi, tools, log);
                menu.FollowStage(gameCompletion);
                return menu;
            }
            if (stage is not null && stage.Reason != "providerUnavailable") { log("unsupported"); return null; }
            if (await Task.WhenAny(Task.Delay(CapabilityInterval, cancellationToken), gameCompletion).ConfigureAwait(false) == gameCompletion) return null;
        }
        log("provider-unavailable");
        return null;
    }

    /// <summary>Returns the tool keys a stage.open capability lists as <c>tool:&lt;key&gt;</c>, in the provider's order.</summary>
    internal static IReadOnlyList<string> Tools(GameNativeUiCapability capability) => capability.Features
        .Where(feature => feature.StartsWith("tool:", StringComparison.Ordinal) && IsKey(feature[5..]))
        .Select(feature => feature[5..]).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>The corner button that opens the list; it passes clicks outside itself to the game.</summary>
    internal const string ButtonHtml = """
        <style>
        :host { left:8px; top:8px; }
        button { font:600 11px/1 system-ui, sans-serif; letter-spacing:.06em; color:#fff; background:rgba(20,24,32,.72);
          border:1px solid rgba(255,255,255,.35); border-radius:4px; padding:5px 8px; cursor:pointer; }
        button:hover { background:rgba(40,60,90,.9); }
        </style>
        <button type="button" data-overlay-action="open" title="Developer tool stages">DEV</button>
        """;

    /// <summary>The list of tool stages plus the way back to the main menu.</summary>
    internal static string MenuHtml(IReadOnlyList<string> tools)
    {
        var html = new StringBuilder("""
            <style>
            :host { left:50%; top:50%; transform:translate(-50%,-50%); }
            .menu { display:flex; flex-direction:column; gap:4px; min-width:220px; padding:12px; font:13px/1.3 system-ui, sans-serif;
              color:#fff; background:rgba(16,20,28,.94); border:1px solid rgba(255,255,255,.3); border-radius:6px; }
            h2 { margin:0 0 4px; font-size:13px; font-weight:600; }
            p { margin:0 0 6px; font-size:11px; color:rgba(255,255,255,.7); }
            button { text-align:left; font:inherit; color:inherit; background:rgba(255,255,255,.08); border:1px solid transparent;
              border-radius:4px; padding:6px 10px; cursor:pointer; }
            button:hover { background:rgba(80,140,255,.35); border-color:rgba(120,170,255,.6); }
            hr { width:100%; border:0; border-top:1px solid rgba(255,255,255,.2); margin:4px 0; }
            </style>
            <div class="menu"><h2>Developer tool stages</h2><p>Tools open from the main menu.</p>
            """);
        foreach (var tool in tools)
            html.Append("<button type=\"button\" data-overlay-action=\"").Append(StagePrefix).Append(tool).Append("\">")
                .Append(WebUtility.HtmlEncode(Names.GetValueOrDefault(tool, tool))).Append("</button>");
        html.Append("<hr><button type=\"button\" data-overlay-action=\"").Append(StagePrefix).Append("mainMenu\">Back to main menu</button>")
            .Append("<button type=\"button\" data-overlay-action=\"").Append(CloseAction).Append("\">Close</button></div>");
        return html.ToString();
    }

    /// <summary>Explains a refused stage move in words; null when the move happened.</summary>
    internal static string? Explain(GameNativeUiResult result) => result.Status switch
    {
        "opened" => null,
        "notReady" when result.Reason == "notMainMenu" => "Open the main menu first: the tool stages open from there.",
        "notReady" when result.Reason == "notInToolStage" => "The game is not in a tool stage.",
        "notReady" when result.Reason == "stageSwitchPending" => "The game is still changing stage. Try again in a moment.",
        "notReady" => "The game is not ready yet.",
        "busy" => "The game is busy. Try again in a moment.",
        _ => "The stage could not be opened (" + result.Status + (result.Reason is null ? "" : ", " + result.Reason) + ").",
    };

    private async Task OpenAsync(string stage)
    {
        if (!IsKey(stage) || Interlocked.Exchange(ref _busy, 1) != 0) return;
        try
        {
            var id = "developer-stage-" + Guid.NewGuid().ToString("N");
            var result = await _nativeUi.InvokeAsync(new GameNativeUiRequest(id, "stage.open", "") { Stage = stage }).ConfigureAwait(false);
            for (var attempt = 0; result.Status == "queued" && attempt < ResultAttempts; attempt++)
            {
                await Task.Delay(100).ConfigureAwait(false);
                try { result = await _nativeUi.GetResultAsync(id).ConfigureAwait(false); }
                catch (GameNativeUiBusyException) { }
            }
            _log(stage + "-" + result.Status + (result.Reason is null ? "" : "-" + result.Reason));
            if (Explain(result) is { } message) ShowNotice(message);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or InvalidOperationException or ArgumentException)
        {
            _log(stage + "-" + error.GetType().Name);
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    private void ShowNotice(string message)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            _notice?.Dispose();
            _notice = _host.ShowMessageBox(NoticePanelId, "Developer tool stages", message, "OK", passThroughOutside: true);
        }
    }

    private static string? Action(OverlayMessageEventArgs args) =>
        args.EventName == "html.action" && args.Data.ValueKind == JsonValueKind.Object
        && args.Data.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;

    private static bool IsKey(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        StopFollowing();
        lock (_gate) { _notice?.Dispose(); _notice = null; }
        _menu.Dispose();
        _button.Dispose();
    }
}
