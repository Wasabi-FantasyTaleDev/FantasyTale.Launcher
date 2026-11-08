using System.Text;
using System.Xml.Linq;
using FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ResourceEditHorizontalTests
{
    private static readonly XNamespace Ss = ResourceEditDocumentTests.Ss;
    private static readonly TableShape Fighter = new("FighterConfig", 2, ["[Field]"], "horizontal");

    private static string S(string text) => "<Cell><Data ss:Type=\"String\">" + text + "</Data></Cell>";
    private static string N(string text) => "<Cell><Data ss:Type=\"Number\">" + text + "</Data></Cell>";

    /// <summary>FighterConfig-shaped sample: marker row, key row, two sections repeating a field name, one sparse cell.</summary>
    internal static byte[] Config(string merge = "")
    {
        var text = "<?xml version=\"1.0\"?>\r\n<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">\r\n"
            + " <Worksheet ss:Name=\"FighterConfig\">\r\n  <Table ss:ExpandedColumnCount=\"5\" ss:ExpandedRowCount=\"7\">\r\n"
            + "   <Column ss:Width=\"80\"/><Column ss:Width=\"120\"/><Column ss:Width=\"40\" ss:Span=\"2\"/>\r\n"
            + "   <Row>" + S("FighterConfig:HorzTable") + "</Row>\r\n"
            + "   <Row>" + S("[Field]") + S("[Desc]") + N("1") + N("2") + N("3") + "</Row>\r\n"
            + "   <Row>" + S("#Basic") + S("basic") + merge + "</Row>\r\n"
            + "   <Row>" + S("name") + S("display name") + S("Bazzi") + S("Dao") + S("Marid") + "</Row>\r\n"
            + "   <Row>" + S("speed") + S("move speed") + N("5") + "<Cell ss:Index=\"5\"><Data ss:Type=\"Number\">7</Data></Cell>" + "</Row>\r\n"
            + "   <Row>" + S("#Skill") + S("skill") + "</Row>\r\n"
            + "   <Row>" + S("name") + S("skill name") + S("Kick") + S("Throw") + S("Push") + "</Row>\r\n"
            + "  </Table>\r\n </Worksheet>\r\n</Workbook>\r\n";
        return Encoding.UTF8.GetBytes(text);
    }

    [Fact]
    public void PatchFindsRecordsByKeyAndFieldsBySectionAndKeepsTypes()
    {
        var document = SpreadsheetDocument.Load(Config(), "Config/FighterConfig.xml");
        document.Apply(Fighter, Edit(EditAction.Patch, "2", ("#Basic/name", "道"), ("speed", "6"), ("#Skill/name", "投掷")));
        var table = Table(document.Save());
        Assert.Equal(["name", "display name", "Bazzi", "道", "Marid"], Row(table, 4, 5));
        Assert.Equal(["speed", "move speed", "5", "6", "7"], Row(table, 5, 5));
        // A new cell beside numeric record cells is a Number, and the sparse cell after it keeps column 5.
        var speed = table.Elements(Ss + "Row").ElementAt(4).Elements(Ss + "Cell").ToArray();
        Assert.Equal("Number", (string?)speed[3].Element(Ss + "Data")!.Attribute(Ss + "Type"));
        Assert.Equal(["name", "skill name", "Kick", "投掷", "Push"], Row(table, 7, 5));
        Assert.Equal("Marid", SpreadsheetDocument.Load(document.Save(), "x.xml").Read(Fighter, ["3"], "#Basic/name"));
    }

    [Fact]
    public void AddAppendsAColumnAndRemoveShiftsLaterRecordsLeft()
    {
        var document = SpreadsheetDocument.Load(Config(), "x.xml");
        document.Apply(Fighter, Edit(EditAction.Add, "9", ("#Basic/name", "New"), ("speed", "8")));
        document.Apply(Fighter, Edit(EditAction.Remove, "2"));
        document.Apply(Fighter, Edit(EditAction.Upsert, "3", ("#Skill/name", "Shove")));
        var table = Table(document.Save());
        Assert.Equal(["[Field]", "[Desc]", "1", "3", "9"], Row(table, 2, 5));
        Assert.Equal("Number", (string?)table.Elements(Ss + "Row").ElementAt(1).Elements(Ss + "Cell").Last().Element(Ss + "Data")!.Attribute(Ss + "Type"));
        Assert.Equal(["name", "display name", "Bazzi", "Marid", "New"], Row(table, 4, 5));
        Assert.Equal(["speed", "move speed", "5", "7", "8"], Row(table, 5, 5));
        Assert.Equal(["name", "skill name", "Kick", "Shove", ""], Row(table, 7, 5));
        Assert.Equal("5", (string?)table.Attribute(Ss + "ExpandedColumnCount"));
        // The two-column definition lost one column: it now covers columns 3 and 4 only.
        Assert.Equal("1", (string?)table.Elements(Ss + "Column").Last().Attribute(Ss + "Span"));
    }

    [Fact]
    public void ReplaceClearsTheRecordsOtherFieldsOnly()
    {
        var document = SpreadsheetDocument.Load(Config(), "x.xml");
        document.Apply(Fighter, Edit(EditAction.Replace, "1", ("#Basic/name", "Only")));
        var table = Table(document.Save());
        Assert.Equal(["name", "display name", "Only", "Dao", "Marid"], Row(table, 4, 5));
        Assert.Equal(["speed", "move speed", "", "", "7"], Row(table, 5, 5));
        Assert.Equal(["", "Throw"], Row(table, 7, 5)[2..4]);
    }

    [Fact]
    public void AmbiguousMissingAndUnsafeHorizontalEditsFailLoudly()
    {
        var document = SpreadsheetDocument.Load(Config(), "x.xml");
        var ambiguous = Assert.Throws<InvalidDataException>(() => document.Apply(Fighter, Edit(EditAction.Patch, "1", ("name", "x"))));
        Assert.Contains("#Section/name", ambiguous.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => document.Apply(Fighter, Edit(EditAction.Patch, "1", ("unknown", "x"))));
        Assert.Throws<InvalidDataException>(() => document.Apply(Fighter, Edit(EditAction.Patch, "99", ("speed", "1"))));
        Assert.Throws<InvalidDataException>(() => document.Apply(Fighter, Edit(EditAction.Add, "1", ("speed", "1"))));
        Assert.Throws<InvalidDataException>(() => document.Apply(Fighter, Edit(EditAction.Patch, "1", ("speed", "fast"))));
        Assert.Throws<InvalidDataException>(() => document.Apply(Fighter, Edit(EditAction.Patch, "1", ("#2", "1"))));
        // A section banner merged across the record columns cannot lose one of them.
        var merged = SpreadsheetDocument.Load(Config("<Cell ss:MergeAcross=\"2\"><Data ss:Type=\"String\">banner</Data></Cell>"), "x.xml");
        Assert.Throws<InvalidDataException>(() => merged.Apply(Fighter, Edit(EditAction.Remove, "2")));
        var vertical = SpreadsheetDocument.Load(ResourceEditDocumentTests.Workbook(), "x.xml");
        Assert.Throws<InvalidDataException>(() => vertical.Apply(new("ShopItem", 1, ["[Field]"], "horizontal"), Edit(EditAction.Patch, "100", ("itemName", "x"))));
        Assert.Throws<InvalidDataException>(() => document.Apply(new("FighterConfig", 1, ["itemId"], "vertical"), Edit(EditAction.Patch, "1", ("speed", "1"))));
    }

    private static RecordEdit Edit(EditAction action, string key, params (string Field, string? Text)[] set) =>
        new(action, [key], set.ToDictionary(p => p.Field, p => new CellValue(p.Text), StringComparer.Ordinal), new Dictionary<string, string>());

    private static XElement Table(byte[] bytes) => XDocument.Parse(Encoding.UTF8.GetString(bytes)).Root!.Element(Ss + "Worksheet")!.Element(Ss + "Table")!;

    /// <summary>Cell texts of one row by column; a missing cell or data reads as an empty string.</summary>
    private static string[] Row(XElement table, int row, int width)
    {
        var values = Enumerable.Repeat("", width).ToArray();
        var index = 0;
        foreach (var cell in table.Elements(Ss + "Row").ElementAt(row - 1).Elements(Ss + "Cell"))
        {
            index = (int?)cell.Attribute(Ss + "Index") ?? index + 1;
            values[index - 1] = cell.Element(Ss + "Data")?.Value ?? "";
        }
        return values;
    }
}
