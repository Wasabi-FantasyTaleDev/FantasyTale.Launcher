namespace FantasyTale.Launcher.Overlay;

/// <summary>Launcher UI languages an overlay page may be opened in.</summary>
internal static class OverlayLocales
{
    /// <summary>Returns whether a value names a launcher UI language.</summary>
    internal static bool IsKnown(string? value) => value is "zh-CN" or "zh-TW" or "ko" or "en" or "id" or "vi" or "th";
}
