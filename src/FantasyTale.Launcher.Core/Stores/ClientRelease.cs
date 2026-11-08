using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Stores;

/// <summary>One immutable source file and its relative installation destination.</summary>
/// <param name="Path">Windows-safe relative installation path.</param>
/// <param name="SourcePath">Unescaped source-root-relative transport path.</param>
/// <param name="Size">Exact length before installation.</param>
/// <param name="Sha256">Lowercase SHA-256 of the file bytes.</param>
/// <param name="PluginArchive">Whether this is the single ZIP payload of a plugin release.</param>
/// <param name="ArchiveFormat">The client/server archive decoder, or null for ordinary files and legacy plugin ZIPs.</param>
/// <param name="ArchivePassword">An optional publisher-provided archive password; it is never included in task snapshots or logs.</param>
/// <param name="ArchiveVolume">One-based archive volume number, or zero for a single-volume archive.</param>
/// <param name="ArchiveRoot">Optional folder inside the archive whose contents become the installation root.</param>
public sealed record ClientReleaseFile(string Path, string SourcePath, long Size, string Sha256, bool PluginArchive = false,
    string? ArchiveFormat = null, [property: System.Text.Json.Serialization.JsonIgnore] string? ArchivePassword = null, int ArchiveVolume = 0,
    string? ArchiveRoot = null)
{
    /// <summary>Optional server entry point used to find the installation root independently of archive wrapper directories.</summary>
    public string? ArchiveExecutable { get; init; }

    /// <summary>Whether this transport file belongs to the unified archive installation path.</summary>
    public bool IsArchive => ArchiveFormat is not null;

    /// <summary>Describes a transport without including a publisher-provided password.</summary>
    public override string ToString() => $"{Path} ({Size} bytes, {ArchiveFormat ?? "files"}, volume {ArchiveVolume})";
}

