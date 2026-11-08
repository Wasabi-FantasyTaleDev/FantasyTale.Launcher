namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>
/// Tab-separated string tables (UTF-16 LE with BOM in every sampled build despite the .csv name). Lines keep their own
/// terminators, column counts, empty headers and trailing empty columns; values never contain tabs or line breaks.
/// </summary>
internal sealed class DelimitedDocument : IRecordDocument
{
    private readonly TextCodec _codec;
    private readonly string _member;
    private readonly List<Line> _lines = [];
    private readonly string _newline;
    private Dictionary<string, List<int>>? _headers;
    private int _headerRow;

    private DelimitedDocument(TextCodec codec, string member, string text)
    {
        _codec = codec; _member = member;
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOfAny(['\r', '\n'], start);
            if (end < 0) { _lines.Add(new(text[start..].Split('\t'), "")); break; }
            var terminator = text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? "\r\n" : text[end].ToString();
            _lines.Add(new(text[start..end].Split('\t'), terminator));
            start = end + terminator.Length;
        }
        _newline = _lines.Select(l => l.Terminator).FirstOrDefault(t => t.Length > 0) ?? "\r\n";
    }

    /// <summary>Decodes a member strictly with its BOM-declared encoding.</summary>
    internal static DelimitedDocument Load(byte[] bytes, string member)
    {
        var codec = TextCodec.Detect(bytes);
        return new(codec, member, codec.Decode(bytes, member));
    }

    /// <inheritdoc />
    public byte[] Save() => _codec.Encode(string.Concat(_lines.Select(l => string.Join('\t', l.Fields) + l.Terminator)), _member);

    /// <inheritdoc />
    public RecordBinding Bind(TableShape shape, IEnumerable<string> fields)
    {
        var headers = Headers(shape);
        string Physical(string field) => "#" + Columns.Resolve(headers, field, _member).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var keys = shape.Keys.Select(Physical).ToArray();
        if (keys.Distinct(StringComparer.Ordinal).Count() != keys.Length) throw new InvalidDataException(_member + ": duplicate key columns");
        return new("", shape with { Sheet = null, Keys = keys },
            fields.Distinct(StringComparer.Ordinal).ToDictionary(f => f, Physical, StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public string? Read(TableShape shape, IReadOnlyList<string> key, string column)
    {
        var line = Find(shape, key);
        if (line < 0) return null;
        var index = Columns.Resolve(Headers(shape), column, _member) - 1;
        return index < _lines[line].Fields.Length ? _lines[line].Fields[index] : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string?> ReadAll(TableShape shape, IReadOnlyList<string> key, string column)
    {
        var index = Columns.Resolve(Headers(shape), column, _member) - 1;
        return Lines(shape, key).Select(line => index < _lines[line].Fields.Length ? _lines[line].Fields[index] : null).ToArray();
    }

    /// <inheritdoc />
    public void Apply(TableShape shape, RecordEdit edit)
    {
        if (edit.Key.Any(k => k.IndexOfAny(['\t', '\r', '\n']) >= 0))
            throw new InvalidDataException(_member + ": tab-separated keys cannot contain tabs or line breaks");
        var headers = Headers(shape);
        var keyColumns = shape.Keys.Select(k => Columns.Resolve(headers, k, _member)).ToArray();
        var lines = Lines(shape, edit.Key);
        if (edit.Action == EditAction.Upsert) edit = edit with { Action = lines.Length == 0 ? EditAction.Add : EditAction.Patch };
        List<(int Column, CellValue Value)> Writes(IReadOnlyDictionary<string, CellValue> set)
        {
            var writes = set.Select(p => (Column: Columns.Resolve(headers, p.Key, _member), p.Value)).ToList();
            foreach (var (column, value) in writes)
            {
                if (value.Text is { } text && text.IndexOfAny(['\t', '\r', '\n']) >= 0)
                    throw new InvalidDataException(_member + ": tab-separated values cannot contain tabs or line breaks");
                var index = Array.IndexOf(keyColumns, column);
                if (index >= 0 && value.Text != edit.Key[index]) throw new InvalidDataException(_member + ": an edit cannot change a record key");
            }
            return writes;
        }
        if (edit.Action == EditAction.Add)
        {
            if (lines.Length > 0) throw new InvalidDataException(_member + ": record already exists " + Columns.Key(edit.Key));
            var writes = Writes(edit.Set);
            var width = Math.Max(_lines[_headerRow - 1].Fields.Length, writes.Concat(keyColumns.Select(c => (c, new CellValue(null)))).Max(w => w.Item1));
            var fields = new string[width];
            Array.Fill(fields, "");
            for (var i = 0; i < keyColumns.Length; i++) fields[keyColumns[i] - 1] = edit.Key[i];
            foreach (var (column, value) in writes) fields[column - 1] = value.Text ?? "";
            // Keep a final line without terminator last; new records follow the file's own line break style.
            var last = _lines.FindLastIndex(l => l.Fields.Length > 1 || l.Fields[0].Length > 0);
            var at = last + 1;
            if (_lines[last].Terminator.Length == 0) { _lines[last] = _lines[last] with { Terminator = _newline }; _lines.Insert(at, new(fields, "")); }
            else _lines.Insert(at, new(fields, _newline));
            _index = null;
            return;
        }
        if (lines.Length == 0) throw new InvalidDataException(_member + ": record not found " + Columns.Key(edit.Key));
        foreach (var line in lines)
        foreach (var (column, expected) in edit.Expect)
        {
            var index = Columns.Resolve(headers, column, _member) - 1;
            if ((index < _lines[line].Fields.Length ? _lines[line].Fields[index] : "") != expected)
                throw new InvalidDataException(_member + ": precondition failed for " + Columns.Key(edit.Key) + " " + column);
        }
        switch (edit.Action)
        {
            case EditAction.Patch:
            case EditAction.Replace:
                for (var occurrence = 0; occurrence < lines.Length; occurrence++)
                {
                    var line = lines[occurrence];
                    var writes = Writes(edit.For(occurrence, lines.Length, _member));
                    var fields = _lines[line].Fields;
                    var width = Math.Max(fields.Length, writes.Select(w => w.Column).DefaultIfEmpty(0).Max());
                    if (width > fields.Length) Array.Resize(ref fields, width);
                    for (var i = 0; i < fields.Length; i++) fields[i] ??= "";
                    if (edit.Action == EditAction.Replace)
                        for (var i = 0; i < fields.Length; i++) if (!keyColumns.Contains(i + 1)) fields[i] = "";
                    foreach (var (column, value) in writes) fields[column - 1] = value.Text ?? "";
                    _lines[line] = _lines[line] with { Fields = fields };
                }
                break;
            case EditAction.Remove:
                // From the last record back, so earlier line numbers stay valid.
                foreach (var line in lines.OrderDescending())
                {
                    var removed = _lines[line];
                    _lines.RemoveAt(line);
                    // A removed final line hands its missing terminator to the new final line.
                    if (removed.Terminator.Length == 0 && line > 0) _lines[line - 1] = _lines[line - 1] with { Terminator = "" };
                }
                _index = null;
                break;
            default:
                throw new InvalidDataException(_member + ": unsupported table action " + edit.Action);
        }
    }

    /// <summary>Line numbers of every record with this key, in document order.</summary>
    private int[] Lines(TableShape shape, IReadOnlyList<string> key)
    {
        if (key.Count != shape.Keys.Count) throw new InvalidDataException(_member + ": key has " + key.Count + " values, expected " + shape.Keys.Count);
        return Index(shape).TryGetValue(Columns.Key(key), out var lines) ? lines.ToArray() : [];
    }

    private Dictionary<string, List<int>> Headers(TableShape shape)
    {
        if (_headers is not null && _headerRow == shape.HeaderRow) return _headers;
        if (shape.HeaderRow < 1 || shape.HeaderRow > _lines.Count) throw new InvalidDataException(_member + ": header row not found");
        _headerRow = shape.HeaderRow;
        _headers = new(StringComparer.Ordinal);
        var fields = _lines[shape.HeaderRow - 1].Fields;
        for (var i = 0; i < fields.Length; i++)
        {
            if (!_headers.TryGetValue(fields[i], out var list)) _headers.Add(fields[i], list = []);
            list.Add(i + 1);
        }
        _index = null;
        return _headers;
    }

    private Dictionary<string, List<int>>? _index;
    private string? _indexShape;

    private int Find(TableShape shape, IReadOnlyList<string> key)
    {
        if (key.Count != shape.Keys.Count) throw new InvalidDataException(_member + ": key has " + key.Count + " values, expected " + shape.Keys.Count);
        if (!Index(shape).TryGetValue(Columns.Key(key), out var lines)) return -1;
        if (lines.Count != 1) throw new InvalidDataException(_member + ": duplicate record key " + Columns.Key(key));
        return lines[0];
    }

    /// <inheritdoc />
    public IReadOnlyList<IReadOnlyList<string>> Keys(TableShape shape) =>
        Index(shape).Keys.Select(k => (IReadOnlyList<string>)k.Split('\u001F')).ToArray();

    private Dictionary<string, List<int>> Index(TableShape shape)
    {
        var headers = Headers(shape);
        var identity = string.Join("\u001E", shape.Keys);
        if (_index is null || _indexShape != identity)
        {
            var columns = shape.Keys.Select(k => Columns.Resolve(headers, k, _member) - 1).ToArray();
            _index = new(StringComparer.Ordinal);
            _indexShape = identity;
            for (var i = shape.HeaderRow; i < _lines.Count; i++)
            {
                var fields = _lines[i].Fields;
                var values = columns.Select(c => c < fields.Length ? fields[c] : "").ToArray();
                if (values.All(v => v.Length == 0)) continue;
                var text = Columns.Key(values);
                if (!_index.TryGetValue(text, out var list)) _index.Add(text, list = []);
                list.Add(i);
            }
        }
        return _index;
    }

    private sealed record Line(string[] Fields, string Terminator);
}
