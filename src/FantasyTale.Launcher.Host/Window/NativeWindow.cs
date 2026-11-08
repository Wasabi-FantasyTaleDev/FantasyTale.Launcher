using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FantasyTale.Launcher.Host.Window;

/// <summary>Window behaviour the chromeless web view cannot do by itself on Windows.</summary>
internal static partial class NativeWindow
{
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    internal static void SetApplicationIdentity()
    {
        // Shell-launched development builds must not inherit the parent app's taskbar group.
        if (OperatingSystem.IsWindows()) Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID("FantasyTale.Launcher"));
    }

    internal static bool IsMaximized(nint window) => OperatingSystem.IsWindows() && IsZoomed(window);

    internal static bool ToggleMaximize(nint window)
    {
        if (!OperatingSystem.IsWindows()) return false;
        _ = ShowWindow(window, IsZoomed(window) ? 9 : 3);
        return IsZoomed(window);
    }

    internal static void Minimize(nint window)
    {
        if (OperatingSystem.IsWindows()) _ = ShowWindow(window, 6);
    }

    internal static void ShowBeforeWebViewInitialization(nint window)
    {
        // An inherited SW_HIDE leaves WebView child HWNDs hidden even after the outer window is activated.
        // The environment callback has not run yet; establish visibility before the controller is created.
        if (OperatingSystem.IsWindows())
        {
            _ = ShowWindow(window, IsIconic(window) ? 9 : 5);
            _ = SetForegroundWindow(window);
        }
    }

    /// <summary>
    /// Starts a system move loop as if the user pressed the title bar, so dragging, snapping
    /// and Aero Shake behave natively. Called when the frontend's drag area is pressed.
    /// </summary>
    public static void BeginDrag(IntPtr window)
    {
        if (window == IntPtr.Zero || !OperatingSystem.IsWindows()) return;
        _ = ReleaseCapture();
        _ = SendMessageW(window, WmNcLButtonDown, HtCaption, IntPtr.Zero);
    }

    /// <summary>True when Windows apps are set to the dark theme (default true elsewhere).</summary>
    public static bool AppsUseDarkTheme()
    {
        if (!OperatingSystem.IsWindows()) return true;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    private static partial IntPtr SendMessageW(IntPtr window, int message, nint wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);
}
