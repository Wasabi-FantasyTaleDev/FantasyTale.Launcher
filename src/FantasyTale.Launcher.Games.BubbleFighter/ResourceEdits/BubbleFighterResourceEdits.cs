using System.Globalization;
using System.Text;
using FantasyTale.Launcher.Games.BubbleFighter.Archives;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>
/// High-level Bubble Fighter client resource capability for resolving per-build resource targets,
/// inspecting <c>.ui</c> control trees, and building or applying item, string table and UI edits.
/// </summary>
public interface IBubbleFighterResourceEdits
{
    /// <summary>Resolves all built-in resource targets for <paramref name="buildId"/> with optional partial overrides.</summary>
    IReadOnlyDictionary<string, ResourceTarget> Targets(string buildId, IEnumerable<ResourceTargetOverride>? targetOverrides = null);

    /// <summary>Resolves a named resource target (<c>item</c>, <c>strings</c>, <c>xml-strings</c> or <c>ui</c>) for <paramref name="buildId"/>.</summary>
    ResourceTarget ResolveTarget(string buildId, string targetName, ResourceTargetOverride? targetOverride = null);

    /// <summary>Parses a <c>.ui</c> XML document from raw bytes into an immutable control tree for parameter inspection.</summary>
    IReadOnlyList<UiControl> ParseUiControls(ReadOnlySpan<byte> bytes, string source = "ui");

    /// <summary>Reads and parses a <c>.ui</c> member from the client installation's UI archive for <paramref name="buildId"/>.</summary>
    IReadOnlyList<UiControl> ReadUiControls(string instanceFolder, string buildId, string memberPath, ResourceTargetOverride? uiTargetOverride = null);

    /// <summary>Finds a single control by slash-separated path (<c>Parent/Child[2]</c>) inside a client's <c>.ui</c> archive member.</summary>
    UiControl? FindUiControl(string instanceFolder, string buildId, string memberPath, string controlPath, ResourceTargetOverride? uiTargetOverride = null);

    /// <summary>Creates a fluent builder for high-level item, string table, XML string table and <c>.ui</c> resource edits.</summary>
    ResourceEditManifestBuilder CreateManifest(params string[] fallbackLanguages);
}

/// <summary>Default implementation of <see cref="IBubbleFighterResourceEdits"/> backed by <see cref="ResourceTargets"/> and <see cref="UiDocument"/>.</summary>
public sealed class BubbleFighterResourceEdits : IBubbleFighterResourceEdits
{
    /// <inheritdoc />
    public IReadOnlyDictionary<string, ResourceTarget> Targets(string buildId, IEnumerable<ResourceTargetOverride>? targetOverrides = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        return ResourceTargets.Resolve(buildId, targetOverrides ?? []);
    }

    /// <inheritdoc />
    public ResourceTarget ResolveTarget(string buildId, string targetName, ResourceTargetOverride? targetOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        var overrides = targetOverride is null ? [] : new[] { targetOverride with { Name = targetName } };
        var targets = ResourceTargets.Resolve(buildId, overrides);
        if (!targets.TryGetValue(targetName, out var target))
            throw new InvalidDataException($"Target {targetName} is not defined for build {buildId}.");
        return target;
    }

    /// <inheritdoc />
    public IReadOnlyList<UiControl> ParseUiControls(ReadOnlySpan<byte> bytes, string source = "ui") =>
        new UiForms().Parse(bytes.ToArray(), source);

    /// <inheritdoc />
    public IReadOnlyList<UiControl> ReadUiControls(string instanceFolder, string buildId, string memberPath, ResourceTargetOverride? uiTargetOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceFolder);
        var target = ResolveTarget(buildId, ResourceTargets.UiTarget, uiTargetOverride);
        var normalizedMember = ArchiveResourceComposer.ValidatePath(memberPath);
        var archivePath = ArchiveResourceComposer.SafeFile(instanceFolder, target.Archive);
        using var archive = Archive.Open(archivePath, target.Archive);
        if (!archive.Contains(normalizedMember))
            throw new FileNotFoundException($"UI member {normalizedMember} was not found in {target.Archive}.", normalizedMember);
        return new UiForms().Parse(archive.Read(normalizedMember), normalizedMember);
    }

    /// <inheritdoc />
    public UiControl? FindUiControl(string instanceFolder, string buildId, string memberPath, string controlPath, ResourceTargetOverride? uiTargetOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(controlPath);
        var target = ResolveTarget(buildId, ResourceTargets.UiTarget, uiTargetOverride);
        var normalizedMember = ArchiveResourceComposer.ValidatePath(memberPath);
        var archivePath = ArchiveResourceComposer.SafeFile(instanceFolder, target.Archive);
        using var archive = Archive.Open(archivePath, target.Archive);
        if (!archive.Contains(normalizedMember))
            throw new FileNotFoundException($"UI member {normalizedMember} was not found in {target.Archive}.", normalizedMember);
        var form = UiDocument.Load(archive.Read(normalizedMember), normalizedMember);
        var canonical = form.CanonicalPath(controlPath);
        return form.Controls().Select(c => c.Find(canonical)).FirstOrDefault(c => c is not null);
    }

    /// <inheritdoc />
    public ResourceEditManifestBuilder CreateManifest(params string[] fallbackLanguages) => new(fallbackLanguages);
}

