using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Games.BubbleFighter.CharacterModels;
using FantasyTale.Launcher.Sdk;

if (args.Length != 3) throw new ArgumentException("Usage: CharacterModelVerifier corpus-requests.json package-folder new-output-directory");
var output = Path.GetFullPath(args[2]);
if (Directory.Exists(output)) throw new IOException("Use a new verification output directory.");
Directory.CreateDirectory(output);
using var inventory = JsonDocument.Parse(File.ReadAllText(args[0]));
using var library = new PluginLibrary(Path.Combine(output, "library"));
PluginDefinition package;
if (File.Exists(args[1]))
{
    var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Verification" });
    var game = new GameDefinition("bubblefighter", GameStatus.Enabled, name, "module.dll", "", "", null, output, []);
    var updates = new List<PluginImportProgress>();
    var preview = library.Prepare(Path.GetFullPath(args[1]), game, updates.Add);
    package = library.Install(preview.Token, game, updates.Add);
    Require(updates[^1].Stage == "completed" && updates[^1].Percent == 100, "ZIP import progress did not complete");
    Console.WriteLine($"PASS final ZIP imported and verified through the real plugin library: {preview.FileCount} files");
}
else package = PluginCatalog.LoadFile(Path.Combine(Path.GetFullPath(args[1]), "plugin.toml"));
var module = new BubbleFighterModule();
var report = new List<object>();
var timer = Stopwatch.StartNew();
foreach (var group in inventory.RootElement.EnumerateArray().GroupBy(row => row.GetProperty("output").GetString()!.Split('/')[0]))
{
    var first = group.First();
    var uri = first.GetProperty("uri").GetString()!;
    var input = first.GetProperty("input").GetString()!;
    var client = Path.GetFullPath(input[..^uri.Length]);
    var baseHashes = group.ToDictionary(row => row.GetProperty("uri").GetString()!, row => Hash(row.GetProperty("input").GetString()!));
    var build = new BuildDefinition(group.Key, "corpus", 1, package.Name, "client.exe", "standin", "", null, null, "none", [], []);
    var game = new GameDefinition("bubblefighter", GameStatus.Enabled, package.Name, "module.dll", "", "", null, package.Folder, [build]);
    var context = new LaunchContext(game, build, client, null, null) { ResourceCacheFolder = Path.Combine(output, group.Key, "cache") };
    var settings = JsonSerializer.SerializeToElement(module.ResourceSettings(context, package));
    var characters = settings.GetProperty("characters").EnumerateArray().Select(row => new
    {
        Key = row.GetProperty("key").GetString()!, Native = row.GetProperty("nativeGeneration").GetString()!,
        Choices = row.GetProperty("choices").EnumerateArray().Select(choice => choice.GetString()!).ToArray()
    }).ToArray();
    GameResourceSelection Selection(IReadOnlyDictionary<string, string> choices) =>
        new(package, new Dictionary<string, object> { ["models"] = JsonSerializer.Serialize(choices) });
    using (var initial = module.PrepareResources(context, [Selection(new Dictionary<string, string>())]))
        Require(initial.Files.Count == 0, "First installation must be a no-op");
    var native = characters.ToDictionary(row => row.Key, row => row.Native);
    using (var matching = module.PrepareResources(context, [Selection(native)]))
        Require(matching.Files.Count == 0, "Native choices must be a no-op");
    var alternate = characters.ToDictionary(row => row.Key, row => row.Native == "new" ? "classic" : "new");
    Require(characters.All(row => row.Choices.Contains(alternate[row.Key])), "Every target must expose its verified alternative");
    var progress = new ProgressRecorder();
    context = context with { ResourceProgress = progress };
    module.ValidateResourceSettings(context, Selection(alternate));
    string[] paths;
    long bytes;
    using (var prepared = module.PrepareResources(context, [Selection(alternate)]))
    {
        Require(prepared.Files.Count == group.Count(), "Prepared archive coverage differs from verified member plans");
        Require(prepared.Files.All(row => !row.CacheReused && row.Changes.Count > 0 && row.OriginalSha256 is not null), "Missing provenance or unexpected cache reuse");
        paths = prepared.Files.Select(row => row.Source).ToArray();
        bytes = prepared.Files.Sum(row => row.Size);
        var snapshot = module.GetCapability<ICharacterModelCapabilities>()!.Read(client);
        Require(snapshot.Count == characters.Length && snapshot.All(row => row.ChangesResources), "Incorrect cross-plugin selection snapshot");
        // Real archive deployment is tested on isolated copies of three format families.
        var example = prepared.Files.FirstOrDefault(row => row.Target.EndsWith(".bfz", StringComparison.OrdinalIgnoreCase))
            ?? prepared.Files[0];
        if (group.Key is "cn-279" or "kr-812" or "kr-1256")
        {
            var isolated = Path.Combine(output, group.Key, "recovery-client");
            var destination = Path.Combine(isolated, example.Target);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(client, example.Target), destination);
            var before = Hash(destination);
            var payload = new FilePluginPayload(example.Source, example.Target, example.Size, example.Sha256)
                { OriginalSha256 = example.OriginalSha256, Changes = example.Changes };
            _ = RuntimeDeployment.Apply(isolated, game.Id, null, [payload]);
            Require(Hash(destination) == example.Sha256, "Real archive deployment failed");
            RuntimeDeployment.RecoverStoppedClient(isolated);
            Require(Hash(destination) == before, "Real archive crash recovery did not restore exact original");
        }
    }
    Require(progress.Count > 0 && progress.Last == 100, "Measured resource progress did not complete");
    var stamps = paths.ToDictionary(path => path, File.GetLastWriteTimeUtc);
    progress.Clear();
    using (var cached = module.PrepareResources(context, [Selection(alternate)]))
    {
        Require(cached.Files.All(row => row.CacheReused && File.GetLastWriteTimeUtc(row.Source) == stamps[row.Source]), "Verified cache was rebuilt unnecessarily");
        Require(progress.Count == 0, "A cache-only launch should not show rebuilding progress");
    }
    using (var disabled = module.PrepareResources(context, [])) Require(disabled.Files.Count == 0, "Disabled plugin must not modify resources");
    Require(module.GetCapability<ICharacterModelCapabilities>()!.Read(client).Count == 0, "Disabled plugin retained an active selection snapshot");
    foreach (var entry in group)
    {
        var path = entry.GetProperty("uri").GetString()!;
        Require(baseHashes[path] == Hash(Path.Combine(client, path)), "Real client input changed");
    }
    report.Add(new { build = group.Key, characters = characters.Length, archives = paths.Length, bytes,
        defaultsNoOp = true, nativeNoOp = true, alternativesPrepared = true, verifiedCacheReused = true,
        monotonicProgress = true, disabledNoOp = true, inputUnchanged = true });
    File.WriteAllText(Path.Combine(output, "verified.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"PASS {group.Key}: {characters.Length} characters, {paths.Length} archives, defaults/native/disabled/cache/snapshot/progress; {timer.Elapsed.TotalSeconds:F1}s");
}
Console.WriteLine($"PASS {report.Count} real-client model preparations and three-format recovery in {timer.Elapsed.TotalSeconds:F1}s");

static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }

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
