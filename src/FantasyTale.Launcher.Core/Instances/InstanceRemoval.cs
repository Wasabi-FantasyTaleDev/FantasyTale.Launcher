using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Instances;

/// <summary>Reports verified game-file cleanup and modified files retained for the user.</summary>
/// <param name="DeletedFiles">Number of installation-listed files whose bytes matched before deletion.</param>
/// <param name="PreservedFiles">Number of installation-listed files retained because their contents changed.</param>
public sealed record ClientRemovalResult(int DeletedFiles, int PreservedFiles);

public sealed partial class InstanceStore
{
    private const string RemovedDefinition = "removed-instance.toml";

    /// <summary>Removes a stopped client's registration and optionally its verified installed files, never recursively deleting a client directory.</summary>
    public async Task<ClientRemovalResult> RemoveAsync(string id, bool deleteFiles, GameDefinition game, CancellationToken cancellationToken = default)
    {
        var registryLock = Path.Combine(_root, ".locks", ".installation-paths.lock");
        RejectLinks(registryLock);
        using var registration = OpenLease(registryLock);
        using var lease = Acquire(id);
        var instance = Load(id);
        if (game.Id != instance.Game) throw new InvalidOperationException("The removal game does not match the client.");
        var build = game.Builds.Single(item => item.Id == instance.Build);
        var client = ClientFolder(id);
        var file = Path.Combine(Folder(id), "instance.toml");
        var removed = Path.Combine(Folder(id), RemovedDefinition);
        RejectLinks(removed);
        if (Path.Exists(removed)) throw new IOException("client_remove_pending");
        try { InstanceRecovery.EnsureStopped(client, build); }
        catch (IOException error) { throw new IOException("client_remove_running", error); }
        if (InstanceRecovery.Required(client) || Path.Exists(Path.Combine(client, ClientInstallTransaction.DirectoryName, "journal.json")))
            throw new IOException("client_remove_pending");
        var deleted = 0; var preserved = 0;
        if (deleteFiles)
        {
            ValidateRemovalPath(id, client);
            foreach (var other in List().Where(item => item.Id != id))
            {
                var path = ClientFolder(other.Id);
                if (path.Equals(client, StringComparison.OrdinalIgnoreCase) || IsWithin(path, client) || IsWithin(client, path))
                    throw new IOException("client_installation_overlap");
            }
            if (Directory.Exists(client) && Directory.EnumerateFileSystemEntries(client).Any())
            {
                if (ClientInstallTransaction.ReadReceipt(client) is null) throw new IOException("client_remove_inventory");
                var files = new ClientFileCache(client).InstalledFiles();
                if (files.Count == 0) throw new IOException("client_remove_inventory");
                // Validate the complete list before the first mutation, including entries no longer present.
                foreach (var entry in files)
                {
                    var path = ClientInstallTransaction.Safe(client, ClientRelease.ValidatePath(entry.Path));
                    if (entry.Path.Split('/')[0].StartsWith(".fantasytale-", StringComparison.OrdinalIgnoreCase)
                        || entry.Path.Equals("session.toml", StringComparison.OrdinalIgnoreCase) || Directory.Exists(path))
                        throw new IOException("client_remove_inventory");
                }
                foreach (var entry in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = ClientInstallTransaction.Safe(client, entry.Path);
                    if (!File.Exists(path)) continue;
                    // The same exclusive Windows handle verifies and deletes the file; a replacement path cannot be deleted instead.
                    if (await VerifiedFileRemoval.TryDeleteAsync(path, entry.Size, entry.Sha256, cancellationToken).ConfigureAwait(false)) deleted++;
                    else preserved++;
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(file, removed);
        return new(deleted, preserved);
    }

    private void ValidateRemovalPath(string id, string client)
    {
        RejectLinks(client);
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(client));
        if (path.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path)!), StringComparison.OrdinalIgnoreCase)
            || path.Equals(_root, StringComparison.OrdinalIgnoreCase) || IsWithin(path, _root)
            || IsWithin(_root, path) && !path.Equals(ChildFolder(id, "client"), StringComparison.OrdinalIgnoreCase))
            throw new IOException("client_remove_unsafe");
        var special = Enum.GetValues<Environment.SpecialFolder>().Select(Environment.GetFolderPath)
            .Append(AppContext.BaseDirectory).Where(value => !string.IsNullOrWhiteSpace(value));
        foreach (var directory in special)
        {
            var protectedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (path.Equals(protectedPath, StringComparison.OrdinalIgnoreCase) || IsWithin(path, protectedPath))
                throw new IOException("client_remove_unsafe");
        }
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows) && IsWithin(windows, path)) throw new IOException("client_remove_unsafe");
    }

    private InstanceDefinition? RetiredInstallation(string client)
    {
        // Only the exact managed client child of a retired registration may bypass the metadata-root restriction.
        if (!IsWithin(_root, client) || !Path.GetFileName(client).Equals("client", StringComparison.OrdinalIgnoreCase)) return null;
        var parent = Path.GetDirectoryName(client)!;
        if (!Path.GetDirectoryName(parent)!.Equals(_root, StringComparison.OrdinalIgnoreCase)) return null;
        var marker = Path.Combine(parent, RemovedDefinition);
        RejectLinks(marker);
        if (!File.Exists(marker) || File.Exists(Path.Combine(parent, "instance.toml"))) return null;
        var retired = InstanceFile.Load(marker);
        if (retired.Id != Path.GetFileName(parent) || retired.InstallationPath is not null) throw new IOException("client_remove_inventory");
        return retired;
    }

    private FileStream AcquireRetired(InstanceDefinition retired)
    {
        var client = ChildFolder(retired.Id, "client");
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(client).ToUpperInvariant())));
        var path = Path.Combine(_root, ".locks", "installation-" + key + ".lock");
        RejectLinks(path);
        return OpenLease(path);
    }
}
