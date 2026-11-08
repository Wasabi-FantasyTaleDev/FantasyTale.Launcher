using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FantasyTale.Launcher.Core.Hosting;

// stdin EOF covers the start/assignment interval; the job also owns descendants after assignment.
internal sealed partial class ServerProcessJob : IDisposable
{
    private readonly SafeFileHandle _handle;
    internal ServerProcessJob()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Local hosting currently requires Windows.");
        _handle = CreateJobObjectW(0, null);
        if (_handle.IsInvalid) throw new Win32Exception();
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
        if (!SetInformationJobObject(_handle, 9, in limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        { var error = new Win32Exception(); _handle.Dispose(); throw error; }
    }
    internal void Assign(Process process)
    { if (!AssignProcessToJobObject(_handle, process.SafeHandle)) throw new Win32Exception(); }
    internal void Terminate()
    { if (!TerminateJobObject(_handle, 1)) throw new Win32Exception(); }

    internal async Task<bool> DrainAsync()
    {
        var terminated = ActiveProcesses() != 0;
        if (terminated) Terminate();
        var watch = Stopwatch.StartNew();
        while (ActiveProcesses() != 0)
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("The owned server process tree did not exit.");
            await Task.Delay(25).ConfigureAwait(false);
        }
        return terminated;
    }

    private uint ActiveProcesses()
    {
        if (!QueryInformationJobObject(_handle, 1, out var accounting, (uint)Marshal.SizeOf<Accounting>(), 0)) throw new Win32Exception();
        return accounting.ActiveProcesses;
    }
    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    { internal long ProcessTime, JobTime; internal uint Flags; internal nuint MinimumWorkingSet, MaximumWorkingSet; internal uint ActiveProcesses; internal nuint Affinity; internal uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits { internal BasicLimits Basic; internal IoCounters Io; internal nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting
    {
        internal long TotalUserTime, TotalKernelTime, ThisPeriodUserTime, ThisPeriodKernelTime;
        internal uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(SafeFileHandle job, int informationClass, out Accounting information, uint length, nint returnedLength);
    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateJobObjectW(nint attributes, string? name);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(SafeFileHandle job, int informationClass, in ExtendedLimits limits, uint length);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateJobObject(SafeFileHandle job, uint exitCode);
}
