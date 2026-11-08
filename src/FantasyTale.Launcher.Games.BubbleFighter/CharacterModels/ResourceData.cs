using Tomlyn;
using Tomlyn.Model;

namespace FantasyTale.Launcher.Games.BubbleFighter.CharacterModels;

internal sealed class ResourceData(TomlTable table, string file)
{
    internal static ResourceData Load(string file)
    {
        try { return new(TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(file)) ?? throw new InvalidDataException("Empty resource document."), file); }
        catch (TomlException error) { throw new InvalidDataException(file + ": " + error.Message, error); }
    }

    internal static ResourceData Parse(string text, string name)
    {
        try { return new(TomlSerializer.Deserialize<TomlTable>(text) ?? throw new InvalidDataException("Empty resource document."), name); }
        catch (TomlException error) { throw new InvalidDataException(name + ": " + error.Message, error); }
    }

    internal string Source => file;

    internal bool Has(string key) => table.ContainsKey(key);

    internal object? Raw(string key) => table.TryGetValue(key, out var value) ? value : null;

    internal ResourceData? Table(string key) => !table.TryGetValue(key, out var value) ? null
        : value is TomlTable child ? new ResourceData(child, file) : throw new InvalidDataException(file + ": " + key + " must be a table");

    internal IEnumerable<string> Names => table.Keys;

    internal long? Integer(string key) => !table.TryGetValue(key, out var value) ? null
        : value is long number ? number : throw new InvalidDataException(file + ": " + key + " must be an integer");

    /// <summary>Reads a string or an array of strings; a missing key yields an empty list.</summary>
    internal string[] List(string key) => !table.TryGetValue(key, out var value) ? []
        : value switch
        {
            string single => [single],
            TomlArray array => array.Select(item => item as string ?? throw new InvalidDataException(file + ": " + key + " must hold strings")).ToArray(),
            _ => throw new InvalidDataException(file + ": " + key + " must be a string or string array")
        };

    internal void Keys(params string[] allowed)
    {
        foreach (var key in table.Keys)
            if (!allowed.Contains(key, StringComparer.Ordinal)) throw new InvalidDataException(file + ": unknown key " + key);
    }

    internal string Text(string key, string? fallback = null) => table.TryGetValue(key, out var value)
        ? value as string ?? throw new InvalidDataException(file + ": " + key + " must be a string")
        : fallback ?? throw new InvalidDataException(file + ": missing " + key);

    internal bool Flag(string key) => table.TryGetValue(key, out var value) ? value is bool b ? b
        : throw new InvalidDataException(file + ": " + key + " must be boolean") : false;

    internal void Schema()
    {
        if (!table.TryGetValue("schema", out var value) || value is not long version || version != 1)
            throw new InvalidDataException(file + ": schema must be 1");
    }

    internal ResourceData[] Rows(string key) => !table.TryGetValue(key, out var value) ? []
        : value is TomlTableArray rows ? rows.Select(row => new ResourceData(row, file)).ToArray()
        : throw new InvalidDataException(file + ": " + key + " must be a table array");

    internal IReadOnlyDictionary<string, string> Labels(string key)
    {
        if (!table.TryGetValue(key, out var value) || value is not TomlTable labels) throw new InvalidDataException(file + ": missing " + key);
        return labels.ToDictionary(p => p.Key, p => p.Value as string ?? throw new InvalidDataException(file + ": invalid label"), StringComparer.Ordinal);
    }

    internal static string Hash(string value)
    {
        if (value.Length != 64 || !value.All(char.IsAsciiHexDigitLower)) throw new InvalidDataException("Expected a lower-case SHA-256 resource digest.");
        return value;
    }
}
