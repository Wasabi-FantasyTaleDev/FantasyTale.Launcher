using System.Globalization;
using System.Text.RegularExpressions;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Games.BubbleFighter.CharacterModels;
using FantasyTale.Launcher.Sdk;
using Tomlyn.Model;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>
/// Reads resource-edits manifests into planned edits for the current build and client language. Besides generic
/// <c>[[edits]]</c>, a manifest may use <c>[[items]]</c> and <c>[[strings]]</c>, which address the build's default item and
/// string table targets by logical field so a plugin never needs physical columns or archive locations, and
/// <c>[[tables]]</c>, which reads many edits of any targets from a CSV sheet with one column per language.
/// </summary>
internal static class ResourceEditPlanner
{
    internal const string Provider = "resource-edits";
    private static readonly string[] EditKeys = ["build", "when", "target", "member", "action", "key", "control", "position", "element", "set", "expect", "clear", "sheet"];
    private static readonly string[] ItemKeys = ["build", "when", "target", "action", "id", "name", "description", "set", "expect", "clear"];
    private static readonly string[] StringKeys = ["build", "when", "target", "action", "key", "text", "set", "expect", "clear"];
    internal static readonly string[] PatternKeys = ["build", "when", "target", "sheet", "member", "member_match", "action", "key", "control",
        "match", "fields", "find", "replace", "set", "clear"];

    /// <summary>The language a launch writes: the selected client language, else the build's native language.</summary>
    internal static string? Language(LaunchContext context) =>
        context.ClientLanguage is { Length: > 0 } selected ? selected : context.Build.NativeLanguage is { Length: > 0 } native ? native : null;

    /// <summary>Parses and validates one selection; edits gated off by build or option are skipped.</summary>
    internal static ResourcePlan Load(LaunchContext context, GameResourceSelection selection)
    {
        var package = selection.Package;
        var data = ResourceData.Load(ArchiveResourceComposer.SafeFile(package.Folder, package.Resources!.Manifest));
        data.Keys("schema", "fallback_languages", "targets", "edits", "items", "strings", "tables", "patterns"); data.Schema();
        var language = Language(context);
        var reader = new ManifestReader(context, selection, data.Source, data.List("fallback_languages"), language,
            ResourceTargets.Resolve(context.Build.Id, language, data.Rows("targets")), new(package.Id, package.Version, package.Name));
        foreach (var row in data.Rows("edits")) { row.Keys(EditKeys); reader.Edit(row); }
        foreach (var row in data.Rows("items"))
        {
            row.Keys(ItemKeys);
            var set = new List<(string Field, object? Raw)>();
            if (row.Has("name")) set.Add(("name", row.Raw("name")));
            if (row.Has("description")) set.Add(("description", row.Raw("description")));
            reader.Record(row, row.Text("target", "item"), "patch", row.Raw("id"), set);
        }
        foreach (var row in data.Rows("strings"))
        {
            row.Keys(StringKeys);
            var set = new List<(string Field, object? Raw)>();
            if (row.Has("text")) set.Add(("text", row.Raw("text")));
            reader.Record(row, row.Text("target", "strings"), "upsert", row.Raw("key"), set);
        }
        foreach (var row in data.Rows("patterns")) { row.Keys(PatternKeys); reader.Pattern(row, data.Source); }
        foreach (var row in data.Rows("tables")) { row.Keys("path", "build", "when", "target", "encoding"); reader.Table(row, package.Folder); }
        return new(reader.Result, reader.Patterns);
    }
}

/// <summary>One selection's literal edits and regex pattern edits for the current build and language.</summary>
internal sealed record ResourcePlan(List<PlannedEdit> Edits, List<PatternEdit> Patterns);

