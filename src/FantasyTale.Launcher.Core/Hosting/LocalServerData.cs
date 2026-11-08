using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Hosting;

internal sealed class LocalServerData
{
    private sealed record Identity(int Schema, string Game, string Build);
    internal sealed record Lease(string DirectoryPath, FileStream Handle) : IDisposable
    {
        public void Dispose() => Handle.Dispose();
    }
    private readonly string _root;
    internal LocalServerData(string root) { _root = Path.GetFullPath(root); InstanceStore.RejectLinks(_root); Directory.CreateDirectory(_root); }

    internal static bool Overlap(string a, string b) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)).Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase) || InstanceStore.IsWithin(a, b) || InstanceStore.IsWithin(b, a);

    internal void ValidatePackage(string package)
    {
        if (Overlap(package, _root)) throw new InvalidOperationException("The server package must be separate from saved player data.");
    }

    internal Lease Acquire(LocalServerProfile profile)
    {
        InstanceFile.ValidateId("hosting", profile.Id);
        var registryLock = Path.Combine(_root, ".data.lock"); InstanceStore.RejectLinks(registryLock);
        using var registry = new FileStream(registryLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var folder = Path.Combine(_root, profile.Id); InstanceStore.RejectLinks(folder);
        Directory.CreateDirectory(folder);
        var lockPath = Path.Combine(folder, ".host.lock"); InstanceStore.RejectLinks(lockPath);
        var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var identityPath = Path.Combine(folder, "identity.json"); InstanceStore.RejectLinks(identityPath);
            var identity = new Identity(1, profile.Game, profile.Build);
            if (File.Exists(identityPath))
            {
                using var input = new FileStream(identityPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (input.Length > 2048 || JsonSerializer.Deserialize<Identity>(input) != identity)
                    throw new InvalidDataException("This local save belongs to another game or client build. Create a separate hosting profile.");
            }
            else
            {
                // Unknown files may be an existing save; missing ownership metadata never authorizes adopting it.
                if (Directory.EnumerateFileSystemEntries(folder).Any(p => Path.GetFileName(p) is not (".host.lock" or "identity.saving")))
                    throw new InvalidDataException("Local save ownership metadata is missing. Existing files were preserved.");
                var staged = Path.Combine(folder, "identity.saving"); InstanceStore.RejectLinks(staged);
                using (var output = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None))
                { output.Write(JsonSerializer.SerializeToUtf8Bytes(identity)); output.Flush(true); }
                File.Move(staged, identityPath);
            }
            return new(folder, lease);
        }
        catch { lease.Dispose(); throw; }
    }
}
