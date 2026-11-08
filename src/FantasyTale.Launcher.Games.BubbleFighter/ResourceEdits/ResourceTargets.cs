using System.Text.Json;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Games.BubbleFighter.CharacterModels;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>A resolved logical resource location for one build: archive, member, sheet, key and field mapping.</summary>
/// <param name="Name">Logical target name such as item, strings, xml-strings or ui.</param>
/// <param name="Kind">spreadsheet, delimited or ui.</param>
/// <param name="Archive">Client-relative physical archive.</param>
/// <param name="Member">Virtual member path; null for ui, where each edit names its form.</param>
/// <param name="Sheet">Worksheet name for spreadsheets.</param>
/// <param name="HeaderRow">One-based header row.</param>
/// <param name="Keys">Record key column references.</param>
/// <param name="Fields">Logical field to original header.</param>
/// <param name="Orientation">auto, vertical or horizontal record layout for spreadsheets.</param>
public sealed record ResourceTarget(string Name, string Kind, string Archive, string? Member, string? Sheet, int HeaderRow,
    IReadOnlyList<string> Keys, IReadOnlyDictionary<string, string> Fields, string Orientation = "auto")
{
    internal TableShape Shape => new(Sheet, HeaderRow, Keys, Orientation);

    /// <summary>Maps a logical field to its header; unknown names are used as raw headers.</summary>
    public string Column(string field) => Fields.TryGetValue(field, out var column) ? column : field;

    /// <summary>Canonical text for cache identity and snapshots.</summary>
    internal string Identity => JsonSerializer.Serialize(new { Name, Kind, Archive, Member, Sheet, HeaderRow, Keys, Orientation, Fields = Fields.OrderBy(p => p.Key, StringComparer.Ordinal) });
}

/// <summary>Built-in per-build defaults from resource-targets.toml, with validated partial overrides from manifests.</summary>
public static partial class ResourceTargets
{
    private static readonly Lazy<ResourceData> Embedded = new(() =>
    {
        using var stream = typeof(ResourceTargets).Assembly.GetManifestResourceStream("FantasyTale.Launcher.Games.BubbleFighter.resource-targets.toml")
            ?? throw new InvalidOperationException("Missing embedded resource-targets.toml");
        using var reader = new StreamReader(stream);
        var data = ResourceData.Parse(reader.ReadToEnd(), "resource-targets.toml");
        data.Keys("schema", "targets"); data.Schema();
        return data;
    });

    private static readonly string[] TargetKeys = ["build", "name", "kind", "archive", "member", "sheet", "header_row", "keys", "fields", "languages", "orientation"];

    /// <summary>Default targets of a build without manifest overrides, for read-only callers and settings pages.</summary>
    internal static IReadOnlyDictionary<string, ResourceTarget> Defaults(string build, string? language) => Resolve(build, language, []);

    /// <summary>
    /// Resolves every target for a build: defaults first, then each override row in order, key by key. A row's
    /// <c>languages.&lt;client language&gt;</c> table maps logical fields to that language's columns and wins over <c>fields</c>.
    /// </summary>
    internal static IReadOnlyDictionary<string, ResourceTarget> Resolve(string build, string? language, IEnumerable<ResourceData> overrides)
    {
        var layers = new Dictionary<string, List<ResourceData>>(StringComparer.Ordinal);
        foreach (var row in Embedded.Value.Rows("targets").Concat(overrides))
        {
            row.Keys(TargetKeys);
            var target = row.Text("build", "*");
            if (target != "*" && target != build) continue;
            var name = row.Text("name");
            if (!layers.TryGetValue(name, out var list)) layers.Add(name, list = []);
            list.Add(row);
        }
        var result = new Dictionary<string, ResourceTarget>(StringComparer.Ordinal);
        foreach (var (name, rows) in layers)
        {
            string? Pick(string key) => rows.LastOrDefault(r => r.Has(key))?.Text(key);
            var kind = Pick("kind") ?? throw new InvalidDataException("Resource target " + name + " has no kind for build " + build);
            if (kind is not ("spreadsheet" or "delimited" or "ui")) throw new InvalidDataException("Unknown resource target kind: " + kind);
            var archive = ArchiveResourceComposer.ValidatePath(Pick("archive") ?? throw new InvalidDataException("Resource target " + name + " has no archive for build " + build));
            var member = Pick("member");
            if (member is not null) ArchiveResourceComposer.ValidatePath(member);
            if (kind != "ui" && member is null) throw new InvalidDataException("Resource target " + name + " has no member for build " + build);
            var orientation = Pick("orientation") ?? "auto";
            if (orientation is not ("auto" or "vertical" or "horizontal") || orientation != "auto" && kind != "spreadsheet")
                throw new InvalidDataException("Resource target " + name + " has an invalid orientation: " + orientation);
            var header = rows.LastOrDefault(r => r.Has("header_row"))?.Integer("header_row") ?? 1;
            if (header < 1) throw new InvalidDataException("Resource target " + name + " header_row must be positive");
            // A horizontal record is identified by its [Field] row cell; there is no key column to name.
            var keys = rows.LastOrDefault(r => r.Has("keys"))?.List("keys") ?? (orientation == "horizontal" ? ["[Field]"] : []);
            if (kind != "ui" && keys.Length == 0) throw new InvalidDataException("Resource target " + name + " has no keys for build " + build);
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            // Later rows win; within a row the language table refines that row's own fields.
            foreach (var row in rows)
            {
                if (row.Table("fields") is { } table)
                    foreach (var field in table.Names) fields[field] = table.Text(field);
                if (language is null || row.Table("languages") is not { } languages) continue;
                foreach (var code in languages.Names.Where(code => string.Equals(code, language, StringComparison.OrdinalIgnoreCase)))
                {
                    var mapping = languages.Table(code)!;
                    foreach (var field in mapping.Names) fields[field] = mapping.Text(field);
                }
            }
            result.Add(name, new(name, kind, archive, member, Pick("sheet"), (int)header, keys, fields, orientation));
        }
        return result;
    }
}