/// <summary>
/// Fluent builder for constructing <c>resource-edits</c> plugin manifests or applying high-level item, string table
/// and <c>.ui</c> edits across client builds.
/// </summary>
public sealed class ResourceEditManifestBuilder
{
    private readonly List<string> _fallbackLanguages = [];
    private readonly List<ResourceTargetOverride> _targets = [];
    private readonly List<Entry> _entries = [];

    /// <summary>Initializes a new builder with optional fallback languages consulted when the active client language has no translation.</summary>
    public ResourceEditManifestBuilder(params string[] fallbackLanguages)
    {
        if (fallbackLanguages is not null)
            _fallbackLanguages.AddRange(fallbackLanguages.Where(l => !string.IsNullOrWhiteSpace(l)));
    }

    /// <summary>Adds or overrides a target location or table layout for one build or <c>*</c>.</summary>
    public ResourceEditManifestBuilder OverrideTarget(ResourceTargetOverride targetOverride)
    {
        ArgumentNullException.ThrowIfNull(targetOverride);
        _targets.Add(targetOverride);
        return this;
    }

    /// <summary>Patches specific columns on an existing <c>ShopItem2.xml</c> item row without touching other columns.</summary>
    public ResourceEditManifestBuilder PatchItem(string id, object? name = null, object? description = null,
        IReadOnlyDictionary<string, object>? fields = null, IReadOnlyDictionary<string, string>? expect = null,
        IReadOnlyList<string>? clear = null, string? build = null, string? when = null, string target = ResourceTargets.ItemTarget) =>
        AddItemEntry(EditAction.Patch, id, name, description, fields, expect, clear, build, when, target);

    /// <summary>Adds a new item row with its key and supplied fields; undeclared cells are absent.</summary>
    public ResourceEditManifestBuilder AddItem(string id, object? name = null, object? description = null,
        IReadOnlyDictionary<string, object>? fields = null, string? build = null, string? when = null, string target = ResourceTargets.ItemTarget) =>
        AddItemEntry(EditAction.Add, id, name, description, fields, null, null, build, when, target);

    /// <summary>Replaces an item row's non-key values with the supplied fields, clearing undeclared values.</summary>
    public ResourceEditManifestBuilder ReplaceItem(string id, object? name = null, object? description = null,
        IReadOnlyDictionary<string, object>? fields = null, IReadOnlyDictionary<string, string>? expect = null,
        IReadOnlyList<string>? clear = null, string? build = null, string? when = null, string target = ResourceTargets.ItemTarget) =>
        AddItemEntry(EditAction.Replace, id, name, description, fields, expect, clear, build, when, target);

    /// <summary>Removes an existing <c>ShopItem2.xml</c> item row by item id.</summary>
    public ResourceEditManifestBuilder RemoveItem(string id, IReadOnlyDictionary<string, string>? expect = null,
        string? build = null, string? when = null, string target = ResourceTargets.ItemTarget) =>
        AddItemEntry(EditAction.Remove, id, null, null, null, expect, null, build, when, target);

    /// <summary>Patches a row in <c>TranslatedStringTable.csv</c> by key.</summary>
    public ResourceEditManifestBuilder PatchString(string key, object? text = null, object? reference = null, object? comment = null,
        IReadOnlyDictionary<string, object>? fields = null, IReadOnlyDictionary<string, string>? expect = null,
        IReadOnlyList<string>? clear = null, string? build = null, string? when = null, string target = ResourceTargets.StringsTarget) =>
        AddStringEntry(EditAction.Patch, key, text, reference, comment, fields, expect, clear, build, when, target);

