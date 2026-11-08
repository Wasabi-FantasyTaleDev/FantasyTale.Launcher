using System.Globalization;
using System.Xml.Linq;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>
/// A horizontal record table as the client's config workbooks lay them out: the first cell is "Name:HorzTable", a row
/// starting with "[Field]" then "[Desc]" holds one record key per later column, and each later row is one field named in
/// column 1. Rows named "#Section" start a section; a field name repeated across sections is addressed as "#Section/name".
/// Records are located by key, never by column number, and adding or removing one inserts or deletes a whole column.
/// </summary>
internal sealed class HorizontalSheet : SpreadsheetGrid
{
    private const int FirstRecordColumn = 3;
    private readonly XElement _keyRow;
    private readonly Dictionary<string, List<int>> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<XElement>> _fields = new(StringComparer.Ordinal);

    internal HorizontalSheet(XElement table, string member) : base(table, member)
    {
        var rows = Rows().ToArray();
        var key = rows.FirstOrDefault(r => Text(r.Element, 1) == "[Field]");
        if (key.Element is null || Text(key.Element, 2) != "[Desc]")
            throw new InvalidDataException(member + ": horizontal table has no [Field] [Desc] key row");
        _keyRow = key.Element;
        foreach (var cell in Cells(_keyRow).Where(c => c.Index >= FirstRecordColumn))
            if (CellText(cell.Element) is { Length: > 0 } text) Add(_records, text, cell.Index);
        var section = "";
        foreach (var (row, index) in rows.Where(r => r.Index > key.Index))
        {
            var name = Text(row, 1);
            if (string.IsNullOrEmpty(name)) continue;
            if (name.StartsWith('#')) { section = name; continue; }
            Add(_fields, name, row);
            if (section.Length > 0) Add(_fields, section + "/" + name, row);
        }
    }

    private static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var list)) map.Add(key, list = []);
        list.Add(value);
    }

    /// <inheritdoc />
    protected override string? SiblingType(XElement? row, int column) => row is null ? null : Cells(row)
        .Where(c => c.Index >= FirstRecordColumn && c.Index != column).Select(c => (string?)c.Element.Element(Ss + "Data")?.Attribute(Ss + "Type"))
        .FirstOrDefault(t => t is not null);

    /// <inheritdoc />
    internal override IReadOnlyList<IReadOnlyList<string>> Keys(TableShape shape) =>
        _records.OrderBy(r => r.Value[0]).Select(r => (IReadOnlyList<string>)[r.Key]).ToArray();

    private int? Record(IReadOnlyList<string> key)
    {
        var columns = All(key);
        if (columns.Length > 1) throw new InvalidDataException(Member + ": duplicate record key " + key[0]);
        return columns.Length == 0 ? null : columns[0];
    }

    /// <summary>Columns of every record with this key, left to right.</summary>
    private int[] All(IReadOnlyList<string> key)
    {
        if (key.Count != 1) throw new InvalidDataException(Member + ": horizontal records take one key value, got " + key.Count);
        return _records.TryGetValue(key[0], out var columns) ? columns.Order().ToArray() : [];
    }

    /// <inheritdoc />
    internal override IReadOnlyList<string?> ReadAll(TableShape shape, IReadOnlyList<string> key, string field)
    {
        var row = Field(field);
        return All(key).Select(column => Text(row, column)).ToArray();
    }

    /// <summary>Resolves a field name, a "#Section/name" or a one-based "#N" row position to its row.</summary>
    private XElement Field(string reference)
    {
        if (reference.Length > 1 && reference[0] == '#' && int.TryParse(reference.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var position))
            return Rows().FirstOrDefault(r => r.Index == position).Element ?? throw new InvalidDataException(Member + ": no row " + reference);
        if (!_fields.TryGetValue(reference, out var rows)) throw new InvalidDataException(Member + ": unknown field " + reference);
        if (rows.Count != 1) throw new InvalidDataException(Member + ": field " + reference + " repeats across sections; write #Section/" + reference);
        return rows[0];
    }

    /// <inheritdoc />
    internal override RecordBinding Bind(TableShape shape, IEnumerable<string> fields) => new(shape.Sheet!,
        shape with { HeaderRow = 1, Keys = ["[Field]"], Orientation = "horizontal" },
        fields.Distinct(StringComparer.Ordinal).ToDictionary(f => f,
            f => "#" + Rows().First(r => r.Element == Field(f)).Index.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal));

    /// <inheritdoc />
    internal override string? Read(TableShape shape, IReadOnlyList<string> key, string field) =>
        Record(key) is { } column ? Text(Field(field), column) : null;

    /// <inheritdoc />
    internal override void Apply(TableShape shape, RecordEdit edit)
    {
        // A key several columns share addresses every one of them.
        var columns = All(edit.Key);
        if (edit.Action == EditAction.Upsert) edit = edit with { Action = columns.Length == 0 ? EditAction.Add : EditAction.Patch };
        List<(XElement Row, CellValue Value)> Writes(IReadOnlyDictionary<string, CellValue> set)
        {
            var list = set.Select(p => (Row: Field(p.Key), p.Value)).ToList();
            if (list.Select(w => w.Row).Distinct().Count() != list.Count) throw new InvalidDataException(Member + ": an edit writes the same field twice");
            if (list.Any(w => w.Row == _keyRow)) throw new InvalidDataException(Member + ": an edit cannot change a record key");
            return list;
        }
        if (edit.Action == EditAction.Add)
        {
            if (columns.Length > 0) throw new InvalidDataException(Member + ": record already exists " + edit.Key[0]);
            var writes = Writes(edit.Set);
            var added = Math.Max(FirstRecordColumn, MaxColumn() + 1);
            Write(_keyRow, added, new CellValue(edit.Key[0], SiblingType(_keyRow, added) == "Number"));
            foreach (var (row, value) in writes) Write(row, added, value);
            _records.Add(edit.Key[0], [added]);
            return;
        }
        if (columns.Length == 0) throw new InvalidDataException(Member + ": record not found " + edit.Key[0]);
        foreach (var column in columns)
        foreach (var (field, expected) in edit.Expect)
            if ((Text(Field(field), column) ?? "") != expected)
                throw new InvalidDataException(Member + ": precondition failed for " + edit.Key[0] + " " + field);
        switch (edit.Action)
        {
            case EditAction.Patch:
                for (var occurrence = 0; occurrence < columns.Length; occurrence++)
                    foreach (var (row, value) in Writes(edit.For(occurrence, columns.Length, Member))) Write(row, columns[occurrence], value);
                break;
            case EditAction.Replace:
                for (var occurrence = 0; occurrence < columns.Length; occurrence++)
                {
                    var (column, writes) = (columns[occurrence], Writes(edit.For(occurrence, columns.Length, Member)));
                    foreach (var (row, _) in Rows().Where(r => r.Element != _keyRow && Text(r.Element, column) is not null).ToArray())
                        if (!writes.Any(w => w.Row == row)) Write(row, column, new CellValue(null));
                    foreach (var (row, value) in writes) Write(row, column, value);
                }
                break;
            case EditAction.Remove:
                _records.Remove(edit.Key[0]);
                // From the rightmost column back, so the columns still to remove keep their positions.
                foreach (var column in columns.OrderDescending())
                {
                    RemoveColumn(column);
                    foreach (var list in _records.Values)
                        for (var i = 0; i < list.Count; i++) if (list[i] > column) list[i]--;
                }
                break;
            default:
                throw new InvalidDataException(Member + ": unsupported table action " + edit.Action);
        }
    }
}
