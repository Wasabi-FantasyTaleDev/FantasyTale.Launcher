using System.Security.Cryptography;
using System.Text.Json;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Hooks;

/// <summary>One physical replacement and its contributing plugin edits; an archive remains one diagnostic unit.</summary>
public sealed record ResourceOverrideFile(string Target, long Size, string Sha256, string? OriginalSha256,
    bool CacheReused, bool RestoreOnExit, IReadOnlyList<GameResourceChange> Changes)
{
    /// <summary>Observed content state: original, overridden, modified, or unreadable.</summary>
    public string State { get; init; } = "original";
}

/// <summary>The latest prepared resource selection for a client, without credentials or decoded game content.</summary>
public sealed record ResourceOverrideSnapshot(DateTimeOffset PreparedAt, IReadOnlyList<ResourceOverrideFile> Files);

/// <summary>Persists resource provenance separately from the authoritative recovery journal and inspects it read-only.</summary>
public static class ResourceOverrideDiagnostics
{
    /// <summary>Records verified replacements before deployment; callers hold the client lease.</summary>
    public static void Capture(InstanceStore store, string id, IReadOnlyList<FilePluginPayload> files)
    {
        var client = store.ClientFolder(id);
        var records = files.Select(file => new ResourceOverrideFile(file.Target, file.Size, file.Sha256,
            file.OriginalSha256 ?? HashOrNull(Path.Combine(client, FilePluginResolver.RelativePath(file.Target))),
            file.CacheReused, file.RestoreOnExit, file.Changes)).ToArray();
        var path = Receipt(store, id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pending = path + ".pending";
        InstanceStore.RejectLinks(pending);
        using (var output = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(output, new ResourceOverrideSnapshot(DateTimeOffset.UtcNow, records));
            output.Flush(true);
        }
        File.Move(pending, path, overwrite: true);
    }

    /// <summary>Lists the last preparation and observes current hashes without composing, deploying or recovering anything.</summary>
    public static ResourceOverrideSnapshot? Read(InstanceStore store, string id)
    {
        var client = store.ClientFolder(id);
        var path = Receipt(store, id);
        if (!File.Exists(path)) return null;
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var snapshot = JsonSerializer.Deserialize<ResourceOverrideSnapshot>(input)
            ?? throw new InvalidDataException("Invalid resource override diagnostics.");
        return snapshot with { Files = snapshot.Files.Select(file =>
        {
            var relative = FilePluginResolver.RelativePath(file.Target);
            string state;
            try
            {
                var current = HashOrNull(Path.Combine(client, relative));
                state = current == file.Sha256 ? "overridden" : current == file.OriginalSha256 ? "original" : "modified";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { state = "unreadable"; }
            return file with { State = state };
        }).ToArray() };
    }

    private static string Receipt(InstanceStore store, string id)
    {
        var path = Path.Combine(store.ResourceCacheFolder(id), "overrides.json");
        InstanceStore.RejectLinks(path);
        return path;
    }

    private static string? HashOrNull(string path)
    {
        InstanceStore.RejectLinks(path);
        if (!File.Exists(path)) return null;
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }
}
