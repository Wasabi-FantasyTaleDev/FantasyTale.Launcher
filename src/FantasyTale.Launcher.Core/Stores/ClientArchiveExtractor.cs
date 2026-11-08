using System.Security.Cryptography;

namespace FantasyTale.Launcher.Core.Stores;

internal sealed class ArchivePasswordRequiredException : Exception
{
    internal ArchivePasswordRequiredException() : base("Archive password is required or incorrect.") { }
}

internal static class ClientArchiveExtractor
{
    internal static Task<List<ClientReleaseFile>> ExtractAsync(string client, IReadOnlyList<ClientReleaseFile> volumes,
        string? password, Action<int> progress, CancellationToken cancellationToken,
        IReadOnlyList<ClientReleaseFile>? expectedFiles = null, string? ignoredFile = null)
        => Task.Run(() => Extract(client, volumes, password, progress, expectedFiles, ignoredFile, cancellationToken), cancellationToken);

    private static List<ClientReleaseFile> Extract(string client, IReadOnlyList<ClientReleaseFile> volumes,
        string? password, Action<int> progress, IReadOnlyList<ClientReleaseFile>? expectedFiles, string? ignoredFile, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Archive installation requires the packaged Windows 7-Zip decoder.");
        if (volumes.Count == 0 || volumes.Any(value => !value.IsArchive))
            throw new InvalidDataException("Invalid archive transport volumes.");
        var ordered = volumes.OrderBy(value => value.ArchiveVolume).ToArray();
        var format = ordered[0].ArchiveFormat!;
        if (ordered.Any(value => value.ArchiveFormat != format || value.ArchiveRoot != ordered[0].ArchiveRoot))
            throw new InvalidDataException("Archive volumes disagree about their format or root.");
        var prefix = ordered[0].ArchiveRoot is { Length: > 0 } folder ? folder + "/" : "";
        var workspace = ClientInstallTransaction.Workspace(client);
        var streams = new List<Stream>();
        var temporary = ClientInstallTransaction.Safe(workspace, "archive-entry.part");
        var selected = expectedFiles?.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var volume in ordered)
            {
                ct.ThrowIfCancellationRequested();
                var stream = new FileStream(ClientInstallTransaction.Staged(client, volume.Sha256), FileMode.Open, FileAccess.Read, FileShare.Read);
                streams.Add(stream);
                if (stream.Length != volume.Size || !Convert.ToHexString(SHA256.HashData(stream)).Equals(volume.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("An archive volume failed verification before extraction.");
                stream.Position = 0;
            }
            using var archive = new NativeArchiveDecoder(format, ordered.Select(volume => volume.Path).ToArray(), streams, password, ct);
            var expected = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.Directory) continue;
                if (!entry.Path.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Archive contains files outside archive_root.");
                var relative = ClientRelease.ValidatePath(entry.Path[prefix.Length..]);
                if (selected is not null &&
                    (relative.Equals(ignoredFile, StringComparison.OrdinalIgnoreCase)
                        ? entry.Size > 65536
                        : !selected.TryGetValue(relative, out var selectedFile) || selectedFile.Size != entry.Size))
                    throw new InvalidDataException("Archive entry differs from the selected file index.");
                expected.Add(relative, entry.Size);
            }
            if (expected.Count == 0) throw new InvalidDataException("Archive contains no files.");
            if (selected is not null && selected.Keys.Any(path => !expected.ContainsKey(path)))
                throw new InvalidDataException("Archive is missing a selected file.");
            var expanded = new List<ClientReleaseFile>(expected.Count);
            File.Delete(temporary);
            archive.Extract(_ => temporary, (entry, hash) =>
            {
                var path = entry.Path[prefix.Length..];
                if (selected is not null && selected.TryGetValue(path, out var selectedFile) &&
                    !hash.Equals(selectedFile.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Archive entry failed the selected SHA-256 verification.");
                File.Move(temporary, ClientInstallTransaction.Staged(client, hash), overwrite: true);
                expanded.Add(new(path, "archive/" + hash, entry.Size, hash));
            }, progress);
            return expanded;
        }
        finally
        {
            foreach (var stream in streams) stream.Dispose();
            File.Delete(temporary);
        }
    }
}
