namespace FantasyTale.Launcher.Sdk;

/// <summary>A native UI rectangle in game client pixels or a stated reference viewport.</summary>
public sealed record OverlayAnchor(double X, double Y, double Width, double Height)
{
    /// <summary>Source viewport width, or zero to use the current physical game client width.</summary>
    public double ReferenceWidth { get; init; }
    /// <summary>Source viewport height, or zero to use the current physical game client height.</summary>
    public double ReferenceHeight { get; init; }
    /// <summary>Whether the source UI is currently visible; hidden sources hide their attached panel.</summary>
    public bool Visible { get; init; } = true;
    /// <summary>Panel placement relative to the native rectangle.</summary>
    public OverlayAnchorPlacement Placement { get; init; } = OverlayAnchorPlacement.Cover;
    /// <summary>Gap in logical overlay pixels for adjacent placement.</summary>
    public double Gap { get; init; }
}

/// <summary>How a panel covers or extends a native UI element.</summary>
public enum OverlayAnchorPlacement
{
    /// <summary>Use the same bounds as the original UI.</summary>
    Cover,
    /// <summary>Place the panel at the left edge, outside the original UI.</summary>
    Left,
    /// <summary>Place the panel at the right edge, outside the original UI.</summary>
    Right,
    /// <summary>Place the panel above the original UI.</summary>
    Above,
    /// <summary>Place the panel below the original UI.</summary>
    Below,
    /// <summary>Center the panel over the original UI without resizing it.</summary>
    Center,
}

/// <summary>Supplies target rectangles from hooks or an optional future image locator.</summary>
public interface IOverlayAnchorProvider
{
    /// <summary>Observes a named target; disposing stops notifications and releases its tracking resources.</summary>
    IDisposable Subscribe(string targetId, Action<OverlayAnchor> changed);
}

/// <summary>Connects source-independent UI location updates to the shared overlay page.</summary>
public static class OverlayAnchors
{
    /// <summary>Updates an anchor without reopening the page; null restores the panel's original layout.</summary>
    public static void SetAnchor(this IOverlayPanel panel, OverlayAnchor? anchor)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (anchor is null) { panel.Send("game-ui.anchor", null); return; }
        if (!new[] { anchor.X, anchor.Y, anchor.Width, anchor.Height, anchor.ReferenceWidth, anchor.ReferenceHeight, anchor.Gap }
            .All(double.IsFinite) || anchor.Width <= 0 || anchor.Height <= 0 || anchor.ReferenceWidth < 0 || anchor.ReferenceHeight < 0
            || (anchor.ReferenceWidth == 0) != (anchor.ReferenceHeight == 0) || anchor.Gap < 0 || !Enum.IsDefined(anchor.Placement))
            throw new ArgumentException("An anchor requires finite bounds and a complete reference viewport.", nameof(anchor));
        panel.Send("game-ui.anchor", new
        {
            x = anchor.X, y = anchor.Y, width = anchor.Width, height = anchor.Height,
            referenceWidth = anchor.ReferenceWidth, referenceHeight = anchor.ReferenceHeight,
            visible = anchor.Visible, placement = anchor.Placement.ToString().ToLowerInvariant(), gap = anchor.Gap,
        });
    }

    /// <summary>Follows a hook or image-locator target; retain and dispose this subscription with the plugin.</summary>
    public static IDisposable FollowAnchor(this IOverlayPanel panel, IOverlayAnchorProvider provider, string targetId)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        return provider.Subscribe(targetId, panel.SetAnchor);
    }
}
