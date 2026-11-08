using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Stores;

internal sealed record SavedDownload(int Schema, string SourceId, string SourceIdentity, StorePackage Package,
    byte[] Manifest, ClientDownloadTask Snapshot, bool ResumeOnRestart, DateTimeOffset CreatedAt, DateTimeOffset QueuedAt, ClientInstallReceipt? PreviousReceipt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LibraryOnly { get; init; }
    [JsonIgnore]
    public string? RecommendationSelection { get; init; }
    [JsonIgnore]
    public bool ServerRecommended { get; init; }
    [JsonIgnore]
    public string? UpgradeSelection { get; init; }
    [JsonIgnore]
    public IReadOnlyList<string>? ActivationTargets { get; init; }
    [JsonIgnore]
    public IReadOnlyDictionary<string, string>? ActivationSelections { get; init; }
}

// Persist the original manifest, never resolved URLs or a separately trusted file table.
internal sealed class DownloadTaskStore
{
    private readonly string _root;
    private sealed record ActivationIntent(int Schema, string Identity, string Selection);
    private sealed record TargetIntent(int Schema, string Identity, string[] Targets, Dictionary<string, string>? Selections = null);
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true, MaxDepth = 16
    };

    internal DownloadTaskStore(string root)
    { _root = Path.GetFullPath(root); InstanceStore.RejectLinks(_root); Directory.CreateDirectory(_root); }

    internal IReadOnlyList<SavedDownload> Load()
    {
        var paths = Directory.EnumerateFiles(_root, "*.json");
        var result = new List<SavedDownload>();
        foreach (var path in paths)
        {
            InstanceStore.RejectLinks(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var item = JsonSerializer.Deserialize<SavedDownload>(file, Json) ?? throw new InvalidDataException("Invalid saved download.");
            if (Path.GetFileNameWithoutExtension(path) != item.Snapshot.Id) throw new InvalidDataException("Download identity does not match its file.");
            var intentPath = IntentPath(item.Snapshot.Id);
            if (File.Exists(intentPath))
            {
                using var intentFile = new FileStream(intentPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var intent = JsonSerializer.Deserialize<ActivationIntent>(intentFile, Json) ?? throw new InvalidDataException("Invalid recommendation activation intent.");
                if (intent.Schema != 1 || intent.Identity != IntentIdentity(item)) throw new InvalidDataException("Recommendation activation intent belongs to another task.");
                item = item with { RecommendationSelection = intent.Selection };
            }
            var serverIntentPath = ServerIntentPath(item.Snapshot.Id);
            if (File.Exists(serverIntentPath))
            {
                using var serverIntentFile = new FileStream(serverIntentPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var intent = JsonSerializer.Deserialize<ActivationIntent>(serverIntentFile, Json) ?? throw new InvalidDataException("Invalid server recommendation intent.");
                if (intent.Schema != 1 || intent.Identity != IntentIdentity(item) || intent.Selection != item.RecommendationSelection)
                    throw new InvalidDataException("Server recommendation intent belongs to another activation.");
                item = item with { ServerRecommended = true };
            }
            Validate(item);
            var targetPath = TargetPath(item.Snapshot.Id);
            if (File.Exists(targetPath))
            {
                using var targetFile = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var intent = JsonSerializer.Deserialize<TargetIntent>(targetFile, Json) ?? throw new InvalidDataException("Invalid plugin activation targets.");
                if (intent.Schema is not (1 or 2) || intent.Identity != IntentIdentity(item) || intent.Targets.Length < 1
                    || intent.Schema == 2 && intent.Selections is null)
                    throw new InvalidDataException("Plugin activation targets belong to another task.");
                item = item with { ActivationTargets = intent.Targets, ActivationSelections = intent.Selections };
                Validate(item);
            }
            var upgradePath = UpgradePath(item.Snapshot.Id);
            if (File.Exists(upgradePath))
            {
                using var input = new FileStream(upgradePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var intent = JsonSerializer.Deserialize<ActivationIntent>(input, Json) ?? throw new InvalidDataException("Invalid plugin activation intent.");
                if (intent.Schema != 1 || intent.Identity != IntentIdentity(item)) throw new InvalidDataException("Plugin activation intent belongs to another task.");
                item = item with { UpgradeSelection = intent.Selection };
                Validate(item);
            }
            result.Add(item);
        }
        if (result.Where(d => d.Snapshot.Stage is not ("completed" or "canceled")).GroupBy(d => (d.Package.Kind == "server", d.Snapshot.Target.InstanceId))
            .Any(g => g.Count() > 1 && (g.Any(d => d.Package.Kind != "plugin") || g.Select(d => d.Package.Id).Distinct(StringComparer.Ordinal).Count() != g.Count())))
            throw new InvalidDataException("Multiple saved tasks target the same client.");
        return result.OrderBy(d => d.CreatedAt).ThenBy(d => d.Snapshot.Id, StringComparer.Ordinal).ToArray();
    }

    internal void Save(SavedDownload item)
    {
        Validate(item);
        var path = FilePath(item.Snapshot.Id); var temporary = path + ".tmp";
        InstanceStore.RejectLinks(temporary);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(item, Json);
        // Activation intent is bound to immutable task identity, not mutable progress.
        var intentPath = IntentPath(item.Snapshot.Id);
        if (item.RecommendationSelection is { } selection)
        {
            var intentTemporary = intentPath + ".tmp"; InstanceStore.RejectLinks(intentTemporary);
            try
            {
                using (var intent = new FileStream(intentTemporary, FileMode.Create, FileAccess.Write, FileShare.None))
                { intent.Write(JsonSerializer.SerializeToUtf8Bytes(new ActivationIntent(1, IntentIdentity(item), selection), Json)); intent.Flush(true); }
                File.Move(intentTemporary, intentPath, true);
            }
            finally { File.Delete(intentTemporary); }
        }
        else File.Delete(intentPath);
        var serverIntentPath = ServerIntentPath(item.Snapshot.Id);
        if (item.ServerRecommended)
        {
            var next = serverIntentPath + ".tmp"; InstanceStore.RejectLinks(next);
            try
            {
                using (var file = new FileStream(next, FileMode.Create, FileAccess.Write, FileShare.None))
                { file.Write(JsonSerializer.SerializeToUtf8Bytes(new ActivationIntent(1, IntentIdentity(item), item.RecommendationSelection!), Json)); file.Flush(true); }
                File.Move(next, serverIntentPath, true);
            }
            finally { File.Delete(next); }
        }
        else File.Delete(serverIntentPath);
        var upgradePath = UpgradePath(item.Snapshot.Id);
        if (item.UpgradeSelection is { } upgrade)
        {
            var next = upgradePath + ".tmp"; InstanceStore.RejectLinks(next);
            try
            {
                using (var file = new FileStream(next, FileMode.Create, FileAccess.Write, FileShare.None))
                { file.Write(JsonSerializer.SerializeToUtf8Bytes(new ActivationIntent(1, IntentIdentity(item), upgrade), Json)); file.Flush(true); }
                File.Move(next, upgradePath, true);
            }
            finally { File.Delete(next); }
        }
        else File.Delete(upgradePath);
        var targetPath = TargetPath(item.Snapshot.Id);
        if (item.ActivationTargets is { Count: > 0 } targets)
        {
            var next = targetPath + ".tmp"; InstanceStore.RejectLinks(next);
            try
            {
                using (var file = new FileStream(next, FileMode.Create, FileAccess.Write, FileShare.None))
                { file.Write(JsonSerializer.SerializeToUtf8Bytes(new TargetIntent(item.ActivationSelections is null ? 1 : 2, IntentIdentity(item), targets.ToArray(),
                    item.ActivationSelections?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)), Json)); file.Flush(true); }
                File.Move(next, targetPath, true);
            }
            finally { File.Delete(next); }
        }
        else File.Delete(targetPath);
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { file.Write(bytes); file.Flush(true); }
        File.Move(temporary, path, true);
    }

    internal void Delete(string id) { File.Delete(FilePath(id)); File.Delete(IntentPath(id)); File.Delete(ServerIntentPath(id)); File.Delete(UpgradePath(id)); File.Delete(TargetPath(id)); }
    private string ServerIntentPath(string id)
    {
        var path = Path.ChangeExtension(FilePath(id), ".server-recommendation"); InstanceStore.RejectLinks(path); return path;
    }
    private string UpgradePath(string id)
    {
        var path = Path.ChangeExtension(FilePath(id), ".plugin-selection"); InstanceStore.RejectLinks(path); return path;
    }
    private string TargetPath(string id)
    {
        var path = Path.ChangeExtension(FilePath(id), ".plugin-targets"); InstanceStore.RejectLinks(path); return path;
    }
    private string IntentPath(string id)
    {
        var path = Path.ChangeExtension(FilePath(id), ".recommendation"); InstanceStore.RejectLinks(path); return path;
    }
    private static string IntentIdentity(SavedDownload item) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        item.Snapshot.Id + "\n" + item.SourceIdentity + "\n" + item.Package.ManifestSha256 + "\n" + item.Snapshot.Target.InstanceId + "\n"
        + item.Snapshot.Target.BuildId + "\n" + item.Package.Id + "\n" + item.Package.Version))).ToLowerInvariant();
    private string FilePath(string id)
    {
        if (id.Length != 32 || !Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid download identity.");
        var path = Path.Combine(_root, id + ".json"); InstanceStore.RejectLinks(path); return path;
    }

    private static void Validate(SavedDownload d)
    {
        InstanceFile.ValidateId("download", d.SourceId);
        InstanceFile.ValidateId("download", d.Snapshot.Target.InstanceId);
        var s = d.Snapshot; var p = d.Package;
        if (d.LibraryOnly && (p.Kind != "plugin" || s.Target.InstanceId != "plugin-library" || d.RecommendationSelection is not null || d.UpgradeSelection is not null))
            throw new InvalidDataException("Invalid plugin library download.");
        if (d.ServerRecommended && d.RecommendationSelection is null) throw new InvalidDataException("Missing server recommendation activation intent.");
        if (d.RecommendationSelection is { } stamp && (p.Kind != "plugin" || stamp.Length != 64 || !stamp.All(char.IsAsciiHexDigit)))
            throw new InvalidDataException("Invalid recommendation activation intent.");
        if (d.UpgradeSelection is { } upgrade && (p.Kind != "plugin" || upgrade.Length != 64 || !upgrade.All(char.IsAsciiHexDigit)))
            throw new InvalidDataException("Invalid plugin activation intent.");
        if (d.ActivationTargets is { } targets && (p.Kind != "plugin" || targets.Count < 1 || targets.Any(target => target.Length is < 1 or > 128)
            || targets.Distinct(StringComparer.Ordinal).Count() != targets.Count))
            throw new InvalidDataException("Invalid plugin activation targets.");
        if (d.ActivationTargets is not null)
            foreach (var target in d.ActivationTargets) InstanceFile.ValidateId("plugin target", target);
        if (d.ActivationSelections is { } selections && (d.ActivationTargets is null || selections.Count != d.ActivationTargets.Count
            || selections.Any(pair => !d.ActivationTargets.Contains(pair.Key, StringComparer.Ordinal) || pair.Value.Length != 64 || !pair.Value.All(char.IsAsciiHexDigit))))
            throw new InvalidDataException("Invalid plugin target selection stamps.");
        if (d.Schema != 1 || !Guid.TryParseExact(s.Id, "N", out _) || d.SourceIdentity.Length != 64 || !d.SourceIdentity.All(char.IsAsciiHexDigit) ||
            s.Mode != "live" || p.Kind is not ("client" or "plugin" or "server") || s.Target.Kind != p.Kind || s.Target.GameId != p.Game ||
            p.Kind == "client" && (p.Builds.Count != 1 || s.Target.BuildId != p.Builds[0]) ||
            p.Kind == "plugin" && p.Builds.Count > 0 && !p.Builds.Contains(s.Target.BuildId, StringComparer.Ordinal) ||
            p.Kind == "server" && (s.Target.BuildId.Length != 0 || s.Target.InstanceId != Hosting.ServerPackageLibrary.Identity(d.SourceId, d.SourceIdentity, p)) ||
            s.Target.ToVersion != p.Version || p.Revision < 0 || !Path.IsPathFullyQualified(s.Target.Directory) ||
            s.Stage is not ("queued" or "checking" or "downloading" or "verifying" or "installing" or "paused" or "failed" or "completed" or "canceled"))
            throw new InvalidDataException("Invalid saved download identity or state.");
        _ = SourceAddress.RelativePath(p.Manifest);
        PluginCatalog.ValidateId("download", "package", p.Id);
        var files = ClientRelease.Parse(p, d.Manifest);
        if (ClientRelease.PluginZip(p) is { } archive && s.Files.Count == 1 && s.Files[0].Path == archive.Path)
            files = [archive];
        if (s.Files.Count > files.Count || s.Files.Any(f => f is null || !files.Any(item => item.Path == f.Path && item.Size == f.Size) || f.Received < 0 || f.Received > f.Size) ||
            s.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != s.Files.Count)
            throw new InvalidDataException("Saved progress does not match the verified manifest.");
    }
}

internal static class StoreSourceIdentity
{
    internal static string Of(StoreSource source) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        source.Kind + "\n" + SourceAddress.BaseUrl(source.BaseUrl ?? throw new InvalidDataException("Source address is missing.")).AbsoluteUri + "\n" + SourceAddress.RelativePath(source.CatalogPath)))).ToLowerInvariant();
}
