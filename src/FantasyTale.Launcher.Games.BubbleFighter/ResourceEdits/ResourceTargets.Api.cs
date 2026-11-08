using FantasyTale.Launcher.Games.BubbleFighter.CharacterModels;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>A partial per-build override for a logical resource target; null properties inherit from the previous layer.</summary>
/// <param name="Name">Logical target name to add or override.</param>
/// <param name="Build">Target build id, or <c>*</c> for all builds.</param>
public sealed record ResourceTargetOverride(string Name, string Build = "*")
{
    /// <summary>Optional format kind override: spreadsheet, delimited or ui.</summary>
    public string? Kind { get; init; }

    /// <summary>Optional client-relative physical archive override.</summary>
    public string? Archive { get; init; }

    /// <summary>Optional virtual member path override.</summary>
    public string? Member { get; init; }

    /// <summary>Optional spreadsheet worksheet name override.</summary>
    public string? Sheet { get; init; }

    /// <summary>Optional one-based physical header row override.</summary>
    public int? HeaderRow { get; init; }

    /// <summary>Optional record orientation override (vertical or horizontal).</summary>
    public string? Orientation { get; init; }

    /// <summary>Optional record key column reference override.</summary>
    public IReadOnlyList<string>? Keys { get; init; }

    /// <summary>Logical field mappings merged entry by entry over inherited fields.</summary>
    public IReadOnlyDictionary<string, string>? Fields { get; init; }

    /// <summary>Per-language logical field mappings merged over inherited mappings.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Languages { get; init; }
}

/// <summary>Public target constants and validated caller overrides backed by the same manifest defaults.</summary>
public static partial class ResourceTargets
{
    /// <summary>Chinese 2013 legacy client build identifier (BFZ archives).</summary>
    public const string BuildCn406 = "cn-406";

    /// <summary>Korean 2015 legacy baseline client build identifier (BFZ archives).</summary>
    public const string BuildKr812 = "kr-812";

    /// <summary>Current Korean client build identifier (PKN archives).</summary>
    public const string BuildKr1256 = "kr-1256";

    /// <summary>Logical target name for the shop item table.</summary>
    public const string ItemTarget = "item";

    /// <summary>Logical target name for the primary translated string table.</summary>
    public const string StringsTarget = "strings";

    /// <summary>Logical target name for the composite-key XML translation string table.</summary>
    public const string XmlStringsTarget = "xml-strings";

    /// <summary>Logical target name for native UI forms.</summary>
    public const string UiTarget = "ui";

    /// <summary>SpreadsheetML 2003 XML target kind.</summary>
    public const string SpreadsheetKind = "spreadsheet";

    /// <summary>Tab-delimited UTF-16 text target kind.</summary>
    public const string DelimitedKind = "delimited";

    /// <summary>Native UiForm XML target kind.</summary>
    public const string UiKind = "ui";

    /// <summary>Row-oriented table layout (one record per row).</summary>
    public const string VerticalOrientation = "vertical";

    /// <summary>Column-oriented table layout (one record per column, such as HorzTable).</summary>
    public const string HorizontalOrientation = "horizontal";

    /// <summary>Default physical archive for legacy item tables (cn-406, kr-812).</summary>
    public const string LegacyItemArchive = "Config/Config.bfz";

    /// <summary>Default physical archive for modern item tables (kr-1256).</summary>
    public const string ModernItemArchive = "Packs/ConfigSh_00000.pkn";

    /// <summary>Default virtual member path for the shop item table across sampled builds.</summary>
    public const string ItemMember = "Config/ShopItem2.xml";

    /// <summary>Default worksheet name inside <see cref="ItemMember"/>.</summary>
    public const string ItemSheet = "ShopItem";

    /// <summary>Default primary key column name inside <see cref="ItemSheet"/>.</summary>
    public const string ItemKeyColumn = "itemId";

    /// <summary>Default physical archive for legacy string tables (cn-406, kr-812).</summary>
    public const string LegacyStringTableArchive = "StringTable/StringTable.bfz";

    /// <summary>Default physical archive for modern string tables (kr-1256).</summary>
    public const string ModernStringTableArchive = "Packs/StringTable_00000.pkn";

    /// <summary>Default virtual member path for the general translated string table.</summary>
    public const string TranslatedStringTableMember = "StringTable/TranslatedStringTable.csv";

    /// <summary>Default virtual member path for the XML translated string table.</summary>
    public const string TranslatedXmlTableMember = "StringTable/TranslatedXmlTable.csv";

    /// <summary>Default physical archive for legacy UI forms (cn-406, kr-812).</summary>
    public const string LegacyUiArchive = "Ui/Ui.bfz";

    /// <summary>Default physical archive for modern UI forms (kr-1256).</summary>
    public const string ModernUiArchive = "Packs/Ui_00000.pkn";

    /// <summary>Resolves built-in targets and caller overrides for the selected build and client language.</summary>
    public static IReadOnlyDictionary<string, ResourceTarget> Resolve(string build, IEnumerable<ResourceTargetOverride>? overrides, string? language = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(build);
        var builder = new ResourceEditManifestBuilder();
        foreach (var row in overrides ?? []) builder.OverrideTarget(row);
        var data = ResourceData.Parse(builder.ToToml(), "resource target overrides");
        return Resolve(build, language, data.Rows("targets"));
    }
}