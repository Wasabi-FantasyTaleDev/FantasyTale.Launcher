using System.Text;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class ResourceEditSheetTests
{
    private static readonly TableShape ItemShape = new("ShopItem", 1, ["itemId"]);
    private static readonly TableShape FighterShape = new("FighterConfig", 2, ["[Field]"], "horizontal");

    private IPreparedGameResources Prepare(string language, params GameResourceSelection[] selections) =>
        new BubbleFighterModule().PrepareResources(Context(language), selections);

    private static string Strings(IPreparedGameResources prepared) =>
        Encoding.Unicode.GetString(Member(prepared, "StringTable/StringTable.bfz", "StringTable/TranslatedStringTable.csv")[2..]);

    [Fact]
    public void FindReplacesOnlyMatchingTextWithGroupsAndLanguages()
    {
        var plugin = Plugin("regex", """
            [[patterns]]
            target = "item"
            match = '.*'
            fields = ["name"]
            find = '^(Bub)ble$'
            replace = { "zh-CN" = "泡泡($1)", en = "B" }
            [[patterns]]
            target = "ui"
            member_match = 'Ui/.*\.ui'
            match = '.*lblAlertStr.*'
            fields = ["Text"]
            find = '^'
            replace = "提示："
            [[patterns]]
            target = "fighter"
            match = '1|3'
            fields = ["speed"]
            find = '\d+'
            replace = "9"
            """, "");
        using var prepared = Prepare("zh-CN", Select(plugin));
        var items = SpreadsheetDocument.Load(Member(prepared, "Config/Config.bfz", "Config/ShopItem2.xml"), "x.xml");
        Assert.Equal(("泡泡(Bub)", "Second"), (items.Read(ItemShape, ["100"], "itemName"), items.Read(ItemShape, ["200"], "itemName")));
        var form = UiDocument.Load(Member(prepared, "Ui/Ui.bfz", "Ui/Test.ui"), "Ui/Test.ui");
        Assert.Equal(("提示：first", "提示：second", "Title", "OK"), (form.Read("pnlMain/lblAlertStr[0]", "Text"), form.Read("pnlMain/lblAlertStr[1]", "Text"),
            form.Read("pnlMain/lblTitle", "Text"), form.Read("pnlMain/btnOk", "Text")));
        var fighter = SpreadsheetDocument.Load(Member(prepared, "Config/Config.bfz", "Config/FighterConfig.xml"), "x.xml");
        // Record 2 has no speed cell, and 1|3 is anchored, so it never matches 12 or 31.
        Assert.Equal(("9", null, "9"), (fighter.Read(FighterShape, ["1"], "speed"), fighter.Read(FighterShape, ["2"], "speed"), fighter.Read(FighterShape, ["3"], "speed")));
        // Only archives a pattern changed are rebuilt.
        Assert.DoesNotContain(prepared.Files, f => f.Target == "StringTable/StringTable.bfz");
    }

    [Fact]
    public void PatternsChainInOrderAndTheSamePluginsLiteralEditWins()
    {
        var plugin = Plugin("chain", """
            [[items]]
            id = 100
            name = "Literal"
            [[patterns]]
            target = "item"
            match = '[12]00'
            set = { price = 7 }
            [[patterns]]
            target = "item"
            match = '.*'
            fields = ["name", "itemDesc"]
            find = 'e'
            replace = "E"
            [[patterns]]
            target = "item"
            match = '.*'
            fields = ["name"]
            find = 'E'
            replace = "3"
            [[patterns]]
            target = "strings"
            match = 'HEL+O'
            set = { text = { "zh-CN" = "你好" } }
            """, "");
        using var prepared = Prepare("zh-CN", Select(plugin));
        var items = SpreadsheetDocument.Load(Member(prepared, "Config/Config.bfz", "Config/ShopItem2.xml"), "x.xml");
        // The literal name wins over the plugin's own patterns; its description still takes them.
        Assert.Equal(("Literal", "old dEsc"), (items.Read(ItemShape, ["100"], "itemName"), items.Read(ItemShape, ["100"], "itemDesc")));
        Assert.Equal("S3cond", items.Read(ItemShape, ["200"], "itemName"));
        Assert.Equal(("7", "7"), (items.Read(ItemShape, ["100"], "price"), items.Read(ItemShape, ["200"], "price")));
        Assert.Equal("KeyString\tTranslated_Kr\r\nHELLO\t你好\r\n", Strings(prepared));
    }

    [Fact]
    public void PatternResultsConflictWithOtherPluginsLikeLiteralEdits()
    {
        var literal = Plugin("a.literal", "[[items]]\nid = 200\nname = \"Mine\"\n", "");
        var pattern = Plugin("b.pattern", "[[patterns]]\ntarget = \"item\"\nmatch = '.*'\nfields = [\"name\"]\nfind = 'Second'\nreplace = \"Theirs\"\n", "");
        var report = Assert.Throws<GameResourceConflictException>(() => Prepare("zh-CN", Select(literal), Select(pattern))).Report;
        var conflict = Assert.Single(report.Conflicts);
        Assert.Equal(("resource.conflict.field-value", "200", "#2"), (conflict.Code, conflict.Key, conflict.Field));
        Assert.Equal([("a.literal", "s:Mine"), ("b.pattern", "s:Theirs")], conflict.Parties.Select(p => (p.PluginId, p.Value)));
        using var resolved = new BubbleFighterModule().PrepareResources(Context("zh-CN") with { ResourceConflictChoices = [new(report.Snapshot, conflict.Id, "b.pattern")] },
            [Select(literal), Select(pattern)]);
        Assert.Equal("Theirs", SpreadsheetDocument.Load(Member(resolved, "Config/Config.bfz", "Config/ShopItem2.xml"), "x.xml").Read(ItemShape, ["200"], "itemName"));
    }

    [Fact]
    public void SheetRowsWithMatchOrFindArePatterns()
    {
        var sheet = "target,key,match,field,find,member,member_match,zh-CN,en,comment\r\n"
            + "item,,.*,name|description,(?i)desc,,,描述,Desc,find in two fields of every item\r\n"
            + "ui,,.*,Text,^OK$,,Ui/.*\\.ui,确定,,every OK button of every form\r\n"
            + "strings,HELLO,,text,안녕,,,你好,,key with find edits one record\r\n"
            + "item,,2.*,price,,,,99,,match without find sets a value\r\n"
            + "strings,,.*,text, ,,,,,template pattern without text is skipped\r\n";
        using var prepared = Prepare("zh-CN", Select(Plugin("sheet.regex", Table, sheet)));
        var items = SpreadsheetDocument.Load(Member(prepared, "Config/Config.bfz", "Config/ShopItem2.xml"), "x.xml");
        Assert.Equal(("old 描述", "99"), (items.Read(ItemShape, ["100"], "itemDesc"), items.Read(ItemShape, ["200"], "price")));
        Assert.Equal("确定", UiDocument.Load(Member(prepared, "Ui/Ui.bfz", "Ui/Test.ui"), "Ui/Test.ui").Read("pnlMain/btnOk", "Text"));
        Assert.Equal("KeyString\tTranslated_Kr\r\nHELLO\t你好\r\n", Strings(prepared));
    }

    [Theory]
    [InlineData("target = \"item\"\nmatch = '(?=1).*'\nset = { name = \"x\" }", "linear-time engine")]
    [InlineData("target = \"item\"\nmatch = '(1'\nset = { name = \"x\" }", "invalid regex")]
    [InlineData("target = \"item\"\nkey = 100\nmatch = '.*'\nset = { name = \"x\" }", "exactly one of key and match")]
    [InlineData("target = \"item\"\nmatch = '.*'\nfind = 'a'\nreplace = \"b\"", "find needs fields")]
    [InlineData("target = \"item\"\naction = \"add\"\nmatch = '.*'\nset = { name = \"x\" }", "patterns patch or remove")]
    [InlineData("target = \"item\"\nkey = 999\nset = { name = \"x\" }", "has no record 999")]
    [InlineData("target = \"ui\"\nmatch = '.*'\nset = { Text = \"x\" }", "exactly one of member and member_match")]
    public void MalformedPatternsFailLoudly(string pattern, string message)
    {
        var error = Assert.Throws<InvalidDataException>(() => Prepare("zh-CN", Select(Plugin("bad.pattern", "[[patterns]]\n" + pattern + "\n", ""))));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SupplementaryCharactersAreMatchedWholeAndNeverSplit()
    {
        Write("StringTable/StringTable.bfz", [("StringTable/TranslatedStringTable.csv", ResourceEditTextTests.Utf16("KeyString\tTranslated_Kr\r\nWIDE\t𠀀字\r\n"))]);
        var whole = Plugin("whole", "[[patterns]]\ntarget = \"strings\"\nmatch = '.*'\nfields = [\"text\"]\nfind = '𠀀'\nreplace = \"𪚥\"\n", "");
        using (var prepared = Prepare("zh-CN", Select(whole))) Assert.Equal("KeyString\tTranslated_Kr\r\nWIDE\t𪚥字\r\n", Strings(prepared));
        var split = Plugin("split", "[[patterns]]\ntarget = \"strings\"\nmatch = '.*'\nfields = [\"text\"]\nfind = '\\uD840'\nreplace = \"\"\n", "");
        var error = Assert.Throws<InvalidDataException>(() => Prepare("zh-CN", Select(split)));
        Assert.Contains("splits a character", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpansionsAreCachedAndRecomputedWhenInputsChange()
    {
        var plugin = Select(Plugin("cached", "[[patterns]]\ntarget = \"item\"\nmatch = '.*'\nfields = [\"name\"]\nfind = 'e'\nreplace = \"E\"\n", ""));
        using (var first = Prepare("zh-CN", plugin)) Assert.False(first.Files.Single().CacheReused);
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "cache"), "*.patterns.json"));
        using (var second = Prepare("zh-CN", plugin)) Assert.True(second.Files.Single().CacheReused);
        Write("Config/Config.bfz", [("Config/ShopItem2.xml", ResourceEditDocumentTests.Workbook().Concat("<!-- v2 -->"u8.ToArray()).ToArray()),
            ("Config/FighterConfig.xml", ResourceEditHorizontalTests.Config())]);
        using var changed = Prepare("zh-CN", plugin);
        Assert.False(changed.Files.Single().CacheReused);
        Assert.Equal("SEcond", SpreadsheetDocument.Load(Member(changed, "Config/Config.bfz", "Config/ShopItem2.xml"), "x.xml").Read(ItemShape, ["200"], "itemName"));
    }

    [Fact]
    public void PatternsOverSharedKeysChangeEachRecordsOwnTextAndConflictAsOne()
    {
        // The client's string table repeats some keys with different texts.
        Write("StringTable/StringTable.bfz", [("StringTable/TranslatedStringTable.csv", ResourceEditTextTests.Utf16("KeyString\tTranslated_Kr\r\nA\t1\r\nB\tb\r\nA\t2\r\n"))]);
        var pattern = Plugin("a.dup", "[[patterns]]\ntarget = \"strings\"\nmatch = 'A'\nfields = [\"text\"]\nfind = '(\\d)'\nreplace = \"<$1>\"\n", "");
        using (var prepared = Prepare("zh-CN", Select(pattern)))
            Assert.Equal("KeyString\tTranslated_Kr\r\nA\t<1>\r\nB\tb\r\nA\t<2>\r\n", Strings(prepared));
        var literal = Plugin("b.literal", "[[strings]]\nkey = \"A\"\ntext = \"x\"\n", "");
        var report = Assert.Throws<GameResourceConflictException>(() => Prepare("zh-CN", Select(pattern), Select(literal))).Report;
        var conflict = Assert.Single(report.Conflicts);
        Assert.Equal(("A", "#2"), (conflict.Key, conflict.Field));
        Assert.Equal([("a.dup", "s:<1> / s:<2>"), ("b.literal", "s:x")], conflict.Parties.Select(p => (p.PluginId, p.Value)));
        using var kept = new BubbleFighterModule().PrepareResources(Context("zh-CN") with { ResourceConflictChoices = [new(report.Snapshot, conflict.Id, "b.literal")] },
            [Select(pattern), Select(literal)]);
        Assert.Equal("KeyString\tTranslated_Kr\r\nA\tx\r\nB\tb\r\nA\tx\r\n", Strings(kept));
    }

    [Fact]
    public void RemovePatternsDeleteMatchedRecordsAndControls()
    {
        var plugin = Plugin("remove", """
            [[patterns]]
            target = "strings"
            action = "remove"
            match = 'HEL.*'
            [[patterns]]
            target = "ui"
            member = "Ui/Test.ui"
            action = "remove"
            match = 'pnlMain/lblAlertStr\[\d\]'
            """, "");
        using var prepared = Prepare("zh-CN", Select(plugin));
        Assert.Equal("KeyString\tTranslated_Kr\r\n", Strings(prepared));
        var panel = Assert.Single(UiDocument.Load(Member(prepared, "Ui/Ui.bfz", "Ui/Test.ui"), "Ui/Test.ui").Controls());
        Assert.Equal(["pnlMain/lblTitle", "pnlMain/btnOk"], panel.Children.Select(c => c.Path));
    }
}
