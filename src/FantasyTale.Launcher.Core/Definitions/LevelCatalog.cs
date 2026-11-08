using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Definitions;

/// <summary>Reads data-defined level tables and atlas ranges without game-specific ids or compiled rank limits.</summary>
public static class LevelCatalog
{
    /// <summary>
    /// Loads a manifest as <see cref="Load"/> does, except that a manifest declaring
    /// <c>optional_assets = true</c> yields <see langword="null"/> when its table or any referenced
    /// image is absent.
    /// </summary>
    /// <remarks>
    /// Original level tables and rank artwork are distributed with release packages but are not part of
    /// the public source tree. A source build therefore runs without level display instead of rejecting
    /// the game definition. Declarations, paths and available assets are still validated; relationships
    /// requiring an absent table or image are checked when that asset is supplied.
    /// </remarks>
    public static LevelCatalogDefinition? LoadAvailable(string file, string gameFolder)
        => LoadCore(file, gameFolder, allowMissingAssets: true);

    /// <summary>Loads one bounded manifest and its CSV/PNG sources, preserving original level ids and names.</summary>
    /// <exception cref="DefinitionException">A manifest declaration or available asset is invalid.</exception>
    /// <exception cref="IOException">A required asset is absent or cannot be read.</exception>
    public static LevelCatalogDefinition Load(string file, string gameFolder)
        => LoadCore(file, gameFolder, allowMissingAssets: false)!;

