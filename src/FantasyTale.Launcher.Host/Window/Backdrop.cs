using System.Runtime.InteropServices;

namespace FantasyTale.Launcher.Host.Window;

/// <summary>
/// Gives the window the Windows 11 system backdrop and title-bar colour mode, so the web
/// content's transparent areas show Mica like a native WinUI 3 window. No effect elsewhere.
/// </summary>
internal static partial class Backdrop
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmSystemBackdropType = 38;
    private const int DwmWindowCornerPreference = 33;

    /// <summary>System backdrop kinds accepted by DWM.</summary>
    public enum Kind
    {
        /// <summary>No backdrop.</summary>
        None = 1,

        /// <summary>Mica, the default for main windows.</summary>
        Mica = 2,

        /// <summary>Acrylic, for transient surfaces.</summary>
        Acrylic = 3,

        /// <summary>Tabbed Mica (Mica Alt).</summary>
        MicaAlt = 4,
    }

    /// <summary>Applies <paramref name="kind"/> and the dark or light frame to <paramref name="window"/>.</summary>
    public static void Apply(IntPtr window, Kind kind, bool dark)
    {
        if (window == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var darkValue = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(window, DwmUseImmersiveDarkMode, ref darkValue, sizeof(int));
        var round = 2;
        _ = DwmSetWindowAttribute(window, DwmWindowCornerPreference, ref round, sizeof(int));
        var backdrop = (int)kind;
        _ = DwmSetWindowAttribute(window, DwmSystemBackdropType, ref backdrop, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
