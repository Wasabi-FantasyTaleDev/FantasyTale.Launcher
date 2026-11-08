using System.Text;
using FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ResourceEditTextTests
{
    /// <summary>Text in every language the interfaces must carry, plus marks, quotes and XML-reserved characters.</summary>
    internal static readonly string[] Languages =
    [
        "泡泡战士 简体", "泡泡戰士 繁體", "버블파이터 한국어", "バブルファイター 日本語", "Bubble Fighter", "Tiếng Việt đấu sĩ",
        "Bahasa Indonesia petarung", "Ünïcödé àçcéñts", "quote \" and ' apostrophe", "<tag> & ampersand", "emoji-free 𠀀 supplementary"
    ];

    internal static byte[] Utf16(string text) => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)];

    private static RecordEdit Edit(EditAction action, string[] key, params (string Column, string? Text)[] set) =>
        new(action, key, set.ToDictionary(p => p.Column, p => new CellValue(p.Text), StringComparer.Ordinal), new Dictionary<string, string>());

    [Fact]
    public void StringTableKeepsEmptyHeadersTrailingColumnsAndOtherLanguages()
    {
        // cn-406 shape: empty first header, several translation columns and trailing empty columns.
        var original = Utf16("\tTranslated_Kr\tTranslated_En\tTranslated_Cn\t\t\r\nKEY_A\tko\ten\tcn\t\t\r\nKEY_B\tko2\ten2\tcn2\t\t");
        var document = DelimitedDocument.Load(original, "StringTable/TranslatedStringTable.csv");
        var shape = new TableShape(null, 1, ["#1"]);
        document.Apply(shape, Edit(EditAction.Patch, ["KEY_A"], ("Translated_Cn", "新文本")));
        document.Apply(shape, Edit(EditAction.Upsert, ["KEY_C"], ("Translated_Cn", "新增")));
        document.Apply(shape, Edit(EditAction.Upsert, ["KEY_B"], ("Translated_En", "patched")));
        var saved = document.Save();
        Assert.Equal(original[..2], saved[..2]);
        Assert.Equal("\tTranslated_Kr\tTranslated_En\tTranslated_Cn\t\t\r\nKEY_A\tko\ten\t新文本\t\t\r\nKEY_B\tko2\tpatched\tcn2\t\t\r\nKEY_C\t\t\t新增\t\t",
            Encoding.Unicode.GetString(saved, 2, saved.Length - 2));
    }

    [Fact]
    public void CompositeKeysTellRecordsApartAndRemoveKeepsLineEndings()
    {
        var original = Utf16("Category\tSubCategory\tKeyString\tComment\tTranslated_Kr\r\nUi\tA\tOK\tnote\t확인\r\nUi\tB\tOK\t\t확인2\r\n");
        var document = DelimitedDocument.Load(original, "StringTable/TranslatedXmlTable.csv");
        var shape = new TableShape(null, 1, ["Category", "SubCategory", "KeyString"]);
        document.Apply(shape, Edit(EditAction.Patch, ["Ui", "B", "OK"], ("Translated_Kr", "변경")));
        document.Apply(shape, Edit(EditAction.Remove, ["Ui", "A", "OK"]));
        Assert.Equal("Category\tSubCategory\tKeyString\tComment\tTranslated_Kr\r\nUi\tB\tOK\t\t변경\r\n", Encoding.Unicode.GetString(document.Save()[2..]));
        Assert.Throws<InvalidDataException>(() => document.Apply(shape, Edit(EditAction.Patch, ["Ui", "B", "OK"], ("Translated_Kr", "tab\tinside"))));
        Assert.Throws<InvalidDataException>(() => document.Apply(shape, Edit(EditAction.Patch, ["Ui", "B", "OK"], ("Translated_Kr", "line\nbreak"))));
        Assert.Throws<InvalidDataException>(() => document.Apply(shape, Edit(EditAction.Patch, ["OK"], ("Translated_Kr", "x"))));
    }

    [Fact]
    public void EveryLanguageRoundTripsThroughEachFormat()
    {
        var shape = new TableShape(null, 1, ["KeyString"]);
        var table = DelimitedDocument.Load(Utf16("KeyString\tTranslated_Kr\r\n"), "x.csv");
        for (var i = 0; i < Languages.Length; i++) table.Apply(shape, Edit(EditAction.Add, ["K" + i], ("Translated_Kr", Languages[i])));
        var reread = DelimitedDocument.Load(table.Save(), "x.csv");
        var sheet = SpreadsheetDocument.Load(ResourceEditDocumentTests.Workbook(), "x.xml");
        var item = new TableShape("ShopItem", 1, ["itemId"]);
        var form = UiDocument.Load(Utf16(ResourceEditUiTests.Form), "Ui/Test.ui");
        for (var i = 0; i < Languages.Length; i++)
        {
            Assert.Equal(Languages[i], reread.Read(shape, ["K" + i], "Translated_Kr"));
            sheet.Apply(item, Edit(EditAction.Upsert, [(1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture)], ("itemName", Languages[i] + "\nsecond line")));
        }
        form.Apply(new(EditAction.Patch, "pnlMain/lblTitle", new Dictionary<string, string> { ["Text"] = string.Join("\n", Languages) }, new Dictionary<string, string>(), null, null));
        var sheetAgain = SpreadsheetDocument.Load(sheet.Save(), "x.xml");
        for (var i = 0; i < Languages.Length; i++)
            Assert.Equal(Languages[i] + "\nsecond line", sheetAgain.Read(item, [(1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture)], "itemName"));
        Assert.Equal(string.Join("\n", Languages), UiDocument.Load(form.Save(), "Ui/Test.ui").Read("pnlMain/lblTitle", "Text"));
    }

    [Fact]
    public void InvalidOrUnencodableTextIsAnErrorNotAReplacementCharacter()
    {
        Assert.Throws<InvalidDataException>(() => DelimitedDocument.Load([0xEF, 0xBB, 0xBF, 0x41, 0xC3], "bad.csv"));
        Assert.Throws<InvalidDataException>(() => SpreadsheetDocument.Load([0x3C, 0xFF, 0x3E], "bad.xml"));
        Assert.Throws<InvalidDataException>(() => DelimitedDocument.Load([0xFF, 0xFE, 0x00, 0xD8], "lone-surrogate.csv"));
        var table = DelimitedDocument.Load(Utf16("KeyString\tTranslated_Kr\r\n"), "x.csv");
        table.Apply(new(null, 1, ["KeyString"]), Edit(EditAction.Add, ["K"], ("Translated_Kr", "\uD800")));
        Assert.Throws<InvalidDataException>(() => table.Save());
        var sheet = SpreadsheetDocument.Load(ResourceEditDocumentTests.Workbook(), "x.xml");
        sheet.Apply(new("ShopItem", 1, ["itemId"]), Edit(EditAction.Patch, ["100"], ("itemName", "\uD800")));
        Assert.Throws<InvalidDataException>(() => sheet.Save());
    }

    [Fact]
    public void SharedBusinessKeysAddressEveryRecordWithThem()
    {
        // The client's own tables repeat some keys; which record it reads is unknown, so an edit changes all of them.
        var shape = new TableShape(null, 1, ["KeyString"]);
        var document = DelimitedDocument.Load(Utf16("KeyString\tTranslated_Kr\r\nA\t1\r\nB\tb\r\nA\t2\r\n"), "x.csv");
        Assert.Equal(["1", "2"], document.ReadAll(shape, ["A"], "Translated_Kr"));
        Assert.Throws<InvalidDataException>(() => document.Read(shape, ["A"], "Translated_Kr"));
        document.Apply(shape, Edit(EditAction.Patch, ["A"], ("Translated_Kr", "x")));
        Assert.Equal("KeyString\tTranslated_Kr\r\nA\tx\r\nB\tb\r\nA\tx\r\n", Encoding.Unicode.GetString(document.Save()[2..]));
        document.Apply(shape, Edit(EditAction.Patch, ["A"], []) with { Occurrences =
            [new Dictionary<string, CellValue> { ["Translated_Kr"] = new("first") }, new Dictionary<string, CellValue>()] });
        Assert.Equal(["first", "x"], document.ReadAll(shape, ["A"], "Translated_Kr"));
        document.Apply(shape, Edit(EditAction.Remove, ["A"]));
        Assert.Equal("KeyString\tTranslated_Kr\r\nB\tb\r\n", Encoding.Unicode.GetString(document.Save()[2..]));
        Assert.Throws<InvalidDataException>(() => document.Apply(shape, Edit(EditAction.Patch, ["B"], []) with
            { Occurrences = [new Dictionary<string, CellValue>(), new Dictionary<string, CellValue>()] }));
    }

    [Theory]
    [InlineData("NEW\tINJECTED")]
    [InlineData("NEW\nFAKE")]
    [InlineData("NEW\rFAKE")]
    public void NewStringKeysCannotInjectColumnsOrRows(string key)
    {
        var original = Utf16("KeyString\tTranslated_Kr\r\nA\tOriginal\r\n");
        var table = DelimitedDocument.Load(original, "x.csv");
        Assert.Throws<InvalidDataException>(() => table.Apply(new(null, 1, ["KeyString"]), Edit(EditAction.Add, [key], ("Translated_Kr", "Translation"))));
        Assert.Equal(original, table.Save());
    }

    [Theory]
    [InlineData("euc-kr")]
    [InlineData("utf-16")]
    public void XmlCannotPreserveAnEncodingDeclarationThatDisagreesWithItsBytes(string declared)
    {
        var bytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"" + declared + "\"?><root />");
        Assert.Throws<InvalidDataException>(() => XmlMember.Load(bytes, "x.xml"));
    }
}
