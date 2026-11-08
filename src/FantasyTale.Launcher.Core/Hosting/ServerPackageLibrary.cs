using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Hosting;

/// <summary>A verified installed server release, independent of hosting profiles and player saves.</summary>
/// <param name="Id">Content identity scoped to the source endpoint and package.</param>
/// <param name="SourceId">Configured source that supplied the package.</param>
/// <param name="Package">Pinned catalog metadata, including compatible client builds.</param>
/// <param name="Directory">Owned package directory available to local hosting.</param>
public sealed record InstalledServerPackage(string Id, string SourceId, StorePackage Package, string Directory);

/// <summary>Stores source-bound server releases separately and leases the same package lock as local hosting.</summary>
public sealed class ServerPackageLibrary
{
    private sealed record Record(int Schema, string SourceId, string SourceIdentity, StorePackage Package, byte[] Manifest);
    private readonly string _root;
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true, MaxDepth = 16
    };

    /// <summary>Opens the owned package root; downloads do not share this directory with clients or saves.</summary>
    public ServerPackageLibrary(string root)
    {
        _root = Path.GetFullPath(root); InstanceStore.RejectLinks(_root); Directory.CreateDirectory(_root);
    }

    internal static string Identity(string sourceId, string sourceIdentity, StorePackage package) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(sourceId + "\n" + sourceIdentity + "\n" + package.Id + "\n" + package.ManifestSha256))).ToLowerInvariant();

    internal string Folder(string sourceId, string sourceIdentity, StorePackage package) => ClientInstallTransaction.Safe(_root, Identity(sourceId, sourceIdentity, package));

    internal FileStream Acquire(string sourceId, string sourceIdentity, StorePackage package)
    {
        using var allocation = new FileStream(ClientInstallTransaction.Safe(_root, ".library.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var folder = Folder(sourceId, sourceIdentity, package);
        Directory.CreateDirectory(folder);
        return new FileStream(ClientInstallTransaction.Safe(folder, ".fantasytale-host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    internal void RecordInstalled(string sourceId, string sourceIdentity, StorePackage package, byte[] manifest)
    {
        if (package.Kind != "server") throw new InvalidDataException("Not a server package.");
        _ = ClientRelease.Parse(package, manifest);
        var folder = Folder(sourceId, sourceIdentity, package);
        var receipt = ClientInstallTransaction.ReadReceipt(folder);
        if (receipt != new ClientInstallReceipt(sourceId, package.Id, package.Version, package.ManifestSha256, sourceIdentity, package.Revision))
            throw new InvalidDataException("Server package has no matching committed installation.");
        var path = ClientInstallTransaction.Safe(ClientInstallTransaction.Workspace(folder), "server-package.json");
        var temporary = ClientInstallTransaction.Safe(ClientInstallTransaction.Workspace(folder), "server-package.tmp");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Record(1, sourceId, sourceIdentity, package, manifest), Json);
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
        File.Move(temporary, path, true);
    }

    /// <summary>Lists committed package records; incomplete downloads and interrupted transactions cannot be selected for hosting.</summary>
    public IReadOnlyList<InstalledServerPackage> List()
    {
        var folders = Directory.EnumerateDirectories(_root);
        var result = new List<InstalledServerPackage>();
        foreach (var folder in folders)
        {
            InstanceStore.RejectLinks(folder);
            var workspace = ClientInstallTransaction.Workspace(folder);
            var path = ClientInstallTransaction.Safe(workspace, "server-package.json");
            if (!File.Exists(path) || File.Exists(ClientInstallTransaction.Safe(workspace, "journal.json"))) continue;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var record = JsonSerializer.Deserialize<Record>(file, Json) ?? throw new InvalidDataException("Invalid server package record.");
            if (record.Schema != 1 || record.Package.Kind != "server" || record.SourceIdentity.Length != 64 || !record.SourceIdentity.All(char.IsAsciiHexDigit))
                throw new InvalidDataException("Invalid server package identity.");
            _ = ClientRelease.Parse(record.Package, record.Manifest);
            var id = Identity(record.SourceId, record.SourceIdentity, record.Package);
            if (Path.GetFileName(folder) != id || ClientInstallTransaction.ReadReceipt(folder) !=
                new ClientInstallReceipt(record.SourceId, record.Package.Id, record.Package.Version, record.Package.ManifestSha256, record.SourceIdentity, record.Package.Revision))
                throw new InvalidDataException("Server package record does not match its installation.");
            result.Add(new(id, record.SourceId, record.Package, folder));
        }
        return result;
    }
}
