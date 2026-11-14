using System.Diagnostics;

namespace FantasyTale.Launcher.Core.Definitions;

/// <summary>
/// Replaces a configuration file with a fully written temporary file. Windows refuses the replacing rename with
/// sharing or access errors while a reader still holds the old file open, even when that reader allows deletion, so
/// the rename is retried for a bounded time and then fails loud.
/// </summary>
internal static class AtomicFile
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    /// <summary>Moves <paramref name="temporary"/> over <paramref name="file"/>, waiting briefly for readers to close it.</summary>
    public static void Replace(string temporary, string file)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            try { File.Move(temporary, file, overwrite: true); return; }
            catch (Exception error) when (OperatingSystem.IsWindows() && error is IOException or UnauthorizedAccessException
                && (error.HResult & 0xffff) is 5 or 32 or 33 && elapsed.Elapsed < Deadline)
            { Thread.Sleep(10); }
        }
    }
}
