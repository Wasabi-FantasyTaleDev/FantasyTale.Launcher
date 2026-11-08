using System.ComponentModel;
using System.Runtime.InteropServices;
using FantasyTale.Launcher.ModuleDependency;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.ModuleFixture;

/// <summary>An SDK-only module package exercising separate managed and optional native dependencies.</summary>
public sealed partial class FixtureModule : IGameModule
{
    /// <summary>The synthetic game served by every isolated fixture package.</summary>
    public string GameId => "module-fixture";

    /// <summary>Returns the resolved dependency directory without starting a game.</summary>
    public LaunchPlan CreateLaunchPlan(LaunchContext context)
    {
        if (File.Exists(Path.Combine(context.InstanceFolder, "http-probe")))
        {
            try { context.AutomaticSelection!.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (HttpRequestException) { return new("fixture.exe", [], PackageIdentity.Folder); }
            throw new InvalidOperationException("The host callback must throw a network failure.");
        }
        return new("fixture.exe", [],
            File.Exists(Path.Combine(context.InstanceFolder, "native-probe")) ? NativeFolder() : PackageIdentity.Folder);
    }

    private static unsafe string NativeFolder()
    {
        var handle = NativeLibrary.Load("fixture-native", typeof(FixtureModule).Assembly, null);
        try
        {
            var query = Marshal.GetDelegateForFunctionPointer<FormatCount>(NativeLibrary.GetExport(handle, "GetNumberOfFormats"));
            if (query(out var count) != 0 || count == 0) throw new InvalidDataException("The native module fixture returned no archive formats.");
            // Query the held library handle directly instead of enumerating modules during parallel loads.
            var buffer = new char[32768];
            fixed (char* path = buffer)
            {
                var length = GetModuleFileNameW(handle, path, (uint)buffer.Length);
                if (length == 0 || length >= buffer.Length) throw new Win32Exception(Marshal.GetLastPInvokeError());
                return Path.GetDirectoryName(new string(buffer, 0, (int)length))!;
            }
        }
        finally { NativeLibrary.Free(handle); }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FormatCount(out uint count);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static unsafe partial uint GetModuleFileNameW(nint module, char* path, uint capacity);
}
