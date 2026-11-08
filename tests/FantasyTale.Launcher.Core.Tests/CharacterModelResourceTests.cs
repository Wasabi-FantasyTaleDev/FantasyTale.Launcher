using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Games.BubbleFighter.CharacterModels;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class CharacterModelResourceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-model-test-").FullName;
    private const string Member = "Models/Characters/Dao/Default/Body.msh";
    private const string Uri = "Packs/Models.pkn";

    [Fact]
    public void DefaultsAndMatchingGenerationDoNotReadOrChangeClientFiles()
    {
        var (context, package) = Fixture();
        var module = new BubbleFighterModule();
        // The plans are deliberately unavailable: Original and native Classic must not touch them.
        File.Delete(Path.Combine(package.Folder, "plan.toml"));
        using var original = module.PrepareResources(context, [Selection(package, "{}")]);
        Assert.Empty(original.Files);
        using var classic = module.PrepareResources(context, [Selection(package, "{\"bubblefighter.dao\":\"classic\"}")]);
        Assert.Empty(classic.Files);
        var state = Assert.Single(module.GetCapability<ICharacterModelCapabilities>()!.Read(context.InstanceFolder));
        Assert.Equal(CharacterModelChoice.Classic, state.Effective);
        Assert.False(state.ChangesResources);
    }

    [Fact]
    public void ChangedSelectionComposesOnceAndReusesVerifiedCache()
    {
        var (context, package) = Fixture();
        var module = new BubbleFighterModule();
        var progress = new Recorder(); context = context with { ResourceProgress = progress };
        var selection = Selection(package, "{\"bubblefighter.dao\":\"new\"}");
        string cache;
        using (var first = module.PrepareResources(context, [selection]))
        {
            var file = Assert.Single(first.Files); cache = file.Source;
            Assert.NotEmpty(progress.Values);
            using var archive = Archive.Open(file.Source, file.Target);
            Assert.Equal("new body", Encoding.UTF8.GetString(archive.Read(Member)));
            Assert.Equal("original body", ReadOriginal(context));
        }
        Assert.True(File.Exists(cache));
        var stamp = File.GetLastWriteTimeUtc(cache);
        progress.Values.Clear();
        using (var second = module.PrepareResources(context, [selection]))
        {
            Assert.Equal(cache, Assert.Single(second.Files).Source);
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(cache));
            Assert.Empty(progress.Values);
        }
        using var disabled = module.PrepareResources(context, []);
        Assert.Empty(disabled.Files);
        Assert.Empty(module.GetCapability<ICharacterModelCapabilities>()!.Read(context.InstanceFolder));
    }

    [Fact]
    public void MixedCacheAndRebuildProgressCompletesAndPreservesOriginals()
    {
        var (context, package) = Fixture();
        const string second = "Packs/Second.pkn";
        // Each archive URI has its own encryption key, so make a valid independent input.
        using (var file = File.Create(Path.Combine(context.InstanceFolder, second)))
            PknComposer.Write(file, second, [(Member, "original body"u8.ToArray())]);
        var plan = Path.Combine(package.Folder, "plan.toml");
        var text = File.ReadAllText(plan);
        File.AppendAllText(plan, "\n" + text[(text.IndexOf("[[archives]]", StringComparison.Ordinal))..].Replace(Uri, second, StringComparison.Ordinal));
        var module = new BubbleFighterModule();
        var selection = Selection(package, "{\"bubblefighter.dao\":\"new\"}");
        string damaged;
        using (var initial = module.PrepareResources(context, [selection]))
            damaged = initial.Files.Single(row => row.Target == Uri).Source;
        File.WriteAllText(damaged, "corrupt");
        var progress = new Recorder();
        using var repaired = module.PrepareResources(context with { ResourceProgress = progress }, [selection]);
        Assert.Single(repaired.Files, row => row.CacheReused);
        Assert.Single(repaired.Files, row => !row.CacheReused);
        Assert.Equal(100, progress.Values[^1].Percentage);
        Assert.Equal(2, progress.Values[^1].CompletedArchives);
        Assert.True(progress.Values.Zip(progress.Values.Skip(1)).All(pair => pair.First.Percentage <= pair.Second.Percentage));
        Assert.Equal("original body", ReadOriginal(context));
    }

    [Fact]
    public void CorruptedCacheAndChangedOriginalRebuildWithoutReusingStaleResources()
    {
        var (context, package) = Fixture();
        var module = new BubbleFighterModule();
        var selection = Selection(package, "{\"bubblefighter.dao\":\"new\"}");
        string output;
        using (var prepared = module.PrepareResources(context, [selection])) output = Assert.Single(prepared.Files).Source;
        File.WriteAllText(Directory.GetFiles(context.ResourceCacheFolder!, "*.json").Single(), "{interrupted");
        File.WriteAllText(output, "damaged cache");
        using (var rebuilt = module.PrepareResources(context, [selection]))
        using (var archive = Archive.Open(Assert.Single(rebuilt.Files).Source, Uri))
            Assert.Equal("new body", Encoding.UTF8.GetString(archive.Read(Member)));
        using (var file = File.Create(Path.Combine(context.InstanceFolder, Uri)))
            PknComposer.Write(file, Uri, [(Member, "original body"u8.ToArray()), ("unrelated.txt", "client update"u8.ToArray())]);
        using var changed = module.PrepareResources(context, [selection]);
        using var current = Archive.Open(Assert.Single(changed.Files).Source, Uri);
        Assert.Equal("client update", Encoding.UTF8.GetString(current.Read("unrelated.txt")));
        Assert.Equal("new body", Encoding.UTF8.GetString(current.Read(Member)));
    }

    [Fact]
    public void ImportProviderAddsCharacterWithoutAnyBuildWhitelist()
    {
        var (context, package) = Fixture();
        context = context with { Build = context.Build with { Id = "future-build" } };
        var module = new BubbleFighterModule();
        var capability = module.GetCapability<ICharacterModelCapabilities>()!;
        using (capability.Register(context.InstanceFolder, [new("expansion.character", package.Name, CharacterModelChoice.Classic,
            "test-expansion", new Dictionary<CharacterModelChoice, string> { [CharacterModelChoice.New] = "plan.toml" }, package.Folder)]))
        {
            using var prepared = module.PrepareResources(context, [Selection(package, "{\"expansion.character\":\"new\"}")]);
            Assert.Single(prepared.Files);
            var result = Assert.Single(capability.Read(context.InstanceFolder));
            Assert.Equal("expansion.character", result.Key);
            Assert.Equal(CharacterModelChoice.New, result.Effective);
            Assert.Equal("test-expansion", result.Provider);
        }
        Assert.Empty(capability.Read(context.InstanceFolder));
    }

    [Fact]
    public void CancellationAndCorruptPayloadNeverModifyTheClient()
    {
        var (context, package) = Fixture();
        var selection = Selection(package, "{\"bubblefighter.dao\":\"new\"}");
        var module = new BubbleFighterModule();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => module.PrepareResources(context with { PreparationCancellation = cancellation.Token }, [selection]));
        File.WriteAllText(Path.Combine(package.Folder, "body"), "corrupt payload");
        Assert.Throws<InvalidDataException>(() => module.PrepareResources(context, [selection]));
        Assert.Equal("original body", ReadOriginal(context));
    }

    [Fact]
    public void GenericResourcePluginUsesSameComposerAndSharesArchiveWithModels()
    {
        var (context, package) = Fixture();
        File.WriteAllText(Path.Combine(package.Folder, "generic.toml"), "schema = 1\n[[plans]]\npath = \"plan.toml\"\nwhen = \"active\"\n");
        var generic = package with { Id = "test.other-resource", Resources = new("archive-members", "generic.toml") };
        var model = Selection(package, "{\"bubblefighter.dao\":\"new\"}");
        var other = new GameResourceSelection(generic, new Dictionary<string, object> { ["active"] = true });
        // Identical edits coalesce instead of two whole-pack replacements colliding.
        using var files = new BubbleFighterModule().PrepareResources(context, [model, other]);
        Assert.Single(files.Files);
    }

    [Theory]
    [InlineData("string", "snow", true)]
    [InlineData("string", "original", false)]
    [InlineData("enum", "snow", true)]
    [InlineData("enum", "original", false)]
    public void GenericStringPlanGatesSelectOnlyTheExplicitChoice(string type, string value, bool changes)
    {
        var (context, package) = Fixture();
        File.WriteAllText(Path.Combine(package.Folder, "generic.toml"),
            "schema = 1\n[[plans]]\npath = \"plan.toml\"\nwhen = \"style\"\nwhen_value = \"snow\"\n");
        var option = new PluginOption("style", type, "original", 0, 0, 16,
            ["original", "snow"], package.Name, null);
        var generic = package with { Resources = new("archive-members", "generic.toml"), Options = [option] };
        var selection = new GameResourceSelection(generic, new Dictionary<string, object> { ["style"] = value });
        var module = new BubbleFighterModule();
        module.ValidateResourceSettings(context, selection);
        using var files = module.PrepareResources(context, [selection]);
        Assert.Equal(changes ? 1 : 0, files.Files.Count);
        Assert.Equal("original body", ReadOriginal(context));
    }

    [Theory]
    [InlineData("when_value = 'snow'", "enum", "snow")]
    [InlineData("when = 'absent'\nwhen_value = 'snow'", "enum", "snow")]
    [InlineData("when = 'style'\nwhen_value = true", "enum", "snow")]
    [InlineData("when = 'style'\nwhen_value = 'winter'", "enum", "snow")]
    [InlineData("when = 'style'\nwhen_value = 'snow'", "bool", "snow")]
    [InlineData("when = 'style'\nwhen_value = 'snow'", "enum", "winter")]
    public void GenericStringPlanGatesRejectInvalidDeclarations(string gate, string type, string value)
    {
        var (context, package) = Fixture();
        File.WriteAllText(Path.Combine(package.Folder, "generic.toml"),
            "schema = 1\n[[plans]]\npath = \"plan.toml\"\n" + gate + "\n");
        var option = new PluginOption("style", type, "original", 0, 0, 16,
            ["original", "snow"], package.Name, null);
        var generic = package with { Resources = new("archive-members", "generic.toml"), Options = [option] };
        var selection = new GameResourceSelection(generic, new Dictionary<string, object> { ["style"] = value });
        Assert.Throws<InvalidDataException>(() => new BubbleFighterModule().PrepareResources(context, [selection]));
        Assert.Equal("original body", ReadOriginal(context));
    }

    [Theory]
    [InlineData("snow", "original", true, true)]
    [InlineData("snow", "snow", true, false)]
    [InlineData("snow", "original", false, false)]
    [InlineData("original", "original", true, false)]
    public void CompoundPlanConditionsBindOneSharedMemberToAnExactOptionCombination(string first, string second, bool music, bool changes)
    {
        var (context, package) = Fixture();
        File.WriteAllText(Path.Combine(package.Folder, "generic.toml"),
            "schema = 1\n[[plans]]\npath = 'plan.toml'\nwhen_options = { first = 'snow', second = 'original', music = true }\n");
        var firstOption = new PluginOption("first", "enum", "original", 0, 0, 16, ["original", "snow"], package.Name, null);
        var secondOption = firstOption with { Key = "second" };
        var musicOption = firstOption with { Key = "music", Type = "bool", Default = true, Values = [] };
        var generic = package with { Resources = new("archive-members", "generic.toml"), Options = [firstOption, secondOption, musicOption] };
        var selection = new GameResourceSelection(generic, new Dictionary<string, object> { ["first"] = first, ["second"] = second, ["music"] = music });
        using var files = new BubbleFighterModule().PrepareResources(context, [selection]);
        Assert.Equal(changes ? 1 : 0, files.Files.Count);
        Assert.Equal("original body", ReadOriginal(context));
    }

    [Theory]
    [InlineData("{ first = 'snow', missing = true }")]
    [InlineData("{ first = 'original', music = 'yes' }")]
    [InlineData("{ first = 'winter', music = true }")]
    [InlineData("{ first = 1 }")]
    [InlineData("true")]
    public void CompoundPlanConditionsValidateEveryDeclarationBeforeSkippingAnInactivePlan(string conditions)
    {
        var (context, package) = Fixture();
        File.WriteAllText(Path.Combine(package.Folder, "generic.toml"), "schema = 1\n[[plans]]\npath = 'plan.toml'\nwhen_options = " + conditions + "\n");
        var option = new PluginOption("first", "enum", "original", 0, 0, 16, ["original", "snow"], package.Name, null);
        var generic = package with { Resources = new("archive-members", "generic.toml"), Options = [option, option with { Key = "music", Type = "bool", Default = true, Values = [] }] };
        var selection = new GameResourceSelection(generic, new Dictionary<string, object> { ["first"] = "original", ["music"] = true });
        Assert.Throws<InvalidDataException>(() => new BubbleFighterModule().PrepareResources(context, [selection]));
        Assert.Equal("original body", ReadOriginal(context));
    }

    [Fact]
    public void CompoundPlanConditionsRejectANullSelectedValueBeforeResourceComposition()
    {
        var (context, package) = Fixture();
        File.WriteAllText(Path.Combine(package.Folder, "generic.toml"), "schema = 1\n[[plans]]\npath = 'plan.toml'\nwhen_options = { first = 'snow' }\n");
        var option = new PluginOption("first", "enum", "original", 0, 0, 16, ["original", "snow"], package.Name, null);
        var generic = package with { Resources = new("archive-members", "generic.toml"), Options = [option] };
        var selection = new GameResourceSelection(generic, new Dictionary<string, object> { ["first"] = null! });
        Assert.Throws<InvalidDataException>(() => new BubbleFighterModule().PrepareResources(context, [selection]));
        Assert.Equal("original body", ReadOriginal(context));
    }

    private (LaunchContext Context, PluginDefinition Package) Fixture()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "package")).FullName;
        var client = Directory.CreateDirectory(Path.Combine(_root, "client", "Packs")).Parent!.FullName;
        using (var file = File.Create(Path.Combine(client, Uri))) PknComposer.Write(file, Uri, [(Member, "original body"u8.ToArray())]);
        File.WriteAllText(Path.Combine(folder, "body"), "new body");
        File.WriteAllText(Path.Combine(folder, "plugin.toml"), """
            schema = 1
            [plugin]
            id = "test.models"
            version = "1.0.0"
            game = "bubblefighter"
            kind = "resources"
            name = { en = "Models" }
            [resources]
            provider = "character-models"
            manifest = "models.toml"
            [[options]]
            key = "models"
            type = "string"
            default = "{}"
            max_length = 65536
            label = { en = "Models" }
            """);
        File.WriteAllText(Path.Combine(folder, "models.toml"), """
            schema = 1
            [[characters]]
            key = "bubblefighter.dao"
            name = { en = "Dao" }
            [[native]]
            build = "test-build"
            key = "bubblefighter.dao"
            generation = "classic"
            new = "plan.toml"
            """);
        File.WriteAllText(Path.Combine(folder, "plan.toml"), $$"""
            schema = 1
            [[archives]]
            path = "{{Uri}}"
            [[archives.members]]
            path = "{{Member}}"
            source = "body"
            sha256 = "{{Hash("new body"u8.ToArray())}}"
            original_sha256 = "{{Hash("original body"u8.ToArray())}}"
            """);
        var package = PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml"));
        var name = package.Name;
        var build = new BuildDefinition("test-build", "test", 1, name, "game.exe", "standin", "", null, null, "nmco", [], []);
        var game = new GameDefinition("bubblefighter", GameStatus.Enabled, name, "module.dll", "", "", null, folder, [build]);
        return (new(game, build, client, null, null) { ResourceCacheFolder = Path.Combine(_root, "cache") }, package);
    }

    private static GameResourceSelection Selection(PluginDefinition package, string models) => new(package, new Dictionary<string, object> { ["models"] = models });
    private static string ReadOriginal(LaunchContext context) { using var archive = Archive.Open(Path.Combine(context.InstanceFolder, Uri), Uri); return Encoding.UTF8.GetString(archive.Read(Member)); }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private sealed class Recorder : IProgress<GameResourceProgress>
    {
        internal List<GameResourceProgress> Values { get; } = [];
        public void Report(GameResourceProgress value) => Values.Add(value);
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
