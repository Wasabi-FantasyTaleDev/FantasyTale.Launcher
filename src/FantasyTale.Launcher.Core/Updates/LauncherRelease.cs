using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Updates;

/// <summary>Generated launcher file index with bounded paths, exact sizes and download checksums.</summary>
public sealed class LauncherRelease
{
    /// <summary>Canonical executable started by the updater; manifests cannot select a command.</summary>
    public const string Executable = "FantasyTale.Launcher.exe";
    /// <summary>Packaged helper required so an installed release retains its next-update capability.</summary>
    public const string UpdaterExecutable = "updater/FantasyTale.Launcher.Updater.exe";
    /// <summary>Inclusive semantic-version boundary below which installation is required.</summary>
    public string? DeprecatedMaxVersion { get; private set; }
    /// <summary>Strictly increasing release number, independent of the optional display label.</summary>
    public long Revision { get; }
    /// <summary>Optional release name shown in progress and settings.</summary>
    public string Version { get; }
    /// <summary>Hash of the exact index bytes selected for this update task.</summary>
    public string ManifestSha256 { get; }
    /// <summary>Immutable file destinations and content identities.</summary>
    public IReadOnlyList<ClientReleaseFile> Files { get; }
    private LauncherRelease(long revision, string version, string hash, IReadOnlyList<ClientReleaseFile> files)
    { Revision = revision; Version = version; ManifestSha256 = hash; Files = files; }

    /// <summary>Checks the expected product and platform before accepting a generated file index.</summary>
    public static LauncherRelease Parse(LauncherUpdatePolicy policy, byte[] manifest)
    {
        var root = TomlReader.Parse("launcher release", new UTF8Encoding(false, true).GetString(manifest));
        // Additive metadata is optional; known identity and integrity fields retain strict typed validation.
        if (root.Integer("schema", true) != 1 || root.String("product", true) != "fantasytale-launcher" ||
            root.String("platform", true) != policy.Platform)
            throw new InvalidDataException("Launcher release targets a different distribution.");
        var revision = root.Integer("revision", true, policy.MinimumRevision)!.Value;
        var version = root.String("version");
        if (version.Length > 64 || version.Any(char.IsControl)) throw new InvalidDataException("Invalid release label.");
        var files = new List<ClientReleaseFile>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var row in root.Tables("files"))
        {
            var path = ClientRelease.ValidatePath(row.String("path", true));
            if (path.Split('/').Any(p => p.Equals(LauncherUpdatePolicy.FileName, StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("remembered-logins.bin", StringComparison.OrdinalIgnoreCase) || p.StartsWith("account-sessions.bin", StringComparison.OrdinalIgnoreCase)) || !paths.Add(path))
                throw new InvalidDataException("Launcher update cannot overwrite source settings, account credentials or duplicate a destination.");
            var size = row.Integer("size", true, 0)!.Value;
            var hash = row.String("sha256", true);
            if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Invalid release file digest.");
            total = checked(total + size);
            files.Add(new(path, SourceAddress.RelativePath(row.String("source_path", true)), size, hash.ToLowerInvariant()));
        }
        foreach (var path in paths)
            for (var at = path.IndexOf('/'); at >= 0; at = path.IndexOf('/', at + 1))
                if (paths.Contains(path[..at])) throw new InvalidDataException("A release path is both a file and a directory.");
        if (files.GroupBy(file => file.Sha256).Any(group => group.Any(file => file.Size != group.First().Size)))
            throw new InvalidDataException("Files sharing a content digest must declare the same size.");
        if (!paths.Contains(Executable) || !paths.Contains("wwwroot/index.html") || !paths.Contains(UpdaterExecutable))
            throw new InvalidDataException("Launcher release is incomplete.");
        var release = new LauncherRelease(revision, version, Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant(), files.AsReadOnly());
        if (root.Has("deprecated_max_version")) release.SetDeprecation(root.String("deprecated_max_version", true));
        return release;
    }

    private void SetDeprecation(string threshold)
    {
        if (PluginCatalog.CompareVersions(threshold, Version) >= 0)
            throw new InvalidDataException("The deprecated version boundary must precede the published release.");
        DeprecatedMaxVersion = threshold;
    }

    internal bool RequiresUpdate(string currentVersion) => DeprecatedMaxVersion is not null &&
        PluginCatalog.CompareVersions(currentVersion, DeprecatedMaxVersion) <= 0;
}
