using System.Security.Cryptography;

namespace FantasyTale.Launcher.Games.BubbleFighter.Archives;

/// <summary>A verified virtual-file edit shared by resource plugins targeting the same archive.</summary>
/// <param name="Path">Full virtual member path, preserving the desired name's case.</param>
/// <param name="Source">Payload file, or null to remove an obsolete member.</param>
/// <param name="Sha256">Expected payload digest, or empty for removals.</param>
/// <param name="ExpectedOriginal">Original decoded digest; null requires the member to be absent.</param>
public sealed record ArchiveResourceEdit(string Path, string? Source, string Sha256, string? ExpectedOriginal);

/// <summary>Reusable interface for composing resource changes from the user's current BFZ or PKN archive.</summary>
public interface IArchiveResourceComposer
{
    /// <summary>Creates an archive, verifies changed decoded members and preserves unchanged stored bytes and metadata.</summary>
    void Compose(string input, string clientRelativePath, string output, IReadOnlyList<ArchiveResourceEdit> edits,
        Action<long, long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a new BFZ from an empty baseline; all edits must add members and the output must not exist.</summary>
    void Create(string clientRelativePath, string output, IReadOnlyList<ArchiveResourceEdit> edits,
        Action<long, long>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Combines edits without transplanting unrelated contents from another client's archive.</summary>
public sealed class ArchiveResourceComposer : IArchiveResourceComposer
{
    /// <inheritdoc />
    public void Compose(string input, string clientRelativePath, string output, IReadOnlyList<ArchiveResourceEdit> edits,
        Action<long, long>? progress = null, CancellationToken cancellationToken = default)
    {
        using var original = Archive.Open(input, clientRelativePath);
        Compose(original, clientRelativePath, output, edits, progress, cancellationToken);
    }

    /// <inheritdoc />
    public void Create(string clientRelativePath, string output, IReadOnlyList<ArchiveResourceEdit> edits,
        Action<long, long>? progress = null, CancellationToken cancellationToken = default)
    {
        ValidatePath(clientRelativePath);
        if (!clientRelativePath.EndsWith(".bfz", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("New resource archives must declare the BFZ format.");
        using var empty = new MemoryStream();
        BfzArchive.Write(empty, clientRelativePath, []);
        empty.Position = 0;
        using var original = BfzArchive.Open(empty, clientRelativePath);
        Compose(original, clientRelativePath, output, edits, progress, cancellationToken);
    }

    private static void Compose(IArchiveReader original, string clientRelativePath, string output,
        IReadOnlyList<ArchiveResourceEdit> edits, Action<long, long>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var changes = new Dictionary<string, ArchiveResourceEdit>(StringComparer.OrdinalIgnoreCase);
        foreach (var edit in edits)
        {
            ValidatePath(edit.Path);
            if (changes.TryGetValue(edit.Path, out var prior))
            {
                if (prior.Sha256 != edit.Sha256 || prior.ExpectedOriginal != edit.ExpectedOriginal || (prior.Source is null) != (edit.Source is null))
                    throw new InvalidDataException("Resource plugins make conflicting edits to " + edit.Path);
                continue;
            }
            changes.Add(edit.Path, edit);
            if (edit.Source is not null && FileHash(edit.Source) != edit.Sha256)
                throw new InvalidDataException("Resource payload hash mismatch: " + edit.Path);
            var exists = original.Contains(edit.Path);
            if (exists != (edit.ExpectedOriginal is not null) || exists && Hash(original.Read(edit.Path)) != edit.ExpectedOriginal)
                throw new InvalidDataException("The current resource differs from this plan: " + edit.Path);
        }
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long completed = 0;
        var total = 2L * (original.Entries.Count + changes.Values.Count(e => e.ExpectedOriginal is null));
        IEnumerable<PknWriteMember> Members()
        {
            foreach (var entry in original.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke(++completed, total);
                if (changes.TryGetValue(entry.Path, out var edit))
                {
                    if (edit.Source is null) continue;
                    var bytes = File.ReadAllBytes(edit.Source);
                    if (Hash(bytes) != edit.Sha256) throw new InvalidDataException("Resource payload changed during preparation: " + edit.Path);
                    expected.Add(edit.Path, edit.Sha256);
                    yield return new(edit.Path, bytes, null);
                }
                else
                {
                    if (original is BfzArchive bfz)
                    {
                        var raw = bfz.ReadStored(entry.Path);
                        expected.Add(entry.Path, Hash(raw.Record));
                        yield return new PknWriteMember(entry.Path, null, null) { Bfz = raw };
                    }
                    else
                    {
                        var raw = StoredPkn.Read(original, entry.Path);
                        expected.Add(entry.Path, Hash(raw.Payload));
                        yield return new(entry.Path, null, raw);
                    }
                }
            }
            foreach (var edit in changes.Values.Where(e => e.ExpectedOriginal is null && e.Source is not null))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke(++completed, total);
                var bytes = File.ReadAllBytes(edit.Source!);
                if (Hash(bytes) != edit.Sha256) throw new InvalidDataException("Resource payload changed during preparation: " + edit.Path);
                expected.Add(edit.Path, edit.Sha256);
                yield return new(edit.Path, bytes, null);
            }
        }
        var created = false;
        try
        {
            using (var destination = new FileStream(output, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                created = true;
                if (original is BfzArchive) BfzArchive.WriteMembers(destination, clientRelativePath, Members().Select(m => new BfzWriteMember(m.Path, m.Data, m.Bfz)));
                else PknComposer.WriteMembers(destination, clientRelativePath, Members(), original as LegacyPknArchive);
                destination.Flush(true);
            }
            using var result = Archive.Open(output, clientRelativePath);
            if (result.Entries.Count != expected.Count) throw new InvalidDataException("Prepared archive has an incorrect member count.");
            foreach (var entry in result.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var unchanged = !changes.ContainsKey(entry.Path);
                var unchangedPack = original is not BfzArchive && unchanged;
                var bytes = unchangedPack ? StoredPkn.Read(result, entry.Path).Payload
                    : unchanged && result is BfzArchive bfz ? bfz.ReadStored(entry.Path).Record : result.Read(entry.Path);
                if (!expected.TryGetValue(entry.Path, out var hash) || Hash(bytes) != hash)
                    throw new InvalidDataException("Prepared resource verification failed: " + entry.Path);
                if (unchangedPack)
                {
                    var before = StoredPkn.Read(original, entry.Path);
                    var after = StoredPkn.Read(result, entry.Path);
                    if (before.Flags != after.Flags || before.Size != after.Size || before.StoredSize != after.StoredSize || before.Path != after.Path || !before.Md5.AsSpan().SequenceEqual(after.Md5))
                        throw new InvalidDataException("Prepared archive changed an untouched member's metadata: " + entry.Path);
                }
                else if (unchanged && original is BfzArchive oldBfz && result is BfzArchive newBfz)
                {
                    var before = oldBfz.ReadStored(entry.Path).Central.ToArray();
                    var after = newBfz.ReadStored(entry.Path).Central.ToArray();
                    before.AsSpan(42, 4).Clear(); after.AsSpan(42, 4).Clear();
                    if (!before.AsSpan().SequenceEqual(after)) throw new InvalidDataException("Prepared BFZ changed untouched directory metadata: " + entry.Path);
                }
                progress?.Invoke(++completed, total);
            }
            progress?.Invoke(total, total);
        }
        catch { if (created && File.Exists(output)) File.Delete(output); throw; }
    }

    internal static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static string ValidatePath(string value)
    {
        if (string.IsNullOrEmpty(value) || Path.IsPathRooted(value) || value.Contains('\\') || value.Contains(':')
            || value.Split('/').Any(p => p.Length == 0 || p is "." or ".." || p.Any(c => char.IsControl(c) || "<>\"|?*".Contains(c))))
            throw new InvalidDataException("Unsafe resource path: " + value);
        return value;
    }

    internal static string SafeFile(string root, string relative)
    {
        ValidatePath(relative);
        var result = Path.GetFullPath(Path.Combine(root, relative));
        var current = result;
        var boundary = Path.GetFullPath(root);
        while (current.Length >= boundary.Length)
        {
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Resource paths cannot traverse filesystem links.");
            current = Path.GetDirectoryName(current) ?? "";
        }
        return result;
    }
}
