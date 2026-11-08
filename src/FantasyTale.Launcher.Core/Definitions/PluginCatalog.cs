using System.Text;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Definitions;

/// <summary>Reads hook manifests and enforces the same option, patch and compatibility contract as FtHook.</summary>
public static class PluginCatalog
{
    /// <summary>Compares validated plugin versions using numeric components and prerelease precedence.</summary>
    public static int CompareVersions(string left, string right) => PluginVersion.Parse(left).CompareTo(PluginVersion.Parse(right));

    /// <summary>The runtime version shipped with this launcher.</summary>
    public const string RuntimeVersion = "3.7.3";

    /// <summary>Loads immediate package folders, rejecting duplicate identities instead of choosing by scan order.</summary>
    public static IReadOnlyList<PluginDefinition> Load(string root)
    {
        if (!Directory.Exists(root)) return [];
        var result = Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal)
            .Where(d => File.Exists(Path.Combine(d, "plugin.toml"))).Select(d => LoadFile(Path.Combine(d, "plugin.toml"))).ToArray();
        if (result.GroupBy(p => (p.Id, p.Version)).Any(g => g.Count() > 1))
            throw new DefinitionException(root, "plugin.id", "duplicate plugin id and version");
        ValidateIdentityConflicts(result, root, allowInstalledPredecessors: true);
        return result;
    }

    /// <summary>Rejects ambiguous and cyclic aliases, optionally retaining same-game predecessors during migration.</summary>
    public static void ValidateIdentityConflicts(IReadOnlyList<PluginDefinition> packages, string source = "plugins", bool allowInstalledPredecessors = false)
    {
        var canonicalIds = packages.Select(package => package.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var alias in packages.SelectMany(package => package.Aliases.Select(alias => (package, alias))))
        {
            if (alias.alias == alias.package.Id)
                throw new DefinitionException(Path.Combine(alias.package.Folder, "plugin.toml"), "plugin.aliases", "must not contain the canonical plugin id");
            if (canonicalIds.Contains(alias.alias) && !(allowInstalledPredecessors && packages.Where(package => package.Id == alias.alias)
                .All(former => former.Game == alias.package.Game)))
                throw new DefinitionException(Path.Combine(alias.package.Folder, "plugin.toml"), "plugin.aliases", $"conflicts with canonical plugin id '{alias.alias}'");
        }

        foreach (var group in packages.SelectMany(package => package.Aliases.Select(alias => (package, alias))).GroupBy(item => item.alias, StringComparer.Ordinal))
        {
            if (ResolveAliasOwner(group.Select(item => item.package).ToArray()) is null)
                throw new DefinitionException(source, "plugin.aliases", $"alias '{group.Key}' maps to multiple canonical plugin ids");
            _ = CanonicalId(packages, group.Key);
        }
    }

    /// <summary>Tests whether a package owns a canonical identifier or one of its former identifiers.</summary>
    public static bool MatchesIdentity(PluginDefinition package, string id) =>
        string.Equals(package.Id, id, StringComparison.Ordinal) || package.Aliases.Contains(id, StringComparer.Ordinal);

    /// <summary>Matches a catalog offer before download using its validated inline former identifiers.</summary>
    public static bool MatchesIdentity(StorePackage package, string id) =>
        string.Equals(package.Id, id, StringComparison.Ordinal) || package.PluginDefinition is { } definition && MatchesIdentity(definition, id);

    /// <summary>Resolves an advertised former identity to its single declared successor without guessing names.</summary>
    public static string CanonicalId(IReadOnlyList<PluginDefinition> packages, string id)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            if (!visited.Add(id)) throw new DefinitionException("plugins", "plugin.aliases", "identity migration contains a cycle");
            var candidates = packages.Where(package => package.Aliases.Contains(id, StringComparer.Ordinal)).ToArray();
            if (candidates.Length == 0) return id;
            var owner = ResolveAliasOwner(candidates)
                ?? throw new DefinitionException("plugins", "plugin.aliases", $"alias '{id}' maps to multiple canonical plugin ids");
            id = owner.Id;
        }
    }

    private static PluginDefinition? ResolveAliasOwner(IReadOnlyList<PluginDefinition> candidates) => candidates
        .OrderByDescending(package => PluginVersion.Parse(package.Version))
        .FirstOrDefault(owner => candidates.All(former => former.Id == owner.Id || former.Game == owner.Game
            && owner.Aliases.Contains(former.Id, StringComparer.Ordinal)));

    /// <summary>Loads a manifest without loading or executing its native DLL.</summary>
    public static PluginDefinition LoadFile(string file)
    {
        return ReadManifest(TomlReader.Load(file), true);
    }

    /// <summary>Validates inline store metadata without reading sidecars or loading native code.</summary>
    public static PluginDefinition Parse(string file, string text) => ReadManifest(TomlReader.Parse(file, text), false);

    private static PluginDefinition ReadManifest(TomlReader root, bool readSidecars)
    {
        var file = root.File;
        root.OnlyKeys("schema", "plugin", "compatibility", "options", "patches", "native", "files", "overlay", "service", "companion", "resources");
        if (root.Integer("schema", true) != 1) throw root.Error("schema", "must be 1");
        var p = root.Table("plugin", true)!;
        p.OnlyKeys("id", "aliases", "version", "game", "kind", "runtime", "name", "description", "authors", "license", "homepage", "warning", "update_source", "minimum_launcher_version", "restore_files", "diagnostics", "exclusive_with");
        var minimumLauncher = p.String("minimum_launcher_version");
        try { LauncherCompatibility.ValidateMinimum(minimumLauncher); }
        catch (InvalidDataException) { throw p.Error("minimum_launcher_version", "must be a SemVer version"); }
        var id = p.String("id", true);
        ValidateId(file, "plugin.id", id);
        var aliases = p.Strings("aliases");
        foreach (var alias in aliases) ValidateId(file, "plugin.aliases", alias);
        if (aliases.Contains(id, StringComparer.Ordinal) || aliases.Distinct(StringComparer.Ordinal).Count() != aliases.Count)
            throw p.Error("aliases", "must contain distinct former identifiers and not the canonical plugin id");
        var exclusiveWith = p.Strings("exclusive_with");
        foreach (var other in exclusiveWith) ValidateId(file, "plugin.exclusive_with", other);
        if (exclusiveWith.Contains(id, StringComparer.Ordinal) || exclusiveWith.Distinct(StringComparer.Ordinal).Count() != exclusiveWith.Count)
            throw p.Error("exclusive_with", "must contain distinct other plugin identities");
        var version = p.String("version", true);
        ValidateVersion(file, "plugin.version", version);
        var game = p.String("game", true);
        ValidateId(file, "plugin.game", game);
        if (p.String("update_source") is { Length: > 0 } updateSource) ValidateId(file, "plugin.update_source", updateSource);
        var kind = p.String("kind", true);
        if (kind is not ("patch" or "native" or "files" or "overlay" or "service" or "resources")) throw p.Error("kind", "must be patch, native, files, overlay, service or resources");
        var restoreFiles = p.Boolean("restore_files", true);
        if (p.Has("restore_files") && kind != "files") throw p.Error("restore_files", "only allowed for file plugins");
        var installationFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, "file-install.toml");
        if (readSidecars && kind == "files" && File.Exists(installationFile))
        {
            Instances.InstanceStore.RejectLinks(installationFile);
            var installation = TomlReader.Load(installationFile);
            installation.OnlyKeys("schema", "restore_files");
            if (installation.Integer("schema", true) != 1) throw installation.Error("schema", "must be 1");
            var retainedPolicy = installation.Boolean("restore_files", true);
            if (p.Has("restore_files") && restoreFiles != retainedPolicy) throw installation.Error("restore_files", "conflicts with plugin manifest");
            restoreFiles = retainedPolicy;
        }
        var runtime = p.String("runtime");
        try { _ = PluginVersion.Satisfies(RuntimeVersion, runtime); }
        catch (Exception e) when (e is FormatException or OverflowException) { throw p.Error("runtime", "invalid version range"); }
        var c = root.Table("compatibility");
        c?.OnlyKeys("builds", "exclude");
        var options = root.Tables("options", 256).Select(ReadOption).ToArray();
        if (options.GroupBy(o => o.Key).Any(g => g.Count() > 1)) throw root.Error("options", "duplicate option key");
        var patches = root.Tables("patches", 256).Select(t => ReadPatch(t, options)).ToArray();
        var native = root.Table("native");
        native?.OnlyKeys("library", "entry");
        string? library = null, entry = null;
        if (native is not null)
        {
            if (kind != "native") throw root.Error("native", "only allowed for native plugins");
            library = native.String("library", true);
            if (library.Length == 0 || library is "." or ".." || library.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw native.Error("library", "must be a file name inside the plugin folder");
            entry = native.String("entry", fallback: "fthook_plugin_main");
            if (entry.Length is < 1 or > 128 || entry.Contains('\0')) throw native.Error("entry", "must be a non-NUL export name of 1..128 characters");
        }
        if (kind == "native" && native is null) throw root.Error("native", "required for native plugins");
        var overlay = root.Table("overlay");
        overlay?.OnlyKeys("entry");
        OverlayPluginDefinition? overlayDefinition = null;
        if (kind == "overlay")
        {
            if (overlay is null) throw root.Error("overlay", "required for overlay plugins");
            var overlayEntry = overlay.String("entry");
            if (overlayEntry.Length > 0 && (Path.GetFileName(overlayEntry) != overlayEntry || !overlayEntry.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                || overlayEntry.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || overlayEntry.Contains("..", StringComparison.Ordinal)))
                throw overlay.Error("entry", "must be a single package-relative .html file name");
            overlayDefinition = new OverlayPluginDefinition(overlayEntry.Length == 0 ? null : overlayEntry);
        }
        else if (overlay is not null) throw root.Error("overlay", "only allowed for overlay plugins");
        var service = root.Table("service");
        LauncherServiceDefinition? serviceDefinition = null;
        if (kind == "service")
        {
            if (service is null) throw root.Error("service", "required for service plugins");
            service.OnlyKeys("provider", "messages");
            var provider = service.String("provider", true);
            if (provider is not ("companion" or "overlay-notifications")) throw service.Error("provider", "unknown built-in launcher service provider");
            var messages = service.Table("messages")?.Scalars() ?? new Dictionary<string, object>();
            if (messages.Count > 128) throw service.Error("messages", "at most 128 message types are allowed");
            var grants = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (type, schema) in messages)
            {
                ValidateId(file, "service.messages", type);
                if (!char.IsAsciiLetterLower(type[0]) || schema is not long versionNumber || versionNumber is < 1 or > 65535)
                    throw service.Error("messages", "requires lower-case message type identities and schema versions between 1 and 65535");
                grants.Add(type, (int)versionNumber);
            }
            if (provider == "companion" && grants.Count > 0 && (grants.Count != 2
                || !grants.TryGetValue("ui.notification.show", out var notificationShow) || notificationShow != 1
                || !grants.TryGetValue("ui.notification.result", out var notificationResult) || notificationResult != 1))
                throw service.Error("messages", "communication accepts only the notification routing schema pair");
            if (provider == "overlay-notifications" && (grants.Count != 2 || !grants.TryGetValue("ui.notification.show", out var showVersion)
                || showVersion != 1 || !grants.TryGetValue("ui.notification.result", out var resultVersion) || resultVersion != 1))
                throw service.Error("messages", "the notification provider requires only ui.notification.show/result schema one");
            serviceDefinition = new(provider, grants);
        }
        else if (service is not null) throw root.Error("service", "only allowed for service plugins");
        var resource = root.Table("resources");
        GameResourceDefinition? resourceDefinition = null;
        if (kind == "resources")
        {
            if (resource is null) throw root.Error("resources", "required for resource plugins");
            resource.OnlyKeys("provider", "manifest");
            var provider = resource.String("provider", true);
            ValidateId(file, "resources.provider", provider);
            resourceDefinition = new(provider, Hooks.FilePluginResolver.RelativePath(resource.String("manifest", true)));
            if (patches.Length != 0 || native is not null) throw resource.Error("provider", "resource plugins cannot contain patches or native libraries");
        }
        else if (resource is not null) throw root.Error("resources", "only allowed for resource plugins");
        var companion = root.Table("companion");
        var companionMessages = new Dictionary<string, int>(StringComparer.Ordinal);
        if (companion is not null)
        {
            if (kind != "native") throw companion.Error("messages", "only native plugins declare companion consumer grants");
            companion.OnlyKeys("messages");
            var messages = companion.Table("messages", true)!.Scalars();
            if (messages.Count is < 1 or > 128) throw companion.Error("messages", "requires 1..128 message types");
            foreach (var (type, schema) in messages)
            {
                ValidateId(file, "companion.messages", type);
                if (!char.IsAsciiLetterLower(type[0]) || schema is not long number || number is < 1 or > 65535)
                    throw companion.Error("messages", "requires lower-case type identities and schema versions between 1 and 65535");
                companionMessages.Add(type, (int)number);
            }
        }
        var files = root.Tables("files").Select(row =>
        {
            row.OnlyKeys("source", "target", "size", "sha256", "builds", "client_languages", "exclude_client_languages");
            var source = Hooks.FilePluginResolver.RelativePath(row.String("source", true));
            var target = Hooks.FilePluginResolver.RelativePath(row.String("target", true));
            var hash = row.String("sha256", true);
            if (hash.Length != 64 || !hash.All(char.IsAsciiHexDigitLower)) throw row.Error("sha256", "must contain 64 lower-case hexadecimal characters");
            var (languages, excluded) = ReadClientLanguages(row);
            return new PluginFile(source, target, row.Integer("size", true, 1)!.Value, hash, row.Strings("builds"))
            { ClientLanguages = languages, ExcludeClientLanguages = excluded };
        }).ToArray();
        if (kind == "files" ? files.Length == 0 : files.Length != 0)
            throw root.Error("files", "file plugins require payloads; other plugin kinds cannot contain files");
        if (kind == "files" && options.Any(option => option.Type != "bool" || !patches.Any(patch => patch.When == option.Key)))
            throw root.Error("files", "file plugin options must be boolean gates referenced by memory patches");
        if (kind == "overlay" && (patches.Length != 0 || options.Length != 0 || native is not null))
            throw root.Error("overlay", "overlay plugins cannot contain patches, options or native libraries");
        if (kind == "service" && (patches.Length != 0 || native is not null))
            throw root.Error("service", "service plugins cannot contain patches or native libraries");
        return new PluginDefinition(id, version, game, kind, runtime, Path.GetDirectoryName(Path.GetFullPath(file))!,
            p.Localized("name"), p.Has("description") ? p.Localized("description") : null, p.Strings("authors"), p.String("license"), p.String("homepage"),
            c is null || !c.Has("builds") ? ["*"] : c.Strings("builds"), c?.Strings("exclude") ?? [],
            options, patches, library, entry) { Files = files, Warning = p.Has("warning") ? p.Localized("warning") : null,
                UpdateSource = p.String("update_source"), MinimumLauncherVersion = minimumLauncher, RestoreFiles = restoreFiles,
                Diagnostics = p.Boolean("diagnostics"), Overlay = overlayDefinition, Service = serviceDefinition, Resources = resourceDefinition, CompanionMessages = companionMessages,
                ExclusiveWith = exclusiveWith, Aliases = aliases };
    }

    /// <summary>Tests build availability, including exclusion selectors.</summary>
    public static bool IsAvailable(PluginDefinition plugin, BuildDefinition build) =>
        Matches(plugin.Builds, build) && !Matches(plugin.Exclude, build);

    /// <summary>Matches exact selected game-language tags, with case-insensitive exclusions taking precedence.</summary>
    public static bool MatchesClientLanguage(IReadOnlyList<string> included, IReadOnlyList<string> excluded, string language) =>
        (included.Count == 0 || included.Contains(language, StringComparer.OrdinalIgnoreCase))
        && !excluded.Contains(language, StringComparer.OrdinalIgnoreCase);

    /// <summary>Tests whether the local client policy locks this plugin on for the selected build.</summary>
    public static bool IsRequired(PluginDefinition plugin, BuildDefinition build) =>
        Plugins.ProfileClientPolicy.Has(build, plugin, "require");

    /// <summary>Whether disabling this component requires explicit confirmation for each launch.</summary>
    public static bool NeedsWarning(PluginDefinition plugin, BuildDefinition build) => plugin.Warning is not null && Plugins.ProfileClientPolicy.Has(build, plugin, "recommend") && IsAvailable(plugin, build);

    /// <summary>Checks a selected package and resolves defaults plus validated instance overrides.</summary>
    public static IReadOnlyDictionary<string, object> ResolveOptions(PluginDefinition plugin, BuildDefinition build,
        IReadOnlyDictionary<string, object> overrides)
    {
        var file = Path.Combine(plugin.Folder, "plugin.toml");
        if (!IsAvailable(plugin, build)) throw new DefinitionException(file, "compatibility", $"unavailable on {build.Id}");
        LauncherCompatibility.Require(plugin.MinimumLauncherVersion, plugin.Id);
        if (!PluginVersion.Satisfies(RuntimeVersion, plugin.Runtime)) throw new DefinitionException(file, "plugin.runtime", $"requires {plugin.Runtime}");
        var result = ResolveOptionValues(plugin, overrides);
        var active = plugin.Patches.Where(p => p.Builds.Count == 0 || Matches(p.Builds, build)).ToArray();
        if (active.GroupBy(p => p.Id).Any(g => g.Count() > 1))
            throw new DefinitionException(file, "patches", $"overlapping patch variants for {build.Id}");
        if (active.Length == 0 && plugin.Kind == "patch") throw new DefinitionException(file, "patches", $"no patch for {build.Id}");
        if (plugin.Kind == "files" && !plugin.Files.Any(f => f.Builds.Count == 0 || Matches(f.Builds, build)))
            throw new DefinitionException(file, "files", $"no file replacement for {build.Id}");
        return result;
    }

    /// <summary>Validates stored option overrides even when a plugin is currently disabled.</summary>
    public static IReadOnlyDictionary<string, object> ResolveOptionValues(PluginDefinition plugin, IReadOnlyDictionary<string, object> overrides)
    {
        var file = Path.Combine(plugin.Folder, "plugin.toml");
        var result = plugin.Options.ToDictionary(o => o.Key, o => o.Default, StringComparer.Ordinal);
        foreach (var (key, value) in overrides)
        {
            var option = plugin.Options.FirstOrDefault(o => o.Key == key) ?? throw new DefinitionException(file, "options." + key, "unknown option");
            ValidateOption(file, "options." + key, option, value);
            result[key] = value;
        }
        return result;
    }

    internal static bool Matches(IReadOnlyList<string> selectors, BuildDefinition build) =>
        selectors.Any(s => s == "*" || s == build.Id || (build.Branch.Length > 0 && s == "branch:" + build.Branch));

    internal static void ValidateId(string file, string key, string id)
    {
        if (id.Length is < 1 or > 96 || !id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-'))
            throw new DefinitionException(file, key, "must be 1..96 characters of a-z 0-9 . -");
    }

    internal static void ValidateVersion(string file, string key, string version)
    {
        try { _ = PluginVersion.Parse(version); }
        catch (Exception e) when (e is FormatException or OverflowException) { throw new DefinitionException(file, key, "must be a SemVer version"); }
    }

    private static PluginOption ReadOption(TomlReader t)
    {
        t.OnlyKeys("key", "type", "default", "min", "max", "max_length", "values", "label", "description");
        var key = t.String("key", true);
        if (key.Length == 0 || key.Contains('\0')) throw t.Error("key", "must be a nonempty non-NUL key");
        var type = t.String("type", true);
        if (type is not ("bool" or "int" or "string" or "enum")) throw t.Error("type", "must be bool, int, string or enum");
        var option = new PluginOption(key, type, t.Scalar("default"), t.Integer("min") ?? long.MinValue,
            t.Integer("max") ?? long.MaxValue, (int)(t.Integer("max_length", min: 1, max: 65536) ?? 256),
            t.Strings("values"), t.Localized("label"), t.Has("description") ? t.Localized("description") : null);
        ValidateOption(t.File, t.Error("default", "").Key, option, option.Default);
        return option;
    }

    private static void ValidateOption(string file, string key, PluginOption spec, object value)
    {
        var valid = spec.Type switch
        {
            "bool" => value is bool,
            "int" => value is long n && n >= spec.Minimum && n <= spec.Maximum,
            "string" or "enum" => value is string s && Encoding.UTF8.GetByteCount(s) <= spec.MaxLength && (spec.Type != "enum" || spec.Values.Contains(s, StringComparer.Ordinal)),
            _ => false,
        };
        if (!valid) throw new DefinitionException(file, key, $"must satisfy the declared {spec.Type} constraints");
    }

    private static PluginPatch ReadPatch(TomlReader t, PluginOption[] options)
    {
        t.OnlyKeys("id", "builds", "when", "locate", "expect", "write", "timing", "deadline_ms", "client_languages", "exclude_client_languages");
        var id = t.String("id", true);
        if (id.Length == 0) throw t.Error("id", "must not be empty");
        var builds = t.Strings("builds");
        var (languages, excluded) = ReadClientLanguages(t);
        var when = t.String("when");
        if (when.Length > 0 && !options.Any(o => o.Key == when && o.Type == "bool")) throw t.Error("when", "must name a bool option");
        var expect = t.String("expect", true); var write = t.String("write", true);
        if (PatternLength(t, "expect", expect, true) != PatternLength(t, "write", write, false)) throw t.Error("write", "must have the same length as expect");
        var timing = t.String("timing", fallback: "unpacked");
        if (timing is not ("unpacked" or "immediate")) throw t.Error("timing", "must be unpacked or immediate");
        var locate = t.Table("locate", true)!;
        locate.OnlyKeys("pattern", "match", "rva", "locator", "offset");
        var pattern = locate.String("pattern"); var locator = locate.String("locator");
        var rva = (uint?)locate.Integer("rva", min: 0, max: uint.MaxValue);
        if ((pattern.Length > 0 ? 1 : 0) + (locator.Length > 0 ? 1 : 0) + (rva.HasValue ? 1 : 0) != 1)
            throw t.Error("locate", "needs exactly one of pattern, rva, locator");
        if (pattern.Length > 0) _ = PatternLength(locate, "pattern", pattern, true);
        if (rva.HasValue && (builds.Count != 1 || builds[0] == "*" || builds[0].StartsWith("branch:", StringComparison.Ordinal)))
            throw locate.Error("rva", "requires builds to name exactly one build");
        var match = locate.String("match", fallback: "unique");
        if (match is not ("unique" or "first")) throw locate.Error("match", "must be unique or first");
        return new PluginPatch(id, builds, when, pattern.Length == 0 ? null : pattern, rva, locator.Length == 0 ? null : locator,
            (int)(locate.Integer("offset", min: -65536, max: 65536) ?? 0), match, expect, write, timing,
            (int)(t.Integer("deadline_ms", min: 0, max: 600000) ?? 60000))
            { ClientLanguages = languages, ExcludeClientLanguages = excluded };
    }

    private static (IReadOnlyList<string> Included, IReadOnlyList<string> Excluded) ReadClientLanguages(TomlReader table)
    {
        var included = table.Strings("client_languages");
        var excluded = table.Strings("exclude_client_languages");
        foreach (var (key, values) in new[] { ("client_languages", included), ("exclude_client_languages", excluded) })
            if (values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count || values.Any(language =>
                language.Length is < 1 or > 32 || language.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
                throw table.Error(key, "requires distinct language tags");
        if (included.Intersect(excluded, StringComparer.OrdinalIgnoreCase).Any())
            throw table.Error("exclude_client_languages", "must not overlap client_languages");
        return (included, excluded);
    }

    private static int PatternLength(TomlReader t, string key, string text, bool wildcards)
    {
        var tokens = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens.Any(s => !(wildcards && s is "?" or "??") && !(s.Length == 2 && s.All(char.IsAsciiHexDigit))))
            throw t.Error(key, "must be hexadecimal bytes" + (wildcards ? " with optional wildcards" : " without wildcards"));
        return tokens.Length;
    }
}
