using System.Text;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>
/// A plugin's edit sheet: a header row and records, read strictly. Accepts UTF-8, UTF-16 and UTF-32 in either byte order,
/// with or without BOM (Excel's "CSV UTF-8" and "Unicode Text" both work), or the encoding the manifest declares; text in a
/// legacy code page fails instead of turning into replacement characters. The delimiter is a tab when the header has one, otherwise a comma. Fields may be quoted
/// with doubled quotes inside; quoted line breaks are kept as LF.
/// </summary>
internal sealed class CsvSheet
{
    private CsvSheet(string[] header, List<(int Line, string[] Fields)> records) { Header = header; Records = records; }

    /// <summary>Header cells, trimmed.</summary>
    internal string[] Header { get; }

    /// <summary>Records with the one-based line each starts on, padded to the header width.</summary>
    internal IReadOnlyList<(int Line, string[] Fields)> Records { get; }

    /// <summary>Decodes and parses a sheet, failing with the file and line on malformed input.</summary>
    internal static CsvSheet Parse(byte[] bytes, string source, string? encoding = null)
    {
        var codec = TextCodec.Sheet(bytes, encoding, source);
        string text;
        try { text = codec.Decode(bytes, source); }
        catch (InvalidDataException error)
        {
            throw new InvalidDataException(source + ": not valid " + codec.Name + " text; save the sheet as \"CSV UTF-8\" or Unicode text, "
                + "or declare encoding in the manifest", error);
        }
        // A NUL never belongs in a sheet; it means UTF-16 or UTF-32 was read as something else.
        if (text.Contains('\0')) throw new InvalidDataException(source + ": decoded as " + codec.Name + " but contains NUL characters; declare encoding in the manifest");
        var firstLine = text.IndexOfAny(['\r', '\n']) is var end and >= 0 ? text[..end] : text;
        var delimiter = firstLine.Contains('\t') ? '\t' : ',';
        var rows = Rows(text, delimiter, source);
        if (rows.Count == 0) throw new InvalidDataException(source + ": the sheet has no header row");
        var header = rows[0].Fields.Select(h => h.Trim()).ToArray();
        var records = new List<(int, string[])>();
        foreach (var (line, fields) in rows.Skip(1))
        {
            if (fields.Length > header.Length && fields.Skip(header.Length).Any(f => f.Length > 0))
                throw new InvalidDataException(source + ":" + line + ": more cells than header columns");
            var padded = new string[header.Length];
            for (var i = 0; i < header.Length; i++) padded[i] = i < fields.Length ? fields[i] : "";
            if (padded.Any(f => f.Length > 0)) records.Add((line, padded));
        }
        return new(header, records);
    }

    private static List<(int Line, string[] Fields)> Rows(string text, char delimiter, string source)
    {
        var rows = new List<(int, string[])>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var (line, start, i) = (1, 1, 0);
        void EndField() { fields.Add(field.ToString()); field.Clear(); }
        void EndRow() { EndField(); rows.Add((start, fields.ToArray())); fields.Clear(); }
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"' && field.Length == 0)
            {
                i++;
                while (true)
                {
                    if (i >= text.Length) throw new InvalidDataException(source + ":" + start + ": unterminated quoted cell");
                    if (text[i] == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                        i++;
                        break;
                    }
                    if (text[i] == '\r') { i++; if (i < text.Length && text[i] == '\n') i++; field.Append('\n'); line++; continue; }
                    if (text[i] == '\n') line++;
                    field.Append(text[i++]);
                }
                if (i < text.Length && text[i] != delimiter && text[i] is not ('\r' or '\n'))
                    throw new InvalidDataException(source + ":" + line + ": text after a closing quote");
                continue;
            }
            if (c == delimiter) { EndField(); i++; continue; }
            if (c is '\r' or '\n')
            {
                i += c == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                EndRow();
                start = ++line;
                continue;
            }
            field.Append(c);
            i++;
        }
        if (field.Length > 0 || fields.Count > 0) EndRow();
        return rows;
    }
}
