using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Games.BubbleFighter;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;
using FantasyTale.Launcher.Sdk;
using Xunit.Abstractions;

namespace FantasyTale.Launcher.Core.Tests;

/// <summary>Read-only acceptance against an installed kr-812 client; archives are copied and the client is never written.</summary>
public sealed class ResourceEditClientTests(ITestOutputHelper output)
{
    private const string Variable = "FT_TEST_KR812_CLIENT";
    private static readonly string[] Archives = ["Config/Config.bfz", "StringTable/StringTable.bfz", "Ui/Ui.bfz"];

    private sealed class ClientFactAttribute : FactAttribute
    {
        public ClientFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
                Skip = "Set " + Variable + " to an installed kr-812 client folder for read-only resource edit acceptance.";
        }
    }

    [ClientFact]
    public void RealArchivesTakeItemStringAndUiEditsAndKeepEverythingElse()
    {
        var source = Environment.GetEnvironmentVariable(Variable)!;
        var root = Directory.CreateTempSubdirectory("ft-kr812-edit-").FullName;
        try
        {
            var client = Directory.CreateDirectory(Path.Combine(root, "client")).FullName;
            foreach (var archive in Archives)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(client, archive))!);
                File.Copy(Path.Combine(source, archive), Path.Combine(client, archive));
            }
            var forms = new UiForms();
            var form = forms.List(client, "kr-812").First(m => m.EndsWith("MainMenuStage2.ui", StringComparison.OrdinalIgnoreCase));
            var label = Flatten(forms.Read(client, "kr-812", form)).First(c => c.Attributes.Any(a => a.Key == "Text"));
            var (_, items, strings) = Originals(client);
            var item = items.First(k => k != "itemId");
            var key = strings.First();
            var package = Plugin(root, $$"""
                [[items]]
                id = "{{item}}"
                name = { "zh-CN" = "验收 名称", ko = "검수 이름" }
                [[strings]]
                key = "{{key}}"
                text = "Tiếng Việt / 日本語 / 繁體"
                [[strings]]
                key = "FT_ACCEPTANCE_NEW"
                text = "Bahasa Indonesia"
                [[edits]]
                target = "ui"
                member = "{{form}}"
                action = "patch"
                control = "{{label.Path}}"
                set = { Text = "验收 <&> \"quote\"" }
                """);
            var clock = Stopwatch.StartNew();
            using var prepared = new BubbleFighterModule().PrepareResources(Context(root, client),
                [new GameResourceSelection(package, new Dictionary<string, object>())]);
            output.WriteLine("prepared in " + clock.Elapsed);
            Assert.Equal(Archives, prepared.Files.Select(f => f.Target));
            Verify(client, prepared, item, key, form, label.Path);
            foreach (var archive in Archives) Assert.Equal(File.ReadAllBytes(Path.Combine(source, archive)), File.ReadAllBytes(Path.Combine(client, archive)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static readonly XNamespace Ss = ResourceEditDocumentTests.Ss;
    private const string Items = "Config/ShopItem2.xml";
    private const string Strings = "StringTable/TranslatedStringTable.csv";

    [ClientFact]
    public void RealHorizontalFighterConfigTakesAPatchAndANewRecordColumn()
    {
        var source = Environment.GetEnvironmentVariable(Variable)!;
        var root = Directory.CreateTempSubdirectory("ft-kr812-horz-").FullName;
        try
        {
            var client = Directory.CreateDirectory(Path.Combine(root, "client")).FullName;
            Directory.CreateDirectory(Path.Combine(client, "Config"));
            File.Copy(Path.Combine(source, Archives[0]), Path.Combine(client, Archives[0]));
            const string member = "Config/FighterConfig.xml";
            var before = Xml(Read(Path.Combine(client, Archives[0]), Archives[0], member));
            var table = Sheet(before, "FighterConfig");
            var rows = table.Elements(Ss + "Row").ToArray();
            var keyRow = Array.FindIndex(rows, r => Cells(r).GetValueOrDefault(1) == "[Field]");
            var record = Cells(rows[keyRow])[3];
            var names = rows.Skip(keyRow + 1).Select(r => Cells(r).GetValueOrDefault(1, "")).ToArray();
            var fieldRow = Enumerable.Range(keyRow + 1, rows.Length - keyRow - 1).First(i =>
                names[i - keyRow - 1] is { Length: > 0 } n && !n.StartsWith('#') && names.Count(x => x == n) == 1 && Cells(rows[i]).ContainsKey(3));
            var field = names[fieldRow - keyRow - 1];
            var numeric = double.TryParse(Cells(rows[fieldRow])[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
            var value = numeric ? "12345" : "验收值";
            output.WriteLine($"record {record}, field {field} (row {fieldRow + 1}), numeric {numeric}");
            var package = Plugin(root, $$"""
                [[edits]]
                target = "fighter"
                action = "patch"
                key = "{{record}}"
                set = { "{{field}}" = "{{value}}" }
                [[edits]]
                target = "fighter"
                action = "add"
                key = "987654"
                set = { "{{field}}" = "{{value}}" }
                """);
            using var prepared = new BubbleFighterModule().PrepareResources(Context(root, client), [new GameResourceSelection(package, new Dictionary<string, object>())]);
            var after = Xml(Read(Assert.Single(prepared.Files).Source, Archives[0], member));
            var newRows = Sheet(after, "FighterConfig").Elements(Ss + "Row").ToArray();
            Assert.Equal(rows.Length, newRows.Length);
            var added = Cells(newRows[keyRow]).Single(p => p.Value == "987654").Key;
            for (var i = 0; i < rows.Length; i++)
            {
                var (a, b) = (Cells(rows[i]), Cells(newRows[i]));
                if (i == fieldRow) { Assert.Equal(value, b[3]); Assert.Equal(value, b[added]); a[3] = value; a[added] = value; }
                if (i == keyRow) a[added] = "987654";
                Assert.Equal(a.OrderBy(p => p.Key), b.OrderBy(p => p.Key));
            }
            foreach (var (a, b) in before.Root!.Elements(Ss + "Worksheet").Zip(after.Root!.Elements(Ss + "Worksheet")))
                if ((string?)a.Attribute(Ss + "Name") != "FighterConfig") Assert.True(XNode.DeepEquals(a, b));
            Assert.Equal(File.ReadAllBytes(Path.Combine(source, Archives[0])), File.ReadAllBytes(Path.Combine(client, Archives[0])));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [ClientFact]
    public void RealPatternsRewriteEveryItemNameAndStringAndKeepEverythingElse()
    {
        var source = Environment.GetEnvironmentVariable(Variable)!;
        var root = Directory.CreateTempSubdirectory("ft-kr812-regex-").FullName;
        try
        {
            var client = Directory.CreateDirectory(Path.Combine(root, "client")).FullName;
            foreach (var archive in Archives)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(client, archive))!);
                File.Copy(Path.Combine(source, archive), Path.Combine(client, archive));
            }
            var package = Plugin(root, """
                [[patterns]]
                target = "item"
                match = '.*'
                fields = ["name"]
                find = '^(.)'
                replace = "[T]$1"
                [[patterns]]
                target = "strings"
                match = '.*'
                fields = ["text"]
                find = '^(.+)$'
                replace = "> $1"
                [[patterns]]
                target = "ui"
                member_match = 'Ui/.*\.ui'
                match = '.*'
                fields = ["Text"]
                find = '(.+)'
                replace = "《$1》"
                """);
            var clock = Stopwatch.StartNew();
            using var prepared = new BubbleFighterModule().PrepareResources(Context(root, client), [new GameResourceSelection(package, new Dictionary<string, object>())]);
            output.WriteLine("prepared in " + clock.Elapsed);
            var before = Xml(Read(Path.Combine(client, Archives[0]), Archives[0], Items));
            var after = Xml(Read(prepared.Files[0].Source, Archives[0], Items));
            var name = Cells(Rows(before)[0]).Single(p => p.Value == "itemName").Key;
            var (oldRows, newRows) = (Rows(before), Rows(after));
            var changed = 0;
            for (var i = 1; i < oldRows.Length; i++)
            {
                var (a, b) = (Cells(oldRows[i]), Cells(newRows[i]));
                if (a.TryGetValue(name, out var text) && text.Length > 0) { Assert.Equal("[T]" + text, b[name]); a[name] = b[name]; changed++; }
                Assert.Equal(a, b);
            }
            output.WriteLine("item names changed: " + changed);
            var oldLines = Lines(Read(Path.Combine(client, Archives[1]), Archives[1], Strings));
            var newLines = Lines(Read(prepared.Files[1].Source, Archives[1], Strings));
            Assert.Equal(oldLines.Length, newLines.Length);
            for (var i = 1; i < oldLines.Length; i++)
                if (oldLines[i].Split('\t') is [var key, { Length: > 0 } text, ..]) Assert.StartsWith(key + "\t> " + text, newLines[i], StringComparison.Ordinal);
            // Every control of every form, same-named and unnamed ones included, matches its original by path; only Text
            // differs, rewritten line by line because (.+) stops at line breaks.
            var forms = new UiForms();
            var wrapped = 0;
            foreach (var form in forms.List(client, "kr-812"))
            {
                var original = Flatten(forms.Parse(Read(Path.Combine(client, Archives[2]), Archives[2], form), form)).ToArray();
                var edited = Flatten(forms.Parse(Read(prepared.Files[2].Source, Archives[2], form), form)).ToArray();
                Assert.Equal(original.Select(c => c.Path), edited.Select(c => c.Path));
                foreach (var (a, b) in original.Zip(edited))
                {
                    var expected = a.Attributes.Select(p => p.Key == "Text" ? KeyValuePair.Create(p.Key, System.Text.RegularExpressions.Regex.Replace(p.Value, "(.+)", "《$1》")) : p).ToArray();
                    Assert.Equal(expected, b.Attributes);
                    if (a.Attributes.Any(p => p.Key == "Text" && p.Value.Length > 0)) wrapped++;
                }
            }
            output.WriteLine("ui texts changed: " + wrapped);
            Assert.True(wrapped > 0);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static XElement Sheet(XDocument document, string name) => document.Root!.Elements(Ss + "Worksheet")
        .Single(w => (string?)w.Attribute(Ss + "Name") == name).Element(Ss + "Table")!;

    private static IEnumerable<UiControl> Flatten(IEnumerable<UiControl> controls) => controls.SelectMany(c => Flatten(c.Children).Prepend(c));

    private static byte[] Read(string file, string archive, string member) { using var reader = Archive.Open(file, archive); return reader.Read(member); }

    private static XDocument Xml(byte[] bytes) => XDocument.Parse(new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true).ReadToEnd(), LoadOptions.PreserveWhitespace);

    private static string[] Lines(byte[] bytes) => Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2).Split("\r\n");

    private static Dictionary<int, string> Cells(XElement row)
    {
        var result = new Dictionary<int, string>();
        var index = 0;
        foreach (var cell in row.Elements(Ss + "Cell"))
        {
            index = (int?)cell.Attribute(Ss + "Index") ?? index + 1;
            result[index] = cell.Element(Ss + "Data")?.Value ?? "";
            index += (int?)cell.Attribute(Ss + "MergeAcross") ?? 0;
        }
        return result;
    }

    private static XElement[] Rows(XDocument document) => document.Root!.Elements(Ss + "Worksheet")
        .Single(w => (string?)w.Attribute(Ss + "Name") == "ShopItem").Element(Ss + "Table")!.Elements(Ss + "Row").ToArray();

    private static (XDocument Items, string[] ItemIds, string[] StringKeys) Originals(string client)
    {
        var items = Xml(Read(Path.Combine(client, Archives[0]), Archives[0], Items));
        var ids = Rows(items).Select(r => Cells(r).GetValueOrDefault(1, "")).Where(v => v.Length > 0).ToArray();
        var keys = Lines(Read(Path.Combine(client, Archives[1]), Archives[1], Strings)).Skip(1).Select(l => l.Split('\t')[0]).Where(k => k.Length > 0).ToArray();
        return (items, ids, keys);
    }

    private static void Verify(string client, IPreparedGameResources prepared, string item, string key, string form, string control)
    {
        // Items: only the chosen record's itemName changed; every other row, cell and worksheet is semantically identical.
        var before = Xml(Read(Path.Combine(client, Archives[0]), Archives[0], Items));
        var afterBytes = Read(prepared.Files[0].Source, Archives[0], Items);
        var after = Xml(afterBytes);
        var name = Cells(Rows(before)[0]).Single(p => p.Value == "itemName").Key;
        var (oldRows, newRows) = (Rows(before), Rows(after));
        Assert.Equal(oldRows.Length, newRows.Length);
        for (var i = 0; i < oldRows.Length; i++)
        {
            var (a, b) = (Cells(oldRows[i]), Cells(newRows[i]));
            if (a.GetValueOrDefault(1) == item) { Assert.Equal("验收 名称", b[name]); a[name] = b[name]; }
            Assert.Equal(a, b);
        }
        foreach (var (a, b) in before.Root!.Elements(Ss + "Worksheet").Zip(after.Root!.Elements(Ss + "Worksheet")))
            if ((string?)a.Attribute(Ss + "Name") != "ShopItem") Assert.True(XNode.DeepEquals(a, b));
        var originalText = Encoding.UTF8.GetString(Read(Path.Combine(client, Archives[0]), Archives[0], Items));
        Assert.Equal(originalText.Contains("<ss:Row", StringComparison.Ordinal), Encoding.UTF8.GetString(afterBytes).Contains("<ss:Row", StringComparison.Ordinal));

        // Strings: one translation replaced in place, one record appended, all other lines byte-identical.
        var oldLines = Lines(Read(Path.Combine(client, Archives[1]), Archives[1], Strings));
        var newLines = Lines(Read(prepared.Files[1].Source, Archives[1], Strings));
        Assert.Contains("FT_ACCEPTANCE_NEW\tBahasa Indonesia", newLines);
        Assert.Equal(oldLines.Where(l => !l.StartsWith(key + "\t", StringComparison.Ordinal) && l.Length > 0),
            newLines.Where(l => !l.StartsWith(key + "\t", StringComparison.Ordinal) && !l.StartsWith("FT_ACCEPTANCE_NEW\t", StringComparison.Ordinal) && l.Length > 0));
        Assert.Equal(key + "\tTiếng Việt / 日本語 / 繁體", newLines.Single(l => l.StartsWith(key + "\t", StringComparison.Ordinal)));

        // UI: the control's Text is the only difference.
        var formBefore = Xml(Read(Path.Combine(client, Archives[2]), Archives[2], form));
        var formAfter = Xml(Read(prepared.Files[2].Source, Archives[2], form));
        var changed = Flatten(new UiForms().Parse(Read(prepared.Files[2].Source, Archives[2], form), form)).Single(c => c.Path == control);
        Assert.Equal("验收 <&> \"quote\"", changed.Attributes.Single(a => a.Key == "Text").Value);
        var target = formBefore.Descendants().Where(e => e.Attribute("Text") is not null)
            .Single(e => formAfter.Descendants().Any(o => o.Attribute("Text")?.Value == "验收 <&> \"quote\"" && (string?)o.Attribute("Name") == (string?)e.Attribute("Name")
                && (string?)o.Attribute("ArrayIndex") == (string?)e.Attribute("ArrayIndex") && (string?)o.Attribute("Text") != (string?)e.Attribute("Text")));
        target.SetAttributeValue("Text", "验收 <&> \"quote\"");
        Assert.True(XNode.DeepEquals(formBefore.Root, formAfter.Root));
    }

    private static PluginDefinition Plugin(string root, string manifest)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "package")).FullName;
        File.WriteAllText(Path.Combine(folder, "plugin.toml"), """
            schema = 1
            [plugin]
            id = "test.acceptance"
            version = "1.0.0"
            game = "bubblefighter"
            kind = "resources"
            name = { en = "Acceptance" }
            [resources]
            provider = "resource-edits"
            manifest = "edits.toml"
            """);
        File.WriteAllText(Path.Combine(folder, "edits.toml"), "schema = 1\n" + manifest);
        return PluginCatalog.LoadFile(Path.Combine(folder, "plugin.toml"));
    }

    private static LaunchContext Context(string root, string client)
    {
        var name = new LocalizedText(new Dictionary<string, string> { ["en"] = "kr-812" });
        var build = new BuildDefinition("kr-812", "test", 1, name, "game.exe", "standin", "", null, null, "nmco", [], []) { NativeLanguage = "ko" };
        var game = new GameDefinition("bubblefighter", GameStatus.Enabled, name, "module.dll", "", "", null, root, [build]);
        return new(game, build, client, null, null) { ResourceCacheFolder = Path.Combine(root, "cache"), ClientLanguage = "zh-CN" };
    }
}
