using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Enumeration;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;

internal sealed class ModeExtensionData
{
    private static readonly string[] Languages = ["zh-CN", "zh-TW", "ko", "en", "id", "vi", "th"];
    private readonly IReadOnlyDictionary<string, int> _tokens;

    internal IReadOnlyList<ModeDefinition> Modes { get; }
    internal IReadOnlyList<ModeDefinition> AllModes { get; }
    // A build whose Create Room constructor picks between resources at run time binds each of them (cn-735 sub_A88A90).
    internal IReadOnlyList<RoomBinding> Bindings { get; }
    internal string Language { get; }
    internal string Title { get; }
    internal string SelectedCaption { get; }
    internal int PollMs { get; }
    internal int OperationTimeoutMs { get; }
    internal int ResultPollMs { get; }
    internal int StartupTimeoutMs { get; }
    internal int MaxPendingActions { get; }

    private ModeExtensionData(IReadOnlyList<ModeDefinition> modes, IReadOnlyList<ModeDefinition> allModes,
        IReadOnlyList<RoomBinding> bindings, string language, string title, string selectedCaption,
        int pollMs, int operationTimeoutMs, int resultPollMs, int startupTimeoutMs, int maxPendingActions,
        IReadOnlyDictionary<string, int> tokens)
    {
        Modes = modes; AllModes = allModes; Bindings = bindings; Language = language; Title = title;
        SelectedCaption = selectedCaption; PollMs = pollMs; OperationTimeoutMs = operationTimeoutMs;
        ResultPollMs = resultPollMs; StartupTimeoutMs = startupTimeoutMs; MaxPendingActions = maxPendingActions;
        _tokens = tokens;
    }

    internal static ModeExtensionData Load(PluginDefinition plugin, BuildDefinition build, string? locale)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(build);
        var files = new OverlayPackageFiles();
        for (var path = Path.GetFullPath(plugin.Folder); path is not null; path = Path.GetDirectoryName(path))
            if (Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Mode Extension package paths cannot traverse filesystem links.");
        files.SetRoots(new Dictionary<string, string> { [plugin.Id] = plugin.Folder });

        using var catalogue = Read(files, plugin.Id, "mode-catalog.json");
        using var availability = Read(files, plugin.Id, "mode-availability.json");
        using var bindings = Read(files, plugin.Id, "room-bindings.json");
        using var settings = Read(files, plugin.Id, "controller.json");
        var allModes = ReadModes(catalogue.RootElement);
        var tokens = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var mode in allModes)
            foreach (var token in mode.Tokens.Values.Concat(mode.Aliases))
            {
                if (token.Length == 0 || token.Any(c => c == '#' || IsTokenWhitespace(c)))
                    throw Invalid("mode-catalog.json", "modes.tokens", "tokens and aliases cannot contain hashes or whitespace");
                if (tokens.TryGetValue(token, out var previous) && previous != mode.Id)
                    throw Invalid("mode-catalog.json", "modes.tokens", "tokens and aliases must identify a unique mode");
                tokens[token] = mode.Id;
            }

