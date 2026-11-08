using System.Xml.Linq;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>
/// Record editor for SpreadsheetML 2003 members (Excel XML renamed .xml). Each worksheet is a vertical table with one record
/// per row, or a horizontal table (first cell "Name:HorzTable") with one record per column; the layout is detected from
/// the sheet and checked against the target's declared orientation. Untouched nodes are preserved.
/// </summary>
internal sealed class SpreadsheetDocument : IRecordDocument
{
    internal static readonly XNamespace Ss = "urn:schemas-microsoft-com:office:spreadsheet";
    private readonly XmlMember _xml;
    private readonly string _member;
    private readonly Dictionary<string, SpreadsheetGrid> _sheets = new(StringComparer.Ordinal);

    private SpreadsheetDocument(XmlMember xml) { _xml = xml; _member = xml.Member; }

    /// <summary>Parses a member strictly; anything that is not a SpreadsheetML workbook is rejected.</summary>
    internal static SpreadsheetDocument Load(byte[] bytes, string member)
    {
        var xml = XmlMember.Load(bytes, member);
        if (xml.Document.Root?.Name != Ss + "Workbook") throw new InvalidDataException(member + ": not a SpreadsheetML workbook");
        return new(xml);
    }

    /// <inheritdoc />
    public byte[] Save()
    {
        foreach (var sheet in _sheets.Values) sheet.UpdateCounts();
        return _xml.Save();
    }

    /// <inheritdoc />
    public string? Read(TableShape shape, IReadOnlyList<string> key, string column) => Open(shape).Read(shape, key, column);

    /// <inheritdoc />
    public IReadOnlyList<string?> ReadAll(TableShape shape, IReadOnlyList<string> key, string column) => Open(shape).ReadAll(shape, key, column);

    /// <inheritdoc />
    public void Apply(TableShape shape, RecordEdit edit) => Open(shape).Apply(shape, edit);

    /// <inheritdoc />
    public RecordBinding Bind(TableShape shape, IEnumerable<string> fields)
    {
        var sheet = Open(shape);
        var name = _sheets.First(p => ReferenceEquals(p.Value, sheet)).Key;
        return sheet.Bind(shape with { Sheet = name }, fields);
    }

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<string>> Keys(TableShape shape) => Open(shape).Keys(shape);

    private SpreadsheetGrid Open(TableShape shape)
    {
        var worksheets = _xml.Document.Root!.Elements(Ss + "Worksheet").ToArray();
        var matches = shape.Sheet is null ? worksheets : worksheets.Where(w => (string?)w.Attribute(Ss + "Name") == shape.Sheet).ToArray();
        if (matches.Length != 1) throw new InvalidDataException(_member + ": worksheet not found uniquely: " + shape.Sheet);
        var name = (string?)matches[0].Attribute(Ss + "Name") ?? throw new InvalidDataException(_member + ": worksheet has no name");
        if (!_sheets.TryGetValue(name, out var sheet))
        {
            var table = matches[0].Element(Ss + "Table") ?? throw new InvalidDataException(_member + ": worksheet has no table: " + name);
            sheet = SpreadsheetGrid.IsHorizontal(table) ? new HorizontalSheet(table, _member) : new VerticalSheet(table, _member, shape.HeaderRow);
            _sheets.Add(name, sheet);
        }
        if (sheet is VerticalSheet vertical && vertical.HeaderRow != shape.HeaderRow)
            throw new InvalidDataException(_member + ": conflicting header rows for worksheet " + name);
        // Every edit's declared layout is checked, not only the first one that opened the worksheet.
        var horizontal = sheet is HorizontalSheet;
        if (shape.Orientation == "vertical" && horizontal || shape.Orientation == "horizontal" && !horizontal)
            throw new InvalidDataException(_member + ": worksheet " + name + " is " + (horizontal ? "horizontal" : "vertical") + ", not " + shape.Orientation);
        return sheet;
    }
}
