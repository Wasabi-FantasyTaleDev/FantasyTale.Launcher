using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace FantasyTale.Launcher.Host.Window;

/// <summary>Owns the Windows notification icon and a native Show/Exit menu without a second UI message loop.</summary>
internal sealed unsafe partial class TrayIcon : IDisposable
{
    private const uint Callback = 0x8000 + 77;
    private const nuint SubclassId = 0x465454;
    private static TrayIcon? _current;
    private readonly nint _window;
    private readonly Action _exit;
    private readonly uint _taskbarCreated;
    private readonly nint _icon;
    private bool _disposed;
    internal string Language { get; set; } = CultureInfo.CurrentUICulture.Name;

    internal TrayIcon(nint window, Action exit)
    {
        _window = window; _exit = exit; _current = this;
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        var size = GetSystemMetricsForDpi(49, GetDpiForWindow(window));
        _icon = LoadImageW(0, Path.Combine(AppContext.BaseDirectory, "Assets", "launcher.ico"), 1, size, size, 0x10);
        if (_icon == 0) { _current = null; throw new Win32Exception(Marshal.GetLastPInvokeError(), "Cannot load the launcher tray icon."); }
        try
        {
            if (!SetWindowSubclass(window, &WindowProcedure, SubclassId, 0)) throw new Win32Exception("Cannot attach the tray handler.");
            Add();
        }
        catch { Dispose(); throw; }
    }

    private void Add()
    {
        var icon = Data();
        if (!Shell_NotifyIconW(0, ref icon)) throw new Win32Exception("Cannot create the launcher notification icon.");
    }

    private NotificationData Data()
    {
        var data = new NotificationData { Size = (uint)sizeof(NotificationData), Window = _window, Id = 1, Flags = 7,
            CallbackMessage = Callback, Icon = _icon };
        const string title = "FantasyTale Launcher";
        for (var i = 0; i < title.Length; i++) data.Tip[i] = title[i];
        return data;
    }

    internal void Hide() { _ = ShowWindow(_window, 0); }
    internal void Show() { _ = ShowWindow(_window, IsIconic(_window) ? 9 : 5); _ = SetForegroundWindow(_window); }

    internal bool ConfirmExit(bool running) => !running || MessageBoxW(_window,
        Text("Games are still running. Exiting the launcher will end them. Unsaved progress may be lost. Continue?",
            "仍有游戏正在运行。退出启动器将结束这些游戏，未保存的进度可能丢失。是否继续？",
            "仍有遊戲正在執行。結束啟動器將關閉這些遊戲，未儲存的進度可能遺失。是否繼續？",
            "게임이 실행 중입니다. 런처를 종료하면 게임도 종료되며 저장하지 않은 진행 상황을 잃을 수 있습니다. 계속할까요?",
            "Game masih berjalan. Keluar dari launcher akan mengakhiri game. Progres yang belum disimpan dapat hilang. Lanjutkan?",
            "Trò chơi vẫn đang chạy. Thoát launcher sẽ kết thúc trò chơi và có thể mất tiến trình chưa lưu. Tiếp tục?"),
        "FantasyTale Launcher", 0x00000004 | 0x00000030 | 0x00000100) == 6;

    internal void Error() => MessageBoxW(_window,
        Text("Could not end the game or restore its files. The launcher will remain open. Check the client diagnostics.",
            "无法结束游戏或恢复客户端文件。启动器将保持运行，请检查客户端诊断记录。",
            "無法結束遊戲或還原用戶端檔案。啟動器將繼續執行，請檢查診斷記錄。",
            "게임 종료 또는 파일 복원에 실패했습니다. 런처를 유지합니다. 진단 기록을 확인하세요.",
            "Game tidak dapat diakhiri atau berkas dipulihkan. Launcher tetap terbuka. Periksa diagnostik.",
            "Không thể kết thúc trò chơi hoặc khôi phục tệp. Launcher vẫn mở. Hãy kiểm tra chẩn đoán."), "FantasyTale Launcher", 0x10);

    private string Text(string en, string cn, string tw, string ko, string id, string vi) => Language switch
    { "zh-TW" or "zh-HK" => tw, var s when s.StartsWith("zh", StringComparison.OrdinalIgnoreCase) => cn,
      var s when s.StartsWith("ko", StringComparison.OrdinalIgnoreCase) => ko, var s when s.StartsWith("id", StringComparison.OrdinalIgnoreCase) => id,
      var s when s.StartsWith("vi", StringComparison.OrdinalIgnoreCase) => vi, _ => en };

    private void Menu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;
        try
        {
            AppendMenuW(menu, 0, 1, Text("Open launcher", "打开启动器", "開啟啟動器", "런처 열기", "Buka launcher", "Mở launcher"));
            AppendMenuW(menu, 0, 2, Text("Exit launcher", "退出启动器", "結束啟動器", "런처 종료", "Keluar dari launcher", "Thoát launcher"));
            GetCursorPos(out var point); SetForegroundWindow(_window);
            var command = TrackPopupMenu(menu, 0x0100 | 0x0002, point.X, point.Y, 0, _window, 0);
            if (command == 1) Show(); else if (command == 2) _exit();
            PostMessageW(_window, 0, 0, 0);
        }
        finally { DestroyMenu(menu); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (_current is { } tray && !tray._disposed && window == tray._window)
        {
            if (message == Callback)
            {
                if ((uint)lParam is 0x0203 or 0x0202) tray.Show();
                else if ((uint)lParam == 0x0205) tray.Menu();
                return 0;
            }
            if (message == tray._taskbarCreated)
            {
                // Explorer recreates its notification area after a restart.
                var icon = tray.Data(); _ = Shell_NotifyIconW(0, ref icon);
            }
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; var icon = Data(); _ = Shell_NotifyIconW(2, ref icon);
        _ = RemoveWindowSubclass(_window, &WindowProcedure, SubclassId); _current = null;
        _ = DestroyIcon(_icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotificationData
    {
        internal uint Size; internal nint Window; internal uint Id, Flags, CallbackMessage; internal nint Icon;
        internal fixed char Tip[128]; internal uint State, StateMask; internal fixed char Info[256];
        internal uint Timeout; internal fixed char InfoTitle[64]; internal uint InfoFlags; internal Guid Guid; internal nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [LibraryImport("shell32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool Shell_NotifyIconW(uint operation, ref NotificationData data);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial uint RegisterWindowMessageW(string message);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)] private static partial nint LoadImageW(nint instance, string name, uint type, int width, int height, uint flags);
    [LibraryImport("user32.dll")] private static partial int GetSystemMetricsForDpi(int index, uint dpi);
    [LibraryImport("user32.dll")] private static partial uint GetDpiForWindow(nint window);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyIcon(nint icon);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(nint window, int command);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool IsIconic(nint window);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(nint window);
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial int MessageBoxW(nint window, string text, string caption, uint flags);
    [LibraryImport("user32.dll")] private static partial nint CreatePopupMenu();
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool AppendMenuW(nint menu, uint flags, nuint id, string text);
    [LibraryImport("user32.dll")] private static partial uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rect);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetCursorPos(out Point point);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool DestroyMenu(nint menu);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool PostMessageW(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetWindowSubclass(nint window, delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id, nuint data);
    [LibraryImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool RemoveWindowSubclass(nint window, delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nuint, nuint, nint> callback, nuint id);
    [LibraryImport("comctl32.dll")] private static partial nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
}