/// <summary>Turns manifest rows into planned edits with one set of gating, value and language rules.</summary>
internal sealed class ManifestReader(LaunchContext context, GameResourceSelection selection, string source, string[] fallback, string? language,
    IReadOnlyDictionary<string, ResourceTarget> targets, EditOwner owner)
{
    internal List<PlannedEdit> Result { get; } = [];

    /// <summary>Regex pattern edits in declaration order.</summary>
    internal List<PatternEdit> Patterns { get; } = [];

    /// <summary>A generic edit row: tables take member and layout from the target, UI edits name a form and control.</summary>
    internal void Edit(ResourceData row)
    {
        if (!Gate(row)) return;
        var target = Target(row.Text("target"));
        var action = Action(row.Text("action"));
        if (target.Kind != "ui")
        {
            if (row.Has("member") || row.Has("control") || row.Has("element") || row.Has("position"))
                throw new InvalidDataException(source + ": table edits take member and layout from their target");
            var set = row.Table("set") is { } values ? values.Names.Select(field => (field, values.Raw(field))).ToList() : [];
            Add(row, target, action, row.Raw("key"), set);
            return;
        }
        if (row.Has("key") || row.Has("clear")) throw new InvalidDataException(source + ": UI edits use control, not key or clear");
        if (action == EditAction.Hide)
            throw new InvalidDataException("resource.ui.hide-unsupported: no native visibility field is proven for this build; use remove or wait for support");
        if (action == EditAction.Upsert) throw new InvalidDataException(source + ": UI edits do not support upsert");
        var member = ArchiveResourceComposer.ValidatePath(row.Text("member"));
        var path = row.Text("control");
        var required = action is EditAction.Add or EditAction.Replace;
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (row.Table("set") is { } patch)
            foreach (var field in patch.Names)
                if (Value(patch.Raw(field), required) is { Text: { } text }) attributes[field] = text;
        var expect = new Dictionary<string, string>(StringComparer.Ordinal);
        if (row.Table("expect") is { } expected) foreach (var field in expected.Names) expect[field] = expected.Text(field);
        var element = row.Has("element") ? Value(row.Raw("element"), true)!.Text : null;
        if (action == EditAction.Patch && attributes.Count == 0) return;
        var position = row.Integer("position");
        Result.Add(new(owner, target, target.Archive, member, "ui", path, action, null,
            new UiEdit(action, path, attributes, expect, element, position is null ? null : checked((int)position.Value))));
    }

    /// <summary>An items or strings row: a logical target with a default action and convenience fields.</summary>
    internal void Record(ResourceData row, string name, string defaultAction, object? key, List<(string Field, object? Raw)> convenience)
    {
        if (!Gate(row)) return;
        var target = Target(name);
        if (target.Kind == "ui") throw new InvalidDataException(source + ": target " + name + " is not a table");
        if (row.Table("set") is { } values)
            foreach (var field in values.Names)
            {
                if (convenience.Any(c => c.Field == field)) throw new InvalidDataException(source + ": field written twice: " + field);
                convenience.Add((field, values.Raw(field)));
            }
        Add(row, target, Action(row.Text("action", defaultAction)), key, convenience);
    }

    private void Add(ResourceData row, ResourceTarget target, EditAction action, object? rawKey, List<(string Field, object? Raw)> fields)
    {
        // One workbook target can serve its sibling worksheets, such as FighterConfigMode beside FighterConfig.
        if (row.Has("sheet"))
        {
            if (target.Kind != "spreadsheet") throw new InvalidDataException(source + ": only spreadsheet targets take a sheet");
            target = target with { Sheet = row.Text("sheet") };
        }
        if (action == EditAction.Hide) throw new InvalidDataException(source + ": tables have no hide action; use remove");
        var key = Key(rawKey);
        if (key.Length != target.Keys.Count) throw new InvalidDataException(source + ": target " + target.Name + " needs " + target.Keys.Count + " key values");
        // Add and replace write a complete record, so a missing translation is an error; patch and upsert keep the original.
        var required = action is EditAction.Add or EditAction.Replace;
        var set = new Dictionary<string, CellValue>(StringComparer.Ordinal);
        foreach (var (field, raw) in fields)
            if (Value(raw, required) is { } value && !set.TryAdd(target.Column(field), value))
                throw new InvalidDataException(source + ": field written twice: " + field);
        foreach (var field in row.List("clear"))
            if (!set.TryAdd(target.Column(field), new CellValue(null))) throw new InvalidDataException(source + ": field written twice: " + field);
        var expect = new Dictionary<string, string>(StringComparer.Ordinal);
        if (row.Table("expect") is { } expected) foreach (var field in expected.Names) expect[target.Column(field)] = expected.Text(field);
        if (action is EditAction.Patch or EditAction.Upsert && set.Count == 0) return;
        Result.Add(new(owner, target, target.Archive, target.Member!, target.Sheet ?? "", Columns.Key(key), action,
            new RecordEdit(action, key, set, expect), null));
    }

    private bool Gate(ResourceData row)
    {
        var builds = row.List("build");
        if (builds.Length > 0 && !builds.Contains("*") && !builds.Contains(context.Build.Id)) return false;
        var when = row.Text("when", "");
        if (when.Length == 0) return true;
        if (!selection.Options.TryGetValue(when, out var value) || value is not bool enabled)
            throw new InvalidDataException(source + ": edit gates require a declared boolean option: " + when);
        return enabled;
    }

    private ResourceTarget Target(string name) => targets.TryGetValue(name, out var target) ? target
        : throw new InvalidDataException(source + ": target " + name + " is not defined for build " + context.Build.Id);

    private EditAction Action(string text) => text switch
    {
        "add" => EditAction.Add, "replace" => EditAction.Replace, "patch" => EditAction.Patch, "remove" => EditAction.Remove,
        "hide" => EditAction.Hide, "upsert" => EditAction.Upsert,
        var other => throw new InvalidDataException(source + ": unknown action " + other)
    };

    /// <summary>Resolves a manifest value; language tables use the client language, then declared fallbacks, else keep the original.</summary>
    private CellValue? Value(object? raw, bool required) => raw switch
    {
        string text => new CellValue(text),
        long number => new CellValue(number.ToString(CultureInfo.InvariantCulture), true),
        double number => new CellValue(number.ToString("R", CultureInfo.InvariantCulture), true),
        TomlTable localized => Localized(localized, required),
        _ => throw new InvalidDataException(source + ": values must be strings, numbers or language tables")
    };

    private CellValue? Localized(TomlTable table, bool required)
    {
        foreach (var candidate in (language is null ? [] : new[] { language }).Concat(fallback))
            foreach (var (code, value) in table)
                if (string.Equals(code, candidate, StringComparison.OrdinalIgnoreCase))
                    return new CellValue(value as string ?? throw new InvalidDataException(source + ": localized values must be strings"));
        return required ? throw new InvalidDataException(source + ": no value for client language " + (language ?? "(none)") + " and no fallback") : null;
    }

    private string[] Key(object? raw) => raw switch
    {
        string text => [text],
        long number => [number.ToString(CultureInfo.InvariantCulture)],
        TomlArray array => array.Select(item => item switch
        {
            string text => text, long number => number.ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException(source + ": key values must be strings or integers")
        }).ToArray(),
        _ => throw new InvalidDataException(source + ": edit requires key")
    };

    private static readonly string[] SheetColumns = ["target", "sheet", "member", "member_match", "key", "match", "field", "find", "action", "build", "value", "comment"];

    /// <summary>
    /// Reads a CSV edit sheet. Columns: target, key, field (required); member for UI rows; optional action, build, sheet,
    /// value (a text for every language) and comment; every other header is a client language code holding that language's
    /// text. Rows for one record and action merge into one edit; an empty language cell means that language is not provided.
    /// </summary>
    internal void Table(ResourceData row, string packageFolder)
    {
        if (!Gate(row)) return;
        var path = ArchiveResourceComposer.ValidatePath(row.Text("path"));
        var file = ArchiveResourceComposer.SafeFile(packageFolder, path);
        var encoding = row.Text("encoding", "");
        var sheet = CsvSheet.Parse(File.ReadAllBytes(file), path, encoding.Length == 0 ? null : encoding.ToLowerInvariant());
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        var languages = new List<(string Code, int Column)>();
        for (var i = 0; i < sheet.Header.Length; i++)
        {
            var name = sheet.Header[i];
            if (name.Length == 0)
            {
                if (sheet.Records.Any(r => r.Fields[i].Length > 0)) throw new InvalidDataException(path + ": a column with values has no header");
                continue;
            }
            var reserved = SheetColumns.FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (reserved is not null && reserved != name) throw new InvalidDataException(path + ": write the column " + name + " as " + reserved);
            if (columns.ContainsKey(name) || languages.Any(l => l.Code.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(path + ": duplicate column " + name);
            if (reserved is not null) columns.Add(name, i); else languages.Add((name, i));
        }
        foreach (var required in new[] { "key", "field" })
            if (!columns.ContainsKey(required)) throw new InvalidDataException(path + ": missing column " + required);
        if (!columns.ContainsKey("target") && !row.Has("target")) throw new InvalidDataException(path + ": missing column target and no table target");
        var groups = new Dictionary<string, (TomlTable Edit, TomlTable Set, List<int> Lines)>(StringComparer.Ordinal);
        foreach (var (line, fields) in sheet.Records)
        {
            string Cell(string column) => columns.TryGetValue(column, out var index) ? fields[index].Trim() : "";
            var targetName = Cell("target") is { Length: > 0 } named ? named : row.Text("target", "");
            if (targetName.StartsWith('#')) continue;
            // Regex cells keep their spaces: whitespace is significant in a pattern.
            string Raw(string column) => columns.TryGetValue(column, out var index) ? fields[index] : "";
            if (Raw("match").Length > 0 || Raw("find").Length > 0) AddSheetPattern(path, line, targetName, Cell, Raw, fields, languages);
            else AddSheetRow(path, line, targetName, Cell, fields, languages, groups);
        }
        foreach (var (_, (edit, set, lines)) in groups)
        {
            if (set.Count > 0) edit["set"] = set;
            try { Edit(new ResourceData(edit, source)); }
            catch (InvalidDataException error) { throw new InvalidDataException(path + ":" + string.Join(",", lines) + ": " + error.Message, error); }
        }
    }

    private void AddSheetRow(string path, int line, string targetName, Func<string, string> cell, string[] fields,
        List<(string Code, int Column)> languages, Dictionary<string, (TomlTable Edit, TomlTable Set, List<int> Lines)> groups)
    {
        string Fail(string message) => throw new InvalidDataException(path + ":" + line + ": " + message);
        if (targetName.Length == 0) Fail("no target");
        if (!targets.TryGetValue(targetName, out var target)) Fail("target " + targetName + " is not defined for build " + context.Build.Id);
        var ui = target!.Kind == "ui";
        var key = cell("key");
        if (key.Length == 0) Fail("no key");
        var action = cell("action") is { Length: > 0 } named ? named : ui ? "patch" : "upsert";
        var field = cell("field");
        var value = cell("value");
        var texts = new TomlTable();
        foreach (var (code, column) in languages) if (fields[column].Length > 0) texts[code] = fields[column];
        if (value.Length > 0 && texts.Count > 0) Fail("value and language columns are both filled");
        if (action == "remove")
        {
            if (field.Length > 0 || value.Length > 0 || texts.Count > 0) Fail("a remove row takes no field or text");
        }
        else
        {
            if (field.Length == 0) Fail("no field");
            // A field with no text in any language is a template row awaiting translation.
            if (value.Length == 0 && texts.Count == 0) return;
        }
        var builds = cell("build").Split([',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries);
        var scope = string.Join("\u001F", targetName, cell("sheet"), cell("member"), key, string.Join(",", builds));
        if (_sheetActions.TryGetValue(scope, out var prior) && prior != action) Fail("rows for one record use both " + prior + " and " + action);
        _sheetActions[scope] = action;
        var id = scope + "\u001F" + action;
        if (!groups.TryGetValue(id, out var group))
        {
            var edit = new TomlTable { ["target"] = targetName, ["action"] = action };
            if (ui) edit["control"] = key;
            else { var parts = new TomlArray(); foreach (var part in key.Split('|')) parts.Add(part); edit["key"] = parts; }
            if (builds.Length > 0) { var list = new TomlArray(); foreach (var build in builds) list.Add(build); edit["build"] = list; }
            if (cell("sheet") is { Length: > 0 } sheetName) edit["sheet"] = sheetName;
            if (cell("member") is { Length: > 0 } member) edit["member"] = member;
            groups.Add(id, group = (edit, new TomlTable(), []));
        }
        group.Lines.Add(line);
        if (action == "remove") return;
        // UI rows may carry a whole native element for add and replace in the field named element.
        var into = ui && field == "element" ? group.Edit : group.Set;
        if (into.ContainsKey(field)) Fail("field " + field + " is written twice for " + key);
        into[field] = value.Length > 0 ? value : texts;
    }

    /// <summary>
    /// A sheet row with match or find becomes one pattern, applied in sheet order. With find, the value or language cells
    /// are the replacement; without it they are the value set on every matched record. key with find edits one record.
    /// </summary>
    private void AddSheetPattern(string path, int line, string targetName, Func<string, string> cell, Func<string, string> raw,
        string[] fields, List<(string Code, int Column)> languages)
    {
        var where = path + ":" + line;
        if (targetName.Length == 0) throw new InvalidDataException(where + ": no target");
        if (!targets.TryGetValue(targetName, out var target)) throw new InvalidDataException(where + ": target " + targetName + " is not defined for build " + context.Build.Id);
        var ui = target.Kind == "ui";
        var edit = new TomlTable { ["target"] = targetName, ["action"] = cell("action") is { Length: > 0 } action ? action : "patch" };
        if (raw("match").Length > 0) edit["match"] = raw("match");
        if (cell("key") is { Length: > 0 } key)
        {
            if (ui) edit["control"] = key;
            else { var parts = new TomlArray(); foreach (var part in key.Split('|')) parts.Add(part); edit["key"] = parts; }
        }
        if (cell("member") is { Length: > 0 } member) edit["member"] = member;
        if (raw("member_match").Length > 0) edit["member_match"] = raw("member_match");
        if (cell("sheet") is { Length: > 0 } sheet) edit["sheet"] = sheet;
        var builds = cell("build").Split([',', ';', '|', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (builds.Length > 0) { var list = new TomlArray(); foreach (var build in builds) list.Add(build); edit["build"] = list; }
        var texts = new TomlTable();
        foreach (var (code, column) in languages) if (fields[column].Length > 0) texts[code] = fields[column];
        var value = cell("value");
        if (value.Length > 0 && texts.Count > 0) throw new InvalidDataException(where + ": value and language columns are both filled");
        object text = value.Length > 0 ? value : texts;
        var field = cell("field");
        if (edit["action"] as string != "remove")
        {
            if (field.Length == 0) throw new InvalidDataException(where + ": no field");
            // A pattern with no text in any language is a template row awaiting translation.
            if (value.Length == 0 && texts.Count == 0) return;
            if (raw("find").Length > 0)
            {
                edit["find"] = raw("find");
                edit["replace"] = text;
                var columns = new TomlArray(); foreach (var name in field.Split('|')) columns.Add(name); edit["fields"] = columns;
            }
            else edit["set"] = new TomlTable { [field] = text };
        }
        else if (field.Length > 0 || value.Length > 0 || texts.Count > 0 || raw("find").Length > 0)
            throw new InvalidDataException(where + ": a remove row takes no field, find or text");
        try { Pattern(new ResourceData(edit, source), where); }
        catch (InvalidDataException error) when (!error.Message.StartsWith(where, StringComparison.Ordinal))
        { throw new InvalidDataException(where + ": " + error.Message, error); }
    }

    private readonly Dictionary<string, string> _sheetActions = new(StringComparer.Ordinal);

    /// <summary>
    /// A regex pattern row: key, control or match selects records or controls, member or member_match selects UI forms,
    /// and either find with replace substitutes text in the listed fields or set and clear write them outright.
    /// </summary>
    internal void Pattern(ResourceData row, string where)
    {
        if (!Gate(row)) return;
        var target = Target(row.Text("target"));
        if (row.Has("sheet"))
        {
            if (target.Kind != "spreadsheet") throw new InvalidDataException(where + ": only spreadsheet targets take a sheet");
            target = target with { Sheet = row.Text("sheet") };
        }
        var action = Action(row.Text("action", "patch"));
        if (action is not (EditAction.Patch or EditAction.Remove)) throw new InvalidDataException(where + ": patterns patch or remove, not " + action.ToString().ToLowerInvariant());
        var ui = target.Kind == "ui";
        var exactName = ui ? "control" : "key";
        if (row.Has(ui ? "key" : "control")) throw new InvalidDataException(where + ": " + target.Kind + " patterns select with " + exactName + " or match");
        if (row.Has(exactName) == row.Has("match")) throw new InvalidDataException(where + ": a pattern needs exactly one of " + exactName + " and match");
        string[]? exact = !row.Has(exactName) ? null : ui ? [row.Text("control")] : Key(row.Raw("key"));
        if (exact is not null && !ui && exact.Length != target.Keys.Count)
            throw new InvalidDataException(where + ": target " + target.Name + " needs " + target.Keys.Count + " key values");
        var match = row.Has("match") ? PatternEdit.Compile(row.Text("match"), true, where) : null;
        string? member = null;
        Regex? memberMatch = null;
        if (ui)
        {
            if (row.Has("member") == row.Has("member_match")) throw new InvalidDataException(where + ": a UI pattern needs exactly one of member and member_match");
            if (row.Has("member")) member = ArchiveResourceComposer.ValidatePath(row.Text("member"));
            else memberMatch = PatternEdit.Compile(row.Text("member_match"), true, where);
        }
        else if (row.Has("member") || row.Has("member_match")) throw new InvalidDataException(where + ": table patterns take member and layout from their target");
        var fields = new List<PatternField>();
        if (row.Has("find"))
        {
            if (action == EditAction.Remove || row.Has("set") || row.Has("clear") || !row.Has("replace"))
                throw new InvalidDataException(where + ": find needs replace and fields, and excludes set, clear and remove");
            var find = PatternEdit.Compile(row.Text("find"), false, where);
            if (Value(row.Raw("replace"), false) is not { Text: { } replacement }) return;
            var columns = row.List("fields");
            if (columns.Length == 0) throw new InvalidDataException(where + ": find needs fields");
            foreach (var field in columns) fields.Add(new(ui ? field : target.Column(field), find, replacement, null));
        }
        else if (action == EditAction.Patch)
        {
            if (row.Has("replace") || row.Has("fields")) throw new InvalidDataException(where + ": replace and fields belong to find");
            if (row.Table("set") is { } values)
                foreach (var field in values.Names)
                    if (Value(values.Raw(field), false) is { } value) fields.Add(new(ui ? field : target.Column(field), null, null, value));
            foreach (var field in row.List("clear"))
            {
                if (ui) throw new InvalidDataException(where + ": UI patterns cannot clear attributes");
                fields.Add(new(target.Column(field), null, null, new CellValue(null)));
            }
            if (fields.Count == 0) return;
        }
        else if (row.Has("set") || row.Has("clear") || row.Has("replace") || row.Has("fields"))
            throw new InvalidDataException(where + ": a remove pattern takes no fields");
        if (fields.Select(f => f.Column).Distinct(StringComparer.Ordinal).Count() != fields.Count) throw new InvalidDataException(where + ": field written twice");
        Patterns.Add(new(owner, target, ui ? member : target.Member, memberMatch, action, exact, match, fields, Patterns.Count));
    }
}
