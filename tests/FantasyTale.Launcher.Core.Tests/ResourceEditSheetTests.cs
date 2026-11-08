using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class ResourceEditSheetTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-resource-sheet-").FullName;
    private readonly string _client;

    public ResourceEditSheetTests()
    {
        _client = Directory.CreateDirectory(Path.Combine(_root, "client")).FullName;
        Write("Config/Config.bfz", [("Config/ShopItem2.xml", ResourceEditDocumentTests.Workbook()), ("Config/FighterConfig.xml", ResourceEditHorizontalTests.Config())]);
        Write("StringTable/StringTable.bfz", [
            ("StringTable/TranslatedStringTable.csv", ResourceEditTextTests.Utf16("KeyString\tTranslated_Kr\r\nHELLO\t안녕\r\n")),
            ("StringTable/TranslatedXmlTable.csv", ResourceEditTextTests.Utf16("Category\tSubCategory\tKeyString\tComment\tTranslated_Kr\r\nUi\tMain\tSTART\tnote\t시작\r\n"))]);
        Write("Ui/Ui.bfz", [("Ui/Test.ui", ResourceEditTextTests.Utf16(ResourceEditUiTests.Form))]);
    }

    private void Write(string uri, (string, byte[])[] members)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_client, uri))!);
        using var stream = File.Create(Path.Combine(_client, uri));
        BfzArchive.Write(stream, uri, members);
    }

    private PluginDefinition Plugin(string id, string manifest, string sheet, byte[]? bytes = null)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "packages", id)).FullName;
        File.WriteAllText(Path.Combine(folder, "plugin.toml"), $$"""
            schema = 1
            [plugin]
            id = "{{id}}"
            version = "1.0.0"
            game = "bubblefighter"
            kind = "resources"
            name = { en = "{{id}}" }
            [resources]
            provider = "resource-edits"
            manifest = "edits.toml"
            """);
        File.WriteAllText(Path.Combine(folder, "edits.toml"), "schema = 1\n" + manifest);
        File.WriteAllBytes(Path.Combine(folder, "sheet.csv"), bytes ?? [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(sheet)]);
        return PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml"));
    }

    private LaunchContext Context(string? language)
    {
        var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "Test" });
        var build = new BuildDefinition("kr-812", "test", 1, name, "game.exe", "standin", "", null, null, "nmco", [], []) { NativeLanguage = "ko" };
        var game = new GameDefinition("bubblefighter", GameStatus.Enabled, name, "module.dll", "", "", null, _root, [build]);
        return new(game, build, _client, null, null) { ResourceCacheFolder = Path.Combine(_root, "cache"), ClientLanguage = language };
    }

    private const string Table = "[[tables]]\npath = \"sheet.csv\"\n";

    private const string Sheet = "target,key,field,member,action,value,zh-CN,ko,en,comment\r\n"
        + "item,100,name,,,,泡泡,버블,Bubble,weapon name\r\n"
        + "item,100,description,,,,\"第一行\r\n第二行\",,,\r\n"
        + "item,100,price,,,30,,,,numbers use value\r\n"
        + "strings,HELLO,text,,,,你好,,Hello,\r\n"
        + "strings,NEW_KEY,text,,,,\"新, \"\"键\"\"\",,,\r\n"
        + "xml-strings,Ui|Main|START,text,,,,开始,,,\r\n"
        + "ui,pnlMain/lblTitle,Text,Ui/Test.ui,,,标题 <&>,,,\r\n"
        + "fighter,2,#Basic/name,,,,道,,,\r\n"
        + "strings,TEMPLATE,text,,,,,,,awaiting translation\r\n"
        + "#note,anything,goes,,,,,,,\r\n";

    [Fact]
    public void OneSheetEditsItemsStringsUiAndHorizontalRecordsTogether()
    {
        using var prepared = new BubbleFighterModule().PrepareResources(Context("zh-CN"), [Select(Plugin("sheet.zh", Table, Sheet))]);
        Assert.Equal(["Config/Config.bfz", "StringTable/StringTable.bfz", "Ui/Ui.bfz"], prepared.Files.Select(f => f.Target));
        var items = SpreadsheetDocument.Load(Member(prepared, "Config/Config.bfz", "Config/ShopItem2.xml"), "x.xml");
        var item = new TableShape("ShopItem", 1, ["itemId"]);
        Assert.Equal(("泡泡", "第一行\n第二行", "30"), (items.Read(item, ["100"], "itemName"), items.Read(item, ["100"], "itemDesc"), items.Read(item, ["100"], "price")));
        var fighter = SpreadsheetDocument.Load(Member(prepared, "Config/Config.bfz", "Config/FighterConfig.xml"), "x.xml");
        Assert.Equal("道", fighter.Read(new("FighterConfig", 2, ["[Field]"], "horizontal"), ["2"], "#Basic/name"));
        var strings = Encoding.Unicode.GetString(Member(prepared, "StringTable/StringTable.bfz", "StringTable/TranslatedStringTable.csv")[2..]);
        Assert.Equal("KeyString\tTranslated_Kr\r\nHELLO\t你好\r\nNEW_KEY\t新, \"键\"\r\n", strings);
        Assert.Contains("Ui\tMain\tSTART\tnote\t开始", Encoding.Unicode.GetString(Member(prepared, "StringTable/StringTable.bfz", "StringTable/TranslatedXmlTable.csv")), StringComparison.Ordinal);
        var form = UiDocument.Load(Member(prepared, "Ui/Ui.bfz", "Ui/Test.ui"), "Ui/Test.ui");
        Assert.Equal("标题 <&>", form.Read("pnlMain/lblTitle", "Text"));
    }

    [Fact]
    public void LanguagesWithoutACellKeepTheOriginalAndTabUtf16SheetsWork()
    {
        using (var korean = new BubbleFighterModule().PrepareResources(Context("ko"), [Select(Plugin("sheet.ko", Table, Sheet))]))
        {
            var items = SpreadsheetDocument.Load(Member(korean, "Config/Config.bfz", "Config/ShopItem2.xml"), "x.xml");
            Assert.Equal(("버블", "old desc"), (items.Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemName"), items.Read(new("ShopItem", 1, ["itemId"]), ["100"], "itemDesc")));
            // No Korean string text and no value cell: the string table archive is not rebuilt at all.
            Assert.DoesNotContain(korean.Files, f => f.Target == "StringTable/StringTable.bfz");
        }
        var tab = ResourceEditTextTests.Utf16("target\tkey\tfield\tzh-TW\tvi\r\nstrings\tHELLO\ttext\t你好繁體\tXin chào\r\n");
        using var prepared = new BubbleFighterModule().PrepareResources(Context("vi"), [Select(Plugin("sheet.tab", Table, "", tab))]);
        Assert.Contains("HELLO\tXin chào", Encoding.Unicode.GetString(Member(prepared, "StringTable/StringTable.bfz", "StringTable/TranslatedStringTable.csv")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("target,Key,field,zh-CN\r\nstrings,A,text,x\r\n", "write the column Key as key")]
    [InlineData("target,key,zh-CN\r\nstrings,A,x\r\n", "missing column field")]
    [InlineData("target,key,field,zh-CN\r\nitem,100,name,a\r\nitem,100,name,b\r\n", ":3: field name is written twice")]
    [InlineData("target,key,field,action,zh-CN\r\nitem,100,name,add,a\r\nitem,100,price,patch,1\r\n", "both add and patch")]
    [InlineData("target,key,field,value,zh-CN\r\nstrings,A,text,v,x\r\n", "value and language columns are both filled")]
    [InlineData("target,key,field,zh-CN\r\nnothing,A,text,x\r\n", "sheet.csv:2: target nothing is not defined")]
    [InlineData("target,key,field,zh-CN\r\nstrings,\"A,text,x\r\n", "unterminated quoted cell")]
    [InlineData("target,key,field,zh-CN,zh-cn\r\nstrings,A,text,x,y\r\n", "duplicate column")]
    public void MalformedSheetsFailWithFileAndLine(string sheet, string message)
    {
        var error = Assert.Throws<InvalidDataException>(() => new BubbleFighterModule().PrepareResources(Context("zh-CN"), [Select(Plugin("bad", Table, sheet))]));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyCodePageSheetsAreRejectedNotGarbled()
    {
        // "你好" in GBK, which is not valid UTF-8.
        var gbk = Encoding.ASCII.GetBytes("target,key,field,zh-CN\r\nstrings,HELLO,text,").Concat(new byte[] { 0xC4, 0xE3, 0xBA, 0xC3, 0x0D, 0x0A }).ToArray();
        var error = Assert.Throws<InvalidDataException>(() => new BubbleFighterModule().PrepareResources(Context("zh-CN"), [Select(Plugin("gbk", Table, "", gbk))]));
        Assert.Contains("CSV UTF-8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SheetEditsConflictWithManifestEditsOfAnotherPlugin()
    {
        var sheet = Plugin("a.sheet", Table, "target,key,field,zh-CN\r\nitem,100,name,表格名\r\n");
        var toml = Plugin("b.toml", "[[items]]\nid = 100\nname = \"清单名\"\n", "");
        var conflict = Assert.Single(Assert.Throws<GameResourceConflictException>(() =>
            new BubbleFighterModule().PrepareResources(Context("zh-CN"), [Select(sheet), Select(toml)])).Report.Conflicts);
        Assert.Equal(("resource.conflict.field-value", "#2"), (conflict.Code, conflict.Field));
        Assert.Equal([("a.sheet", "s:表格名"), ("b.toml", "s:清单名")], conflict.Parties.Select(p => (p.PluginId, p.Value)));
    }

    private static GameResourceSelection Select(PluginDefinition package) => new(package, new Dictionary<string, object>());

    private static byte[] Member(IPreparedGameResources prepared, string archive, string member)
    {
        var file = prepared.Files.Single(f => f.Target == archive);
        using var reader = Archive.Open(file.Source, file.Target);
        return reader.Read(member);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