    /// <summary>Adds a new row to <c>TranslatedStringTable.csv</c>.</summary>
    public ResourceEditManifestBuilder AddString(string key, object? text = null, object? reference = null, object? comment = null,
        IReadOnlyDictionary<string, object>? fields = null, string? build = null, string? when = null, string target = ResourceTargets.StringsTarget) =>
        AddStringEntry(EditAction.Add, key, text, reference, comment, fields, null, null, build, when, target);

    /// <summary>Replaces a row in <c>TranslatedStringTable.csv</c>.</summary>
    public ResourceEditManifestBuilder ReplaceString(string key, object? text = null, object? reference = null, object? comment = null,
        IReadOnlyDictionary<string, object>? fields = null, IReadOnlyDictionary<string, string>? expect = null,
        IReadOnlyList<string>? clear = null, string? build = null, string? when = null, string target = ResourceTargets.StringsTarget) =>
        AddStringEntry(EditAction.Replace, key, text, reference, comment, fields, expect, clear, build, when, target);

    /// <summary>Removes a row from <c>TranslatedStringTable.csv</c>.</summary>
    public ResourceEditManifestBuilder RemoveString(string key, IReadOnlyDictionary<string, string>? expect = null,
        string? build = null, string? when = null, string target = ResourceTargets.StringsTarget) =>
        AddStringEntry(EditAction.Remove, key, null, null, null, null, expect, null, build, when, target);

    /// <summary>Patches a composite-key row in <c>TranslatedXmlTable.csv</c>.</summary>
    public ResourceEditManifestBuilder PatchXmlString(string category, string subCategory, string keyString, object? text = null, object? comment = null,
        IReadOnlyDictionary<string, object>? fields = null, IReadOnlyDictionary<string, string>? expect = null,
        IReadOnlyList<string>? clear = null, string? build = null, string? when = null, string target = ResourceTargets.XmlStringsTarget) =>
        AddXmlStringEntry(EditAction.Patch, [category, subCategory, keyString], text, comment, fields, expect, clear, build, when, target);

    /// <summary>Adds a composite-key row to <c>TranslatedXmlTable.csv</c>.</summary>
    public ResourceEditManifestBuilder AddXmlString(string category, string subCategory, string keyString, object? text = null, object? comment = null,
        IReadOnlyDictionary<string, object>? fields = null, string? build = null, string? when = null, string target = ResourceTargets.XmlStringsTarget) =>
        AddXmlStringEntry(EditAction.Add, [category, subCategory, keyString], text, comment, fields, null, null, build, when, target);

    /// <summary>Replaces a composite-key row in <c>TranslatedXmlTable.csv</c>.</summary>
    public ResourceEditManifestBuilder ReplaceXmlString(string category, string subCategory, string keyString, object? text = null, object? comment = null,
        IReadOnlyDictionary<string, object>? fields = null, IReadOnlyDictionary<string, string>? expect = null,
        IReadOnlyList<string>? clear = null, string? build = null, string? when = null, string target = ResourceTargets.XmlStringsTarget) =>
        AddXmlStringEntry(EditAction.Replace, [category, subCategory, keyString], text, comment, fields, expect, clear, build, when, target);

    /// <summary>Removes a composite-key row from <c>TranslatedXmlTable.csv</c>.</summary>
    public ResourceEditManifestBuilder RemoveXmlString(string category, string subCategory, string keyString,
        IReadOnlyDictionary<string, string>? expect = null, string? build = null, string? when = null, string target = ResourceTargets.XmlStringsTarget) =>
        AddXmlStringEntry(EditAction.Remove, [category, subCategory, keyString], null, null, null, expect, null, build, when, target);

    /// <summary>Patches attributes on a <c>.ui</c> control without altering sibling or child controls.</summary>
    public ResourceEditManifestBuilder PatchUiControl(string member, string control, object? text = null, object? translateId = null,
        IReadOnlyDictionary<string, object>? attributes = null, IReadOnlyDictionary<string, string>? expect = null,
        string? build = null, string? when = null, string target = ResourceTargets.UiTarget) =>
        AddUiEntry(EditAction.Patch, member, control, text, translateId, attributes, expect, null, null, build, when, target);

    /// <summary>Adds a new <c>.ui</c> control element under the parent path of <paramref name="control"/>.</summary>
    public ResourceEditManifestBuilder AddUiControl(string member, string control, object element, int? position = null,
        IReadOnlyDictionary<string, object>? attributes = null, string? build = null, string? when = null, string target = ResourceTargets.UiTarget) =>
        AddUiEntry(EditAction.Add, member, control, null, null, attributes, null, element, position, build, when, target);

