# LaunchOverlayAgainstGame.ps1
# Finds the first BubbleFighter.exe window and launches the overlay attached to it.
# Usage: .\LaunchOverlayAgainstGame.ps1 [-OverlayExe <path>]
param(
    [string]$OverlayExe = "$PSScriptRoot\bin\Release\net10.0-windows\FantasyTale.Launcher.Overlay.exe"
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
    [DllImport("user32.dll")] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
}
"@

# BubbleFighter uses window class "CAbstractBubbleFighterWindow" and title varies by build.
# Fall back to searching by process name if FindWindow returns zero.
$hwnd = [Win32]::FindWindow("CAbstractBubbleFighterWindow", $null)

if ($hwnd -eq [IntPtr]::Zero) {
    $proc = Get-Process -Name "BubbleFighter*" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($proc) {
        $hwnd = $proc.MainWindowHandle
    }
}

if ($hwnd -eq [IntPtr]::Zero) {
    Write-Host "BubbleFighter window not found. Start the game first, then run this script."
    exit 1
}

Write-Host "Found game HWND: $($hwnd.ToInt64())"
Write-Host "Starting overlay..."
Start-Process -FilePath $OverlayExe -ArgumentList $hwnd.ToInt64()
