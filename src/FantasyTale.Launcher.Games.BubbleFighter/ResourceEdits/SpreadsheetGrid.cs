using System.Globalization;
using System.Xml.Linq;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>
/// One worksheet table with physical row and cell addressing shared by the vertical and horizontal record layouts: sparse
/// ss:Index cells and rows, merged regions, column definitions and the Expanded counts stay consistent with every write.
/// </summary>
internal abstract class SpreadsheetGrid
{
    protected static readonly XNamespace Ss = SpreadsheetDocument.Ss;
    private readonly int _originalMaxRow;
    private readonly int _originalMaxColumn;
    private bool _changed;
    private bool _columnsRemoved;

    protected SpreadsheetGrid(XElement table, string member)
    {
        Table = table; Member = member;
        var rows = Rows().ToArray();
        _originalMaxRow = rows.Length == 0 ? 0 : rows.Max(r => r.Index);
        _originalMaxColumn = MaxColumn();
        Merged = table.Descendants(Ss + "Cell").Any(c => c.Attribute(Ss + "MergeAcross") is not null || c.Attribute(Ss + "MergeDown") is not null);
    }

    /// <summary>The worksheet's Table element.</summary>
    protected XElement Table { get; }

    /// <summary>Member path used in error messages.</summary>
    protected string Member { get; }

    /// <summary>Whether any cell of the table is merged, enabling the merge checks.</summary>
    protected bool Merged { get; }

    /// <summary>Applies one record edit, failing loudly when the record or a field cannot be located uniquely.</summary>
    internal abstract void Apply(TableShape shape, RecordEdit edit);

    /// <summary>Resolves keys and field aliases to physical row or column positions in this worksheet.</summary>
    internal abstract RecordBinding Bind(TableShape shape, IEnumerable<string> fields);

    /// <summary>Reads the original text of one field of one record, or null when the record or cell is absent.</summary>
    internal abstract string? Read(TableShape shape, IReadOnlyList<string> key, string field);

    /// <summary>One field of every record with this key, in document order.</summary>
    internal abstract IReadOnlyList<string?> ReadAll(TableShape shape, IReadOnlyList<string> key, string field);

    /// <summary>Every record key in document order, each once.</summary>
    internal abstract IReadOnlyList<IReadOnlyList<string>> Keys(TableShape shape);

    /// <summary>Type of the record layout's neighbouring cells, used to type a newly created cell.</summary>
    protected abstract string? SiblingType(XElement? row, int column);

    /// <summary>Whether a table is a horizontal record table, whose first cell ends with ":HorzTable".</summary>
    internal static bool IsHorizontal(XElement table) =>
        table.Elements(Ss + "Row").FirstOrDefault() is { } row && (Text(row, 1) ?? "").EndsWith(":HorzTable", StringComparison.Ordinal);

    internal readonly record struct CellSlot(XElement Element, int Index, int End);

    /// <summary>Rows with their one-based indexes, honouring ss:Index and ss:Span.</summary>
    internal IEnumerable<(XElement Element, int Index)> Rows()
    {
        var index = 0;
        foreach (var row in Table.Elements(Ss + "Row"))
        {
            index = Int(row, "Index") ?? index + 1;
            yield return (row, index);
            index += Int(row, "Span") ?? 0;
        }
    }

    /// <summary>Cells of a row with their one-based start and merged end columns.</summary>
    internal static IEnumerable<CellSlot> Cells(XElement row)
    {
        var index = 0;
        foreach (var cell in row.Elements(Ss + "Cell"))
        {
            index = Int(cell, "Index") ?? index + 1;
            var end = index + (Int(cell, "MergeAcross") ?? 0);
            yield return new(cell, index, end);
            index = end;
        }
    }

    /// <summary>Text of the cell starting at a column, or null when the row has no such cell.</summary>
    internal static string? Text(XElement row, int column)
    {
        var slot = Cells(row).FirstOrDefault(c => c.Index == column);
        return slot.Element is null ? null : CellText(slot.Element);
    }

    /// <summary>Text of a cell's data, or null when it has none.</summary>
    protected static string? CellText(XElement cell) => cell.Element(Ss + "Data")?.Value;

    /// <summary>Marks the table as changed so its counts are refreshed on save.</summary>
    protected void Changed() => _changed = true;

    /// <summary>Parses an integer ss: attribute strictly.</summary>
    protected static int? Int(XElement element, string name)
    {
        var value = (string?)element.Attribute(Ss + name);
        if (value is null) return null;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ? result
            : throw new InvalidDataException("Invalid spreadsheet attribute ss:" + name + "=" + value);
    }

    /// <summary>Rewrites ss:Index wherever a cell no longer follows its predecessor or had an explicit index.</summary>
    protected static void Normalize(List<(XElement Element, int Index)> ordered)
    {
        var end = 0;
        foreach (var (element, index) in ordered)
        {
            if (index != end + 1 || element.Attribute(Ss + "Index") is not null) element.SetAttributeValue(Ss + "Index", index);
            // Cells widen with MergeAcross, column definitions with Span.
            end = index + (Int(element, "MergeAcross") ?? Int(element, "Span") ?? 0);
        }
    }

    /// <summary>Largest merged end column of any row.</summary>
    protected int MaxColumn() => Table.Elements(Ss + "Row").SelectMany(Cells).Select(c => c.End).DefaultIfEmpty(0).Max();

