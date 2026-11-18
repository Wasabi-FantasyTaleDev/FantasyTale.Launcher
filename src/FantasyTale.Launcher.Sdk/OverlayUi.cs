namespace FantasyTale.Launcher.Sdk;

/// <summary>Convenience methods for optional, event-driven game-style UI.</summary>
public static class OverlayUi
{
    /// <summary>Shows an exported game-UI component with default centered layout and owner-scoped events.</summary>
    public static IOverlayPanel ShowGameUi(this IOverlayHost host, string panelId, string component,
        object? props = null, bool interactive = false, bool modal = false, int layer = 0, bool passThroughOutside = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        var panel = host.CreateGameUiPanel(panelId, layer, interactive, modal, passThroughOutside);
        try { panel.MountGameUi(component, props); panel.Show(); return panel; }
        catch { panel.Dispose(); throw; }
    }

    /// <summary>Registers a centered component mount point without displaying or capturing input.</summary>
    public static IOverlayPanel CreateGameUiPanel(this IOverlayHost host, string panelId, int layer = 0,
        bool interactive = false, bool modal = false, bool passThroughOutside = false)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.RegisterPanel(panelId, new OverlayPanelDescriptor
        {
            Layer = layer, Interactive = interactive, Modal = modal, PassThroughOutside = passThroughOutside,
            InlineHtml = """
                <style>:host { left:50%;top:50%;transform:translate(-50%,-50%); }</style>
                <div data-game-ui-root></div>
                """,
        });
    }

    /// <summary>Displays a game-style message and returns its lifetime handle; action hides it automatically.</summary>
    public static IOverlayPanel ShowMessageBox(this IOverlayHost host, string panelId,
        string title, string text, string buttonLabel = "确定", Action<OverlayMessageEventArgs>? onAction = null, bool passThroughOutside = false) =>
        ShowDialog(host, panelId, title, text, [new("ok", buttonLabel)], onAction, passThroughOutside);

    /// <summary>Displays a confirmation with caller-localized labels and returns button events to the owner.</summary>
    public static IOverlayPanel ShowConfirmation(this IOverlayHost host, string panelId,
        string title, string text, string confirmLabel = "确定", string cancelLabel = "取消", Action<OverlayMessageEventArgs>? onAction = null, bool passThroughOutside = false) =>
        ShowDialog(host, panelId, title, text, [new("confirm", confirmLabel), new("cancel", cancelLabel)], onAction, passThroughOutside);

    /// <summary>Displays a reusable game-style dialog; dispose the handle when its owning plugin unloads.</summary>
    public static IOverlayPanel ShowDialog(this IOverlayHost host, string panelId,
        string title, string text, IReadOnlyList<OverlayDialogButton> buttons, Action<OverlayMessageEventArgs>? onAction = null, bool passThroughOutside = false)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(buttons);
        if (title.Length > 256 || text.Length > 8192 || buttons.Count is < 1 or > 4
            || buttons.Any(b => string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 64
                || string.IsNullOrWhiteSpace(b.Label) || b.Label.Length > 128)
            || buttons.Select(b => b.Name).Distinct(StringComparer.Ordinal).Count() != buttons.Count)
            throw new ArgumentException("Dialogs require bounded text and one to four uniquely named buttons.", nameof(buttons));
        var panel = host.CreateGameUiPanel(panelId, interactive: true, modal: true, passThroughOutside: passThroughOutside);
        try
        {
            if (onAction is not null)
                panel.MessageReceived += (_, args) => { if (args.EventName == "game-ui.action") onAction(args); };
            panel.Send("game-ui.message-box", new
            {
                title, text, buttons = buttons.Select(b => new { name = b.Name, label = b.Label }).ToArray(),
            });
            return panel;
        }
        catch { panel.Dispose(); throw; }
    }
}

/// <summary>Overlay drawing of the client's buttonless notification box.</summary>
public static class OverlayNotices
{
    /// <summary>
    /// Shows the client's notification box (NotifyBox) above every overlay surface. The overlay keeps one box: a new
    /// notice replaces the shown one. The returned task completes once the box was drawn and the request panel has
    /// been released after the notice closed; it faults when the page cannot draw the box.
    /// </summary>
    /// <param name="host">The overlay.</param>
    /// <param name="panelId">A unique id for the request panel; it is unregistered when the notice closes.</param>
    /// <param name="text">The notice, at most 2048 characters.</param>
    /// <param name="duration">How long the box stays, 1 to 15 seconds; three seconds when null.</param>
    /// <param name="cancellationToken">Stops waiting and releases the request panel; a drawn box still closes on its own.</param>
    public static async Task ShowNotifyBoxAsync(this IOverlayHost host, string panelId, string text, TimeSpan? duration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrEmpty(text);
        if (text.Length > 2048) throw new ArgumentException("A notice holds at most 2048 characters.", nameof(text));
        var milliseconds = duration is { } value ? value.TotalMilliseconds : 3000;
        if (milliseconds is < 1000 or > 15000) throw new ArgumentOutOfRangeException(nameof(duration), "A notice stays 1 to 15 seconds.");
        // The request panel is never shown; the page draws the shared NotifyBox and reports back through this panel.
        using var panel = host.CreateGameUiPanel(panelId);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        panel.MessageReceived += (_, args) =>
        {
            if (args.EventName == "game-ui.close") closed.TrySetResult();
            else if (args.EventName == "game-ui.error")
                closed.TrySetException(new InvalidOperationException("The overlay could not draw the notice."));
        };
        panel.Send("game-ui.notify-box", new { text, durationMs = milliseconds });
        await closed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>A stable action name and caller-localized label for one game dialog button.</summary>
public sealed record OverlayDialogButton(string Name, string Label);
