using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace FantasyTale.Launcher.Core.Instances;

internal static partial class VerifiedFileRemoval
{
    internal static async Task<bool> TryDeleteAsync(string path, long size, string hash, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Verified file removal requires Windows file handles.");
        InstanceStore.RejectLinks(path);
        // GENERIC_READ | DELETE, FILE_SHARE_READ, OPEN_EXISTING, OPEN_REPARSE_POINT | SEQUENTIAL_SCAN.
        using var handle = CreateFileW(path, 0x80010000, 1, 0, 3, 0x08200000, 0);
        if (handle.IsInvalid) throw new IOException("The game file could not be locked for removal.", new Win32Exception(Marshal.GetLastPInvokeError()));
        var resolved = ResolvedPath(handle);
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) resolved = @"\\" + resolved[8..];
        else if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved[4..];
        if (!Path.GetFullPath(resolved).Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            throw new IOException("client_remove_unsafe");
        using var input = new FileStream(handle, FileAccess.Read);
        if (input.Length != size || !Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false)).Equals(hash, StringComparison.OrdinalIgnoreCase)) return false;
        cancellationToken.ThrowIfCancellationRequested();
        byte delete = 1;
        // FileDispositionInfo marks this verified handle, rather than looking the path up again.
        if (!SetFileInformationByHandle(handle, 4, in delete, 1))
            throw new IOException("The verified game file could not be removed.", new Win32Exception(Marshal.GetLastPInvokeError()));
        return true;
    }

    private static unsafe string ResolvedPath(SafeFileHandle handle)
    {
        var buffer = new char[32768];
        fixed (char* pointer = buffer)
        {
            var length = GetFinalPathNameByHandleW(handle, pointer, (uint)buffer.Length, 0);
            if (length == 0 || length >= buffer.Length) throw new IOException("The game file path could not be verified.");
            return new string(buffer, 0, (int)length);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint sharing, nint attributes, uint disposition, uint flags, nint template);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandleW(SafeFileHandle file, char* path, uint length, uint flags);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, in byte information, uint size);
}
