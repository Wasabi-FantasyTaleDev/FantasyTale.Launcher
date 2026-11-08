using System.Text.Json;

namespace FantasyTale.Launcher.Sdk;

/// <summary>Optional game-window UI service. A game or plugin must work when no overlay is available.</summary>
public interface IOverlayHost
{
    /// <summary>Whether the page has installed its message listener and can render panels.</summary>
    bool IsReady => false;

    /// <summary>Selected build identity and data-defined UI capabilities, when supplied by the launcher.</summary>
    OverlayBuildContext? BuildContext => null;

    /// <summary>Waits for browser readiness without blocking the game's render thread.</summary>
    Task WaitUntilReadyAsync(CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("This overlay host does not report readiness."));

    /// <summary>Registers an isolated HTML panel and returns its lifetime handle.</summary>
    IOverlayPanel RegisterPanel(string panelId, OverlayPanelDescriptor descriptor);
}

/// <summary>Describes one bounded HTML panel supplied by a trusted installed package.</summary>
public sealed class OverlayPanelDescriptor
{
    /// <summary>Inline HTML for a small panel. Exactly one content form is required.</summary>
    public string? InlineHtml { get; init; }

    /// <summary>File name relative to the package's approved overlay directory.</summary>
    public string? HtmlFileName { get; init; }

    /// <summary>Ordering band from zero to 10000; modal panels precede ordinary panels.</summary>
    public int Layer { get; init; }

    /// <summary>Whether shown panel bounds receive input instead of forwarding it to the game.</summary>
    public bool Interactive { get; init; }

    /// <summary>Whether the shown panel blocks lower panels and, unless PassThroughOutside is enabled, the game client area.</summary>
    public bool Modal { get; init; }

    /// <summary>Lets clicks outside this panel reach the game while retaining modal priority among overlay panels.</summary>
    public bool PassThroughOutside { get; init; }

    /// <summary>Whether a client-area scrim is shown beneath this panel.</summary>
    public bool Backdrop { get; init; }

    /// <summary>Optional CSS scrim color; null uses the foundation default pending final art direction.</summary>
    public string? BackdropColor { get; init; }
}

/// <summary>Controls one panel and receives events emitted by its page.</summary>
public interface IOverlayPanel : IDisposable
{
    /// <summary>The stable, package-scoped panel identifier.</summary>
    string PanelId { get; }

    /// <summary>Shows the panel.</summary>
    void Show();

    /// <summary>Hides the panel without unregistering it.</summary>
    void Hide();

    /// <summary>Sends a bounded named event and JSON payload to the panel.</summary>
    void Send(string eventName, object? data = null);

    /// <summary>Raised when the page emits an event to its owner.</summary>
    event EventHandler<OverlayMessageEventArgs> MessageReceived;

    /// <summary>
    /// Mounts a game-UI Vue component into the panel's shadow root.
    /// The panel HTML must contain a <c>&lt;div data-game-ui-root&gt;&lt;/div&gt;</c> mount point.
    /// <paramref name="component"/> is the exported component name from the game-ui library,
    /// for example <c>"BfMessageBox"</c> or <c>"BfWindow"</c>.
    /// Listen for <c>"game-ui.mounted"</c> or <c>"game-ui.error"</c> on <see cref="MessageReceived"/>
    /// to know whether mounting succeeded.
    /// </summary>
    void MountGameUi(string component, object? props = null);

    /// <summary>Enables input only while this panel is shown; hidden panels never capture game input.</summary>
    void SetInteractive(bool interactive) => Send("game-ui.interactive", new { interactive });

    /// <summary>Raises this panel within its layer without navigating or recreating the shared page.</summary>
    void BringToFront() => Send("game-ui.bring-to-front");

    /// <summary>Sets the optional full-client-area scrim beneath this panel.</summary>
    void SetBackdrop(bool enabled, string? color = null) => Send("game-ui.configure", new { backdrop = enabled, backdropColor = color });

    /// <summary>Changes whether a modal panel captures clicks outside its bounds; dragging still retains pointer capture.</summary>
    void SetPassThroughOutside(bool enabled) => Send("game-ui.configure", new { passThroughOutside = enabled });

    /// <summary>Requests an optional UI sound; unassigned cues are silent until sound assets are supplied.</summary>
    void PlaySound(OverlaySoundCue cue) => Send("game-ui.sound", new { cue = cue switch
    {
        OverlaySoundCue.DialogOpen => "dialog-open",
        OverlaySoundCue.ButtonPress => "button-press",
        OverlaySoundCue.Notification => "notification",
        _ => throw new ArgumentOutOfRangeException(nameof(cue)),
    } });
}

/// <summary>Stable UI sound cues; concrete audio assets remain optional.</summary>
public enum OverlaySoundCue
{
    /// <summary>A modal dialog has opened.</summary>
    DialogOpen,
    /// <summary>A UI action button has been pressed.</summary>
    ButtonPress,
    /// <summary>A nonmodal notification has arrived.</summary>
    Notification,
}

/// <summary>Data emitted by an overlay panel.</summary>
public sealed class OverlayMessageEventArgs(string panelId, string eventName, JsonElement data) : EventArgs
{
    /// <summary>The panel that emitted the event.</summary>
    public string PanelId { get; } = panelId;

    /// <summary>The event name.</summary>
    public string EventName { get; } = eventName;

    /// <summary>The JSON payload, or an empty element when no payload was supplied.</summary>
    public JsonElement Data { get; } = data;
}
