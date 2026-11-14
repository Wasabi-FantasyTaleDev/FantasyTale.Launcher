using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Window;

/// <summary>Retains bounded local lifecycle events without bridge arguments, replies or exception messages.</summary>
internal sealed class HostLifecycleLog(string dataRoot) : ILaunchDiagnosticSink
{
    private readonly string _path = Path.Combine(dataRoot, "host-lifecycle.jsonl");
    private readonly string _launchPath = Path.Combine(dataRoot, "launch-lifecycle.jsonl");
    private readonly string _controllerPath = Path.Combine(dataRoot, "overlay-controller.jsonl");
    private readonly object _gate = new();
    internal const int MaximumBytes = 64 * 1024;

    internal void Write(string name, string? detail = null)
    {
        // Call sites supply event names, registered method names and exception types only.
        if (!Safe(name) || detail is not null && !Safe(detail)) throw new ArgumentException("Invalid lifecycle event.");
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            utc = DateTimeOffset.UtcNow, pid = Environment.ProcessId,
            name, detail,
        }) + "\n");
        lock (_gate)
        {
            TryAppend(_path, bytes);
            // Frequent UI polling must not rotate away launch, recovery or controller evidence.
            if (name.StartsWith("launch-", StringComparison.Ordinal) || name.StartsWith("gateway-", StringComparison.Ordinal))
                TryAppend(_launchPath, bytes);
            if (name.EndsWith("-controller", StringComparison.Ordinal) || name.StartsWith("overlay-controller-", StringComparison.Ordinal))
                TryAppend(_controllerPath, bytes);
        }
    }

    /// <inheritdoc />
    public void Record(string eventName, string? detail = null) => Write(eventName, detail);

    internal string ReadLaunchRecords()
    {
        var records = new List<string>();
        lock (_gate)
        {
            foreach (var path in new[] { _launchPath + ".1", _launchPath })
            {
                RejectLinks(path);
                if (!File.Exists(path)) continue;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > MaximumBytes) throw new IOException("Lifecycle record exceeds its bounded size.");
                var bytes = new byte[checked((int)stream.Length)];
                stream.ReadExactly(bytes);
                foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        using var json = JsonDocument.Parse(line);
                        var row = json.RootElement;
                        var name = row.GetProperty("name").GetString();
                        var detail = row.GetProperty("detail").ValueKind == JsonValueKind.Null ? null : row.GetProperty("detail").GetString();
                        if (name is null || !Safe(name) || detail is not null && !Safe(detail)) continue;
                        records.Add(JsonSerializer.Serialize(new
                        {
                            utc = row.GetProperty("utc").GetDateTimeOffset(), pid = row.GetProperty("pid").GetInt32(), name, detail,
                        }));
                    }
                    catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { }
                }
            }
        }
        return string.Join('\n', records.TakeLast(512));
    }

    private static void TryAppend(string path, byte[] bytes)
    {
        try { Append(path, bytes); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Each optional sink fails independently, without affecting the window or another sink.
            Trace.WriteLine("Lifecycle recording unavailable: " + error.GetType().Name);
        }
    }

    private static void Append(string path, byte[] bytes)
    {
        RejectLinks(path); RejectLinks(path + ".1");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > MaximumBytes)
            File.Move(path, path + ".1", true);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
    }

    private static bool Safe(string value) => value.Length is > 0 and <= 160 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Lifecycle path contains a filesystem link.");
    }
}
