using System.Xml.Linq;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>A vertical record table: one header row of field names, then one record per row located by its key columns.</summary>
internal sealed class VerticalSheet : SpreadsheetGrid
{
    private readonly int _headerRow;
    private readonly Dictionary<string, Dictionary<string, List<XElement>>> _indexes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<int>> _headers = new(StringComparer.Ordinal);

    internal VerticalSheet(XElement table, string member, int headerRow) : base(table, member)
    {
        _headerRow = headerRow;
        var header = Rows().FirstOrDefault(r => r.Index == headerRow);
        if (header.Element is null) throw new InvalidDataException(member + ": header row not found");
        foreach (var cell in Cells(header.Element))
        {
            var text = CellText(cell.Element);
            if (text is null) continue;
            if (!_headers.TryGetValue(text, out var list)) _headers.Add(text, list = []);
            list.Add(cell.Index);
        }
    }

    /// <summary>The physical row used to interpret field names.</summary>
    internal int HeaderRow => _headerRow;

    /// <inheritdoc />
    internal override RecordBinding Bind(TableShape shape, IEnumerable<string> fields)
    {
        string Physical(string field) => "#" + Columns.Resolve(_headers, field, Member).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var keys = shape.Keys.Select(Physical).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) throw new InvalidDataException(Member + ": duplicate key columns");
        return new(shape.Sheet!, shape with { Keys = keys, Orientation = "vertical" },
            fields.Distinct(StringComparer.Ordinal).ToDictionary(f => f, Physical, StringComparer.Ordinal));
    }

    /// <inheritdoc />
    internal override string? Read(TableShape shape, IReadOnlyList<string> key, string field)
    {
        var row = Find(shape, key);
        return row is null ? null : Text(row, Columns.Resolve(_headers, field, Member));
    }

    /// <inheritdoc />
    protected override string? SiblingType(XElement? row, int column)
    {
        foreach (var (other, index) in Rows())
        {
            if (index <= _headerRow || other == row) continue;
            if (Cells(other).FirstOrDefault(c => c.Index == column).Element?.Element(Ss + "Data") is { } data) return (string?)data.Attribute(Ss + "Type");
        }
        return null;
    }

    /// <inheritdoc />
    internal override IReadOnlyList<IReadOnlyList<string>> Keys(TableShape shape) =>
        Index(shape).Keys.Select(k => (IReadOnlyList<string>)k.Split('\u001F')).ToArray();

    private XElement? Find(TableShape shape, IReadOnlyList<string> key)
    {
        if (key.Count != shape.Keys.Count) throw new InvalidDataException(Member + ": key has " + key.Count + " values, expected " + shape.Keys.Count);
        if (!Index(shape).TryGetValue(Columns.Key(key), out var rows)) return null;
        if (rows.Count != 1) throw new InvalidDataException(Member + ": duplicate record key " + Columns.Key(key));
        return rows[0];
    }

    /// <inheritdoc />
    internal override void Apply(TableShape shape, RecordEdit edit)
    {
        // A key several rows share addresses every one of them.
        var rows = All(shape, edit.Key);
        if (edit.Action == EditAction.Upsert) edit = edit with { Action = rows.Length == 0 ? EditAction.Add : EditAction.Patch };
        var keyColumns = shape.Keys.Select(k => Columns.Resolve(_headers, k, Member)).ToArray();
        List<(int Column, CellValue Value)> Writes(IReadOnlyDictionary<string, CellValue> set)
        {
            var list = set.Select(p => (Column: Columns.Resolve(_headers, p.Key, Member), p.Value)).ToList();
            if (list.Select(w => w.Column).Distinct().Count() != list.Count) throw new InvalidDataException(Member + ": an edit writes the same column twice");
            foreach (var (column, value) in list)
            {
                var index = Array.IndexOf(keyColumns, column);
                if (index >= 0 && value.Text != edit.Key[index]) throw new InvalidDataException(Member + ": an edit cannot change a record key");
            }
            return list;
        }
        if (edit.Action == EditAction.Add)
        {
            if (rows.Length > 0) throw new InvalidDataException(Member + ": record already exists " + Columns.Key(edit.Key));
            var writes = Writes(edit.Set);
            for (var i = 0; i < keyColumns.Length; i++)
                if (!writes.Any(w => w.Column == keyColumns[i])) writes.Add((keyColumns[i], new CellValue(edit.Key[i], SiblingType(null, keyColumns[i]) == "Number")));
            AddRow(shape, edit.Key, writes);
            return;
        }
        if (rows.Length == 0) throw new InvalidDataException(Member + ": record not found " + Columns.Key(edit.Key));
        foreach (var row in rows)
        foreach (var (column, expected) in edit.Expect)
            if ((Text(row, Columns.Resolve(_headers, column, Member)) ?? "") != expected)
                throw new InvalidDataException(Member + ": precondition failed for " + Columns.Key(edit.Key) + " " + column);
        for (var occurrence = 0; occurrence < rows.Length; occurrence++)
        {
            var row = rows[occurrence];
            switch (edit.Action)
            {
                case EditAction.Patch:
                    foreach (var (column, value) in Writes(edit.For(occurrence, rows.Length, Member))) Write(row, column, value);
                    break;
                case EditAction.Replace:
                    var writes = Writes(edit.For(occurrence, rows.Length, Member));
                    foreach (var cell in Cells(row).ToArray())
                        if (!keyColumns.Contains(cell.Index) && !writes.Any(w => w.Column == cell.Index)) Write(row, cell.Index, new CellValue(null));
                    foreach (var (column, value) in writes) Write(row, column, value);
                    break;
                case EditAction.Remove:
                    RemoveRow(row);
                    break;
                default:
                    throw new InvalidDataException(Member + ": unsupported table action " + edit.Action);
            }
        }
    }

    /// <inheritdoc />
    internal override IReadOnlyList<string?> ReadAll(TableShape shape, IReadOnlyList<string> key, string field)
    {
        var column = Columns.Resolve(_headers, field, Member);
        return All(shape, key).Select(row => Text(row, column)).ToArray();
    }

    private XElement[] All(TableShape shape, IReadOnlyList<string> key)
    {
        if (key.Count != shape.Keys.Count) throw new InvalidDataException(Member + ": key has " + key.Count + " values, expected " + shape.Keys.Count);
        return Index(shape).TryGetValue(Columns.Key(key), out var rows) ? rows.ToArray() : [];
    }

    private Dictionary<string, List<XElement>> Index(TableShape shape)
    {
        var identity = string.Join("|", shape.Keys);
        if (_indexes.TryGetValue(identity, out var cached)) return cached;
        var columns = shape.Keys.Select(k => Columns.Resolve(_headers, k, Member)).ToArray();
        var result = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
        foreach (var (row, index) in Rows())
        {
            if (index <= _headerRow) continue;
            var cells = Cells(row).ToDictionary(c => c.Index, c => CellText(c.Element) ?? "");
            var values = columns.Select(c => cells.GetValueOrDefault(c, "")).ToArray();
            if (values.All(v => v.Length == 0)) continue;
            var key = Columns.Key(values);
            if (!result.TryGetValue(key, out var list)) result.Add(key, list = []);
            list.Add(row);
        }
        _indexes.Add(identity, result);
        return result;
    }

    private void AddRow(TableShape shape, IReadOnlyList<string> key, List<(int Column, CellValue Value)> writes)
    {
        Changed();
        var types = writes.ToDictionary(w => w.Column, w => SiblingType(null, w.Column));
        var row = new XElement(Ss + "Row");
        var last = Table.Elements(Ss + "Row").LastOrDefault();
        if (last is not null) last.AddAfterSelf(row); else Table.Add(row);
        var ordered = new List<(XElement, int)>();
        foreach (var (column, value) in writes.Where(w => w.Value.Text is not null).OrderBy(w => w.Column))
        {
            var cell = new XElement(Ss + "Cell");
            WriteData(cell, value, types[column]);
            row.Add(cell);
            ordered.Add((cell, column));
        }
        Normalize(ordered);
        Index(shape).Add(Columns.Key(key), [row]);
    }

    private void RemoveRow(XElement row)
    {
        Changed();
        var rows = Rows().ToList();
        var position = rows.FindIndex(r => r.Element == row);
        var removed = rows[position].Index;
        if (Merged)
            foreach (var (other, index) in rows)
            foreach (var slot in Cells(other))
            {
                var down = Int(slot.Element, "MergeDown") ?? 0;
                if (down > 0 && index <= removed && removed <= index + down)
                    throw new InvalidDataException(Member + ": cannot remove a row inside a merged region");
            }
        row.Remove();
        var previous = position > 0 ? rows[position - 1].Index + (Int(rows[position - 1].Element, "Span") ?? 0) : 0;
        foreach (var (other, index) in rows.Skip(position + 1))
        {
            var shifted = index - 1;
            if (shifted != previous + 1 || other.Attribute(Ss + "Index") is not null) other.SetAttributeValue(Ss + "Index", shifted);
            previous = shifted + (Int(other, "Span") ?? 0);
        }
        // Every key index forgets this row; a key goes once no row has it, so shared keys keep their other rows.
        foreach (var entry in _indexes.Values)
        foreach (var (name, list) in entry.ToArray())
            if (list.Remove(row) && list.Count == 0) entry.Remove(name);
    }
}