    /// <summary>Writes one cell, creating it in column order when absent; a null text removes the cell's data.</summary>
    internal void Write(XElement row, int column, CellValue value)
    {
        Changed();
        var slots = Cells(row).ToList();
        if (Merged) RequireUnmerged(row, column, slots);
        var existing = slots.FirstOrDefault(c => c.Index == column);
        if (existing.Element is not null) { WriteData(existing.Element, value, null); return; }
        if (value.Text is null) return;
        var cell = new XElement(Ss + "Cell");
        WriteData(cell, value, SiblingType(row, column));
        var next = slots.FirstOrDefault(c => c.Index > column);
        if (next.Element is not null) next.Element.AddBeforeSelf(cell);
        else if (slots.Count > 0) slots[^1].Element.AddAfterSelf(cell);
        else row.Add(cell);
        Normalize(slots.Select(s => (s.Element, s.Index)).Append((cell, column)).OrderBy(s => s.Item2).ToList());
    }

    /// <summary>
    /// Writes cell data. An existing typed cell keeps its type and rejects text that does not fit it; a new cell takes its
    /// record layout neighbours' Number type when the text is numeric, so text from a CSV sheet stays a number.
    /// </summary>
    protected void WriteData(XElement cell, CellValue value, string? sibling)
    {
        cell.Attribute(Ss + "Formula")?.Remove();
        var data = cell.Element(Ss + "Data");
        if (value.Text is null) { cell.Elements(Ss + "Data").Remove(); return; }
        var type = (string?)data?.Attribute(Ss + "Type");
        var numeric = double.TryParse(value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
        if (value.Number && !numeric) throw new InvalidDataException(Member + ": numeric value expected: " + value.Text);
        var target = value.Number ? "Number" : type switch
        {
            "Number" when numeric => "Number",
            "Number" => throw new InvalidDataException(Member + ": cannot write text into a numeric cell: " + value.Text),
            null => sibling == "Number" && numeric ? "Number" : "String",
            _ => type
        };
        if (data is null) { data = new XElement(Ss + "Data"); cell.AddFirst(data); }
        data.RemoveNodes();
        data.SetAttributeValue(Ss + "Type", target);
        data.Value = value.Text;
    }

    private void RequireUnmerged(XElement row, int column, List<CellSlot> slots)
    {
        if (slots.Any(s => s.Index <= column && column <= s.End && (s.End > s.Index || (Int(s.Element, "MergeDown") ?? 0) > 0)))
            throw new InvalidDataException(Member + ": cell is covered by a merged region");
        var rowIndex = Rows().First(r => r.Element == row).Index;
        foreach (var (other, index) in Rows())
        {
            if (index >= rowIndex) break;
            foreach (var slot in Cells(other))
                if ((Int(slot.Element, "MergeDown") ?? 0) + index >= rowIndex && slot.Index <= column && column <= slot.End)
                    throw new InvalidDataException(Member + ": cell is covered by a merged region");
        }
    }

    /// <summary>
    /// Removes one physical column from every row and shifts later cells and column definitions left, as deleting a
    /// column in Excel does. A merged region crossing the column is rejected rather than reshaped.
    /// </summary>
    protected void RemoveColumn(int column)
    {
        Changed();
        _columnsRemoved = true;
        foreach (var row in Table.Elements(Ss + "Row"))
        {
            var slots = Cells(row).ToList();
            if (slots.Any(c => c.Index <= column && column <= c.End && c.End > c.Index))
                throw new InvalidDataException(Member + ": cannot remove a column inside a merged region");
            var kept = new List<(XElement Element, int Index)>();
            foreach (var slot in slots)
            {
                if (slot.Index == column) { slot.Element.Remove(); continue; }
                kept.Add((slot.Element, slot.Index > column ? slot.Index - 1 : slot.Index));
            }
            Normalize(kept);
        }
        var definitions = new List<(XElement Element, int Index)>();
        var position = 0;
        foreach (var definition in Table.Elements(Ss + "Column").ToArray())
        {
            position = Int(definition, "Index") ?? position + 1;
            var span = Int(definition, "Span") ?? 0;
            var (start, end) = (position, position + span);
            position = end;
            if (start <= column && column <= end)
            {
                if (span == 0) { definition.Remove(); continue; }
                if (span == 1) definition.Attribute(Ss + "Span")!.Remove(); else definition.SetAttributeValue(Ss + "Span", span - 1);
            }
            definitions.Add((definition, start > column ? start - 1 : start));
        }
        Normalize(definitions);
    }

    /// <summary>Refreshes ExpandedRowCount and ExpandedColumnCount after changes, keeping any slack the original declared.</summary>
    internal void UpdateCounts()
    {
        if (!_changed) return;
        var rows = Rows().ToArray();
        var maxRow = rows.Length == 0 ? 0 : rows.Max(r => r.Index);
        if (Int(Table, "ExpandedRowCount") is { } rowCount) Table.SetAttributeValue(Ss + "ExpandedRowCount", Math.Max(maxRow, rowCount + maxRow - _originalMaxRow));
        var maxColumn = MaxColumn();
        if (Int(Table, "ExpandedColumnCount") is { } columnCount)
            Table.SetAttributeValue(Ss + "ExpandedColumnCount", _columnsRemoved
                ? Math.Max(maxColumn, columnCount + maxColumn - _originalMaxColumn) : Math.Max(columnCount, maxColumn));
        _changed = false;
    }
}
