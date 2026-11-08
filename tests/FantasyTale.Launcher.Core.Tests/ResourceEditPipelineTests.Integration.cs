using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;
using FantasyTale.Launcher.Host.Bridge;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class ResourceEditPipelineTests
{
    [Theory]
    [InlineData("itemName")]
    [InlineData("#2")]
    public void FieldAliasesReportTheSamePhysicalConflictAndRespectAChoice(string field)
    {
        var first = Select(Plugin("a.alias", "[[items]]\nid = 100\nname = \"A\"\n"));
        var second = Select(Plugin("b.alias", "[[items]]\nid = 100\nset = { \"" + field + "\" = \"B\" }\n"));
        var module = new BubbleFighterModule();
        var report = Assert.Throws<GameResourceConflictException>(() => module.PrepareResources(Context(), [first, second])).Report;
        var conflict = Assert.Single(report.Conflicts);
        Assert.Equal("resource.conflict.field-value", conflict.Code);
        Assert.Equal("#2", conflict.Field);
        using var prepared = module.PrepareResources(Context() with { ResourceConflictChoices = [new(report.Snapshot, conflict.Id, "a.alias")] }, [second, first]);
        Assert.Equal("A", SpreadsheetDocument.Load(Member(Assert.Single(prepared.Files), Items), Items).Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemName"));
    }

    [Fact]
    public void ImplicitWorksheetAndExplicitWorksheetCannotBypassConflicts()
    {
        var xml = XDocument.Parse(Encoding.UTF8.GetString(ResourceEditDocumentTests.Workbook()).TrimStart('\uFEFF'));
        var ss = ResourceEditDocumentTests.Ss;
        xml.Root!.Elements(ss + "Worksheet").First().Remove();
        Write(Config, [(Items, Encoding.UTF8.GetBytes(xml.ToString()))]);
        const string target = "[[targets]]\nname = \"alternate\"\nkind = \"spreadsheet\"\narchive = \"Config/Config.bfz\"\nmember = \"Config/ShopItem2.xml\"\nkeys = [\"#1\"]\n";
        var implicitSheet = Select(Plugin("a.implicit", target + "[[edits]]\ntarget = \"alternate\"\naction = \"patch\"\nkey = 100\nset = { \"#2\" = \"A\" }\n"));
        var explicitSheet = Select(Plugin("b.explicit", "[[items]]\nid = 100\nname = \"B\"\n"));
        var conflict = Assert.Single(Assert.Throws<GameResourceConflictException>(() => new BubbleFighterModule().PrepareResources(Context(), [implicitSheet, explicitSheet])).Report.Conflicts);
        Assert.Equal(("ShopItem", "#2"), (conflict.Target, conflict.Field));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExpectationsAreCheckedAgainstTheOriginalBeforeAnyPluginWrites(bool identical)
    {
        var first = Select(Plugin("a.expect", "[[items]]\nid = 100\nname = \"Updated\"\nexpect = { description = \"old desc\" }\n"));
        var second = Select(Plugin("b.expect", identical
            ? "[[items]]\nid = 100\nname = \"Updated\"\nexpect = { name = \"Bubble\" }\n"
            : "[[items]]\nid = 100\ndescription = \"New desc\"\nexpect = { name = \"Bubble\" }\n"));
        using var prepared = new BubbleFighterModule().PrepareResources(Context(), [second, first]);
        var document = SpreadsheetDocument.Load(Member(Assert.Single(prepared.Files), Items), Items);
        Assert.Equal("Updated", document.Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemName"));
        Assert.Equal(identical ? "old desc" : "New desc", document.Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemDesc"));
        var wrong = Select(Plugin("c.invalid", "[[items]]\nid = 100\nname = \"Updated\"\nexpect = { name = \"Wrong original\" }\n"));
        Assert.Throws<InvalidDataException>(() => new BubbleFighterModule().PrepareResources(Context(), [first, wrong]));
    }

    [Fact]
    public void DelimitedAndUiExpectationsAlsoUseTheOriginal()
    {
        Write("Ui/Ui.bfz", [("Ui/Test.ui", ResourceEditTextTests.Utf16(ResourceEditUiTests.Form))]);
        var first = Select(Plugin("a.original", """
            [[strings]]
            key = "HELLO"
            text = "Translated"
            expect = { text = "안녕" }
            [[edits]]
            target = "ui"
            member = "Ui/Test.ui"
            action = "patch"
            control = "pnlMain/btnOk"
            set = { Text = "Start" }
            expect = { X = "20" }
            """));
        var second = Select(Plugin("b.original", """
            [[strings]]
            key = "HELLO"
            text = "Translated"
            expect = { text = "안녕" }
            [[edits]]
            target = "ui"
            member = "Ui/Test.ui"
            action = "patch"
            control = "pnlMain/btnOk"
            set = { X = "50" }
            expect = { Text = "OK" }
            """));
        using var prepared = new BubbleFighterModule().PrepareResources(Context(), [first, second]);
        Assert.Contains("HELLO\tTranslated", Encoding.Unicode.GetString(Member(prepared.Files.Single(f => f.Target == Strings), Table)), StringComparison.Ordinal);
        var form = UiDocument.Load(Member(prepared.Files.Single(f => f.Target == "Ui/Ui.bfz"), "Ui/Test.ui"), "Ui/Test.ui");
        Assert.Equal(("Start", "50"), (form.Read("pnlMain/btnOk", "Text"), form.Read("pnlMain/btnOk", "X")));
    }

    [Theory]
    [InlineData(false, "whole.a")]
    [InlineData(false, "whole.b")]
    [InlineData(true, "whole.a")]
    [InlineData(true, "whole.b")]
    [InlineData(true, "fine.c")]
    public void WholeMemberChoicesDropEveryLosingClaimAndProvenance(bool includeFine, string keep)
    {
        var selections = new List<GameResourceSelection>
        {
            Select(WholePlugin("whole.a", "Whole A")), Select(WholePlugin("whole.b", "Whole B"))
        };
        if (includeFine) selections.Add(Select(Plugin("fine.c", "[[items]]\nid = 100\nname = \"Fine C\"\n")));
        var module = new BubbleFighterModule();
        var report = Assert.Throws<GameResourceConflictException>(() => module.PrepareResources(Context(), selections)).Report;
        var conflict = Assert.Single(report.Conflicts);
        Assert.Equal(includeFine ? 3 : 2, conflict.Parties.Count);
        using var prepared = module.PrepareResources(Context() with { ResourceConflictChoices = [new(report.Snapshot, conflict.Id, keep)] }, selections);
        var file = Assert.Single(prepared.Files);
        var name = SpreadsheetDocument.Load(Member(file, Items), Items).Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemName");
        Assert.Equal(keep switch { "whole.a" => "Whole A", "whole.b" => "Whole B", _ => "Fine C" }, name);
        Assert.Equal(keep, Assert.Single(file.Changes).PluginId);
    }

    [Fact]
    public void CurrentSnapshotRejectsUnknownConflictsAndNonParticipatingWinners()
    {
        var selections = new[] { Select(Plugin("a.choice", "[[items]]\nid = 100\nname = \"A\"\n")), Select(Plugin("b.choice", "[[items]]\nid = 100\nname = \"B\"\n")) };
        var module = new BubbleFighterModule();
        var report = Assert.Throws<GameResourceConflictException>(() => module.PrepareResources(Context(), selections)).Report;
        var conflict = Assert.Single(report.Conflicts);
        Assert.Throws<InvalidDataException>(() => module.PrepareResources(Context() with { ResourceConflictChoices = [new(report.Snapshot, conflict.Id, "unknown.plugin")] }, selections));
        Assert.Throws<InvalidDataException>(() => module.PrepareResources(Context() with { ResourceConflictChoices = [new(report.Snapshot, "unknown-conflict", "a.choice")] }, selections));
    }

    [Fact]
    public void ChoosingFineEditsCanExposeAndResolveTheirOwnFieldConflictOnTheNextRetry()
    {
        var selections = new[] { Select(WholePlugin("whole.a", "Whole A")), Select(Plugin("fine.b", "[[items]]\nid = 100\nname = \"B\"\n")),
            Select(Plugin("fine.c", "[[items]]\nid = 100\nname = \"C\"\n")) };
        var module = new BubbleFighterModule();
        var initial = Assert.Throws<GameResourceConflictException>(() => module.PrepareResources(Context(), selections)).Report;
        var memberChoice = new GameResourceConflictChoice(initial.Snapshot, Assert.Single(initial.Conflicts).Id, "fine.b");
        var retry = Context() with { ResourceConflictChoices = [memberChoice] };
        var next = Assert.Throws<GameResourceConflictException>(() => module.PrepareResources(retry, selections)).Report;
        Assert.Equal(initial.Snapshot, next.Snapshot);
        var field = Assert.Single(next.Conflicts);
        Assert.Equal("resource.conflict.field-value", field.Code);
        using var prepared = module.PrepareResources(retry with { ResourceConflictChoices = [memberChoice, new(next.Snapshot, field.Id, "fine.c")] }, selections);
        Assert.Equal("C", SpreadsheetDocument.Load(Member(Assert.Single(prepared.Files), Items), Items).Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemName"));
    }

    [Fact]
    public void UiNumericAliasesCannotBypassConflicts()
    {
        Write("Ui/Ui.bfz", [("Ui/Test.ui", ResourceEditTextTests.Utf16(ResourceEditUiTests.Form))]);
        GameResourceSelection Edit(string id, string path, string text) => Select(Plugin(id,
            "[[edits]]\ntarget = \"ui\"\nmember = \"Ui/Test.ui\"\naction = \"patch\"\ncontrol = \"" + path + "\"\nset = { Text = \"" + text + "\" }\n"));
        var conflict = Assert.Single(Assert.Throws<GameResourceConflictException>(() => new BubbleFighterModule().PrepareResources(Context(),
            [Edit("a.ui", "pnlMain/lblAlertStr[01]", "A"), Edit("b.ui", "pnlMain/lblAlertStr[1]", "B")])).Report.Conflicts);
        Assert.Equal("resource.conflict.field-value", conflict.Code);
    }

    [Fact]
    public void FluentPublicApiProducesMultilingualEditsAndAppliesAddedControlAttributes()
    {
        var module = new BubbleFighterModule();
        var api = Assert.IsAssignableFrom<IBubbleFighterResourceEdits>(module.GetCapability<IBubbleFighterResourceEdits>());
        var target = api.ResolveTarget(ResourceTargets.BuildKr812, ResourceTargets.ItemTarget, new(ResourceTargets.ItemTarget)
        { Fields = new Dictionary<string, string> { ["description"] = "itemDesc" } });
        Assert.Equal((Config, Items, "itemDesc"), (target.Archive, target.Member, target.Column("description")));
        var languages = ResourceTargets.Resolve(ResourceTargets.BuildKr812, [new(ResourceTargets.StringsTarget)
        {
            Languages = new Dictionary<string, IReadOnlyDictionary<string, string>>
            { ["en"] = new Dictionary<string, string> { ["text"] = "Translated_En" } }
        }], "en");
        Assert.Equal("Translated_En", languages[ResourceTargets.StringsTarget].Column("text"));
        Write("Ui/Ui.bfz", [("Ui/Test.ui", ResourceEditTextTests.Utf16(ResourceEditUiTests.Form))]);
        var toml = api.CreateManifest("en").PatchItem("100", new Dictionary<string, string> { ["zh-CN"] = "中文模型", ["en"] = "Model" })
            .AddUiControl("Ui/Test.ui", "pnlMain/btnNew", "<UiButton Name=\"btnNew\" Text=\"Original\" />",
                attributes: new Dictionary<string, object> { ["Text"] = new Dictionary<string, string> { ["zh-CN"] = "开始", ["en"] = "Start" } }).ToToml();
        using var prepared = module.PrepareResources(Context("zh-CN"), [Select(Plugin("public.api", toml["schema = 1".Length..]))]);
        Assert.Equal("中文模型", SpreadsheetDocument.Load(Member(prepared.Files.Single(f => f.Target == Config), Items), Items)
            .Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemName"));
        var tree = api.ParseUiControls(Member(prepared.Files.Single(f => f.Target == "Ui/Ui.bfz"), "Ui/Test.ui"));
        Assert.Equal("开始", Assert.Single(tree).Find("pnlMain/btnNew")!.Attribute("Text"));
        Assert.Equal(20, Assert.Single(tree).Find("pnlMain/btnOk")!.IntAttribute("X"));
    }

    [Fact]
    public void AddedSubtreeOwnershipProducesAStructuredConflict()
    {
        Write("Ui/Ui.bfz", [("Ui/Test.ui", ResourceEditTextTests.Utf16(ResourceEditUiTests.Form))]);
        const string prefix = "[[edits]]\ntarget = \"ui\"\nmember = \"Ui/Test.ui\"\naction = \"add\"\n";
        var first = Select(Plugin("a.subtree", prefix + "control = \"pnlMain/extra\"\nelement = '<UiPanel Name=\"extra\"><UiLabel Name=\"title\" Text=\"A\" /></UiPanel>'\n"));
        var second = Select(Plugin("b.subtree", prefix + "control = \"pnlMain/extra/title\"\nelement = '<UiLabel Name=\"title\" Text=\"B\" />'\n"));
        var module = new BubbleFighterModule();
        var report = Assert.Throws<GameResourceConflictException>(() => module.PrepareResources(Context(), [first, second])).Report;
        var conflict = Assert.Single(report.Conflicts);
        Assert.Equal("resource.conflict.parent-child", conflict.Code);
        using var prepared = module.PrepareResources(Context() with { ResourceConflictChoices = [new(report.Snapshot, conflict.Id, "a.subtree")] }, [first, second]);
        var form = UiDocument.Load(Member(Assert.Single(prepared.Files), "Ui/Test.ui"), "Ui/Test.ui");
        Assert.Equal("A", form.Read("pnlMain/extra/title", "Text"));
    }

    [Fact]
    public void BindingMetadataCorruptionRebuildsWithoutReusingStaleFieldAddresses()
    {
        var module = new BubbleFighterModule();
        var selected = Select(Plugin("binding.cache", "[[items]]\nid = 100\nname = \"Cached\"\n"));
        using (var first = module.PrepareResources(Context(), [selected])) Assert.False(Assert.Single(first.Files).CacheReused);
        using (var second = module.PrepareResources(Context(), [selected])) Assert.True(Assert.Single(second.Files).CacheReused);
        var metadata = Assert.Single(Directory.GetFiles(Path.Combine(_root, "cache"), "*.bindings.json"));
        File.WriteAllText(metadata, "{invalid");
        using var rebuilt = module.PrepareResources(Context(), [selected]);
        Assert.Equal("Cached", SpreadsheetDocument.Load(Member(Assert.Single(rebuilt.Files), Items), Items)
            .Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemName"));
        Assert.NotEqual("{invalid", File.ReadAllText(metadata));
    }

    [Fact]
    public async Task BridgePreservesStructuredReportsAndAcceptsLargeConflictDecisions()
    {
        var selections = new[] { Select(Plugin("a.bridge", "[[items]]\nid = 100\nname = \"A\"\n")), Select(Plugin("b.bridge", "[[items]]\nid = 100\nname = \"B\"\n")) };
        var error = Assert.Throws<GameResourceConflictException>(() => new BubbleFighterModule().PrepareResources(Context(), selections));
        JsonNode? reply = null;
        var bridge = new BridgeRouter(message => reply = JsonNode.Parse(message));
        bridge.Add("test.conflict", _ => throw error);
        await bridge.HandleAsync("{\"id\":1,\"method\":\"test.conflict\"}", default);
        Assert.Equal("resource_conflict", reply!["code"]!.GetValue<string>());
        Assert.Equal(error.Report.Snapshot, reply["conflictReport"]!["snapshot"]!.GetValue<string>());
        Assert.Equal(2, reply["conflictReport"]!["conflicts"]![0]!["parties"]!.AsArray().Count);
        var choices = Enumerable.Range(0, 300).Select(i => new GameResourceConflictChoice(error.Report.Snapshot, "field-" + i, "a.bridge")).ToArray();
        Assert.Equal(300, ResourceConflictBridge.Parse(JsonSerializer.SerializeToNode(choices, BridgeRouter.Json)).Count);
        Assert.Throws<ArgumentException>(() => ResourceConflictBridge.Parse(JsonNode.Parse("{}")));
        Assert.Throws<ArgumentException>(() => ResourceConflictBridge.Parse(JsonNode.Parse("[{\"snapshot\":5,\"conflictId\":\"x\",\"keepPluginId\":\"a\"}]")));
    }

    private PluginDefinition WholePlugin(string id, string name)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "whole-" + id)).FullName;
        var payload = ResourceEditDocumentTests.Workbook();
        var text = Encoding.UTF8.GetString(payload).Replace("Bubble", name, StringComparison.Ordinal);
        payload = Encoding.UTF8.GetBytes(text);
        File.WriteAllBytes(Path.Combine(folder, "items.xml"), payload);
        File.WriteAllText(Path.Combine(folder, "plan.toml"), $$"""
            schema = 1
            [[archives]]
            path = "{{Config}}"
            [[archives.members]]
            path = "{{Items}}"
            source = "items.xml"
            sha256 = "{{Hash(payload)}}"
            original_sha256 = "{{Hash(ResourceEditDocumentTests.Workbook())}}"
            """);
        File.WriteAllText(Path.Combine(folder, "plans.toml"), "schema = 1\n[[plans]]\npath = \"plan.toml\"\n");
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
        return PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml"));
    }
}
