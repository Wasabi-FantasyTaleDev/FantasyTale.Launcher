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

/// <summary>A stable action name and caller-localized label for one game dialog button.</summary>
public sealed record OverlayDialogButton(string Name, string Label);
