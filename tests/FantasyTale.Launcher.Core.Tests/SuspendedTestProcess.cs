using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FantasyTale.Launcher.Core.Tests;

// A suspended primary thread leaves the image mapped before the user-mode loader initializes its module list.
internal sealed partial class SuspendedTestProcess : IDisposable
{
    private readonly SafeProcessHandle _process;

    internal SuspendedTestProcess(string executable)
    {
        var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };
        if (!CreateProcessW(executable, 0, 0, 0, false, 0x4, 0, Path.GetDirectoryName(executable)!, ref startup, out var information))
            throw new Win32Exception();
        _process = new SafeProcessHandle(information.Process, ownsHandle: true);
        using var thread = new SafeWaitHandle(information.Thread, ownsHandle: true);
    }

    public void Dispose()
    {
        try
        {
            if (!TerminateProcess(_process, 0)) throw new Win32Exception();
            if (WaitForSingleObject(_process, 15000) != 0)
                throw new TimeoutException("The owned suspended test process did not exit.");
        }
        finally { _process.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        internal uint Size;
        internal nint Reserved, Desktop, Title;
        internal uint X, Y, XSize, YSize, XCharacters, YCharacters, FillAttribute, Flags;
        internal ushort ShowWindow, ReservedSize;
        internal nint ReservedData, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        internal nint Process, Thread;
        internal uint ProcessId, ThreadId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(string application, nint commandLine, nint processAttributes, nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment, string directory,
        ref StartupInfo startup, out ProcessInformation information);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
}
