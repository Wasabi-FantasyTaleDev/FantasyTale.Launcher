using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>One field a pattern edit changes: a regex substitution of the current text, or a value set outright.</summary>
/// <param name="Column">Physical column, field or UI attribute.</param>
/// <param name="Find">Unanchored regex replaced everywhere in the current text; null when the value is set.</param>
/// <param name="Replacement">Replacement with $1 or ${name} group references, resolved for the client language.</param>
/// <param name="Set">Value written when there is no Find; Text null clears the cell.</param>
internal sealed record PatternField(string Column, Regex? Find, string? Replacement, CellValue? Set);

/// <summary>
/// A regex-driven edit: it selects records by key or controls by path, either exactly or with an anchored regex matched
/// against the whole key ("|" between composite parts) or path, and is expanded against the user's original member into
/// ordinary patches or removals before merging, so conflicts and choices treat its results like any other edit.
/// </summary>
/// <param name="Owner">Contributing plugin.</param>
/// <param name="Target">Resolved target, with any worksheet override applied.</param>
/// <param name="Member">Exact member path, or null when <paramref name="MemberMatch"/> selects UI forms.</param>
/// <param name="MemberMatch">Anchored regex selecting .ui members of the target's archive.</param>
/// <param name="Action">Patch or Remove.</param>
/// <param name="Exact">Exact key parts or control path, or null when <paramref name="Match"/> selects.</param>
/// <param name="Match">Anchored regex over the joined key or control path.</param>
/// <param name="Fields">Field changes in declaration order; empty for removals.</param>
/// <param name="Order">Declaration order within the owner's manifest; later patterns see earlier patterns' results.</param>
internal sealed record PatternEdit(EditOwner Owner, ResourceTarget Target, string? Member, Regex? MemberMatch, EditAction Action,
    IReadOnlyList<string>? Exact, Regex? Match, IReadOnlyList<PatternField> Fields, int Order)
{
    /// <summary>Whether this pattern edits .ui controls rather than table records.</summary>
    internal bool Ui => Target.Kind == "ui";

    /// <summary>Owner-independent canonical content for snapshots and the expansion cache.</summary>
    internal string Content => JsonSerializer.Serialize(new
    {
        target = Target.Identity, member = Member, memberMatch = MemberMatch?.ToString(), action = Action.ToString(),
        exact = Exact, match = Match?.ToString(), Order,
        fields = Fields.Select(f => new { f.Column, find = f.Find?.ToString(), f.Replacement, set = f.Set?.ToString() })
    });

    /// <summary>Compiles a manifest regex with the linear-time engine, so no pattern can stall preparation.</summary>
    internal static Regex Compile(string pattern, bool anchored, string source)
    {
        try { return new Regex(anchored ? @"\A(?:" + pattern + @")\z" : pattern, RegexOptions.NonBacktracking | RegexOptions.CultureInvariant); }
        catch (NotSupportedException error) { throw new InvalidDataException(source + ": regex " + pattern + " uses a construct the linear-time engine does not support (lookaround, backreference or atomic group)", error); }
        catch (ArgumentException error) { throw new InvalidDataException(source + ": invalid regex " + pattern + ": " + error.Message, error); }
    }
}

/// <summary>One record or control a plugin's patterns change, with its final values, kept in the expansion cache.</summary>
internal sealed class PatternRow
{
    /// <summary>Member path as the archive stores it.</summary>
    public string Member { get; set; } = "";
    /// <summary>Worksheet name, empty for delimited tables, or "ui".</summary>
    public string Scope { get; set; } = "";
    /// <summary>Canonical record key or control path.</summary>
    public string Key { get; set; } = "";
    /// <summary>Record key parts, or the control path alone.</summary>
    public string[] Parts { get; set; } = [];
    /// <summary>Whether the record or control is removed.</summary>
    public bool Remove { get; set; }
    /// <summary>
    /// Final values per record that shares this key, in document order (one entry for a unique key or a control); only
    /// values that differ from the original survive.
    /// </summary>
    public List<Dictionary<string, CellValue>> Values { get; set; } = [];
    /// <summary>Index of the first pattern that touched it, whose target the planned edit uses.</summary>
    public int Pattern { get; set; }
}

