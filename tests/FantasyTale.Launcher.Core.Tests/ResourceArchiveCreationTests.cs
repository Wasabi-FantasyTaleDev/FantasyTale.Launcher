using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ResourceArchiveCreationTests : IDisposable
{
    private const string Uri = "Models/Expressions/Expressions.bfz";
    private readonly string _root = Directory.CreateTempSubdirectory("ft-create-resource-").FullName;

    [Fact]
    public void NewArchiveSharesContributionsCachesAndRecoversItsAbsentBaseline()
    {
        var first = Package("test.first", "first.face", "first");
        var second = Package("test.second", "second.face", "second");
        var context = Context(first);
        var module = new BubbleFighterModule();
        using var prepared = module.PrepareResources(context, [Selection(first), Selection(second)]);
        var file = Assert.Single(prepared.Files);
        Assert.True(file.ExpectedAbsent);
        Assert.Null(file.OriginalSha256);
        Assert.False(file.CacheReused);
        Assert.Equal(2, file.Changes.Count);
        Assert.False(File.Exists(Path.Combine(context.InstanceFolder, Uri)));
        using (var archive = Archive.Open(file.Source, Uri))
        {
            Assert.Equal(2, archive.Entries.Count);
            Assert.Equal("first", Encoding.UTF8.GetString(archive.Read("first.face")));
            Assert.Equal("second", Encoding.UTF8.GetString(archive.Read("second.face")));
        }
        using (var cached = module.PrepareResources(context, [Selection(first), Selection(second)]))
        {
            var hit = Assert.Single(cached.Files);
            Assert.True(hit.CacheReused);
            Assert.True(hit.ExpectedAbsent);
            Assert.Equal(file.Sha256, hit.Sha256);
        }
        _ = RuntimeDeployment.Apply(context.InstanceFolder, "bubblefighter", null, [Payload(file)]);
        Assert.True(File.Exists(Path.Combine(context.InstanceFolder, Uri)));
        RuntimeDeployment.RecoverStoppedClient(context.InstanceFolder);
        Assert.False(File.Exists(Path.Combine(context.InstanceFolder, Uri)));
        Assert.False(Directory.Exists(Path.Combine(context.InstanceFolder, ".fantasytale-runtime")));
        using var disabled = module.PrepareResources(context, []);
        Assert.Empty(disabled.Files);
    }

    [Fact]
    public void ConflictingNewMembersUseTheExistingChoiceSnapshot()
    {
        var first = Package("test.first", "same.face", "first");
        var second = Package("test.second", "same.face", "second");
        var context = Context(first);
        var module = new BubbleFighterModule();
        var conflict = Assert.Throws<GameResourceConflictException>(() =>
            module.PrepareResources(context, [Selection(first), Selection(second)]));
        var group = Assert.Single(conflict.Report.Conflicts);
        Assert.Equal(2, group.Parties.Count);
        Assert.False(File.Exists(Path.Combine(context.InstanceFolder, Uri)));
        context = context with
        {
            ResourceConflictChoices = [new(conflict.Report.Snapshot, group.Id, first.Id)]
        };
        using var prepared = module.PrepareResources(context, [Selection(first), Selection(second)]);
        var file = Assert.Single(prepared.Files);
        Assert.Single(file.Changes);
        using var archive = Archive.Open(file.Source, Uri);
        Assert.Equal("first", Encoding.UTF8.GetString(archive.Read("same.face")));
    }

    [Fact]
    public void ExistingInputCannotBeReplacedByACreationPlanOrItsCache()
    {
        var package = Package("test.first", "a.face", "prepared");
        var context = Context(package);
        var module = new BubbleFighterModule();
        using (var prepared = module.PrepareResources(context, [Selection(package)])) Assert.Single(prepared.Files);
        var input = Path.Combine(context.InstanceFolder, Uri);
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        using (var stream = File.Create(input)) BfzArchive.Write(stream, Uri, [("existing", "owned by client"u8.ToArray())]);
        var before = File.ReadAllBytes(input);
        Assert.Throws<InvalidDataException>(() => module.PrepareResources(context, [Selection(package)]));
        Assert.Equal(before, File.ReadAllBytes(input));
    }

    [Fact]
    public void DestinationAppearingAfterPreparationIsPreserved()
    {
        var package = Package("test.first", "a.face", "prepared");
        var context = Context(package);
        using var prepared = new BubbleFighterModule().PrepareResources(context, [Selection(package)]);
        var input = Path.Combine(context.InstanceFolder, Uri);
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        File.WriteAllText(input, "arrived after preparation");
        Assert.Throws<IOException>(() => RuntimeDeployment.Apply(context.InstanceFolder, "bubblefighter", null,
            [Payload(Assert.Single(prepared.Files))]));
        Assert.Equal("arrived after preparation", File.ReadAllText(input));
        Assert.False(Directory.Exists(Path.Combine(context.InstanceFolder, ".fantasytale-runtime")));
    }

    [Fact]
    public void MissingArchiveStillRequiresAnExplicitCreationDeclaration()
    {
        var package = Package("test.first", "a.face", "prepared", create: false);
        Assert.Throws<DirectoryNotFoundException>(() =>
            new BubbleFighterModule().PrepareResources(Context(package), [Selection(package)]));
    }

    [Theory]
    [InlineData("../outside.bfz")]
    [InlineData("Models/new.pkn")]
    public void CreationRejectsUnsafePathsAndUnspecifiedPackFormats(string uri)
    {
        var package = Package("test.first", "a.face", "prepared", uri: uri);
        Assert.Throws<InvalidDataException>(() =>
            new BubbleFighterModule().PrepareResources(Context(package), [Selection(package)]));
    }

    [Fact]
    public void CreationCannotMixAbsentAndExistingArchiveAssumptions()
    {
        var first = Package("test.first", "a.face", "first");
        var second = Package("test.second", "b.face", "second", create: false);
        Assert.Throws<InvalidDataException>(() => new BubbleFighterModule().PrepareResources(Context(first),
            [Selection(first), Selection(second)]));
    }

    [Fact]
    public void CreationRejectsOriginalMemberDigestsAndCorruptPayloads()
    {
        var package = Package("test.first", "a.face", "prepared");
        var context = Context(package);
        var plan = Path.Combine(package.Folder, "plan.toml");
        File.AppendAllText(plan, "\noriginal_sha256 = \"" + Hash("old"u8.ToArray()) + "\"\n");
        Assert.Throws<InvalidDataException>(() => new BubbleFighterModule().PrepareResources(context, [Selection(package)]));
        File.WriteAllText(plan, File.ReadAllText(plan).Split("original_sha256", StringSplitOptions.None)[0]);
        File.WriteAllText(Path.Combine(package.Folder, "payload"), "corrupt");
        Assert.Throws<InvalidDataException>(() => new BubbleFighterModule().PrepareResources(context, [Selection(package)]));
        Assert.False(File.Exists(Path.Combine(context.InstanceFolder, Uri)));
    }

    [Fact]
    public void ComposerPreservesExistingOutputAndHonorsCancellation()
    {
        var output = Path.Combine(_root, "existing.bfz");
        File.WriteAllText(output, "user output");
        var composer = new ArchiveResourceComposer();
        Assert.Throws<IOException>(() => composer.Create(Uri, output, []));
        Assert.Equal("user output", File.ReadAllText(output));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = Path.Combine(_root, "canceled.bfz");
        Assert.Throws<OperationCanceledException>(() => composer.Create(Uri, canceled, [], cancellationToken: cancellation.Token));
        Assert.False(File.Exists(canceled));
    }

    private PluginDefinition Package(string id, string member, string text, bool create = true, string uri = Uri)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, id)).FullName;
        File.WriteAllText(Path.Combine(folder, "payload"), text, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "plugin.toml"), $$"""
            schema = 1
            [plugin]
            id = "{{id}}"
            version = "1.0.0"
            game = "bubblefighter"
            kind = "resources"
            name = { en = "{{id}}" }
            [resources]
            provider = "archive-members"
            manifest = "plans.toml"
            """);
        File.WriteAllText(Path.Combine(folder, "plans.toml"), "schema = 1\n[[plans]]\npath = \"plan.toml\"\n");
        File.WriteAllText(Path.Combine(folder, "plan.toml"), $$"""
            schema = 1
            [[archives]]
            path = "{{uri}}"
            create = {{(create ? "true" : "false")}}
            [[archives.members]]
            path = "{{member}}"
            source = "payload"
            sha256 = "{{Hash(Encoding.UTF8.GetBytes(text))}}"
            """);
        return PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml"));
    }

    private LaunchContext Context(PluginDefinition package)
    {
        var client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        var build = new BuildDefinition("test-build", "test", 1, package.Name, "game.exe", "standin", "", null, null, "none", [], []);
        var game = new GameDefinition("bubblefighter", GameStatus.Enabled, package.Name, "module.dll", "", "", null, package.Folder, [build]);
        return new(game, build, client, null, null) { ResourceCacheFolder = Path.Combine(_root, "cache") };
    }

    private static GameResourceSelection Selection(PluginDefinition package) => new(package, new Dictionary<string, object>());

    private static FilePluginPayload Payload(PreparedGameResource file) => new(file.Source, file.Target, file.Size, file.Sha256)
    { OriginalSha256 = file.OriginalSha256, ExpectedAbsent = file.ExpectedAbsent, Changes = file.Changes };

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public void Dispose() => Directory.Delete(_root, true);
}
