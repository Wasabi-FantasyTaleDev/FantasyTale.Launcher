using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Sdk;

if (args.Length is < 4 or > 5 || args.Length == 5 && args[4] != "--expect-upgrade")
    throw new ArgumentException("Usage: PirateExpressionVerifier package-inventory.json package-report.json final.zip new-output-directory [--expect-upgrade]");
var output = Path.GetFullPath(args[3]);
if (Path.Exists(output)) throw new IOException("Use a new verification output directory.");
Directory.CreateDirectory(output);
using var inventory = JsonDocument.Parse(File.ReadAllText(args[0]));
using var manifest = JsonDocument.Parse(File.ReadAllText(args[1]));
var targets = manifest.RootElement.GetProperty("targets").EnumerateArray().ToArray();
var zip = Path.GetFullPath(args[2]);
var zipHash = Hash(zip);
Require(zipHash == manifest.RootElement.GetProperty("zip_sha256").GetString(), "Candidate ZIP differs from its packaging report");
var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Pirate expression verification" });
BuildDefinition Build(string id) => new(id, id.Split('-')[0], 1, name, "client.exe", "standin", "", null, null, "none", [], []);
var builds = targets.Select(row => Build(row.GetProperty("build").GetString()!)).ToArray();
var game = new GameDefinition("bubblefighter", GameStatus.Enabled, name, "module.dll", "", "", null, output, builds);
using var library = new PluginLibrary(Path.Combine(output, "library"));
var imports = new List<PluginImportProgress>();
PluginImportPreview preview;
try
{
    preview = library.Prepare(zip, game, imports.Add);
}
catch (LauncherUpgradeRequiredException error) when (args.Length == 5)
{
    Require(error.MinimumVersion == "0.1.62" && error.CurrentVersion == "0.1.61", "Unexpected launcher-version rejection");
    Save("verified.json", new { launcher = error.CurrentVersion, minimum = error.MinimumVersion, zipHash, oldLauncherRejected = true });
    Console.WriteLine("PASS unchanged final ZIP rejected by Launcher 0.1.61; requires 0.1.62");
    return;
}
Require(args.Length == 4, "The old launcher unexpectedly accepted the final ZIP");
var package = library.Install(preview.Token, game, imports.Add);
Require(imports[^1].Stage == "completed" && imports[^1].Percent == 100, "ZIP import progress did not complete");
Require(package.Id == "bubblefighter.fantasy.pirate-expression-fix" && package.MinimumLauncherVersion == "0.1.62", "Unexpected installed package identity");
Require(package.Kind == "resources" && package.Resources?.Provider == "archive-members", "Package is not resource-only");
foreach (var id in new[] { "cn-735", "cn-738", "cn-742", "kr-812", "kr-1256", "id-400", "tw-2019" })
    Require(!PluginCatalog.IsAvailable(package, Build(id)), "Package accepts an excluded client: " + id);
var source = inventory.RootElement.EnumerateArray().Single(row => row.GetProperty("build").GetString() == "kr-812");
const string prefix = "Models/Characters/Dao/raccoon2/";
var timelines = source.GetProperty("files").EnumerateArray()
    .Where(row => row.GetProperty("path").GetString()!.StartsWith(prefix, StringComparison.Ordinal))
    .ToDictionary(row => row.GetProperty("path").GetString()!, row => (Size: row.GetProperty("size").GetInt64(), Hash: row.GetProperty("sha256").GetString()!), StringComparer.Ordinal);