    private static LevelCatalogDefinition? LoadCore(string file, string gameFolder, bool allowMissingAssets)
    {
        InstanceStore.RejectLinks(file);
        var root = TomlReader.Load(file);
        root.OnlyKeys("schema", "display_offset", "names_pending", "optional_assets", "source", "atlases", "icons", "badges");
        if (root.Integer("schema", true) != 1) throw root.Error("schema", "must be 1");
        var optional = root.Boolean("optional_assets") && allowMissingAssets;
        var displayOffset = (int)(root.Integer("display_offset", min: -1, max: 1) ?? 0);
        var namesPending = root.Boolean("names_pending");
        var missingAsset = false;
        FileStream? OpenAsset(string path)
        {
            try { return File.OpenRead(path); }
            catch (IOException error) when (optional && error is FileNotFoundException or DirectoryNotFoundException)
            {
                missingAsset = true;
                return null;
            }
        }
        var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        var source = root.Table("source", true)!;
        source.OnlyKeys("file", "encoding", "skip_lines", "level_column", "exp_column", "name_column", "filter_column", "filter_value", "initial_level", "initial_exp");
        Encoding encoding = source.String("encoding", fallback: "utf-8") switch
        {
            "utf-8" => new UTF8Encoding(false, true),
            "utf-16le" => new UnicodeEncoding(false, true, true),
            "utf-16be" => new UnicodeEncoding(true, true, true),
            _ => throw source.Error("encoding", "must be utf-8, utf-16le or utf-16be"),
        };
        var path = LocalFile(folder, source.String("file", true), source);
        var skip = (int)(source.Integer("skip_lines", min: 0, max: 32) ?? 0);
        var levelColumn = Column(source, "level_column", true)!.Value;
        var expColumn = Column(source, "exp_column", true)!.Value;
        var nameColumn = Column(source, "name_column");
        var filterColumn = Column(source, "filter_column");
        if (filterColumn.HasValue != source.Has("filter_value")) throw source.Error("filter_column", "requires filter_value, and vice versa");
        var rows = new SortedDictionary<int, (long Exp, string? Name)>();
        if (source.Has("initial_level")) rows.Add((int)source.Integer("initial_level", true, 0, 1000000)!, (source.Integer("initial_exp", true, 0)!.Value, null));
        else if (source.Has("initial_exp")) throw source.Error("initial_exp", "requires initial_level");
        using var stream = OpenAsset(path);
        var tableAvailable = stream is not null;
        if (stream?.Length > 4 * 1024 * 1024) throw source.Error("file", "exceeds 4 MiB");
        using var reader = stream is null ? null : new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        var lineNumber = 0;
        while (reader?.ReadLine() is { } line)
        {
            if (++lineNumber > 16384) throw source.Error("file", "exceeds 16384 lines");
            if (line.Length > 4096) throw source.Error("file", $"line {lineNumber} exceeds 4096 characters");
            if (lineNumber <= skip || string.IsNullOrWhiteSpace(line) || line.StartsWith("//", StringComparison.Ordinal)) continue;
            var values = Csv(line, source, lineNumber);
            string Cell(int column) => column < values.Count ? values[column] : throw source.Error("file", $"line {lineNumber} lacks column {column}");
            if (filterColumn is { } filter && Cell(filter) != source.String("filter_value")) continue;
            if (!int.TryParse(Cell(levelColumn), NumberStyles.None, CultureInfo.InvariantCulture, out var level) || level is < 0 or > 1000000 ||
                !long.TryParse(Cell(expColumn), NumberStyles.None, CultureInfo.InvariantCulture, out var exp) || exp < 0)
                throw source.Error("file", $"line {lineNumber} has an invalid level or cumulative experience");
            var name = nameColumn is { } n ? Cell(n) : null;
            if (name?.Length > 128 || rows.Count >= 4096 || !rows.TryAdd(level, (exp, string.IsNullOrWhiteSpace(name) ? null : name)))
                throw source.Error("file", $"line {lineNumber} has a duplicate level, oversized name or exceeds 4096 levels");
        }
        if (tableAvailable && rows.Count == 0) throw source.Error("file", "contains no matching levels");
        long previous = -1;
        foreach (var row in rows.Values)
        {
            if (row.Exp <= previous) throw source.Error("file", "experience must increase with level id");
            previous = row.Exp;
        }
        var images = new Dictionary<string, (int Width, int Height)?>(StringComparer.Ordinal);
        LevelIcon Image(TomlReader table, int index = 0, bool atlas = false)
        {
            var imagePath = LocalFile(folder, table.String("file", true), table);
            if (!images.TryGetValue(imagePath, out var dimensions))
            {
                using var image = OpenAsset(imagePath);
                images.Add(imagePath, dimensions = image is null ? null : PngSize(image, table));
            }
            var width = atlas ? (int)table.Integer("cell_width", true, 1, 512)! : (int)(table.Integer("width", min: 1, max: 512) ?? dimensions?.Width ?? 1);
            var height = atlas ? (int)table.Integer("cell_height", true, 1, 512)! : (int)(table.Integer("height", min: 1, max: 512) ?? dimensions?.Height ?? 1);
            var columns = atlas ? (int)table.Integer("columns", true, 1, 256)! : 1;
            var x = (int)(table.Integer("x", min: 0, max: 8192) ?? 0) + index % columns * width;
            var y = (int)(table.Integer("y", min: 0, max: 8192) ?? 0) + index / columns * height;
            if (dimensions is { } size && (width > 512 || height > 512 || x + width > size.Width || y + height > size.Height)) throw table.Error("file", "icon rectangle exceeds PNG bounds");
            return new LevelIcon(Path.GetRelativePath(gameFolder, imagePath).Replace('\\', '/'), x, y, width, height, dimensions?.Width ?? 1, dimensions?.Height ?? 1);
        }
        var icons = rows.Keys.ToDictionary(level => level, _ => new Dictionary<string, LevelIcon>(StringComparer.Ordinal));
        foreach (var atlas in root.Tables("atlases", 32))
        {
            atlas.OnlyKeys("file", "first_level", "last_level", "first_cell", "columns", "cell_width", "cell_height", "x", "y", "variant");
            var first = (int)atlas.Integer("first_level", true, 0, 1000000)!;
            var last = (int)(atlas.Integer("last_level", min: first, max: 1000000) ?? (rows.Count == 0 ? first : rows.Keys.Last()));
            var start = (int)(atlas.Integer("first_cell", min: 0, max: 65536) ?? 0);
            var variant = Identifier(atlas, "variant", "default");
            // Validate declarations and present artwork even when the optional source table is absent.
            _ = Image(atlas, start, true);
            foreach (var level in rows.Keys.Where(level => level >= first && level <= last))
                if (!icons[level].TryAdd(variant, Image(atlas, checked(start + level - first), true))) throw atlas.Error("first_level", "overlapping icon mappings");
        }
        foreach (var icon in root.Tables("icons", 4096))
        {
            icon.OnlyKeys("level", "file", "x", "y", "width", "height", "variant");
            var level = (int)icon.Integer("level", true, 0, 1000000)!;
            var variant = Identifier(icon, "variant", "default");
            var image = Image(icon);
            if (tableAvailable && (!icons.TryGetValue(level, out var variants) || !variants.TryAdd(variant, image))) throw icon.Error("level", "unknown level or duplicate icon variant");
        }
        var badges = new Dictionary<string, LevelBadge>(StringComparer.Ordinal);
        foreach (var badge in root.Tables("badges", 64))
        {
            badge.OnlyKeys("id", "icon_id", "name", "file", "x", "y", "width", "height");
            var level = (int)badge.Integer("icon_id", true, 0, int.MaxValue)!;
            var name = badge.String("name", true);
            if (rows.ContainsKey(level) || string.IsNullOrWhiteSpace(name) || name.Length > 128 || badges.Values.Any(value => value.IconId == level) ||
                !badges.TryAdd(Identifier(badge, "id", ""), new LevelBadge(level, name, Image(badge)))) throw badge.Error("id", "duplicate badge, reserved level or invalid name");
        }
        if (tableAvailable && icons.Any(pair => !pair.Value.ContainsKey("default"))) throw root.Error("icons", "each configured level needs a default icon");
        if (missingAsset) return null;
        var entries = rows.Select(pair => new LevelEntry(pair.Key, pair.Value.Exp, pair.Value.Name, icons[pair.Key])).ToArray();
        return new LevelCatalogDefinition(entries, badges, displayOffset, namesPending);
    }

