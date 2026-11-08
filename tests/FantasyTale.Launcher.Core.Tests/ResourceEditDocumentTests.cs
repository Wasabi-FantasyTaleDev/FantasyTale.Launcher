using System.Text;
using System.Xml.Linq;
using FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class ResourceEditDocumentTests
{
    internal static readonly XNamespace Ss = "urn:schemas-microsoft-com:office:spreadsheet";
    private static readonly TableShape Item = new("ShopItem", 1, ["itemId"]);

    /// <summary>SpreadsheetML sample in the client's style: default and ss prefixes on one namespace, CRLF layout.</summary>
    internal static byte[] Workbook(string nameColumnFirst = "itemName", string other = "price", bool bom = true)
    {
        var text = "<?xml version=\"1.0\"?>\r\n<?mso-application progid=\"Excel.Sheet\"?>\r\n"
            + "<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:o=\"urn:schemas-microsoft-com:office:office\""
            + " xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">\r\n"
            + " <Styles>\r\n  <Style ss:ID=\"s1\"/>\r\n </Styles>\r\n"
            + " <Worksheet ss:Name=\"Notes\">\r\n  <Table>\r\n   <Row><Cell><Data ss:Type=\"String\">keep</Data></Cell></Row>\r\n  </Table>\r\n </Worksheet>\r\n"
            + " <Worksheet ss:Name=\"ShopItem\">\r\n  <Table ss:ExpandedColumnCount=\"5\" ss:ExpandedRowCount=\"3\" x:FullColumns=\"1\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">\r\n"
            + $"   <Row><Cell><Data ss:Type=\"String\">itemId</Data></Cell><Cell><Data ss:Type=\"String\">{nameColumnFirst}</Data></Cell>"
            + $"<Cell><Data ss:Type=\"String\">{other}</Data></Cell><Cell ss:Index=\"5\"><Data ss:Type=\"String\">itemDesc</Data></Cell></Row>\r\n"
            + "   <Row><Cell><Data ss:Type=\"Number\">100</Data></Cell><Cell ss:StyleID=\"s1\"><Data ss:Type=\"String\">Bubble</Data></Cell>"
            + "<Cell><Data ss:Type=\"Number\">25</Data></Cell><Cell ss:Index=\"5\" ss:Formula=\"=RC[-3]\"><Data ss:Type=\"String\">old desc</Data></Cell></Row>\r\n"
            + "   <Row><Cell><Data ss:Type=\"Number\">200</Data></Cell><Cell><Data ss:Type=\"String\">Second</Data></Cell>"
            // Rich text redeclares the default namespace, so the Data element inside it keeps its ss prefix.
            + "<Cell ss:Index=\"5\"><ss:Data ss:Type=\"String\" xmlns=\"http://www.w3.org/TR/REC-html40\"><Font>rich</Font></ss:Data></Cell></Row>\r\n"
            + "  </Table>\r\n </Worksheet>\r\n</Workbook>\r\n";
        var body = Encoding.UTF8.GetBytes(text);
        return bom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    private static RecordEdit Edit(EditAction action, string key, params (string Column, string? Text)[] set) =>
        new(action, [key], set.ToDictionary(p => p.Column, p => new CellValue(p.Text), StringComparer.Ordinal), new Dictionary<string, string>());

    private static XElement[] Rows(byte[] bytes) => XDocument.Parse(Encoding.UTF8.GetString(bytes).TrimStart('﻿'))
        .Root!.Elements(Ss + "Worksheet").Single(w => (string?)w.Attribute(Ss + "Name") == "ShopItem").Element(Ss + "Table")!.Elements(Ss + "Row").ToArray();

    private static string? Cell(XElement row, int column)
    {
        var index = 0;
        foreach (var cell in row.Elements(Ss + "Cell"))
        {
            index = (int?)cell.Attribute(Ss + "Index") ?? index + 1;
            if (index == column) return cell.Element(Ss + "Data")?.Value;
            index += (int?)cell.Attribute(Ss + "MergeAcross") ?? 0;
        }
        return null;
    }

    [Fact]
    public void PatchWritesOnlyNamedFieldsAndKeepsEverythingElse()
    {
        var original = Workbook();
        var document = SpreadsheetDocument.Load(original, "Config/ShopItem2.xml");
        document.Apply(Item, Edit(EditAction.Patch, "100", ("itemName", "泡泡 \"<&>\"")));
        var saved = document.Save();
        Assert.Equal(original[..3], saved[..3]);
        var text = Encoding.UTF8.GetString(saved, 3, saved.Length - 3);
        Assert.StartsWith("<?xml version=\"1.0\"?>\r\n<?mso-application progid=\"Excel.Sheet\"?>\r\n<Workbook ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n ", text.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("<ss:Row", text, StringComparison.Ordinal);
        Assert.Contains("<Data ss:Type=\"String\">keep</Data>", text, StringComparison.Ordinal);
        Assert.Contains("<ss:Data ss:Type=\"String\" xmlns=\"http://www.w3.org/TR/REC-html40\"><Font>rich</Font></ss:Data>", text, StringComparison.Ordinal);
        var row = Rows(saved)[1];
        Assert.Equal("泡泡 \"<&>\"", Cell(row, 2));
        Assert.Equal("s1", (string?)row.Elements(Ss + "Cell").ElementAt(1).Attribute(Ss + "StyleID"));
        Assert.Equal("25", Cell(row, 3));
        Assert.Equal("=RC[-3]", (string?)row.Elements(Ss + "Cell").Last().Attribute(Ss + "Formula"));
        Assert.Equal("Second", Cell(Rows(saved)[2], 2));
    }

    [Fact]
    public void ColumnsAreFoundByHeaderWhateverTheirOrder()
    {
        // Same logical field, another build's column order: itemName is the third column here.
        var swapped = SpreadsheetDocument.Load(Workbook("price", "itemName"), "x.xml");
        swapped.Apply(Item, new(EditAction.Patch, ["100"], new Dictionary<string, CellValue> { ["itemName"] = new("77", true) }, new Dictionary<string, string>()));
        var row = Rows(swapped.Save())[1];
        Assert.Equal("77", Cell(row, 3));
        Assert.Equal("Bubble", Cell(row, 2));
    }

    [Fact]
    public void SparseCellsKeepTheirIndexesWhenWrittenBetween()
    {
        var document = SpreadsheetDocument.Load(Workbook(), "x.xml");
        // Column 4 has no header and no cell; write it through a one-based position reference.
        document.Apply(Item, Edit(EditAction.Patch, "100", ("#4", "fourth"), ("itemDesc", "new desc")));
        var row = Rows(document.Save())[1];
        Assert.Equal("fourth", Cell(row, 4));
        Assert.Equal("new desc", Cell(row, 5));
        Assert.Equal("25", Cell(row, 3));
    }

    [Fact]
    public void AddReplaceAndRemoveUseBusinessKeysAndUpdateCounts()
    {
        var document = SpreadsheetDocument.Load(Workbook(), "x.xml");
        document.Apply(Item, Edit(EditAction.Add, "300", ("itemName", "Third"), ("itemDesc", "desc")));
        document.Apply(Item, Edit(EditAction.Replace, "200", ("itemName", "Replaced")));
        document.Apply(Item, Edit(EditAction.Remove, "100"));
        document.Apply(Item, Edit(EditAction.Upsert, "400", ("itemName", "Fourth")));
        var saved = document.Save();
        var rows = Rows(saved);
        Assert.Equal(["itemId", "200", "300", "400"], rows.Select(r => Cell(r, 1)));
        Assert.Equal("Number", (string?)rows[2].Elements(Ss + "Cell").First().Element(Ss + "Data")!.Attribute(Ss + "Type"));
        Assert.Equal("Replaced", Cell(rows[1], 2));
        Assert.Equal("desc", Cell(rows[2], 5));
        var table = rows[0].Parent!;
        Assert.Equal("4", (string?)table.Attribute(Ss + "ExpandedRowCount"));
        Assert.Throws<InvalidDataException>(() => document.Apply(Item, Edit(EditAction.Add, "200", ("itemName", "dup"))));
        Assert.Throws<InvalidDataException>(() => document.Apply(Item, Edit(EditAction.Patch, "999", ("itemName", "missing"))));
        Assert.Throws<InvalidDataException>(() => document.Apply(Item, Edit(EditAction.Patch, "200", ("itemId", "201"))));
    }

    [Fact]
    public void UnsafeSpreadsheetWritesFailLoudly()
    {
        var document = SpreadsheetDocument.Load(Workbook(), "x.xml");
        Assert.Throws<InvalidDataException>(() => document.Apply(Item, Edit(EditAction.Patch, "100", ("price", "not a number"))));
        Assert.Throws<InvalidDataException>(() => document.Apply(Item, Edit(EditAction.Patch, "100", ("unknownColumn", "x"))));
        var precondition = new RecordEdit(EditAction.Patch, ["100"], new Dictionary<string, CellValue> { ["itemName"] = new("x") },
            new Dictionary<string, string> { ["itemName"] = "not the original" });
        Assert.Throws<InvalidDataException>(() => document.Apply(Item, precondition));
        var horizontal = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Workbook(bom: false)).Replace(">itemId<", ">Fighter:HorzTable<", StringComparison.Ordinal));
        var error = Assert.Throws<InvalidDataException>(() => SpreadsheetDocument.Load(horizontal, "Config/FighterConfig.xml").Apply(new("ShopItem", 2, ["[Field]"]), Edit(EditAction.Patch, "a", ("b", "c"))));
        Assert.Contains("horizontal", error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => SpreadsheetDocument.Load("<root/>"u8.ToArray(), "x.xml"));
    }
}
