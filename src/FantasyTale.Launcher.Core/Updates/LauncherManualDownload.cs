using FantasyTale.Launcher.Core.Definitions;

namespace FantasyTale.Launcher.Core.Updates;

/// <summary>Reads the distribution's independent manual download page when automatic updates cannot finish.</summary>
public static class LauncherManualDownload
{
    /// <summary>Name of the optional, separately versioned manual-download definition.</summary>
    public const string FileName = "launcher-download.toml";

    /// <summary>Returns a validated HTTP(S) download page, or null when the distribution supplies no fallback.</summary>
    public static string? Load(string installationDirectory)
    {
        var path = UpdateFiles.Safe(installationDirectory, FileName);
        if (!File.Exists(path)) return null;
        var definition = TomlReader.Load(path);
        definition.OnlyKeys("schema", "url");
        if (definition.Integer("schema", true) != 1) throw definition.Error("schema", "unsupported manual-download definition");
        var value = definition.String("url", true);
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var url) ||
            url.Scheme is not ("http" or "https") || url.UserInfo.Length != 0)
            throw definition.Error("url", "must be an HTTP(S) page without credentials");
        return url.AbsoluteUri;
    }
}
