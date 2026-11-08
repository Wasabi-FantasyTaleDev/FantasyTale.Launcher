using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class ResourceEditPipelineTests : IDisposable
{
    private const string Config = "Config/Config.bfz";
    private const string Strings = "StringTable/StringTable.bfz";
    private const string Items = "Config/ShopItem2.xml";
    private const string Table = "StringTable/TranslatedStringTable.csv";
    private readonly string _root = Directory.CreateTempSubdirectory("ft-resource-edit-").FullName;
    private readonly string _client;

    public ResourceEditPipelineTests()
    {
        _client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        Write(Config, [(Items, ResourceEditDocumentTests.Workbook()), ("Config/Other.xml", "<keep/>"u8.ToArray())]);
        Write(Strings, [(Table, ResourceEditTextTests.Utf16("KeyString\tTranslated_Kr\r\nHELLO\t안녕\r\n"))]);
    }

    private void Write(string uri, (string, byte[])[] members)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_client, uri))!);
        using var stream = File.Create(Path.Combine(_client, uri));
        BfzArchive.Write(stream, uri, members);
    }

    private PluginDefinition Plugin(string id, string manifest, string version = "1.0.0")
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "packages", id + "-" + version)).FullName;
        File.WriteAllText(Path.Combine(folder, "plugin.toml"), $$"""
            schema = 1
            [plugin]
            id = "{{id}}"
            version = "{{version}}"
            game = "bubblefighter"
            kind = "resources"
            name = { en = "{{id}} EN", "zh-CN" = "{{id}} 中文" }
            [resources]
            provider = "resource-edits"
            manifest = "edits.toml"
            """);
        File.WriteAllText(Path.Combine(folder, "edits.toml"), "schema = 1\n" + manifest);
        return PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml"));
    }

    private LaunchContext Context(string? language = null, string build = "kr-812")
    {
        var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Test" });
        var definition = new BuildDefinition(build, "test", 1, name, "game.exe", "standin", "", null, null, "nmco", [], []) { NativeLanguage = "ko" };
        var game = new GameDefinition("bubblefighter", GameStatus.Enabled, name, "module.dll", "", "", null, _root, [definition]);
        return new(game, definition, _client, null, null) { ResourceCacheFolder = Path.Combine(_root, "cache"), ClientLanguage = language };
    }

    private const string Translation = """
        fallback_languages = ["en"]
        [[items]]
        id = 100
        name = { "zh-CN" = "泡泡", en = "Bubble EN" }
        description = { "zh-CN" = "描述" }
        [[strings]]
        key = "HELLO"
        text = { "zh-CN" = "你好", ko = "안녕하세요" }
        [[strings]]
        key = "NEW_KEY"
        text = { "zh-CN" = "新键" }
        """;

    [Fact]
    public void ItemAndStringShortcutsUseBuildDefaultsAndClientLanguage()
    {
        var module = new BubbleFighterModule();
        var originalConfig = File.ReadAllBytes(Path.Combine(_client, Config));
        using var prepared = module.PrepareResources(Context("zh-CN"), [Select(Plugin("lang.zh", Translation))]);
        Assert.Equal([Config, Strings], prepared.Files.Select(f => f.Target));
        var config = prepared.Files[0];
        var sheet = SpreadsheetDocument.Load(Member(config, Items), Items);
        var item = new TableShape("ShopItem", 1, ["itemId"]);
        Assert.Equal("泡泡", sheet.Read(item, ["100"], "itemName"));
        Assert.Equal("描述", sheet.Read(item, ["100"], "itemDesc"));
        Assert.Equal("25", sheet.Read(item, ["100"], "price"));
        Assert.Equal("Second", sheet.Read(item, ["200"], "itemName"));
        Assert.Equal("<keep/>"u8.ToArray(), Member(config, "Config/Other.xml"));
        var change = Assert.Single(config.Changes);
        Assert.Equal(("lang.zh", Items, "edit"), (change.PluginId, change.Member, change.Action));
        Assert.Equal(Hash(ResourceEditDocumentTests.Workbook()), change.OriginalSha256);
        var table = Encoding.Unicode.GetString(Member(prepared.Files[1], Table)[2..]);
        // The original ends with a line break, so the appended record does too.
        Assert.Equal("KeyString\tTranslated_Kr\r\nHELLO\t你好\r\nNEW_KEY\t新键\r\n", table);
        Assert.Equal(originalConfig, File.ReadAllBytes(Path.Combine(_client, Config)));
    }

    [Fact]
    public void MissingTranslationUsesDeclaredFallbackElseKeepsTheOriginal()
    {
        var module = new BubbleFighterModule();
        // Native language ko: strings have ko, the item name falls back to en, the description has neither and stays.
        using var prepared = module.PrepareResources(Context(), [Select(Plugin("lang.multi", Translation))]);
        var sheet = SpreadsheetDocument.Load(Member(prepared.Files[0], Items), Items);
        Assert.Equal("Bubble EN", sheet.Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemName"));
        Assert.Equal("old desc", sheet.Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemDesc"));
        Assert.StartsWith("KeyString\tTranslated_Kr\r\nHELLO\t안녕하세요\r\n", Encoding.Unicode.GetString(Member(prepared.Files[1], Table)[2..]), StringComparison.Ordinal);
    }

    [Fact]
    public void NonOverlappingPluginsMergeIntoOneArchiveOutput()
    {
        var names = Plugin("a.names", "[[items]]\nid = 100\nname = \"A name\"\n");
        var descriptions = Plugin("b.descriptions", "[[items]]\nid = 100\ndescription = \"B desc\"\n[[items]]\nid = 200\nname = \"B second\"\n");
        var same = Plugin("c.same", "[[items]]\nid = 100\nname = \"A name\"\n");
        using var prepared = new BubbleFighterModule().PrepareResources(Context(), [Select(descriptions), Select(same), Select(names)]);
        var file = Assert.Single(prepared.Files);
        var sheet = SpreadsheetDocument.Load(Member(file, Items), Items);
        var item = new TableShape("ShopItem", 1, ["itemId"]);
        Assert.Equal(("A name", "B desc", "B second"), (sheet.Read(item, ["100"], "itemName"), sheet.Read(item, ["100"], "itemDesc"), sheet.Read(item, ["200"], "itemName")));
        Assert.Equal(["a.names", "b.descriptions", "c.same"], file.Changes.Select(c => c.PluginId));
    }

    [Fact]
    public void ConflictingValuesReportStructuredConflictAndHonourAChoice()
    {
        var first = Plugin("a.first", "[[items]]\nid = 100\nname = \"First\"\ndescription = \"kept from first\"\n[[strings]]\nkey = \"HELLO\"\ntext = \"only A\"\n");
        var second = Plugin("b.second", "[[items]]\nid = 100\nname = \"Second\"\n");
        var module = new BubbleFighterModule();
        var selections = new[] { Select(second), Select(first) };
        var cache = Path.Combine(_root, "cache");
        var error = Assert.Throws<GameResourceConflictException>(() => module.PrepareResources(Context(), selections));
        // Nothing is staged or cached when preparation stops at a conflict.
        Assert.False(Directory.Exists(cache) && Directory.EnumerateFiles(cache).Any());
        var report = error.Report;
        Assert.Equal(("kr-812", "ko"), (report.Build, report.Language));
        var conflict = Assert.Single(report.Conflicts);
        Assert.Equal(("resource.conflict.field-value", Config, Items, "ShopItem", "100", "#2"),
            (conflict.Code, conflict.Archive, conflict.Member, conflict.Target, conflict.Key, conflict.Field));
        Assert.Equal([("a.first", "1.0.0", "patch", "s:First"), ("b.second", "1.0.0", "patch", "s:Second")],
            conflict.Parties.Select(p => (p.PluginId, p.Version, p.Action, p.Value)));
        Assert.Equal("a.first 中文", conflict.Parties[0].Name.Values["zh-CN"]);
        // The report is serializable and independent of plugin enumeration order.
        var reversed = Assert.Throws<GameResourceConflictException>(() => module.PrepareResources(Context(), [selections[1], selections[0]])).Report;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(report), System.Text.Json.JsonSerializer.Serialize(reversed));

        var chosen = Context() with { ResourceConflictChoices = [new(report.Snapshot, conflict.Id, "b.second")] };
        using (var prepared = module.PrepareResources(chosen, selections))
        {
            var sheet = SpreadsheetDocument.Load(Member(prepared.Files.Single(f => f.Target == Config), Items), Items);
            Assert.Equal("Second", sheet.Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemName"));
            // The losing plugin's other edits still apply.
            Assert.Equal("kept from first", sheet.Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemDesc"));
            Assert.Contains("only A", Encoding.Unicode.GetString(Member(prepared.Files.Single(f => f.Target == Strings), Table)), StringComparison.Ordinal);
        }
        // A plugin update changes the snapshot, so the old choice no longer applies.
        var updated = Plugin("b.second", "[[items]]\nid = 100\nname = \"Second v2\"\n", "1.1.0");
        var stale = Assert.Throws<GameResourceConflictException>(() => module.PrepareResources(chosen, [Select(first), Select(updated)]));
        Assert.NotEqual(report.Snapshot, stale.Report.Snapshot);
        // So does another client language.
        Assert.NotEqual(report.Snapshot, Assert.Throws<GameResourceConflictException>(() => module.PrepareResources(chosen with { ClientLanguage = "en" }, selections)).Report.Snapshot);
    }

    [Fact]
    public void RecordLevelConflictsCoverDuplicateAddsRemovalsAndRowVersusField()
    {
        var module = new BubbleFighterModule();
        string Code(string a, string b) => Assert.Single(Assert.Throws<GameResourceConflictException>(() =>
            module.PrepareResources(Context(), [Select(Plugin("a." + Guid.NewGuid().ToString("N")[..6], a)), Select(Plugin("b." + Guid.NewGuid().ToString("N")[..6], b))])).Report.Conflicts).Code;
        Assert.Equal("resource.conflict.duplicate-add", Code("[[items]]\naction = \"add\"\nid = 300\nname = \"x\"\n", "[[items]]\naction = \"add\"\nid = 300\nname = \"y\"\n"));
        Assert.Equal("resource.conflict.remove-edit", Code("[[items]]\naction = \"remove\"\nid = 200\n", "[[items]]\nid = 200\nname = \"y\"\n"));
        Assert.Equal("resource.conflict.row-vs-field", Code("[[items]]\naction = \"replace\"\nid = 200\nname = \"x\"\n", "[[items]]\nid = 200\nname = \"y\"\n"));
    }

    [Fact]
    public void HideIsRejectedWhilePlanningWithAStableCode()
    {
        var plugin = Plugin("ui.hide", "[[edits]]\ntarget = \"ui\"\nmember = \"Ui/Test.ui\"\naction = \"hide\"\ncontrol = \"pnlMain/btnOk\"\n");
        var error = Assert.Throws<InvalidDataException>(() => new BubbleFighterModule().ValidateResourceSettings(Context(), Select(plugin)));
        Assert.StartsWith("resource.ui.hide-unsupported", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CacheIsReusedOnlyForIdenticalInputs()
    {
        var module = new BubbleFighterModule();
        var plugin = Select(Plugin("lang.cache", Translation));
        string Name(LaunchContext context)
        {
            using var prepared = module.PrepareResources(context, [plugin]);
            return SpreadsheetDocument.Load(Member(prepared.Files[0], Items), Items).Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemName")!;
        }
        Assert.Equal("泡泡", Name(Context("zh-CN")));
        using (var again = module.PrepareResources(Context("zh-CN"), [plugin])) Assert.All(again.Files, f => Assert.True(f.CacheReused));
        Assert.Equal("Bubble EN", Name(Context("en")));
        // A changed original archive is never patched from a cached member of the old one.
        Write(Config, [(Items, ResourceEditDocumentTests.Workbook().Concat("<!-- updated -->"u8.ToArray()).ToArray())]);
        using var changed = module.PrepareResources(Context("zh-CN"), [plugin]);
        var config = changed.Files.Single(f => f.Target == Config);
        Assert.False(config.CacheReused);
        Assert.EndsWith("<!-- updated -->", Encoding.UTF8.GetString(Member(config, Items)), StringComparison.Ordinal);
    }

    [Fact]
    public void CancellationLeavesClientFilesUntouched()
    {
        var before = File.ReadAllBytes(Path.Combine(_client, Config));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new BubbleFighterModule().PrepareResources(
            Context() with { PreparationCancellation = cancelled.Token }, [Select(Plugin("lang.cancel", Translation))]));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_client, Config)));
    }

    [Fact]
    public void WholeMemberReplacementConflictsWithFineEditsUntilTheUserChooses()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "packages", "whole")).FullName;
        File.WriteAllBytes(Path.Combine(folder, "items.xml"), ResourceEditDocumentTests.Workbook(other: "cost"));
        File.WriteAllText(Path.Combine(folder, "plugin.toml"), """
            schema = 1
            [plugin]
            id = "z.whole"
            version = "2.0.0"
            game = "bubblefighter"
            kind = "resources"
            name = { en = "Whole" }
            [resources]
            provider = "archive-members"
            manifest = "plans.toml"
            """);
        File.WriteAllText(Path.Combine(folder, "plans.toml"), "schema = 1\n[[plans]]\npath = \"plan.toml\"\n");
        File.WriteAllText(Path.Combine(folder, "plan.toml"), $$"""
            schema = 1
            [[archives]]
            path = "{{Config}}"
            [[archives.members]]
            path = "{{Items}}"
            source = "items.xml"
            sha256 = "{{Hash(File.ReadAllBytes(Path.Combine(folder, "items.xml")))}}"
            original_sha256 = "{{Hash(ResourceEditDocumentTests.Workbook())}}"
            """);
        var whole = Select(PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml")));
        var fine = Select(Plugin("a.fine", "[[items]]\nid = 100\nname = \"fine\"\n"));
        var module = new BubbleFighterModule();
        var conflict = Assert.Single(Assert.Throws<GameResourceConflictException>(() => module.PrepareResources(Context(), [whole, fine])).Report.Conflicts);
        Assert.Equal("resource.conflict.member-replace", conflict.Code);
        Assert.Equal(["edit", "member-replace"], conflict.Parties.Select(p => p.Action));
        var snapshot = Assert.Throws<GameResourceConflictException>(() => module.PrepareResources(Context(), [whole, fine])).Report.Snapshot;
        using (var keepWhole = module.PrepareResources(Context() with { ResourceConflictChoices = [new(snapshot, conflict.Id, "z.whole")] }, [whole, fine]))
        {
            var file = Assert.Single(keepWhole.Files);
            Assert.Equal(File.ReadAllBytes(Path.Combine(folder, "items.xml")), Member(file, Items));
            Assert.Equal(["z.whole"], file.Changes.Select(c => c.PluginId));
        }
        using var keepFine = module.PrepareResources(Context() with { ResourceConflictChoices = [new(snapshot, conflict.Id, "a.fine")] }, [whole, fine]);
        var result = Assert.Single(keepFine.Files);
        Assert.Equal("fine", SpreadsheetDocument.Load(Member(result, Items), Items).Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemName"));
        Assert.Equal(["a.fine"], result.Changes.Select(c => c.PluginId));
    }

    [Theory]
    [InlineData("kr-812", "zh-CN", 10)]
    [InlineData("cn-406", "en", 6)]
    public void DocumentedExamplePlansOnEachBuild(string build, string language, int expected)
    {
        var docs = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "examples", "resource-edits"));
        var folder = Directory.CreateDirectory(Path.Combine(_root, "example")).FullName;
        foreach (var file in Directory.GetFiles(docs)) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        var package = PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml"));
        var plan = ResourceEditPlanner.Load(Context(language, build), new(package, new Dictionary<string, object> { ["rename_buttons"] = true }));
        var edits = plan.Edits;
        Assert.Equal(expected, edits.Count);
        // The documented pattern is gated to kr-812.
        Assert.Equal(build == "kr-812" ? 1 : 0, plan.Patterns.Count);
        if (build == "cn-406")
            Assert.Equal("Translated_En", Assert.Single(edits, e => e.Key == "MSG_EXAMPLE").Record!.Set.Keys.Single());
        else Assert.Contains(edits, e => e.Action == EditAction.Add && e.Key == "1999001");
    }

    private static GameResourceSelection Select(PluginDefinition package) => new(package, new Dictionary<string, object>());

    private static byte[] Member(PreparedGameResource file, string member)
    {
        using var archive = Archive.Open(file.Source, file.Target);
        return archive.Read(member);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
