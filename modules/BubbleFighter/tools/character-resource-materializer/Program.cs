using System.Security.Cryptography;
using System.Text.Json;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;

if (args.Length == 3 && args[0] == "scan-textures")
{
    using var batch = JsonDocument.Parse(File.ReadAllText(args[1]));
    var clients = new List<object>();
    foreach (var client in batch.RootElement.EnumerateArray())
    {
        var build = client.GetProperty("build").GetString()!;
        var root = client.GetProperty("root").GetString()!;
        var files = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in client.GetProperty("archives").EnumerateArray())
        {
            var relative = item.GetString()!;
            using var archive = Archive.Open(Path.Combine(root, relative), relative);
            foreach (var entry in archive.Entries)
            {
                var path = entry.Path.Replace('\\', '/');
                if (!path.StartsWith("Models/Characters/Texture/", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(path)) throw new InvalidDataException("Duplicate shared texture: " + path);
                var bytes = archive.Read(path);
                files.Add(new { path, archive = relative, size = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
            }
        }
        clients.Add(new { build, root, files });
        Console.WriteLine($"Verified {build}: {files.Count} shared texture members");
    }
    File.WriteAllText(args[2], JsonSerializer.Serialize(clients));
    return;
}

if (args.Length == 3 && args[0] == "compose")
{
    using var batch = JsonDocument.Parse(File.ReadAllText(args[1]));
    Directory.CreateDirectory(args[2]);
    var report = new List<object>();
    var timer = System.Diagnostics.Stopwatch.StartNew();
    foreach (var job in batch.RootElement.EnumerateArray())
    {
        var input = job.GetProperty("input").GetString()!;
        var uri = job.GetProperty("uri").GetString()!;
        var relative = job.GetProperty("output").GetString()!;
        var output = Path.GetFullPath(Path.Combine(args[2], relative));
        var boundary = Path.GetFullPath(args[2]) + Path.DirectorySeparatorChar;
        if (!output.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Output escapes the composition directory.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string Digest(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
        var before = Digest(input);
        var edits = job.GetProperty("edits").EnumerateArray().Select(row => new ArchiveResourceEdit(
            row.GetProperty("path").GetString()!,
            row.TryGetProperty("remove", out var remove) && remove.GetBoolean() ? null : row.GetProperty("source").GetString()!,
            row.TryGetProperty("sha256", out var digest) ? digest.GetString()! : "",
            row.GetProperty("original_sha256").GetString() is { Length: > 0 } original ? original : null)).ToArray();
        long last = 0, total = 0;
        new ArchiveResourceComposer().Compose(input, uri, output, edits, (completed, expected) =>
        {
            if (completed < last || completed > expected || expected < total) throw new InvalidDataException("Non-monotonic resource progress.");
            last = completed; total = expected;
        });
        if (last != total || Digest(input) != before) throw new InvalidDataException("Composition changed its source or did not complete progress.");
        using var check = Archive.Open(output, uri);
        report.Add(new { relative, uri, inputSha256 = before, outputSha256 = Digest(output),
            edits = edits.Length, members = check.Entries.Count, bytes = new FileInfo(output).Length });
        File.WriteAllText(Path.Combine(args[2], "composition-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (report.Count % 25 == 0) Console.WriteLine($"Verified {report.Count} composed archives in {timer.Elapsed.TotalSeconds:F1}s");
    }
    Console.WriteLine($"PASS {report.Count} composed archives; source hashes unchanged; progress complete; {timer.Elapsed.TotalSeconds:F1}s");
    return;
}

if (args.Length != 2) throw new ArgumentException("Usage: CharacterResourceMaterializer requests.json output-directory");
using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
Directory.CreateDirectory(args[1]);
var requests = document.RootElement.EnumerateArray().Select(row => new
{
    Root = row.GetProperty("root").GetString()!, Archive = row.GetProperty("archive").GetString()!,
    Path = row.GetProperty("path").GetString()!, Sha256 = row.GetProperty("sha256").GetString()!,
    Size = row.GetProperty("size").GetInt64()
}).ToArray();
foreach (var group in requests.GroupBy(row => (row.Root, row.Archive)))
{
    using var archive = Archive.Open(Path.Combine(group.Key.Root, group.Key.Archive), group.Key.Archive);
    foreach (var row in group)
    {
        var bytes = archive.Read(row.Path);
        if (bytes.Length != row.Size || Convert.ToHexStringLower(SHA256.HashData(bytes)) != row.Sha256)
            throw new InvalidDataException("Source changed since the evidence inventory: " + row.Path);
        File.WriteAllBytes(Path.Combine(args[1], row.Sha256), bytes);
    }
}
Console.WriteLine($"PASS {requests.Length} verified content-addressed payloads");