Require(timelines.Count == 193 && timelines.Keys.All(path => path.EndsWith(".face", StringComparison.Ordinal)), "Unexpected timeline evidence");
var module = new BubbleFighterModule();
var results = new List<object>();
var timer = Stopwatch.StartNew();
Console.WriteLine($"PASS final ZIP imported through Launcher {LauncherCompatibility.CurrentVersion}: {preview.FileCount} files");
foreach (var target in targets)
{
    var id = target.GetProperty("build").GetString()!;
    var root = Path.GetFullPath(target.GetProperty("root").GetString()!);
    var uri = target.GetProperty("archive").GetString()!;
    var create = target.GetProperty("create").GetBoolean();
    var original = Path.Combine(root, uri);
    Require(File.Exists(original) != create, "Real client archive baseline differs: " + id);
    var client = inventory.RootElement.EnumerateArray().Single(row => row.GetProperty("build").GetString() == id);
    Require(Path.GetFullPath(client.GetProperty("root").GetString()!) == root, "Inventory root differs from package report");
    var inputs = client.GetProperty("files").EnumerateArray().Select(row => row.GetProperty("archive").GetString()!)
        .Append(uri).Distinct(StringComparer.Ordinal).Where(path => File.Exists(Path.Combine(root, path)))
        .ToDictionary(path => path, path => Hash(Path.Combine(root, path)), StringComparer.Ordinal);
    var build = builds.Single(row => row.Id == id);
    Require(PluginCatalog.IsAvailable(package, build), "Package rejects a declared client: " + id);
    var options = PluginCatalog.ResolveOptions(package, build, new Dictionary<string, object>());
    var selection = new GameResourceSelection(package, options);
    var progress = new ProgressRecorder();
    var context = new LaunchContext(game, build, root, null, null)
    {
        ResourceCacheFolder = Path.Combine(output, id, "cache"), ResourceProgress = progress
    };
    module.ValidateResourceSettings(context, selection);
    Require(module.ResourceSettings(context, package).Count == 0, "Repair unexpectedly requires model settings");
    string preparedPath;
    string preparedHash;
    long preparedSize;
    var preservedMembers = 0;
    using (var prepared = module.PrepareResources(context, [selection]))
    {
        Require(prepared.Files.Count == 1, "Repair must produce exactly its declared archive");
        var file = prepared.Files[0];
        Require(file.Target == uri && file.ExpectedAbsent == create && file.OriginalSha256 == (create ? null : inputs[uri]), "Prepared baseline differs");
        Require(!file.CacheReused && file.Changes.Count == timelines.Count, "Unexpected cache reuse or member count");
        foreach (var change in file.Changes)
            Require(change.PluginId == package.Id && change.Action == "add" && change.OriginalSha256 is null
                && timelines.TryGetValue(change.Member, out var row) && change.Sha256 == row.Hash, "Unexpected resource edit");
        using (var archive = Archive.Open(file.Source, uri))
        {
            foreach (var (path, row) in timelines)
            {
                var data = archive.Read(path);
                Require(data.LongLength == row.Size && Digest(data) == row.Hash, "Prepared timeline differs: " + path);
            }
            if (create) Require(archive.Entries.Count == timelines.Count, "New archive contains unrelated resources");
            else
            {
                using var before = Archive.Open(original, uri);
                Require(before.GetType() == archive.GetType(), "Existing archive format was not preserved");
                if (before is LegacyPknArchive oldPack)
                {
                    var newPack = (LegacyPknArchive)archive;
                    Require(oldPack.Root == newPack.Root && oldPack.IndexGap == newPack.IndexGap
                        && oldPack.Unknown0 == newPack.Unknown0 && oldPack.Unknown1 == newPack.Unknown1, "Early PKN header changed");
                }
                else if (before is PknArchive oldModernPack)
                {
                    var newMembers = ((PknArchive)archive).Members.ToDictionary(member => member.Path, StringComparer.Ordinal);
                    foreach (var member in oldModernPack.Members)
                        Require(newMembers.TryGetValue(member.Path, out var kept) && kept.Flags == member.Flags
                            && kept.Size == member.Size && kept.StoredSize == member.StoredSize
                            && kept.Md5.AsSpan().SequenceEqual(member.Md5), "Existing PKN member metadata changed: " + member.Path);
                }
                Require(archive.Entries.Count == before.Entries.Count + timelines.Count, "Existing pack member count differs");
                foreach (var entry in before.Entries)
                {
                    Require(!timelines.ContainsKey(entry.Path), "A timeline already exists in the original pack");
                    Require(before.Read(entry.Path).AsSpan().SequenceEqual(archive.Read(entry.Path)), "Untouched pack member changed: " + entry.Path);
                    preservedMembers++;
                }
            }
        }
        Require(File.Exists(original) != create, "Preparation wrote to the real client");
        var isolated = Directory.CreateDirectory(Path.Combine(output, id, "recovery-client")).FullName;
        var destination = Path.Combine(isolated, uri);
        if (!create)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(original, destination);
        }
        var payload = new FilePluginPayload(file.Source, file.Target, file.Size, file.Sha256)
        { OriginalSha256 = file.OriginalSha256, ExpectedAbsent = file.ExpectedAbsent, Changes = file.Changes };
        var deployment = RuntimeDeployment.Apply(isolated, game.Id, null, [payload]);
        Require(Hash(destination) == file.Sha256, "Isolated deployment differs from prepared archive");
        deployment.Restore();
        CheckRestored();
        _ = RuntimeDeployment.Apply(isolated, game.Id, null, [payload]);
        RuntimeDeployment.RecoverStoppedClient(isolated);
        CheckRestored();
        void CheckRestored()
        {
            Require(create ? !Path.Exists(destination) : Hash(destination) == inputs[uri], "Isolated recovery did not restore the baseline");
            Require(!Directory.Exists(Path.Combine(isolated, ".fantasytale-runtime")), "Recovery journal remains after restoration");
        }
        preparedPath = file.Source; preparedHash = file.Sha256; preparedSize = file.Size;
    }
    Require(progress.Count > 0 && progress.Last == 100, "Measured resource progress did not complete");
    var stamp = File.GetLastWriteTimeUtc(preparedPath);
    progress.Clear();
    using (var cached = module.PrepareResources(context, [selection]))
    {
        Require(cached.Files.Count == 1 && cached.Files[0].CacheReused && cached.Files[0].Sha256 == preparedHash
            && File.GetLastWriteTimeUtc(cached.Files[0].Source) == stamp, "Valid cache was not reused");
        Require(progress.Count == 0, "Cache-only preparation reports rebuilding progress");
    }
    using (var disabled = module.PrepareResources(context, [])) Require(disabled.Files.Count == 0, "Disabled plugin modifies resources");
    foreach (var (path, hash) in inputs) Require(Hash(Path.Combine(root, path)) == hash, "Real client input changed: " + path);
    Require(File.Exists(original) != create, "Real client archive existence changed");
    results.Add(new { build = id, archive = uri, create, timelines = timelines.Count, preservedMembers, preparedSize, preparedHash,
        normalRestore = true, stoppedRecovery = true, cacheReused = true, disabledNoOp = true, realInputsUnchanged = true });
    Save("verified.json", new { launcher = LauncherCompatibility.CurrentVersion, zipHash, finalZipImported = true, excludedBuildsRejected = true, targets = results });
    Console.WriteLine($"PASS {id}: 193 timelines, {preservedMembers} original members preserved, restore/recovery/cache/disabled; {timer.Elapsed.TotalSeconds:F1}s");
}
Require(Hash(zip) == zipHash, "Final ZIP changed during verification");
Console.WriteLine($"PASS {results.Count} real-client preparations and isolated transactions in {timer.Elapsed.TotalSeconds:F1}s");

void Save(string filename, object value) => File.WriteAllText(Path.Combine(output, filename), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

internal sealed class ProgressRecorder : IProgress<GameResourceProgress>
{
    private readonly object _gate = new();
    internal int Count { get; private set; }
    internal double Last { get; private set; }
    public void Report(GameResourceProgress value)
    {
        lock (_gate)
        {
            if (value.Percentage < Last || value.Percentage > 100) throw new InvalidDataException("Non-monotonic resource progress");
            Last = value.Percentage; Count++;
        }
    }
    internal void Clear() { lock (_gate) { Last = 0; Count = 0; } }
}