        var modes = FilterModes(availability.RootElement, allModes, build.Id);
        var bound = ReadBindings(bindings.RootElement, build.Id);
        var controller = settings.RootElement;
        RequireFormat(controller, "controller.json");
        OnlyKeys(controller, "controller.json", "", "format", "pollMs", "operationTimeoutMs", "resultPollMs",
            "startupTimeoutMs", "maxPendingActions", "selectedCaption");
        var pollMs = Integer(controller, "pollMs", "controller.json", 1, 60000);
        var operationTimeoutMs = Integer(controller, "operationTimeoutMs", "controller.json", 1, 60000);
        var resultPollMs = Integer(controller, "resultPollMs", "controller.json", 1, operationTimeoutMs);
        var startupTimeoutMs = Integer(controller, "startupTimeoutMs", "controller.json", 1, 300000);
        var maxPendingActions = Integer(controller, "maxPendingActions", "controller.json", 1, 256);
        var captions = Strings(Property(controller, "selectedCaption", "controller.json"), "controller.json", "selectedCaption");
        foreach (var caption in captions.Values)
            if (caption.Split("{mode}", StringSplitOptions.None).Length != 2)
                throw Invalid("controller.json", "selectedCaption", "each caption must contain exactly one {mode} placeholder");
        var language = Languages.FirstOrDefault(value => value.Equals(locale, StringComparison.OrdinalIgnoreCase))
            ?? Languages.FirstOrDefault(value => value.Equals(locale?.Split('-')[0], StringComparison.OrdinalIgnoreCase)) ?? "en";
        var title = plugin.Name.Resolve(language);
        if (string.IsNullOrWhiteSpace(title) || title.Contains('\0'))
            throw Invalid("plugin.toml", "plugin.name", "a native entry title is required");
        return new(modes, allModes, bound, language, title, captions.GetValueOrDefault(language, captions["en"]),
            pollMs, operationTimeoutMs, resultPollMs, startupTimeoutMs, maxPendingActions,
            new ReadOnlyDictionary<string, int>(tokens));
    }

    internal (string Room, int? SelectedId) ParseTitle(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tags = Tags(text).ToArray();
        var numeric = System.Array.FindIndex(tags, tag => tag.Token.EnumerateRunes().All(rune => Rune.GetUnicodeCategory(rune) == UnicodeCategory.DecimalDigitNumber));
        int? selected = numeric >= 0 ? Lookup(tags[numeric].Token) : null;
        // The first numeric tag reserves precedence even when its value is invalid.
        foreach (var tag in tags.Take(numeric >= 0 ? numeric : tags.Length))
            if (Lookup(tag.Token) is { } id) { selected = id; break; }
        var room = text;
        while (true)
        {
            var recognized = Tags(room).FirstOrDefault(tag => Lookup(tag.Token) is not null);
            if (recognized.Token is null) break;
            var before = TrimEnd(room[..recognized.Start]);
            var after = TrimStart(room[(recognized.Start + recognized.Length)..]);
            room = before.Length != 0 && after.Length != 0 ? before + " " + after : before + after;
        }
        return (room, selected);
    }

    private int? Lookup(string token) => token.All(char.IsAsciiDigit)
        && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 65535
        ? number : _tokens.TryGetValue(token, out var mode) ? mode : null;

    private static IEnumerable<(int Start, int Length, string Token)> Tags(string text)
    {
        var start = text.IndexOf('#');
        while (start >= 0)
        {
            var end = text.IndexOf('#', start + 1);
            if (end < 0) yield break;
            if (end > start + 1)
            {
                var token = text[(start + 1)..end];
                if (!token.Any(IsTokenWhitespace)) yield return (start, end - start + 1, token);
            }
            start = end;
        }
    }

    private static bool IsTokenWhitespace(char value) => char.IsWhiteSpace(value) || value == '\ufeff';
    private static bool IsTrimWhitespace(char value) => value != '\u0085' && IsTokenWhitespace(value);
    private static string TrimStart(string text)
    {
        var start = 0;
        while (start < text.Length && IsTrimWhitespace(text[start])) start++;
        return text[start..];
    }
    private static string TrimEnd(string text)
    {
        var end = text.Length;
        while (end > 0 && IsTrimWhitespace(text[end - 1])) end--;
        return text[..end];
    }

    private static ReadOnlyCollection<ModeDefinition> ReadModes(JsonElement root)
    {
        const string file = "mode-catalog.json";
        RequireFormat(root, file);
        var result = new List<ModeDefinition>();
        var ids = new HashSet<int>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Array(root, "modes", file))
        {
            var id = Integer(entry, "id", file, 0, ushort.MaxValue);
            var key = Text(entry, "key", file);
            if (!ids.Add(id) || !keys.Add(key)) throw Invalid(file, "modes", "mode IDs and keys must be unique");
            var names = Strings(Property(entry, "names", file), file, "modes.names");
            var tokens = Strings(Property(entry, "tokens", file), file, "modes.tokens");
            var aliases = entry.TryGetProperty("aliases", out var aliasValue) ? StringArray(aliasValue, file, "modes.aliases") : [];
            var extra = entry.EnumerateObject().Where(property => property.Name is not ("id" or "key" or "names" or "tokens" or "aliases"))
                .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
            result.Add(new(id, key, names, tokens, System.Array.AsReadOnly(aliases)) { Metadata = extra });
        }
        if (result.Count == 0) throw Invalid(file, "modes", "at least one mode is required");
        return result.AsReadOnly();
    }

    private static ReadOnlyCollection<ModeDefinition> FilterModes(JsonElement root, IReadOnlyList<ModeDefinition> modes, string build)
    {
        const string file = "mode-availability.json";
        RequireFormat(root, file);
        OnlyKeys(root, file, "", "format", "rules");
        var known = modes.Select(mode => mode.Id).ToHashSet();
        var visible = known.ToHashSet();
        foreach (var rule in Array(root, "rules", file))
        {
            OnlyKeys(rule, file, "rules", "builds", "allow", "hidden");
            var builds = StringArray(Property(rule, "builds", file), file, "rules.builds");
            if (builds.Length == 0 || builds.Any(pattern => pattern.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('*' or '_' or '.' or '-'))))
                throw Invalid(file, "rules.builds", "nonempty build IDs or wildcard patterns are required");
            var hasAllow = rule.TryGetProperty("allow", out var allowValue);
            var hasHidden = rule.TryGetProperty("hidden", out var hiddenValue);
            if (!hasAllow && !hasHidden) throw Invalid(file, "rules", "allow or hidden is required");
            var allow = hasAllow ? IdSet(allowValue, known, file, "rules.allow") : null;
            var hidden = hasHidden ? IdSet(hiddenValue, known, file, "rules.hidden") : [];
            if (!builds.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, build, ignoreCase: true))) continue;
            if (allow is not null) visible.IntersectWith(allow);
            visible.ExceptWith(hidden);
        }
        return System.Array.AsReadOnly(modes.Where(mode => visible.Contains(mode.Id)).ToArray());
    }

    /// <summary>
    /// Reads every build's bindings and returns this build's: one object, or an array with one object per resource the
    /// build's dialog may be loaded from. A build without a row has none.
    /// </summary>
    private static ReadOnlyCollection<RoomBinding> ReadBindings(JsonElement root, string build)
    {
        const string file = "room-bindings.json";
        RequireFormat(root, file);
        OnlyKeys(root, file, "", "format", "builds");
        var builds = Property(root, "builds", file);
        RequireObject(builds, file, "builds");
        RoomBinding[] result = [];
        foreach (var row in builds.EnumerateObject())
        {
            if (row.Name.Length == 0 || row.Name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '.' or '-')))
                throw Invalid(file, "builds", "bindings require literal build IDs");
            var key = "builds." + row.Name;
            RoomBinding[] bindings;
            if (row.Value.ValueKind == JsonValueKind.Array)
            {
                bindings = row.Value.EnumerateArray().Select(value => ReadBinding(value, file, key)).ToArray();
                if (bindings.Length == 0) throw Invalid(file, key, "at least one binding is required");
                if (bindings.DistinctBy(binding => binding.Form, StringComparer.OrdinalIgnoreCase).Count() != bindings.Length)
                    throw Invalid(file, key, "each binding must name a different form");
            }
            else bindings = [ReadBinding(row.Value, file, key)];
            if (row.Name.Equals(build, StringComparison.Ordinal)) result = bindings;
        }
        return System.Array.AsReadOnly(result);
    }

    private static RoomBinding ReadBinding(JsonElement value, string file, string key)
    {
        OnlyKeys(value, file, key, "form", "roomName", "modeButton", "descriptionLabel", "createButton", "cancelButton", "entry");
        var form = Text(value, "form", file);
        var roomName = Text(value, "roomName", file);
        try { new GameNativeUiControlTarget { Form = form, Name = roomName }.Validate(); }
        catch (ArgumentException error) { throw Invalid(file, key + ".form", "invalid native room selector", error); }
        var entry = Property(value, "entry", file);
        OnlyKeys(entry, file, "builds.entry", "x", "y", "width", "height");
        return new RoomBinding(form, roomName, Text(value, "modeButton", file), Text(value, "descriptionLabel", file),
            Text(value, "createButton", file), Text(value, "cancelButton", file), new RoomEntry(Integer(entry, "x", file, 0, int.MaxValue), Integer(entry, "y", file, 0, int.MaxValue),
                Integer(entry, "width", file, 1, int.MaxValue), Integer(entry, "height", file, 1, int.MaxValue)));
    }

    private static JsonDocument Read(OverlayPackageFiles files, string package, string name)
    {
        var resolved = files.Resolve("GET", OverlayPackageFiles.Url(package, name));
        if (resolved.Status != 200 || resolved.Path is null) throw Invalid(name, "", "required package data is missing or inaccessible");
        using var stream = File.OpenRead(resolved.Path);
        JsonDocument document;
        try { document = JsonDocument.Parse(stream); }
        catch (JsonException error) { throw Invalid(name, "", "invalid JSON", error); }
        try { RejectDuplicateProperties(document.RootElement, name, ""); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void RejectDuplicateProperties(JsonElement value, string file, string key)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid(file, key + "." + property.Name, "duplicate property");
                RejectDuplicateProperties(property.Value, file, key + "." + property.Name);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) RejectDuplicateProperties(child, file, key + "[]");
    }

    private static void RequireFormat(JsonElement root, string file)
    {
        RequireObject(root, file, "");
        if (Integer(root, "format", file, 1, 1) != 1) throw Invalid(file, "format", "unsupported format");
    }
    private static void RequireObject(JsonElement value, string file, string key)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid(file, key, "an object is required");
    }
    private static void OnlyKeys(JsonElement value, string file, string key, params string[] permitted)
    {
        RequireObject(value, file, key);
        foreach (var property in value.EnumerateObject())
            if (!permitted.Contains(property.Name)) throw Invalid(file, key + "." + property.Name, "unknown property");
    }
    private static JsonElement Property(JsonElement value, string key, string file)
    {
        RequireObject(value, file, key);
        return value.TryGetProperty(key, out var result) ? result : throw Invalid(file, key, "required property is missing");
    }
    private static JsonElement.ArrayEnumerator Array(JsonElement value, string key, string file)
    {
        var result = Property(value, key, file);
        if (result.ValueKind != JsonValueKind.Array) throw Invalid(file, key, "an array is required");
        return result.EnumerateArray();
    }
    private static int Integer(JsonElement value, string key, string file, int minimum, int maximum)
    {
        var element = Property(value, key, file);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var result) || result < minimum || result > maximum)
            throw Invalid(file, key, $"an integer from {minimum} through {maximum} is required");
        return result;
    }
    private static string Text(JsonElement value, string key, string file)
    {
        var element = Property(value, key, file);
        if (element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()) || element.GetString()!.Contains('\0'))
            throw Invalid(file, key, "nonempty text without NUL is required");
        return element.GetString()!;
    }
    private static ReadOnlyDictionary<string, string> Strings(JsonElement value, string file, string key)
    {
        RequireObject(value, file, key);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject()) result.Add(property.Name, Text(value, property.Name, file));
        if (!result.ContainsKey("en")) throw Invalid(file, key, "an English fallback is required");
        return new ReadOnlyDictionary<string, string>(result);
    }
    private static string[] StringArray(JsonElement value, string file, string key)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Invalid(file, key, "an array is required");
        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()) || item.GetString()!.Contains('\0'))
                throw Invalid(file, key, "nonempty strings without NUL are required");
            result.Add(item.GetString()!);
        }
        if (result.Count != result.Distinct(StringComparer.Ordinal).Count()) throw Invalid(file, key, "duplicate strings are not allowed");
        return result.ToArray();
    }
    private static HashSet<int> IdSet(JsonElement value, HashSet<int> known, string file, string key)
    {
        if (value.ValueKind != JsonValueKind.Array) throw Invalid(file, key, "an array is required");
        var result = new HashSet<int>();
        foreach (var item in value.EnumerateArray())
            if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var id) || !known.Contains(id) || !result.Add(id))
                throw Invalid(file, key, "distinct catalogue IDs are required");
        return result;
    }
    private static InvalidDataException Invalid(string file, string key, string reason, Exception? inner = null) => new($"{file}: {key}: {reason}.", inner);
}

internal sealed record ModeDefinition(int Id, string Key, IReadOnlyDictionary<string, string> Names,
    IReadOnlyDictionary<string, string> Tokens, IReadOnlyList<string> Aliases)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Metadata { get; init; } = [];
}

/// <summary>One build's Create Room dialog: its resource and the native control names the controller reads and writes.</summary>
internal sealed record RoomBinding(string Form, string RoomName, string ModeButton, string DescriptionLabel, string CreateButton,
    string CancelButton, RoomEntry Entry);
/// <summary>The overlay entry's region inside the dialog, in the form's own pixels; the button is left-aligned in it.</summary>
internal sealed record RoomEntry(int X, int Y, int Width, int Height);