    /// <summary>Finds the level at an inclusive experience boundary; explicit server level ids take precedence.</summary>
    public static LevelEntry? Resolve(LevelCatalogDefinition catalog, int? level, long? experience)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return level is { } id ? catalog.Entries.SingleOrDefault(entry => entry.Level == id)
            : experience is { } exp ? catalog.Entries.LastOrDefault(entry => entry.MinimumExperience <= exp) : null;
    }

    private static int? Column(TomlReader table, string key, bool required = false) => (int?)table.Integer(key, required, 0, 31);
    private static string Identifier(TomlReader table, string key, string fallback)
    {
        var value = table.String(key, fallback: fallback);
        return value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? value : throw table.Error(key, "must be a short ASCII identifier");
    }
    private static string LocalFile(string folder, string relative, TomlReader table)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':', StringComparison.Ordinal)) throw table.Error("file", "must be a relative local file");
        var full = Path.GetFullPath(Path.Combine(folder, relative));
        if (!InstanceStore.IsWithin(folder, full)) throw table.Error("file", "must remain within the level data folder");
        InstanceStore.RejectLinks(full);
        return full;
    }
    private static (int Width, int Height) PngSize(FileStream stream, TomlReader table)
    {
        if (stream.Length > 16 * 1024 * 1024 || stream.Length < 33) throw table.Error("file", "PNG must be between 33 bytes and 16 MiB");
        Span<byte> header = stackalloc byte[24]; stream.ReadExactly(header);
        if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) || !header[12..16].SequenceEqual("IHDR"u8)) throw table.Error("file", "must be a PNG image");
        var width = BinaryPrimitives.ReadInt32BigEndian(header[16..20]); var height = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
        if (width is < 1 or > 8192 || height is < 1 or > 8192) throw table.Error("file", "PNG dimensions must be 1..8192");
        return (width, height);
    }
    private static List<string> Csv(string line, TomlReader source, int number)
    {
        var values = new List<string>(); var cell = new StringBuilder(); var quoted = false; var closed = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (quoted) { quoted = false; closed = true; }
                else if (cell.Length == 0 && !closed) quoted = true;
                else throw source.Error("file", $"malformed quoted field on line {number}");
            }
            else if (c == ',' && !quoted) { values.Add(cell.ToString().Trim()); cell.Clear(); closed = false; }
            else if (closed && !char.IsWhiteSpace(c)) throw source.Error("file", $"unexpected text after quote on line {number}");
            else if (!closed) cell.Append(c);
        }
        if (quoted) throw source.Error("file", $"unclosed quote on line {number}");
        values.Add(cell.ToString().Trim());
        if (values.Count > 32) throw source.Error("file", $"line {number} exceeds 32 columns");
        return values;
    }
}
