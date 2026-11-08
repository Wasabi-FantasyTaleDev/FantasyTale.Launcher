namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>
/// Record-level edit actions shared by tables and UI controls. Upsert patches an existing record or adds it when absent, so a
/// translation can target several builds whose tables differ in which keys exist; it merges field by field like Patch.
/// </summary>
internal enum EditAction { Add, Replace, Patch, Remove, Hide, Upsert }

/// <summary>A value written to one cell; null text empties the cell while keeping its style.</summary>
/// <param name="Text">Cell text, or null to remove the cell data.</param>
/// <param name="Number">Whether a spreadsheet cell is typed Number rather than String.</param>
internal sealed record CellValue(string? Text, bool Number = false)
{
    /// <summary>Stable text for conflict comparison and cache identity.</summary>
    public override string ToString() => Text is null ? "<empty>" : (Number ? "n:" : "s:") + Text;
}

/// <summary>Where records live inside a table member and which columns form the record key.</summary>
/// <param name="Sheet">Worksheet name for SpreadsheetML; ignored by delimited text.</param>
/// <param name="HeaderRow">One-based physical header row.</param>
/// <param name="Keys">Key column references: header text, or #N for a one-based column position. Horizontal tables have one
/// key, the record identity in their [Field] row, and ignore the reference.</param>
/// <param name="Orientation">auto (detect from the sheet), vertical or horizontal; a declared orientation is checked.</param>
internal sealed record TableShape(string? Sheet, int HeaderRow, IReadOnlyList<string> Keys, string Orientation = "auto");

/// <summary>
/// One resolved edit addressed by business key, never by physical row number. A key that several records share in the
/// original table addresses all of them: Set and Expect apply to each, and removal removes each.
/// </summary>
/// <param name="Action">Edit action.</param>
/// <param name="Key">Key values in the order of <see cref="TableShape.Keys"/>.</param>
/// <param name="Set">Column reference to value; replace and add write exactly these plus the key.</param>
/// <param name="Expect">Optional preconditions: column reference to expected original text.</param>
internal sealed record RecordEdit(EditAction Action, IReadOnlyList<string> Key,
    IReadOnlyDictionary<string, CellValue> Set, IReadOnlyDictionary<string, string> Expect)
{
    /// <summary>
    /// Per-record values for a patch of a shared key, one entry per record in document order, used instead of Set when the
    /// records get different results (a pattern substituting in texts that differ). Null for every other edit.
    /// </summary>
    internal IReadOnlyList<IReadOnlyDictionary<string, CellValue>>? Occurrences { get; init; }

    /// <summary>The values written to record <paramref name="occurrence"/> of <paramref name="count"/>.</summary>
    internal IReadOnlyDictionary<string, CellValue> For(int occurrence, int count, string member) => Occurrences is null ? Set
        : Occurrences.Count == count ? Occurrences[occurrence]
        : throw new InvalidDataException(member + ": " + Columns.Key(Key) + " has " + count + " records, the edit was planned for " + Occurrences.Count);
}

/// <summary>An editable decoded member that preserves everything an edit does not address.</summary>
internal interface IRecordDocument
{
    /// <summary>Resolves a target and its field references against the original member before conflict detection.</summary>
    RecordBinding Bind(TableShape shape, IEnumerable<string> fields);

    /// <summary>Applies one edit, failing loudly when the record cannot be located uniquely.</summary>
    void Apply(TableShape shape, RecordEdit edit);

    /// <summary>Reads the original text of a field for conflict reports, or null when absent.</summary>
    string? Read(TableShape shape, IReadOnlyList<string> key, string column);

    /// <summary>A field of every record with this key in document order; empty when there is none.</summary>
    IReadOnlyList<string?> ReadAll(TableShape shape, IReadOnlyList<string> key, string column);

    /// <summary>Every record key in document order, each once, for pattern edits that select records by regex.</summary>
    IReadOnlyList<IReadOnlyList<string>> Keys(TableShape shape);

    /// <summary>Encodes the edited member with its original encoding, BOM and declaration.</summary>
    byte[] Save();
}

/// <summary>Physical worksheet, key columns and field addresses shared by every alias of the same target.</summary>
internal sealed record RecordBinding(string Scope, TableShape Shape, IReadOnlyDictionary<string, string> Fields)
{
    /// <summary>Rewrites field references, rejecting contradictory aliases within one edit.</summary>
    internal IReadOnlyDictionary<string, T> Map<T>(IReadOnlyDictionary<string, T> values, string member)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var (field, value) in values)
        {
            var physical = Fields[field];
            if (result.TryGetValue(physical, out var previous) && !EqualityComparer<T>.Default.Equals(previous, value))
                throw new InvalidDataException(member + ": contradictory aliases for field " + physical);
            result[physical] = value;
        }
        return result;
    }
}

/// <summary>Shared helpers for column references.</summary>
internal static class Columns
{
    /// <summary>Resolves a header text or #N reference to a one-based column, failing on unknown or duplicated headers.</summary>
    internal static int Resolve(IReadOnlyDictionary<string, List<int>> headers, string reference, string member)
    {
        if (reference.Length > 1 && reference[0] == '#' && int.TryParse(reference.AsSpan(1), out var position) && position > 0) return position;
        if (!headers.TryGetValue(reference, out var found)) throw new InvalidDataException(member + ": unknown column " + reference);
        if (found.Count != 1) throw new InvalidDataException(member + ": ambiguous column " + reference);
        return found[0];
    }

    /// <summary>Joins key values into the canonical record identity used by conflicts and caches.</summary>
    internal static string Key(IReadOnlyList<string> values) => string.Join("\u001F", values);
}
