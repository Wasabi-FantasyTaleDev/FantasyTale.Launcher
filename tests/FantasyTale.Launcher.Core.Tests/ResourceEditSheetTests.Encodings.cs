using System.Text;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class ResourceEditSheetTests
{
    private const string Multilingual = "target,key,field,zh-CN,zh-TW,ko,ja,vi,en\r\n"
        + "strings,HELLO,text,你好,你好繁體,안녕하세요,こんにちは,Xin chào,𠀀 Hello\r\n";

    private static Encoding Unicode(string name) => name switch
    {
        "utf-8" => new UTF8Encoding(false), "utf-16le" => new UnicodeEncoding(false, false), "utf-16be" => new UnicodeEncoding(true, false),
        "utf-32le" => new UTF32Encoding(false, false), _ => new UTF32Encoding(true, false)
    };

    private static byte[] Encode(string name, bool bom, string text) =>
        [.. bom ? Unicode(name).GetBytes("\uFEFF") : [], .. Unicode(name).GetBytes(text)];

    [Theory]
    [InlineData("utf-8", true)] [InlineData("utf-8", false)]
    [InlineData("utf-16le", true)] [InlineData("utf-16le", false)]
    [InlineData("utf-16be", true)] [InlineData("utf-16be", false)]
    [InlineData("utf-32le", true)] [InlineData("utf-32le", false)]
    [InlineData("utf-32be", true)] [InlineData("utf-32be", false)]
    public void SheetsInEveryUnicodeEncodingCarryEveryLanguage(string encoding, bool bom)
    {
        var plugin = Select(Plugin("enc", Table, "", Encode(encoding, bom, Multilingual)));
        foreach (var (language, text) in new[] { ("zh-CN", "你好"), ("zh-TW", "你好繁體"), ("ko", "안녕하세요"), ("ja", "こんにちは"), ("vi", "Xin chào"), ("en", "𠀀 Hello") })
        {
            using var prepared = new BubbleFighterModule().PrepareResources(Context(language), [plugin]);
            var table = Encoding.Unicode.GetString(Member(prepared, "StringTable/StringTable.bfz", "StringTable/TranslatedStringTable.csv")[2..]);
            Assert.Equal("KeyString\tTranslated_Kr\r\nHELLO\t" + text + "\r\n", table);
        }
    }

    [Fact]
    public void DeclaredEncodingsAreUsedAndCheckedAgainstTheBom()
    {
        // A header that starts with a non-ASCII cell cannot be recognised without a BOM; a declaration settles it.
        var chinese = Encode("utf-16le", false, "中文,target\r\n");
        Assert.Equal("中文", CsvSheet.Parse(chinese, "x.csv", "utf-16le").Header[0]);
        Assert.Throws<InvalidDataException>(() => CsvSheet.Parse(chinese, "x.csv"));
        var mismatch = Assert.Throws<InvalidDataException>(() => CsvSheet.Parse(Encode("utf-16le", true, "a,b\r\n"), "x.csv", "utf-8"));
        Assert.Contains("byte order mark says utf-16le", mismatch.Message, StringComparison.Ordinal);
        var misread = Assert.Throws<InvalidDataException>(() => CsvSheet.Parse(Encode("utf-16le", false, "a,b\r\n"), "x.csv", "utf-8"));
        Assert.Contains("NUL", misread.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => CsvSheet.Parse("a,b"u8.ToArray(), "x.csv", "gbk"));
        // The manifest key reaches the reader.
        using var prepared = new BubbleFighterModule().PrepareResources(Context("ko"),
            [Select(Plugin("declared", "[[tables]]\npath = \"sheet.csv\"\nencoding = \"UTF-32BE\"\n", "", Encode("utf-32be", false, Multilingual)))]);
        Assert.Contains("HELLO\t안녕하세요", Encoding.Unicode.GetString(Member(prepared, "StringTable/StringTable.bfz", "StringTable/TranslatedStringTable.csv")), StringComparison.Ordinal);
    }
}
