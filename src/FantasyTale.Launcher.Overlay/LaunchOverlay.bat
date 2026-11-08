@echo off
rem Launch the FantasyTale overlay attached to a running BubbleFighter window.
rem The overlay exe is expected beside this script in the Release build output.
rem
rem Usage:
rem   LaunchOverlay.bat                  attach to the first BubbleFighter window
rem   LaunchOverlay.bat demo             free-floating demo mode (no game needed)
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
set "OVERLAY_EXE=%SCRIPT_DIR%bin\Release\net10.0-windows\FantasyTale.Launcher.Overlay.exe"

if not exist "%OVERLAY_EXE%" (
    echo Overlay executable not found:
    echo   %OVERLAY_EXE%
    echo Run "dotnet build -c Release" first.
    pause
    exit /b 1
)

if /i "%~1"=="demo" (
    echo Starting overlay in demo mode...
    start "" "%OVERLAY_EXE%"
    exit /b 0
)

echo Looking for BubbleFighter window...
for /f "usebackq tokens=*" %%H in (
    `powershell -NoProfile -NonInteractive -Command "(Get-Process -Name 'BubbleFighter*' -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1).MainWindowHandle"`
) do set "HWND=%%H"

if "%HWND%"=="" (
    echo No BubbleFighter window found. Start the game first, or run with 'demo' for free mode.
    pause
    exit /b 1
)

echo Found game HWND: %HWND%
echo Starting overlay...
start "" "%OVERLAY_EXE%" %HWND%
exit /b 0
