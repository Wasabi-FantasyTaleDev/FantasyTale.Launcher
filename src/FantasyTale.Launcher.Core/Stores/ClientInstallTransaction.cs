using System.Security.Cryptography;
using System.Text.Json;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Downloads;

namespace FantasyTale.Launcher.Core.Stores;

internal sealed record ClientInstallReceipt(string SourceId, string PackageId, string Version, string ManifestSha256, string? SourceIdentity = null, long Revision = 0);

internal static class ClientInstallTransaction
{
    internal const string DirectoryName = ".fantasytale-install";
    private sealed record Entry(string Path, long Size, string Hash, long? OriginalSize, string? OriginalHash, bool Remove = false);
    private sealed record OwnedFile(string Path, long Size, string Hash);
    private sealed record Ownership(ClientInstallReceipt Receipt, OwnedFile[] Files);
    private sealed record Journal(int Schema, bool Committed, Entry[] Files, ClientInstallReceipt? Receipt = null, Ownership? Ownership = null);

    internal static string Workspace(string client) => Safe(client, DirectoryName);
    internal static string Staged(string client, string hash) => Safe(Workspace(client), hash + ".verified");

    internal static string Safe(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!InstanceStore.IsWithin(root, path)) throw new IOException("Installation path escapes its directory.");
        InstanceStore.RejectLinks(path); return path;
    }

    internal static async Task ApplyAsync(string client, IReadOnlyList<ClientReleaseFile> files, Action<int> progress, CancellationToken ct, ClientInstallReceipt? receipt = null, ClientFileCache? cache = null, bool deferCommit = false, bool pruneOwned = false, ClientInstallReceipt? sourceTransitionFrom = null)
    {
        if (sourceTransitionFrom is not null)
        {
            if (!pruneOwned) throw new ArgumentException("Source transitions require an owned-file update.");
            ValidateReceipt(sourceTransitionFrom);
            if (receipt is null || ReadReceipt(client) != sourceTransitionFrom || sourceTransitionFrom.SourceId != receipt.SourceId ||
                sourceTransitionFrom.PackageId != receipt.PackageId)
                throw new IOException("Installed receipt does not match the authorized source transition.");
        }
        var workspace = Workspace(client); Directory.CreateDirectory(workspace);
        if (File.Exists(Safe(workspace, "journal.json"))) throw new IOException("An interrupted installation requires recovery first.");
        var entries = new List<Entry>();
        Ownership? ownership = null;
        if (pruneOwned)
        {
            if (receipt?.SourceIdentity is null) throw new ArgumentException("Owned-file updates require a source-bound installation receipt.");
            ownership = new(receipt, files.Select(f => new OwnedFile(f.Path, f.Size, f.Sha256)).ToArray());
            ValidateOwnership(ownership);
            var previous = ReadOwnership(client);
            if (previous is not null)
            {
                if (ReadReceipt(client) != previous.Receipt || previous.Receipt.SourceId != receipt.SourceId ||
                    previous.Receipt.PackageId != receipt.PackageId ||
                    previous.Receipt.SourceIdentity != receipt.SourceIdentity && previous.Receipt != sourceTransitionFrom)
                    throw new IOException("Installed file ownership does not match this update source.");
                var retained = files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var old in previous.Files.Where(f => !retained.Contains(f.Path)))
                {
                    if (files.Any(f => f.Path.StartsWith(old.Path + "/", StringComparison.OrdinalIgnoreCase) || old.Path.StartsWith(f.Path + "/", StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("An update cannot change an owned file into a directory or a directory into a file.");
                    var target = Safe(client, old.Path);
                    if (Directory.Exists(target)) throw new IOException("An obsolete owned file was replaced by a directory.");
                    if (!File.Exists(target)) continue;
                    if (!await HttpDownloadEngine.MatchesAsync(target, old.Size, old.Hash, ct).ConfigureAwait(false))
                        throw new IOException("An obsolete owned file changed externally; the update was not installed.");
                    entries.Add(new(old.Path, old.Size, old.Hash, old.Size, old.Hash, Remove: true));
                }
            }
            // Vite fingerprints frontend bundles, so an older release can leave
            // stale files when it was installed before ownership metadata existed.
            // Only the launcher-owned wwwroot/assets tree is considered here.
            var retainedAssets = files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var knownEntries = entries.Select(entry => entry.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var assetRoot = Safe(client, "wwwroot/assets");
            if (Directory.Exists(assetRoot))
            {
                foreach (var path in FrontendAssets(assetRoot, ct))
                {
                    InstanceStore.RejectLinks(path);
                    var relative = ClientRelease.ValidatePath(Path.GetRelativePath(client, path).Replace(Path.DirectorySeparatorChar, '/'));
                    if (retainedAssets.Contains(relative) || !knownEntries.Add(relative)) continue;
                    var info = new FileInfo(path);
                    await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
                    entries.Add(new(relative, info.Length, hash, info.Length, hash, Remove: true));
                }
            }
        }
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var target = Safe(client, ClientRelease.ValidatePath(file.Path));
            if (cache is not null ? await cache.MatchesAsync(file, ct).ConfigureAwait(false) : await HttpDownloadEngine.MatchesAsync(target, file.Size, file.Sha256, ct).ConfigureAwait(false)) continue;
            if (!await HttpDownloadEngine.MatchesAsync(Staged(client, file.Sha256), file.Size, file.Sha256, ct).ConfigureAwait(false))
                throw new InvalidDataException("Staged installation file failed verification.");
            long? oldSize = null; string? oldHash = null;
            if (File.Exists(target))
            {
                await using var old = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
                oldSize = old.Length;
                oldHash = Convert.ToHexString(await SHA256.HashDataAsync(old, ct).ConfigureAwait(false));
            }
            entries.Add(new(file.Path, file.Size, file.Sha256, oldSize, oldHash));
        }
        ct.ThrowIfCancellationRequested();
        if (receipt is not null) ValidateReceipt(receipt);
        var journal = new Journal(1, false, entries.ToArray(), receipt, ownership); Save(workspace, journal);
        try
        {
            // Cancellation stops at the transaction boundary; rollback must finish before the lease is released.
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i]; var target = Safe(client, entry.Path); var backup = Safe(workspace, $"original-{i}");
                if (entry.OriginalHash is not null)
                {
                    if (!await HttpDownloadEngine.MatchesAsync(target, entry.OriginalSize!.Value, entry.OriginalHash, CancellationToken.None).ConfigureAwait(false))
                        throw new IOException("An installed file changed during update preparation.");
                    File.Move(target, backup, overwrite: false);
                }
                if (entry.Remove) { progress((int)((long)(i + 1) * 100 / entries.Count)); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); InstanceStore.RejectLinks(target);
                // Copy permits multiple manifest destinations to share one verified content object.
                var staged = Staged(client, entry.Hash);
                var next = Safe(workspace, $"next-{i}");
                File.Copy(staged, next, overwrite: false);
                using (var durable = new FileStream(next, FileMode.Open, FileAccess.Write, FileShare.None)) durable.Flush(true);
                File.Move(next, target, overwrite: false);
                progress((int)((long)(i + 1) * 100 / entries.Count));
            }
            if (!deferCommit) Save(workspace, journal with { Committed = true });
        }
        catch
        {
            await RecoverAsync(client).ConfigureAwait(false);
            throw;
        }
        if (!deferCommit) await RecoverAsync(client).ConfigureAwait(false);
        progress(100);
    }

    private static IEnumerable<string> FrontendAssets(string root, CancellationToken ct)
    {
        var directories = new Stack<string>(); directories.Push(root);
        while (directories.TryPop(out var directory))
        {
            InstanceStore.RejectLinks(directory);
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                ct.ThrowIfCancellationRequested();
                // Validate directories before descending so junctions cannot expand the scan outside the installation.
                InstanceStore.RejectLinks(path);
                if (Directory.Exists(path)) { directories.Push(path); continue; }
                yield return path;
            }
        }
    }

    internal static async Task CommitAsync(string client, Action? afterCommit = null, ClientFileCache? cache = null)
    {
        var workspace = Workspace(client); var path = Safe(workspace, "journal.json");
        var journal = await ReadJournalAsync(path).ConfigureAwait(false);
        foreach (var entry in journal.Files)
            if (entry.Remove ? Path.Exists(Safe(client, entry.Path)) : !await HttpDownloadEngine.MatchesAsync(Safe(client, ClientRelease.ValidatePath(entry.Path)), entry.Size, entry.Hash, CancellationToken.None).ConfigureAwait(false))
                throw new IOException("Installed update changed before its health check completed; recovery files were retained.");
        if (journal.Ownership is not null)
            foreach (var file in journal.Ownership.Files)
                if (cache is not null
                    ? !await cache.MatchesAsync(new ClientReleaseFile(file.Path, file.Path, file.Size, file.Hash), CancellationToken.None).ConfigureAwait(false)
                    : !await HttpDownloadEngine.MatchesAsync(Safe(client, file.Path), file.Size, file.Hash, CancellationToken.None).ConfigureAwait(false))
                    throw new IOException("An owned installation file changed before commit; recovery files were retained.");
        Save(workspace, journal with { Committed = true });
        afterCommit?.Invoke();
        await RecoverAsync(client).ConfigureAwait(false);
    }

    internal static async Task RecoverAsync(string client)
    {
        var workspace = Workspace(client); var path = Safe(workspace, "journal.json");
        if (!File.Exists(path)) return;
        var journal = await ReadJournalAsync(path).ConfigureAwait(false);
        for (var i = journal.Files.Length - 1; i >= 0; i--)
        {
            var entry = journal.Files[i]; var target = Safe(client, entry.Path); var backup = Safe(workspace, $"original-{i}");
            if (!journal.Committed)
            {
                if (entry.OriginalHash is not null && !File.Exists(backup))
                {
                    if (!await HttpDownloadEngine.MatchesAsync(target, entry.OriginalSize!.Value, entry.OriginalHash, CancellationToken.None).ConfigureAwait(false))
                        throw new IOException("Original installation file is missing or changed; recovery files were retained.");
                }
                else
                {
                    if (entry.OriginalHash is not null && !await HttpDownloadEngine.MatchesAsync(backup, entry.OriginalSize!.Value, entry.OriginalHash, CancellationToken.None).ConfigureAwait(false))
                        throw new IOException("Original backup failed verification; recovery files were retained.");
                    if (File.Exists(target))
                    {
                        if (entry.Remove || !await HttpDownloadEngine.MatchesAsync(target, entry.Size, entry.Hash, CancellationToken.None).ConfigureAwait(false))
                            throw new IOException("An installed file changed externally; recovery files were retained.");
                        File.Delete(target);
                    }
                    if (Directory.Exists(target)) throw new IOException("An installation target became a directory; recovery files were retained.");
                    if (entry.OriginalHash is not null) File.Move(backup, target, overwrite: false);
                }
            }
            else File.Delete(backup);
            File.Delete(Safe(workspace, $"next-{i}"));
        }
        // Keep verified payloads available for rollback retries; a durable commit
        // consumes only this journal's objects, including shared destinations.
        if (journal.Committed)
            foreach (var hash in journal.Files.Where(entry => !entry.Remove).Select(entry => entry.Hash).Distinct(StringComparer.Ordinal))
                File.Delete(Staged(client, hash));
        if (journal.Committed && journal.Receipt is not null) SaveJson(workspace, "receipt", journal.Receipt);
        if (journal.Committed && journal.Ownership is not null) SaveJson(workspace, "owned-files", journal.Ownership);
        File.Delete(path);
    }

    private static async Task<Journal> ReadJournalAsync(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var journal = await JsonSerializer.DeserializeAsync<Journal>(input).ConfigureAwait(false) ?? throw new IOException("Invalid installation journal.");
        input.Close();
        if (journal.Schema != 1 || journal.Files is null) throw new IOException("Invalid installation journal.");
        if (journal.Receipt is not null) ValidateReceipt(journal.Receipt);
        if (journal.Ownership is not null)
        {
            ValidateOwnership(journal.Ownership);
            if (journal.Ownership.Receipt != journal.Receipt) throw new IOException("Installation ownership and receipt disagree.");
        }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in journal.Files)
        {
            if (entry is null || !paths.Add(ClientRelease.ValidatePath(entry.Path)) || !ValidHash(entry.Hash) || entry.Size < 0 ||
                (entry.OriginalHash is null) != (entry.OriginalSize is null) || entry.Remove && entry.OriginalHash is null || entry.OriginalHash is not null && (!ValidHash(entry.OriginalHash) || entry.OriginalSize < 0))
                throw new IOException("Invalid installation recovery entry.");
        }
        return journal;
    }

    private static Ownership? ReadOwnership(string client)
    {
        var path = Safe(Workspace(client), "owned-files.json");
        if (!File.Exists(path)) return null;
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var ownership = JsonSerializer.Deserialize<Ownership>(input) ?? throw new IOException("Invalid installed file ownership.");
        ValidateOwnership(ownership); return ownership;
    }

    private static void ValidateOwnership(Ownership ownership)
    {
        if (ownership.Receipt is null || ownership.Files is null) throw new IOException("Invalid installed file ownership.");
        ValidateReceipt(ownership.Receipt);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ownership.Files)
            if (file is null || !paths.Add(ClientRelease.ValidatePath(file.Path)) || !ValidHash(file.Hash) || file.Size < 0)
                throw new IOException("Invalid owned installation file.");
    }

    internal static ClientInstallReceipt? ReadReceipt(string client)
    {
        var path = Safe(Workspace(client), "receipt.json");
        if (!File.Exists(path)) return null;
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var receipt = JsonSerializer.Deserialize<ClientInstallReceipt>(input) ?? throw new IOException("Invalid installation receipt.");
        ValidateReceipt(receipt); return receipt;
    }

    internal static void CopyReceipt(string source, string destination)
    {
        var receipt = ReadReceipt(source);
        if (receipt is null) return;
        var workspace = Workspace(destination); Directory.CreateDirectory(workspace);
        SaveJson(workspace, "receipt", receipt);
    }

    private static void ValidateReceipt(ClientInstallReceipt receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt.SourceId) || receipt.SourceId.Length > 128 || string.IsNullOrWhiteSpace(receipt.PackageId) || receipt.PackageId.Length > 128 ||
            receipt.Version is null || receipt.Version.Length > 64 || receipt.Version.Any(char.IsControl) || !ValidHash(receipt.ManifestSha256) ||
            receipt.SourceIdentity is not null && !ValidHash(receipt.SourceIdentity) || receipt.Revision < 0)
            throw new IOException("Invalid installation receipt.");
    }

    private static bool ValidHash(string value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static void Save(string workspace, Journal journal) => SaveJson(workspace, "journal", journal);
    private static void SaveJson<T>(string workspace, string name, T value)
    {
        var temp = Safe(workspace, name + ".tmp");
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(file, value); file.Flush(true); }
        File.Move(temp, Safe(workspace, name + ".json"), overwrite: true);
    }
}
