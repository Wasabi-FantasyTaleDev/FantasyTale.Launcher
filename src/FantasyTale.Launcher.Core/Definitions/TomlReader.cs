using FantasyTale.Launcher.Sdk;
using Tomlyn;
using Tomlyn.Model;

namespace FantasyTale.Launcher.Core.Definitions;

/// <summary>Strict typed access to one parsed TOML table; every failure names the file and key.</summary>
internal sealed class TomlReader
{
    private readonly TomlTable _table;
    private readonly string _path;

    private TomlReader(string file, string path, TomlTable table)
    {
        File = file;
        _path = path;
        _table = table;
    }

    /// <summary>The file the table came from.</summary>
    public string File { get; }

    /// <summary>Parses a whole file.</summary>
    public static TomlReader Load(string file)
    {
        // Configuration writers atomically replace files; readers keep their opened snapshot.
        using var stream = OpenSnapshot(file);
        using var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true));
        return Parse(file, reader.ReadToEnd());
    }

    private static FileStream OpenSnapshot(string file)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
            catch (UnauthorizedAccessException) when (started.Elapsed < TimeSpan.FromSeconds(1)) { Thread.Sleep(10); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 && started.Elapsed < TimeSpan.FromSeconds(1)) { Thread.Sleep(10); }
        }
    }

    internal static TomlReader Parse(string file, string text)
    {
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(text)
                ?? throw new DefinitionException(file, "", "empty document");
        }
        catch (TomlException e)
        {
            throw new DefinitionException(file, "", e.Message);
        }
        return new TomlReader(file, "", table);
    }

    private string Key(string key) => _path.Length == 0 ? key : _path + "." + key;

    internal DefinitionException Error(string key, string reason) => new(File, Key(key), reason);
    internal bool Has(string key) => _table.ContainsKey(key);
    internal object Scalar(string key) => _table.TryGetValue(key, out var value) && value is bool or long or string
        ? value : throw Error(key, "must be a boolean, integer or string");
    internal IReadOnlyDictionary<string, object> Scalars() => _table.Keys.ToDictionary(key => key, Scalar, StringComparer.Ordinal);
    internal bool Boolean(string key, bool fallback = false) => !_table.TryGetValue(key, out var value)
        ? fallback : value is bool b ? b : throw Error(key, "must be a boolean");
    internal IReadOnlyList<TomlReader> Tables(string key, int? maximum = null)
    {
        if (!_table.TryGetValue(key, out var value)) return [];
        if (value is not System.Collections.IEnumerable items || value is string or TomlTable)
            throw Error(key, "must be an array of tables");
        var result = new List<TomlReader>();
        foreach (var item in items)
        {
            if (maximum is { } count && result.Count >= count) throw Error(key, $"at most {maximum} entries are allowed");
            if (item is not TomlTable table) throw Error(key, "must be an array of tables");
            result.Add(new TomlReader(File, Key(key) + $"[{result.Count}]", table));
        }
        return result;
    }

    /// <summary>Rejects keys other than <paramref name="allowed"/>.</summary>
    public void OnlyKeys(params string[] allowed)
    {
        foreach (var key in _table.Keys)
            if (Array.IndexOf(allowed, key) < 0) throw new DefinitionException(File, Key(key), "unknown key");
    }

    /// <summary>A sub-table, or null when absent.</summary>
    public TomlReader? Table(string key, bool required = false)
    {
        if (!_table.TryGetValue(key, out var value))
            return required ? throw new DefinitionException(File, Key(key), "missing") : null;
        return value is TomlTable t ? new TomlReader(File, Key(key), t) : throw new DefinitionException(File, Key(key), "must be a table");
    }

    /// <summary>A string, or <paramref name="fallback"/> when absent and not required.</summary>
    public string String(string key, bool required = false, string fallback = "")
    {
        if (!_table.TryGetValue(key, out var value))
            return required ? throw new DefinitionException(File, Key(key), "missing") : fallback;
        return value as string ?? throw new DefinitionException(File, Key(key), "must be a string");
    }

    /// <summary>An integer within the given bounds, or null when absent and not required.</summary>
    public long? Integer(string key, bool required = false, long min = long.MinValue, long max = long.MaxValue)
    {
        if (!_table.TryGetValue(key, out var value))
            return required ? throw new DefinitionException(File, Key(key), "missing") : null;
        if (value is not long n) throw new DefinitionException(File, Key(key), "must be an integer");
        return n < min || n > max ? throw new DefinitionException(File, Key(key), $"must be between {min} and {max}") : n;
    }

    /// <summary>An array of strings, empty when absent.</summary>
    public IReadOnlyList<string> Strings(string key)
    {
        if (!_table.TryGetValue(key, out var value)) return [];
        if (value is not TomlArray array) throw new DefinitionException(File, Key(key), "must be an array of strings");
        return array.Select(item => item as string ?? throw new DefinitionException(File, Key(key), "must be an array of strings")).ToArray();
    }

    /// <summary>A string-valued table without a required fallback language.</summary>
    internal IReadOnlyDictionary<string, string> StringMap(string key) => Table(key) is { } table
        ? table._table.Keys.ToDictionary(language => language, language => table.String(language, true), StringComparer.Ordinal)
        : new Dictionary<string, string>();

    /// <summary>A table of string arrays keyed by language tag.</summary>
    internal IReadOnlyDictionary<string, IReadOnlyList<string>> StringListMap(string key) => Table(key) is { } table
        ? table._table.Keys.ToDictionary(language => language, table.Strings, StringComparer.Ordinal)
        : new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>A localized text table; it must contain <c>en</c>.</summary>
    public LocalizedText Localized(string key)
    {
        var table = Table(key, required: true)!;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (language, text) in table._table)
            values[language] = text as string ?? throw new DefinitionException(File, table.Key(language), "must be a string");
        return values.ContainsKey("en") ? new LocalizedText(values) : throw new DefinitionException(File, Key(key), "needs an 'en' entry");
    }
}