/// <summary>
/// Expands pattern edits against the user's original members into ordinary planned edits. Each plugin's patterns apply in
/// declaration order to the untouched original, later patterns seeing earlier results; a plugin's literal edit of the same
/// field or record wins over its own patterns. Results are cached per archive and plugin by original digest and content.
/// </summary>
internal static class PatternExpander
{
    // Bump when the row format or expansion rules change, so cached expansions are recomputed.
    private const int Version = 2;
    private static readonly UTF8Encoding Strict = new(false, true);

    internal static List<PlannedEdit> Expand(IReadOnlyList<PatternEdit> patterns, IReadOnlyList<PlannedEdit> literal, string instance,
        IReadOnlyDictionary<string, string> inputs, string? cache, CancellationToken cancellationToken)
    {
        var result = new List<PlannedEdit>();
        foreach (var archive in patterns.GroupBy(p => p.Target.Archive, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var owners = archive.GroupBy(p => p.Owner.PluginId, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.OrderBy(p => p.Order).ToArray()).ToArray();
            var rows = new Dictionary<string, List<PatternRow>>(StringComparer.Ordinal);
            var missing = new List<PatternEdit[]>();
            foreach (var ordered in owners)
            {
                var identity = Identity(archive.Key, inputs[archive.Key], ordered);
                if (Load(cache, archive.Key, ordered[0].Owner, identity) is { } cached) rows[ordered[0].Owner.PluginId] = cached;
                else missing.Add(ordered);
            }
            if (missing.Count > 0)
            {
                using var reader = Archive.Open(ArchiveResourceComposer.SafeFile(instance, archive.Key), archive.Key);
                foreach (var (owner, computed) in Compute(reader, missing, cancellationToken))
                {
                    rows[owner] = computed;
                    var ordered = missing.First(m => m[0].Owner.PluginId == owner);
                    Save(cache, archive.Key, ordered[0].Owner, Identity(archive.Key, inputs[archive.Key], ordered), computed);
                }
            }
            foreach (var ordered in owners) result.AddRange(Planned(ordered, rows[ordered[0].Owner.PluginId], literal));
        }
        return result;
    }

    private static List<(string Owner, List<PatternRow> Rows)> Compute(IArchiveReader reader, List<PatternEdit[]> owners, CancellationToken cancellationToken)
    {
        var state = owners.ToDictionary(o => o[0].Owner.PluginId, _ => new Dictionary<string, PatternRow>(StringComparer.Ordinal), StringComparer.Ordinal);
        var originals = owners.ToDictionary(o => o[0].Owner.PluginId, _ => new Dictionary<string, IReadOnlyList<string?>>(StringComparer.Ordinal), StringComparer.Ordinal);
        var members = new SortedDictionary<string, List<PatternEdit>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in owners.SelectMany(o => o))
        {
            IEnumerable<string> paths = pattern.Member is { } exact
                ? [reader.Entries.SingleOrDefault(e => e.Path.Equals(exact, StringComparison.OrdinalIgnoreCase))?.Path
                    ?? throw new InvalidDataException(pattern.Owner.PluginId + ": " + pattern.Target.Archive + " has no member " + exact)]
                : reader.Entries.Where(e => e.Path.EndsWith(".ui", StringComparison.OrdinalIgnoreCase) && pattern.MemberMatch!.IsMatch(e.Path)).Select(e => e.Path);
            foreach (var path in paths) { if (!members.TryGetValue(path, out var list)) members.Add(path, list = []); list.Add(pattern); }
        }
        foreach (var (member, hits) in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hits.Select(p => p.Target.Kind).Distinct().Count() > 1) throw new InvalidDataException(member + ": patterns of different kinds address the same member");
            var bytes = reader.Read(member);
            foreach (var owner in hits.GroupBy(p => p.Owner.PluginId, StringComparer.Ordinal))
            {
                var ordered = owner.OrderBy(p => p.Order).ToArray();
                var kind = ordered[0].Target.Kind;
                IRecordDocument? table = kind == "ui" ? null : kind == "spreadsheet" ? SpreadsheetDocument.Load(bytes, member) : DelimitedDocument.Load(bytes, member);
                var form = kind == "ui" ? UiDocument.Load(bytes, member) : null;
                foreach (var pattern in ordered)
                    Apply(pattern, member, table, form, state[owner.Key], originals[owner.Key], cancellationToken);
            }
        }
        // Keep only values that differ from the original; a record whose values all fell away is no edit at all.
        return state.Select(o => (o.Key, o.Value.Values.Where(row =>
        {
            for (var i = 0; i < row.Values.Count; i++)
                foreach (var column in row.Values[i].Keys.ToArray())
                    if (originals[o.Key][Id(row) + "\u001E" + column][i] == row.Values[i][column].Text) row.Values[i].Remove(column);
            return row.Remove || row.Values.Any(v => v.Count > 0);
        }).ToList())).ToList();
    }

    private static string Id(PatternRow row) => row.Member.ToLowerInvariant() + "\u001E" + row.Scope + "\u001E" + row.Key;

    /// <summary>Turns a plugin's rows into planned edits; its literal edit of the same field or record takes precedence.</summary>
    private static IEnumerable<PlannedEdit> Planned(PatternEdit[] ordered, List<PatternRow> rows, IReadOnlyList<PlannedEdit> literal)
    {
        var owner = ordered[0].Owner;
        var mine = literal.Where(e => e.Owner.PluginId == owner.PluginId)
            .ToLookup(e => e.Member.ToLowerInvariant() + "\u001E" + e.Scope + "\u001E" + e.Key, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var target = ordered.First(p => p.Order == row.Pattern).Target;
            var values = row.Values.Select(v => new Dictionary<string, CellValue>(v, StringComparer.Ordinal)).ToList();
            var overriding = mine[Id(row)].ToArray();
            if (overriding.Any(e => e.Action is not (EditAction.Patch or EditAction.Upsert))) continue;
            if (overriding.Length > 0)
            {
                if (row.Remove) continue;
                foreach (var column in overriding.SelectMany(e => e.Fields.Keys)) values.ForEach(v => v.Remove(column));
                if (values.All(v => v.Count == 0)) continue;
            }
            var action = row.Remove ? EditAction.Remove : EditAction.Patch;
            // One record keeps the plain Set; records sharing a key carry their own values.
            var single = values.Count == 1 ? values[0] : new Dictionary<string, CellValue>(StringComparer.Ordinal);
            if (row.Scope == "ui")
                yield return new(owner, target, target.Archive, row.Member, "ui", row.Key, action, null,
                    new UiEdit(action, row.Key, single.ToDictionary(p => p.Key, p => p.Value.Text!, StringComparer.Ordinal), new Dictionary<string, string>(), null, null));
            else
                yield return new(owner, target, target.Archive, row.Member, row.Scope, row.Key, action,
                    new RecordEdit(action, row.Parts, single, new Dictionary<string, string>())
                    { Occurrences = values.Count > 1 ? values.Select(v => (IReadOnlyDictionary<string, CellValue>)v).ToArray() : null }, null);
        }
    }

    private static string Identity(string archive, string input, PatternEdit[] ordered) => Digest(JsonSerializer.Serialize(new
    {
        version = Version, archive = archive.ToLowerInvariant(), input, owner = ordered[0].Owner.PluginId, ordered[0].Owner.Version,
        patterns = ordered.Select(p => p.Content)
    }));

    private sealed record CacheFile(string Identity, List<PatternRow> Rows);

    private static string? Slot(string? cache, string archive, EditOwner owner) => cache is null ? null
        : ArchiveResourceComposer.SafeFile(cache, Digest(string.Join("|", "patterns", archive.ToLowerInvariant(), owner.PluginId)) + ".patterns.json");

    private static List<PatternRow>? Load(string? cache, string archive, EditOwner owner, string identity)
    {
        if (Slot(cache, archive, owner) is not { } file || !File.Exists(file)) return null;
        try { return JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(file)) is { } stored && stored.Identity == identity ? stored.Rows : null; }
        catch (JsonException) { return null; /* Interrupted cache metadata is disposable; the original archive stays authoritative. */ }
    }

    private static void Save(string? cache, string archive, EditOwner owner, string identity, List<PatternRow> rows)
    {
        if (Slot(cache, archive, owner) is not { } file) return;
        Directory.CreateDirectory(cache!);
        var pending = file + ".pending";
        File.WriteAllText(pending, JsonSerializer.Serialize(new CacheFile(identity, rows)));
        File.Move(pending, file, overwrite: true);
    }

    private static string Digest(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Applies one pattern to one parsed member, recording each touched field's original text once.</summary>
    private static void Apply(PatternEdit pattern, string member, IRecordDocument? table, UiDocument? form, Dictionary<string, PatternRow> state,
        Dictionary<string, IReadOnlyList<string?>> originals, CancellationToken cancellationToken)
    {
        var shape = pattern.Target.Shape;
        var scope = form is null ? pattern.Target.Sheet ?? "" : "ui";
        IEnumerable<IReadOnlyList<string>> selected;
        if (form is not null)
        {
            var paths = form.Paths();
            if (pattern.Exact is { } control && !paths.Contains(control[0], StringComparer.Ordinal))
                throw new InvalidDataException(pattern.Owner.PluginId + ": " + member + " has no control " + control[0]);
            selected = pattern.Exact is not null ? [pattern.Exact] : paths.Where(p => pattern.Match!.IsMatch(p)).Select(p => (IReadOnlyList<string>)[p]);
        }
        else
        {
            var keys = table!.Keys(shape);
            if (pattern.Exact is { } key && !keys.Any(k => k.SequenceEqual(key)))
                throw new InvalidDataException(pattern.Owner.PluginId + ": " + member + " has no record " + string.Join("|", key));
            selected = pattern.Exact is not null ? [pattern.Exact] : keys.Where(k => pattern.Match!.IsMatch(string.Join("|", k)));
        }
        foreach (var parts in selected.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var canonical = form is null ? Columns.Key(parts) : parts[0];
            var id = member.ToLowerInvariant() + "\u001E" + scope + "\u001E" + canonical;
            if (!state.TryGetValue(id, out var row)) state.Add(id, row = new() { Member = member, Scope = scope, Key = canonical, Parts = [.. parts], Pattern = pattern.Order });
            if (row.Remove) throw new InvalidDataException(pattern.Owner.PluginId + ": a pattern edits " + canonical + " after an earlier pattern removed it");
            if (pattern.Action == EditAction.Remove) { row.Remove = true; row.Values.Clear(); continue; }
            foreach (var field in pattern.Fields)
            {
                var key = id + "\u001E" + field.Column;
                // Every record that shares the key is read, so each keeps or changes its own text.
                if (!originals.TryGetValue(key, out var original))
                    originals[key] = original = form is not null ? [form.Read(canonical, field.Column)] : table!.ReadAll(shape, parts, field.Column);
                while (row.Values.Count < original.Count) row.Values.Add(new(StringComparer.Ordinal));
                for (var i = 0; i < original.Count; i++)
                {
                    var current = row.Values[i].TryGetValue(field.Column, out var staged) ? staged.Text : original[i];
                    if (field.Find is null) { row.Values[i][field.Column] = field.Set!; continue; }
                    if (current is null) continue;
                    var replaced = field.Find.Replace(current, field.Replacement!);
                    if (replaced == current) continue;
                    try { _ = Strict.GetByteCount(replaced); }
                    catch (EncoderFallbackException error)
                    { throw new InvalidDataException(pattern.Owner.PluginId + ": replacing " + field.Find + " in " + canonical + " splits a character outside the Basic Multilingual Plane", error); }
                    row.Values[i][field.Column] = new CellValue(replaced);
                }
            }
        }
    }
}
