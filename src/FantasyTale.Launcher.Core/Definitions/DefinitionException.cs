namespace FantasyTale.Launcher.Core.Definitions;

/// <summary>A definition file is missing, malformed or inconsistent. The message names the file and key.</summary>
public sealed class DefinitionException : Exception
{
    /// <summary>Creates the exception for <paramref name="file"/> and <paramref name="key"/>.</summary>
    public DefinitionException(string file, string key, string reason)
        : base($"{file}: {(key.Length == 0 ? "" : key + ": ")}{reason}")
    {
        File = file;
        Key = key;
    }

    /// <summary>The definition file.</summary>
    public string File { get; }

    /// <summary>The dotted key path, empty for file-level problems.</summary>
    public string Key { get; }
}
