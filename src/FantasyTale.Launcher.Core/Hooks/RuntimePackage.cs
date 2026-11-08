using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;

namespace FantasyTale.Launcher.Core.Hooks;

internal sealed record RuntimePayload(string Path, long Size, string Sha256);
internal sealed record RuntimePackage(string Version, IReadOnlyDictionary<string, RuntimePayload> Files)
{
    internal static RuntimePackage Load(string folder, string game)
    {
        if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException("A native package directory must be absolute.");
        var file = Path.Combine(folder, "runtime.toml");
        InstanceStore.RejectLinks(file);
        var root = TomlReader.Load(file);
        root.OnlyKeys("schema", "runtime", "files");
        if (root.Integer("schema", true) != 1) throw root.Error("schema", "must be 1");
        var runtime = root.Table("runtime", true)!;
        runtime.OnlyKeys("game", "version", "session_schema");
        if (runtime.String("game", true) != game) throw runtime.Error("game", "package belongs to a different game");
        if (runtime.Integer("session_schema", true) != 2) throw runtime.Error("session_schema", "must support schema 2 readiness reports");
        var version = runtime.String("version", true);
        _ = PluginVersion.Parse(version);
        var files = new Dictionary<string, RuntimePayload>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in root.Tables("files"))
        {
            row.OnlyKeys("path", "size", "sha256");
            var path = FileName(row.String("path", true));
            var size = row.Integer("size", true, 1)!.Value;
            var hash = row.String("sha256", true);
            if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigitLower(c))) throw row.Error("sha256", "must contain 64 lower-case hexadecimal characters");
            if (!files.TryAdd(path, new RuntimePayload(Path.Combine(folder, path), size, hash))) throw row.Error("path", "duplicate payload");
        }
        if (files.Count == 0) throw root.Error("files", "at least one payload is required");
        return new RuntimePackage(version, files);
    }

    internal static string FileName(string value)
    {
        if (value.Length is < 1 or > 128 || value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ')
            || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            || value.Equals(SessionWriter.FileName, StringComparison.OrdinalIgnoreCase) || value.StartsWith("fthook-", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Native deployment requires a plain client-directory file name.");
        var stem = value.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '0' and <= '9'))
            throw new IOException("Reserved native file name.");
        return value;
    }
}
