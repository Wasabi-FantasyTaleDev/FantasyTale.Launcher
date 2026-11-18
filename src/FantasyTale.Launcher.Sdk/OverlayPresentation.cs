using System.Text.Json;

namespace FantasyTale.Launcher.Sdk;

/// <summary>
/// What the overlay is displaying over the game: every shown surface in page pixels of a viewport that spans the game's
/// client area. Owners read it to decide whether something the game draws itself would be hidden behind the overlay.
/// </summary>
/// <param name="Visible">Whether any overlay surface is shown, the overlay's own notices included.</param>
/// <param name="ViewportWidth">Overlay page width in page pixels; zero while unknown.</param>
/// <param name="ViewportHeight">Overlay page height in page pixels; zero while unknown.</param>
/// <param name="Surfaces">Shown surfaces other than the overlay's own notices, lowest first.</param>
public sealed record OverlayPresentation(bool Visible, double ViewportWidth, double ViewportHeight, IReadOnlyList<OverlaySurface> Surfaces)
{
    /// <summary>The overlay is not showing anything, or has not reported yet.</summary>
    public static OverlayPresentation Hidden { get; } = new(false, 0, 0, []);

    // The game draws its own notification box in the client's middle band; the overlay page uses the same band.
    private const double NoticeLeft = 0.2, NoticeTop = 0.2, NoticeRight = 0.8, NoticeBottom = 0.8;

    /// <summary>
    /// Whether a shown surface would hide the game's own notification box: any modal or scrim, or a surface that
    /// reaches into the client's middle band. The overlay should then draw the notice itself.
    /// </summary>
    public bool CoversNativeNotice() => Surfaces.Any(surface => surface.Modal || surface.Backdrop
        || surface.Width > 0 && surface.Height > 0 && ViewportWidth > 0 && ViewportHeight > 0
        && surface.X < ViewportWidth * NoticeRight && surface.X + surface.Width > ViewportWidth * NoticeLeft
        && surface.Y < ViewportHeight * NoticeBottom && surface.Y + surface.Height > ViewportHeight * NoticeTop);

    /// <summary>Reads the overlay page's presentation report; a report without surfaces describes none.</summary>
    /// <param name="state">The page's <c>presentation-state</c> object.</param>
    /// <exception cref="FormatException">The report is not an object or a surface is malformed.</exception>
    public static OverlayPresentation FromState(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Object) throw new FormatException("A presentation report must be an object.");
        var visible = state.TryGetProperty("visible", out var shown) && shown.ValueKind == JsonValueKind.True;
        double width = 0, height = 0;
        if (state.TryGetProperty("viewport", out var viewport) && viewport.ValueKind == JsonValueKind.Object)
        {
            width = Finite(viewport, "width");
            height = Finite(viewport, "height");
        }
        var surfaces = state.TryGetProperty("surfaces", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Take(64).Select(item => new OverlaySurface(Finite(item, "x"), Finite(item, "y"),
                Math.Max(0, Finite(item, "width")), Math.Max(0, Finite(item, "height")),
                item.TryGetProperty("modal", out var modal) && modal.ValueKind == JsonValueKind.True,
                item.TryGetProperty("backdrop", out var backdrop) && backdrop.ValueKind == JsonValueKind.True)).ToArray()
            : [];
        return new(visible, width, height, surfaces);
    }

    private static double Finite(JsonElement owner, string name)
    {
        if (owner.ValueKind != JsonValueKind.Object || !owner.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number || !double.IsFinite(value.GetDouble()))
            throw new FormatException($"The presentation report's {name} must be a finite number.");
        return value.GetDouble();
    }
}

/// <summary>One shown overlay surface in page pixels.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
/// <param name="Modal">Whether it blocks the panels below it.</param>
/// <param name="Backdrop">Whether it draws a scrim over the whole client area.</param>
public sealed record OverlaySurface(double X, double Y, double Width, double Height, bool Modal, bool Backdrop);