/// <summary>Verified client, plugin or server file manifest bound to a catalog release, independent of HTTP or AList.</summary>
public static class ClientRelease
{
    /// <summary>Checks the manifest digest and identity before accepting a collision-free file list.</summary>
    public static IReadOnlyList<ClientReleaseFile> Parse(StorePackage package, byte[] bytes)
    {
        _ = PluginZip(package);
        if (package.GitHubAsset is { } github)
        {
            GitHubStoreReleases.Validate(github);
            if (!bytes.AsSpan().SequenceEqual(GitHubStoreReleases.Manifest(package)))
                throw new InvalidDataException("The retained manifest disagrees with its pinned GitHub archive.");
        }
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(package.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Release manifest failed integrity verification.");
        var root = TomlReader.Parse(package.Manifest, new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'));
        root.OnlyKeys("schema", "release", "files", "github_asset");
        if (root.Has("github_asset") && package.GitHubAsset is null) throw root.Error("github_asset", "requires a pinned GitHub package");
        if (root.Integer("schema", true) != 1 || package.Kind is not ("client" or "plugin" or "server")) throw root.Error("schema", "unsupported resource release");
        if (package.Kind == "server")
        {
            PluginCatalog.ValidateVersion(package.Manifest, "release.version", package.Version);
            if (package.Builds.Count < 1 || package.Builds.Distinct(StringComparer.Ordinal).Count() != package.Builds.Count)
                throw root.Error("release.builds", "a server requires unique client builds");
            foreach (var build in package.Builds) InstanceFile.ValidateId(package.Manifest, build);
        }
        var release = root.Table("release", true)!;
        if (package.Kind == "client") release.OnlyKeys("id", "game", "build", "version", "format", "archive_format", "archive_root", "password");
        else if (package.Kind == "server") release.OnlyKeys("id", "game", "builds", "version", "format", "archive_format", "archive_root", "password", "executable");
        else release.OnlyKeys("id", "game", "builds", "version", "format");
        var format = release.Has("format") ? release.String("format", true) : "files";
        if (package.Kind == "plugin")
        {
            if (format is not ("files" or "zip")) throw release.Error("format", "must be files or zip");
        }
        else if (format is not ("files" or "archive")) throw release.Error("format", "must be files or archive");
        var archiveFormat = format == "archive" ? release.String("archive_format", true).ToLowerInvariant() : null;
        if (archiveFormat is not (null or "zip" or "7z" or "rar")) throw release.Error("archive_format", "must be zip, 7z or rar");
        var archivePassword = format == "archive" && release.Has("password") ? release.String("password") : null;
        if (archivePassword is { Length: > 256 } || archivePassword?.Any(char.IsControl) == true)
            throw release.Error("password", "must be at most 256 printable characters");
        if (format == "archive" && archiveFormat is null) throw release.Error("archive_format", "is required for archive releases");
        var archiveRoot = format == "archive" && release.Has("archive_root") ? ValidatePath(release.String("archive_root", true)) : null;
        var executable = release.Has("executable") ? ValidatePath(release.String("executable", true)) : null;
        if (format != "archive" && (release.Has("archive_format") || release.Has("password") || release.Has("archive_root") || executable is not null))
            throw release.Error("format", "archive metadata requires format = archive");
        var matchingBuilds = package.Kind == "client"
            ? package.Builds.Count == 1 && release.String("build", true) == package.Builds[0]
            : release.Strings("builds").SequenceEqual(package.Builds, StringComparer.Ordinal);
        if (release.String("id", true) != package.Id || release.String("game", true) != package.Game || !matchingBuilds || release.String("version") != package.Version)
            throw release.Error("id", "manifest identity does not match the selected catalog release");
        var files = new List<ClientReleaseFile>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        var volumeNumbers = new HashSet<int>();
        foreach (var row in root.Tables("files"))
        {
            if (format == "archive") row.OnlyKeys("path", "source_path", "size", "sha256", "volume");
            else row.OnlyKeys("path", "source_path", "size", "sha256");
            var path = ValidatePath(row.String("path", true));
            if (format == "archive" && path.Contains('/')) throw row.Error("path", "archive volume names must be file names without directories");
            if (!paths.Add(path)) throw row.Error("path", "duplicate installation destination");
            var source = SourceAddress.RelativePath(row.String("source_path", true));
            var size = row.Integer("size", true, 0)!.Value;
            var hash = row.String("sha256", true);
            if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c))) throw row.Error("sha256", "must be a SHA-256 digest");
            total = checked(total + size);
            var volume = format == "archive" ? checked((int)(row.Integer("volume", false, 1, int.MaxValue) ?? files.Count + 1)) : 0;
            if (format == "archive" && !volumeNumbers.Add(volume)) throw row.Error("volume", "duplicate archive volume number");
            files.Add(new(path, source, size, hash.ToLowerInvariant(), format == "zip", archiveFormat, archivePassword, volume, archiveRoot) { ArchiveExecutable = executable });
        }
        if (files.Count == 0 || total != package.Size) throw root.Error("files", "empty release or incorrect total size");
        foreach (var path in paths)
            for (var at = path.IndexOf('/'); at >= 0; at = path.IndexOf('/', at + 1))
                if (paths.Contains(path[..at])) throw root.Error("files", "a destination is both a file and a parent directory");
        if (package.Kind == "plugin" && (format == "files" && !paths.Contains("plugin.toml")))
            throw root.Error("files", "a plugin requires plugin.toml");
        if (format == "zip" && (files.Count != 1 || !files[0].Path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            throw root.Error("files", "a ZIP plugin release requires exactly one ZIP transport file");
        if (format == "archive")
        {
            if (!volumeNumbers.SetEquals(Enumerable.Range(1, files.Count))) throw root.Error("files", "archive volumes must be numbered consecutively from 1");
            if (files.Any(file => file.ArchiveFormat != archiveFormat || file.ArchivePassword != archivePassword)) throw root.Error("files", "archive metadata is inconsistent");
        }
        return files;
    }

    internal static ClientReleaseFile? PluginZip(StorePackage package)
    {
        if (package.PluginArchive is not { } archive) return null;
        if (package.Kind != "plugin" || archive.Size < 1 || archive.Sha256 is not { Length: 64 }
            || archive.Sha256.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidDataException("Invalid optional plugin archive identity.");
        var source = SourceAddress.RelativePath(archive.SourcePath);
        var name = ValidatePath(Path.GetFileName(source));
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A plugin archive must be a ZIP file.");
        return new(name, source, archive.Size, archive.Sha256.ToLowerInvariant(), ArchiveFormat: "zip");
    }

    internal static string ValidatePath(string path)
    {
        // Destination names are literal filesystem paths; '#' and '%' are valid
        // assets, while transport paths retain SourceAddress's URL restrictions.
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || path.Any(char.IsControl) || path.IndexOfAny(['\\', ':', '?']) >= 0 ||
            path.Split('/').Any(part => part.Length == 0 || part is "." or ".." || part != part.Trim()))
            throw new InvalidDataException("Invalid relative installation path.");
        foreach (var part in path.Split('/'))
        {
            var device = part.Split('.')[0].ToUpperInvariant();
            if (part.EndsWith('.') || part.IndexOfAny(['<', '>', '"', '|', '*']) >= 0 || part.StartsWith(".fantasytale", StringComparison.OrdinalIgnoreCase) ||
                device is "CON" or "PRN" or "AUX" or "NUL" || device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) || device.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(device[3]) ||
                part.Equals("session.toml", StringComparison.OrdinalIgnoreCase) || part.Equals("instance.toml", StringComparison.OrdinalIgnoreCase) || part.StartsWith("fthook-status-", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Reserved or ambiguous installation path.");
        }
        return path;
    }
}
