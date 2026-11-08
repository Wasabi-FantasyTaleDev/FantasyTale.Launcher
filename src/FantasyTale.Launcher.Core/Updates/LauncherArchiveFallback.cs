using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Updates;

internal static class LauncherArchiveFallback
{
    internal static async Task StageAsync(LauncherUpdatePolicy policy, LauncherRelease release, string root, HttpClient http,
        Action<ClientDownloadFile, double> progress, Action<int> extracting, CancellationToken ct)
    {
        var source = policy.OpenSource(http);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var bytes = await SourceDocumentReader.ReadBytesAsync(http, source, policy.FeedPath + ".archive.toml", timeout.Token).ConfigureAwait(false);
        var document = TomlReader.Parse("launcher archive", new UTF8Encoding(false, true).GetString(bytes));
        if (document.Integer("schema", true) != 1 || document.String("manifest_sha256", true) != release.ManifestSha256)
            throw new InvalidDataException("Launcher archive belongs to another release.");
        var format = document.String("format", true);
        if (format is not ("zip" or "7z" or "rar")) throw new InvalidDataException("Unsupported launcher archive format.");
        var volumes = new List<ClientReleaseFile>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in document.Tables("files"))
        {
            var path = ClientRelease.ValidatePath(row.String("path", true));
            var relative = SourceAddress.RelativePath(row.String("source_path", true));
            var size = row.Integer("size", true, 1)!.Value;
            var hash = row.String("sha256", true).ToLowerInvariant();
            var volume = checked((int)row.Integer("volume", true, 1, int.MaxValue)!.Value);
            if (hash.Length != 64 || hash.Any(character => !char.IsAsciiHexDigit(character)) ||
                !names.Add(path) || volume != volumes.Count + 1)
                throw new InvalidDataException("Invalid launcher archive volumes.");
            volumes.Add(new(path, relative, size, hash, ArchiveFormat: format, ArchiveVolume: volume));
        }
        if (volumes.Count == 0) throw new InvalidDataException("Launcher archive has no volumes.");
        var client = UpdateFiles.Safe(root, "archive-fallback");
        var workspace = ClientInstallTransaction.Workspace(client);
        Directory.CreateDirectory(workspace);
        // This isolated workspace contains only transport and extraction objects, never installed files.
        var retained = Directory.EnumerateFiles(workspace);
        var keep = volumes.Select(file => file.Sha256 + ".verified").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in retained)
            if (!keep.Contains(Path.GetFileName(path)) && !volumes.Any(file => Path.GetFileName(path).StartsWith(file.Sha256 + ".verified.", StringComparison.OrdinalIgnoreCase)))
                File.Delete(UpdateFiles.Safe(workspace, Path.GetFileName(path)));
        var engine = new HttpDownloadEngine(http);
        foreach (var volume in volumes)
        {
            ct.ThrowIfCancellationRequested();
            var resolved = await source.ResolveAsync(volume.SourcePath, ct).ConfigureAwait(false);
            await engine.DownloadAsync(new(resolved, ClientInstallTransaction.Staged(client, volume.Sha256), volume.Size, volume.Sha256),
                new InlineProgress(value => progress(new(volume.Path, volume.Size, value.Received, "downloading"), value.BytesPerSecond)), ct).ConfigureAwait(false);
            progress(new(volume.Path, volume.Size, volume.Size, "verified"), 0);
        }
        List<ClientReleaseFile> expanded;
        try
        {
            expanded = await ClientArchiveExtractor.ExtractAsync(client, volumes, null, extracting, ct,
                release.Files, LauncherUpdatePolicy.FileName).ConfigureAwait(false);
        }
        catch (ArchivePasswordRequiredException)
        {
            throw new InvalidDataException("Automatic launcher update archives cannot require an interactive password.");
        }
        var expected = release.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        var moved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in expanded)
        {
            ct.ThrowIfCancellationRequested();
            // Portable source settings belong only to manual installs.
            if (file.Path.Equals(LauncherUpdatePolicy.FileName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!expected.Remove(file.Path, out var selected) || file.Size != selected.Size || file.Sha256 != selected.Sha256)
                throw new InvalidDataException("Launcher archive differs from the selected file index.");
            var staged = ClientInstallTransaction.Staged(client, file.Sha256);
            if (moved.Add(file.Sha256)) File.Move(staged, UpdateFiles.Safe(root, "objects/" + file.Sha256 + ".verified"), true);
        }
        if (expected.Count != 0) throw new InvalidDataException("Launcher archive is incomplete.");
    }

    private sealed class InlineProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