    /// <summary>Replaces an existing <c>.ui</c> control element in place while keeping its sibling position.</summary>
    public ResourceEditManifestBuilder ReplaceUiControl(string member, string control, object element,
        IReadOnlyDictionary<string, object>? attributes = null, IReadOnlyDictionary<string, string>? expect = null,
        string? build = null, string? when = null, string target = ResourceTargets.UiTarget) =>
        AddUiEntry(EditAction.Replace, member, control, null, null, attributes, expect, element, null, build, when, target);

    /// <summary>Removes an existing <c>.ui</c> control element and its subtree.</summary>
    public ResourceEditManifestBuilder RemoveUiControl(string member, string control, IReadOnlyDictionary<string, string>? expect = null,
        string? build = null, string? when = null, string target = ResourceTargets.UiTarget) =>
        AddUiEntry(EditAction.Remove, member, control, null, null, null, expect, null, null, build, when, target);

    /// <summary>Records a <c>hide</c> UI action, which fails loudly at apply time because the client has no persistent XML visibility attribute.</summary>
    public ResourceEditManifestBuilder HideUiControl(string member, string control, string? build = null, string? when = null, string target = ResourceTargets.UiTarget) =>
        AddUiEntry(EditAction.Hide, member, control, null, null, null, null, null, null, build, when, target);

