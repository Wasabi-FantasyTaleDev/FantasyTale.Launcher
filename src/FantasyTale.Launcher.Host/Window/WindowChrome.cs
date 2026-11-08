using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FantasyTale.Launcher.Host.Window;

/// <summary>Preserves native input handling and work-area bounds for the borderless WebView window.</summary>
internal static unsafe partial class WindowChrome
{
    private const uint GetMinMaxInfo = 0x0024;
    private const uint NcDestroy = 0x0082;
    private const nuint SubclassId = 0x4654;

    internal static void Attach(nint window)
    {
        if (!OperatingSystem.IsWindows()) return;
        // A layered HWND makes transparent WebView pixels click through to other applications.
        // DWM supplies the backdrop; the top-level window must keep rectangular input bounds.
        const int extendedStyle = -20;
        const int layered = 0x00080000;
        const int appWindow = 0x00040000, toolWindow = 0x00000080;
        var style = GetWindowLongW(window, extendedStyle);
        var normalStyle = (style & ~(layered | toolWindow)) | appWindow;
        if (style != normalStyle)
        {
            if (SetWindowLongW(window, extendedStyle, normalStyle) == 0 && Marshal.GetLastPInvokeError() is var error && error != 0)
                throw new Win32Exception(error, "Cannot restore native window input bounds.");
            if (!SetWindowPos(window, 0, 0, 0, 0, 0, 0x0037))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot refresh the window frame.");
        }
        if (!SetWindowSubclass(window, &WindowProcedure, SubclassId, 0)) throw new Win32Exception("Cannot install the window work-area handler.");
    }

    internal static (Size Size, System.Drawing.Point Location, Size Minimum)? InitialBounds(nint window)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var monitor = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        if (!GetMonitorInfoW(MonitorFromWindow(window, 2), ref monitor)) throw new Win32Exception("Cannot read the monitor work area.");
        var scale = GetDpiForWindow(window) / 96.0;
        var availableWidth = monitor.Work.Right - monitor.Work.Left;
        var availableHeight = monitor.Work.Bottom - monitor.Work.Top;
        // Photino accepts physical pixels, while the WebView lays out in device-independent pixels.
        var width = Math.Min((int)(1360 * scale), availableWidth - (int)(48 * scale));
        var height = Math.Min((int)(840 * scale), availableHeight - (int)(48 * scale));
        return (new Size(width, height), new System.Drawing.Point(monitor.Work.Left + (availableWidth - width) / 2,
            monitor.Work.Top + (availableHeight - height) / 2), new Size(Math.Min(width, (int)(960 * scale)), Math.Min(height, (int)(600 * scale))));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == NcDestroy) _ = RemoveWindowSubclass(window, &WindowProcedure, id);
        var result = DefSubclassProc(window, message, wParam, lParam);
        if (message == GetMinMaxInfo && lParam != 0)
        {
            var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
            if (GetMonitorInfoW(MonitorFromWindow(window, 2), ref info))
            {
                var bounds = (MinMaxInfo*)lParam;
                // Popup windows otherwise maximize over the taskbar; retain the host's minimum tracking size.
                bounds->MaximumPosition = new Point { X = info.Work.Left - info.Monitor.Left, Y = info.Work.Top - info.Monitor.Top };
                bounds->MaximumSize = new Point { X = info.Work.Right - info.Work.Left, Y = info.Work.Bottom - info.Work.Top };
            }
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo { internal Point Reserved, MaximumSize, MaximumPosition, MinimumTrackSize, MaximumTrackSize; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { internal uint Size; internal Rect Monitor, Work; internal uint Flags; }

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(nint window, delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id, nuint data);
    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveWindowSubclass(nint window, delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id);
    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint window, uint flags);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint window);
    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(nint window, int index);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int SetWindowLongW(nint window, int index, int value);
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
