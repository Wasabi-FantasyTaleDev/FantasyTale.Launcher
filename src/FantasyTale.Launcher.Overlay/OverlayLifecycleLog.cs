using System.IO;
using System.Text;
using System.Text.Json;

namespace FantasyTale.Launcher.Overlay;

// Records lifecycle transitions only, never panel HTML, event data or launch secrets.
internal static class OverlayLifecycleLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FantasyTale", "Launcher", "overlay-lifecycle.jsonl");

    internal static void Write(string name, object? detail = null)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            utc = DateTimeOffset.UtcNow, pid = Environment.ProcessId, name, detail,
        }) + "\n");
        lock (Gate)
        {
            try
            {
                for (var path = LogPath; path is not null; path = Path.GetDirectoryName(path))
                    if (Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
                if (Path.Exists(LogPath + ".1") && (File.GetAttributes(LogPath + ".1") & FileAttributes.ReparsePoint) != 0) return;
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length + bytes.Length > 64 * 1024)
                    File.Move(LogPath, LogPath + ".1", true);
                using var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(bytes);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
