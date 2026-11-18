namespace FantasyTale.Launcher.Sdk;

/// <summary>
/// A rectangle in game client pixels where the game shows something of its own, such as a native message box, that the
/// overlay must not cover: panels are cut open there and pointer input inside it reaches the game.
/// </summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width; always positive.</param>
/// <param name="Height">Height; always positive.</param>
public readonly record struct OverlayNativeHole(int X, int Y, int Width, int Height)
{
    /// <summary>The most holes the overlay accepts at once; further forms are ignored.</summary>
    public const int MaximumCount = 16;

    /// <summary>
    /// Builds the holes for the visible native forms with a nonempty rectangle, each grown by <paramref name="margin"/> on
    /// every side. The result is ordered by position so equal inputs give equal outputs.
    /// </summary>
    /// <param name="forms">Live native forms, as <see cref="IGameNativeUi.GetFormsAsync"/> reports them.</param>
    /// <param name="margin">Extra game pixels around each form.</param>
    /// <exception cref="ArgumentOutOfRangeException">The margin is negative.</exception>
    public static OverlayNativeHole[] FromForms(IEnumerable<GameNativeUiForm> forms, int margin = 0)
    {
        ArgumentNullException.ThrowIfNull(forms);
        ArgumentOutOfRangeException.ThrowIfNegative(margin);
        return [.. forms.Where(form => form.Visible && form.Width > 0 && form.Height > 0)
            .Select(form => new OverlayNativeHole(form.X - margin, form.Y - margin, form.Width + 2 * margin, form.Height + 2 * margin))
            .Distinct().OrderBy(hole => hole.Y).ThenBy(hole => hole.X).ThenBy(hole => hole.Width).ThenBy(hole => hole.Height)
            .Take(MaximumCount)];
    }
}
