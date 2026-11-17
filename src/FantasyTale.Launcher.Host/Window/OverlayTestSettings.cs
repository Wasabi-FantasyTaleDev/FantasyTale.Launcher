using Tomlyn;
using Tomlyn.Model;

namespace FantasyTale.Launcher.Host.Window;

// Developer diagnostics are local preferences, never part of a shipped game definition.
internal sealed record OverlayTestSettings(bool StartupMessage, string? ResourceRoot, bool PassThroughOutside = true, bool DeveloperStages = false)
{
    internal static OverlayTestSettings Load(string dataRoot)
    {
        var file = Path.Combine(dataRoot, "overlay-test.toml");
        RejectLinks(file);
        if (!File.Exists(file)) return new(false, null);
        var table = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(file))
            ?? throw new InvalidDataException("overlay-test.toml must contain a table.");
        if (table.Keys.Any(key => key is not ("startup_message" or "resource_root" or "pass_through_outside" or "developer_stages")))
            throw new InvalidDataException("overlay-test.toml contains an unknown setting.");
        var enabled = table.TryGetValue("startup_message", out var value)
            ? value is bool flag ? flag : throw new InvalidDataException("overlay-test.toml: startup_message must be a boolean.")
            : false;
        string? resources = null;
        if (table.TryGetValue("resource_root", out var root))
        {
            if (root is not string path || !Path.IsPathFullyQualified(path) || !Directory.Exists(path))
                throw new InvalidDataException("overlay-test.toml: resource_root must be an existing absolute directory.");
            resources = Path.GetFullPath(path);
        }
        var passThrough = table.TryGetValue("pass_through_outside", out var outside)
            ? outside is bool passFlag ? passFlag : throw new InvalidDataException("overlay-test.toml: pass_through_outside must be a boolean.")
            : true;
        var stages = table.TryGetValue("developer_stages", out var developer)
            ? developer is bool stageFlag ? stageFlag : throw new InvalidDataException("overlay-test.toml: developer_stages must be a boolean.")
            : false;
        return new(enabled, resources, passThrough, stages);
    }

    internal void Save(string dataRoot)
    {
        var file = Path.Combine(dataRoot, "overlay-test.toml");
        RejectLinks(file);
        Directory.CreateDirectory(dataRoot);
        var table = new TomlTable { ["startup_message"] = StartupMessage, ["pass_through_outside"] = PassThroughOutside, ["developer_stages"] = DeveloperStages };
        if (ResourceRoot is not null) table["resource_root"] = ResourceRoot;
        var temporary = file + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, TomlSerializer.Serialize(table));
            File.Move(temporary, file, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Overlay test settings cannot traverse filesystem links.");
    }
}