    /// <summary>Renders the configured target overrides and edits as a valid <c>resource-edits</c> TOML manifest.</summary>
    public string ToToml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("schema = 1");
        if (_fallbackLanguages.Count > 0)
            sb.Append("fallback_languages = [").Append(string.Join(", ", _fallbackLanguages.Select(Quote))).AppendLine("]");
        foreach (var t in _targets)
        {
            sb.AppendLine().AppendLine("[[targets]]");
            sb.Append("name = ").AppendLine(Quote(t.Name));
            if (!string.IsNullOrEmpty(t.Build))
                sb.Append("build = ").AppendLine(Quote(t.Build));
            if (t.Kind is not null) sb.Append("kind = ").AppendLine(Quote(t.Kind));
            if (t.Archive is not null) sb.Append("archive = ").AppendLine(Quote(t.Archive));
            if (t.Member is not null) sb.Append("member = ").AppendLine(Quote(t.Member));
            if (t.Sheet is not null) sb.Append("sheet = ").AppendLine(Quote(t.Sheet));
            if (t.HeaderRow is { } hr) sb.Append("header_row = ").AppendLine(hr.ToString(CultureInfo.InvariantCulture));
            if (t.Orientation is not null) sb.Append("orientation = ").AppendLine(Quote(t.Orientation));
            if (t.Keys is { Count: > 0 })
                sb.Append("keys = [").Append(string.Join(", ", t.Keys.Select(Quote))).AppendLine("]");
            if (t.Fields is { Count: > 0 })
                WriteInlineStringMap(sb, "fields", t.Fields);
            if (t.Languages is { Count: > 0 })
                sb.Append("languages = { ").Append(string.Join(", ", t.Languages.Select(language => QuoteKey(language.Key)
                    + " = { " + string.Join(", ", language.Value.Select(p => QuoteKey(p.Key) + " = " + Quote(p.Value))) + " }"))).AppendLine(" }");
        }
        foreach (var e in _entries)
        {
            sb.AppendLine().AppendLine("[[edits]]");
            if (e.Build is not null) sb.Append("build = ").AppendLine(Quote(e.Build));
            if (e.When is not null) sb.Append("when = ").AppendLine(Quote(e.When));
            sb.Append("target = ").AppendLine(Quote(e.Target));
            if (e.Member is not null) sb.Append("member = ").AppendLine(Quote(e.Member));
            sb.Append("action = ").AppendLine(Quote(e.Action.ToString().ToLowerInvariant()));
            if (e.Keys is { Length: 1 }) sb.Append("key = ").AppendLine(Quote(e.Keys[0]));
            else if (e.Keys is { Length: > 1 }) sb.Append("key = [").Append(string.Join(", ", e.Keys.Select(Quote))).AppendLine("]");
            if (e.Control is not null) sb.Append("control = ").AppendLine(Quote(e.Control));
            if (e.Position is { } pos) sb.Append("position = ").AppendLine(pos.ToString(CultureInfo.InvariantCulture));
            if (e.Element is not null) sb.Append("element = ").AppendLine(FormatValue(e.Element));
            if (e.Clear is { Count: > 0 }) sb.Append("clear = [").Append(string.Join(", ", e.Clear.Select(Quote))).AppendLine("]");
            if (e.Expect is { Count: > 0 }) WriteInlineStringMap(sb, "expect", e.Expect);
            if (e.Set is { Count: > 0 })
            {
                sb.AppendLine("[edits.set]");
                foreach (var (field, val) in e.Set)
                    sb.Append(QuoteKey(field)).Append(" = ").AppendLine(FormatValue(val));
            }
        }
        return sb.ToString();
    }

    private ResourceEditManifestBuilder AddItemEntry(EditAction action, string id, object? name, object? description,
        IReadOnlyDictionary<string, object>? fields, IReadOnlyDictionary<string, string>? expect, IReadOnlyList<string>? clear,
        string? build, string? when, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var set = MergeFields(fields, ("name", name), ("description", description));
        _entries.Add(new(target, action, [id], null, null, null, set, expect, clear, build, when));
        return this;
    }

    private ResourceEditManifestBuilder AddStringEntry(EditAction action, string key, object? text, object? reference, object? comment,
        IReadOnlyDictionary<string, object>? fields, IReadOnlyDictionary<string, string>? expect, IReadOnlyList<string>? clear,
        string? build, string? when, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var set = MergeFields(fields, ("text", text), ("reference", reference), ("comment", comment));
        _entries.Add(new(target, action, [key], null, null, null, set, expect, clear, build, when));
        return this;
    }

    private ResourceEditManifestBuilder AddXmlStringEntry(EditAction action, string[] keys, object? text, object? comment,
        IReadOnlyDictionary<string, object>? fields, IReadOnlyDictionary<string, string>? expect, IReadOnlyList<string>? clear,
        string? build, string? when, string target)
    {
        if (keys.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Composite keys cannot be empty.", nameof(keys));
        var set = MergeFields(fields, ("text", text), ("comment", comment));
        _entries.Add(new(target, action, keys, null, null, null, set, expect, clear, build, when));
        return this;
    }

    private ResourceEditManifestBuilder AddUiEntry(EditAction action, string member, string control, object? text, object? translateId,
        IReadOnlyDictionary<string, object>? attributes, IReadOnlyDictionary<string, string>? expect, object? element, int? position,
        string? build, string? when, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        ArgumentException.ThrowIfNullOrWhiteSpace(control);
        var set = MergeFields(attributes, ("Text", text), ("TranslateID", translateId));
        _entries.Add(new(target, action, null, ArchiveResourceComposer.ValidatePath(member), control, position, set, expect, null, build, when, element));
        return this;
    }

    private static Dictionary<string, object> MergeFields(IReadOnlyDictionary<string, object>? extra, params (string Key, object? Value)[] named)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, val) in named)
            if (val is not null) result[key] = val;
        if (extra is not null)
            foreach (var (key, val) in extra)
                if (!result.TryAdd(key, val))
                    throw new InvalidDataException($"Field {key} was specified twice.");
        return result;
    }

    private static void WriteInlineStringMap(StringBuilder sb, string name, IReadOnlyDictionary<string, string> map)
    {
        sb.Append(name).Append(" = { ");
        sb.Append(string.Join(", ", map.Select(p => QuoteKey(p.Key) + " = " + Quote(p.Value))));
        sb.AppendLine(" }");
    }

    private static string QuoteKey(string key) =>
        key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? key : Quote(key);

    private static string FormatValue(object value) => value switch
    {
        string s => Quote(s),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IReadOnlyDictionary<string, string> map => "{ " + string.Join(", ", map.Select(p => QuoteKey(p.Key) + " = " + Quote(p.Value))) + " }",
        _ => throw new InvalidDataException($"Unsupported manifest value type: {value.GetType().Name}.")
    };

    private static string Quote(string value)
    {
        _ = new UTF8Encoding(false, true).GetByteCount(value);
        var quoted = new StringBuilder("\"");
        foreach (var character in value)
            quoted.Append(character switch
            {
                '\\' => "\\\\", '"' => "\\\"", '\r' => "\\r", '\n' => "\\n", '\t' => "\\t",
                < ' ' or '\u007F' => "\\u" + ((int)character).ToString("X4", CultureInfo.InvariantCulture),
                _ => character.ToString()
            });
        return quoted.Append('"').ToString();
    }

    private sealed record Entry(string Target, EditAction Action, string[]? Keys, string? Member, string? Control,
        int? Position, IReadOnlyDictionary<string, object>? Set, IReadOnlyDictionary<string, string>? Expect,
        IReadOnlyList<string>? Clear, string? Build, string? When, object? Element = null);
}
