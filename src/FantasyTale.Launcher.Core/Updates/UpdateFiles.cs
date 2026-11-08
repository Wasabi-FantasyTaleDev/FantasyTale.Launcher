using System.Text.Json;
using FantasyTale.Launcher.Core.Stores;

namespace FantasyTale.Launcher.Core.Updates;

internal static class UpdateFiles
{
    internal static string Safe(string root, string path) => ClientInstallTransaction.Safe(root, path);
    internal static T? Read<T>(string path)
    {
        // Readers keep the opened snapshot while the helper atomically publishes the next record.
        using var stream = OpenRecord(path);
        if (stream is null) return default;
        return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidDataException("Invalid update record.");
    }
    private static FileStream? OpenRecord(string path)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
            // Optional records can disappear between observation and open during lifecycle transitions.
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            // Atomic replacement can briefly own the destination exclusively on Windows.
            catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) is 32 or 33 && elapsed.ElapsedMilliseconds < 1000)
            { Thread.Sleep(10); }
            // A delete-pending Windows handle reports access denied until its last owner closes it.
            catch (UnauthorizedAccessException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 5 && elapsed.ElapsedMilliseconds < 1000)
            { Thread.Sleep(10); }
        }
    }
    internal static void Write<T>(string path, T value) => WriteFile(path, file => JsonSerializer.Serialize(file, value));
    internal static void WriteBytes(string path, byte[] value) => WriteFile(path, file => file.Write(value));
    private static void WriteFile(string path, Action<FileStream> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        FantasyTale.Launcher.Core.Instances.InstanceStore.RejectLinks(temp);
        try
        {
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { write(file); file.Flush(true); }
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    // Replacement keeps an already-open reader on the previous complete record.
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                    break;
                }
                catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) is 32 or 33 && elapsed.ElapsedMilliseconds < 1000)
                { Thread.Sleep(10); }
            }
        }
        finally { File.Delete(temp); }
    }
    internal static byte[] ReadIndex(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[checked((int)input.Length)]; input.ReadExactly(bytes); return bytes;
    }
}
