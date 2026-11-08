using System.Runtime.InteropServices;
using FantasyTale.Launcher.Host.Window;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class WindowTests
{
    [Fact]
    public void NativeWindowEntryPointRetainsTheStaThreadContract()
    {
        var entry = typeof(NativeWindow).Assembly.EntryPoint;
        Assert.NotNull(entry);
        Assert.True(entry.IsDefined(typeof(STAThreadAttribute), inherit: false));
        Assert.Equal(typeof(void), entry.ReturnType);
        Assert.False(entry.IsDefined(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), inherit: false));
    }

    [Fact]
    public void BorderlessToolWindowBecomesAnIndependentTaskbarApplication()
    {
        if (!OperatingSystem.IsWindows()) return;
        var window = CreateWindowExW(0x80, "STATIC", "Launcher taskbar regression", 0x80000000, 100, 100, 600, 400, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        try
        {
            WindowChrome.Attach(window);
            Assert.Equal(0, GetWindowLongW(window, -20) & 0x80);
            Assert.NotEqual(0, GetWindowLongW(window, -20) & 0x40000);
        }
        finally { _ = DestroyWindow(window); }
    }
    [Fact]
    public void HiddenStartupIsMadeVisibleBeforeWebViewControllerCreation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var window = CreateWindowExW(0, "STATIC", "Launcher startup regression", 0x80000000, 100, 100, 600, 400, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        try
        {
            Assert.False(IsWindowVisible(window));
            NativeWindow.ShowBeforeWebViewInitialization(window);
            Assert.True(IsWindowVisible(window));
            NativeWindow.Minimize(window);
            Assert.True(IsIconic(window));
            NativeWindow.ShowBeforeWebViewInitialization(window);
            Assert.False(IsIconic(window));
            Assert.True(IsWindowVisible(window));
        }
        finally { _ = DestroyWindow(window); }
    }

    [Fact]
    public void TransparentHostRetainsInputAcrossItsEntireClientArea()
    {
        if (!OperatingSystem.IsWindows()) return;
        const uint layered = 0x00080000;
        // SS_NOTIFY makes this stand-in accept client input like the WebView host.
        var window = CreateWindowExW(layered, "STATIC", "Launcher input regression", 0x80000100, 100, 100, 600, 400, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        try
        {
            Assert.NotEqual(0, GetWindowLongW(window, -20) & layered);
            WindowChrome.Attach(window);
            Assert.Equal(0, GetWindowLongW(window, -20) & layered);
            foreach (var (x, y) in new[] { (128, 480), (320, 120), (350, 300) })
            {
                // Settings, title-bar and empty content points must remain client input, not HTTRANSPARENT.
                var result = SendMessageW(window, 0x0084, 0, (y << 16) | x);
                Assert.Equal((nint)1, result);
            }
        }
        finally { _ = DestroyWindow(window); }
    }

    [Fact]
    public void BorderlessWindowMaximizesToWorkAreaAndRestoresRepeatedly()
    {
        if (!OperatingSystem.IsWindows()) return;
        var window = CreateWindowExW(0, "STATIC", "Launcher window regression", 0x80070000, 100, 100, 600, 400, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        try
        {
            WindowChrome.Attach(window);
            Assert.True(GetWindowRect(window, out var original));
            for (var i = 0; i < 3; i++)
            {
                Assert.True(NativeWindow.ToggleMaximize(window));
                Assert.True(NativeWindow.IsMaximized(window));
                var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
                Assert.True(GetMonitorInfoW(MonitorFromWindow(window, 2), ref monitor));
                Assert.True(GetWindowRect(window, out var maximized));
                Assert.Equal(monitor.Work, maximized);
                Assert.False(NativeWindow.ToggleMaximize(window));
                Assert.True(GetWindowRect(window, out var restored));
                Assert.Equal(original, restored);
            }
            NativeWindow.Minimize(window);
            Assert.True(IsIconic(window));
            _ = ShowWindow(window, 9);
            Assert.False(IsIconic(window));
            Assert.True(GetWindowRect(window, out var afterMinimize));
            Assert.Equal(original, afterMinimize);
        }
        finally { _ = DestroyWindow(window); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { internal uint Size; internal Rect Monitor, Work; internal uint Flags; }
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint extended, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rect rect);
    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint window, uint flags);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);
    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(nint window, int index);
    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
}
