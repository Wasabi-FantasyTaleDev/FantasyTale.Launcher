using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Definitions;

/// <summary>
/// Discovers games from a <c>games</c> folder: every sub-folder with a <c>game.toml</c> is a game,
/// and every <c>builds/*.toml</c> beside it is one of its client builds.
/// </summary>
public static class GameCatalog
{
    // The client is a Korean release; release dates are classified in Korea Standard
    // Time (UTC+09:00), even when the Authenticode timestamp is encoded as UTC. Korea
    // does not observe daylight saving time, so the fixed offset is intentional.
    private static readonly TimeSpan KoreaStandardTime = TimeSpan.FromHours(9);
    /// <summary>Loads every game under <paramref name="gamesRoot"/>, sorted by id.</summary>
    /// <exception cref="DefinitionException">A definition is malformed; the message names the file and key.</exception>
    public static IReadOnlyList<GameDefinition> Load(string gamesRoot)
    {
        var games = new List<GameDefinition>();
        foreach (var folder in Directory.EnumerateDirectories(gamesRoot).Order(StringComparer.Ordinal))
        {
            var file = Path.Combine(folder, "game.toml");
            if (File.Exists(file)) games.Add(LoadGame(file));
        }
        var duplicate = games.GroupBy(g => g.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new DefinitionException(gamesRoot, "game.id", $"'{duplicate.Key}' is defined more than once");
        return games.OrderBy(g => g.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Loads one <c>game.toml</c> and its builds.</summary>
    public static GameDefinition LoadGame(string file)
    {
        var root = TomlReader.Load(file);
        root.OnlyKeys("schema", "game", "branches", "client_policy_sources", "client_policy_rules");
        if (root.Integer("schema", required: true) != 1) throw new DefinitionException(file, "schema", "must be 1");
        var game = root.Table("game", required: true)!;
        game.OnlyKeys("id", "status", "module", "hook_host", "accent", "name", "media", "home_order", "preparing", "service_notice", "hero_title", "supports_instances", "supports_plugins", "store_managed_plugins", "default_plugins", "plugin_sources", "launcher_login", "profile_fields", "level_icon", "bundled_instances", "client_languages", "levels", "navigation_entries", "rooms_enabled", "local_hosting_enabled", "unsupported_regions");
        var unsupportedRegions = game.Strings("unsupported_regions");
        if (unsupportedRegions.Distinct(StringComparer.OrdinalIgnoreCase).Count() != unsupportedRegions.Count
            || unsupportedRegions.Any(tag => tag.Length > 32 || !System.Text.RegularExpressions.Regex.IsMatch(tag, @"\A[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*\z")))
            throw game.Error("unsupported_regions", "requires distinct launcher locale tags");
        var heroTitle = game.Has("hero_title") ? game.String("hero_title", required: true) : null;
        if (heroTitle is not null && (heroTitle.Length > 128 || heroTitle.Any(char.IsControl)))
            throw game.Error("hero_title", "must be a single title of at most 128 characters");
        var id = game.String("id", required: true);
        IReadOnlyList<string> navigation = game.Has("navigation_entries")
            ? game.Strings("navigation_entries") : ["home", "downloads", "accounts"];
        if (navigation.Count > 9 || navigation.Distinct(StringComparer.Ordinal).Count() != navigation.Count ||
            !navigation.Contains("home", StringComparer.Ordinal) ||
            navigation.Any(entry => entry is not ("home" or "downloads" or "accounts" or "instances" or "multiplayer" or "hosting" or "store" or "plugins" or "settings")))
            throw game.Error("navigation_entries", "must include home and contain only unique supported menu route identifiers");
        var status = game.String("status", required: true) switch
        {
            "enabled" => GameStatus.Enabled,
            "placeholder" => GameStatus.Placeholder,
            _ => throw new DefinitionException(file, "game.status", "must be enabled or placeholder"),
        };
        var folder = Path.GetDirectoryName(Path.GetFullPath(file))!;
        LevelCatalogDefinition? levels = null;
        if (game.Has("levels"))
        {
            var relative = game.String("levels", true);
            var levelFile = Path.GetFullPath(Path.Combine(folder, relative));
            if (Path.IsPathRooted(relative) || relative.Contains(':', StringComparison.Ordinal) || !levelFile.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw game.Error("levels", "must be a relative manifest inside the game folder");
            levels = LevelCatalog.LoadAvailable(levelFile, folder);
        }
        string[] videos = [];
        string? backgroundImage = null;
        string?[] audioTracks = [];
        if (game.Table("media") is { } media)
        {
            media.OnlyKeys("background_video", "background_videos", "background_fade", "background_audio_tracks", "background_image");
            if (media.Has("background_image"))
            {
                var relative = media.String("background_image", required: true);
                backgroundImage = Path.GetFullPath(Path.Combine(folder, relative));
                if (Path.IsPathRooted(relative) || relative.Contains(':', StringComparison.Ordinal) ||
                    !backgroundImage.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(backgroundImage).ToLowerInvariant() is not (".jpg" or ".jpeg" or ".png" or ".webp"))
                    throw media.Error("background_image", "must be a JPG, PNG or WebP file inside the game folder");
            }
            if (media.Has("background_video") && media.Has("background_videos"))
                throw media.Error("background_videos", "cannot be combined with background_video");
            var key = media.Has("background_videos") ? "background_videos" : "background_video";
            var entries = media.Has("background_videos") ? media.Strings(key) : media.String(key) is { Length: > 0 } single ? [single] : Array.Empty<string>();
            if (entries.Count > 16) throw media.Error(key, "at most 16 videos are allowed");
            videos = entries.Select(relative =>
            {
                if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':', StringComparison.Ordinal))
                    throw media.Error(key, "each video must be a relative local path");
                var full = Path.GetFullPath(Path.Combine(folder, relative));
                if (!full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(full).ToLowerInvariant() is not (".mp4" or ".webm"))
                    throw media.Error(key, "videos must be MP4 or WebM files inside the game folder");
                return full;
            }).ToArray();
        }
        if (game.Table("media") is { } audioMedia && audioMedia.Has("background_audio_tracks"))
        {
            var tracks = audioMedia.Strings("background_audio_tracks");
            if (tracks.Count != videos.Length) throw audioMedia.Error("background_audio_tracks", "must have one entry per video; an empty entry uses embedded audio");
            audioTracks = tracks.Select(relative =>
            {
                if (relative.Length == 0) return null;
                if (Path.IsPathRooted(relative) || relative.Contains(':', StringComparison.Ordinal))
                    throw audioMedia.Error("background_audio_tracks", "must contain relative local paths");
                var full = Path.GetFullPath(Path.Combine(folder, relative));
                if (!full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(full).ToLowerInvariant() is not (".mp3" or ".m4a" or ".ogg" or ".wav" or ".webm"))
                    throw audioMedia.Error("background_audio_tracks", "audio must remain inside the game folder and use a supported format");
                return full;
            }).ToArray();
        }
        var storeManagedPlugins = game.Strings("store_managed_plugins");
        if (storeManagedPlugins.Distinct(StringComparer.Ordinal).Count() != storeManagedPlugins.Count)
            throw game.Error("store_managed_plugins", "must contain unique plugin identities");
        foreach (var pluginId in storeManagedPlugins) PluginCatalog.ValidateId(file, "game.store_managed_plugins", pluginId);
        var defaultPlugins = game.Strings("default_plugins");
        if (defaultPlugins.Distinct(StringComparer.Ordinal).Count() != defaultPlugins.Count)
            throw game.Error("default_plugins", "must contain unique plugin identities");
        foreach (var pluginId in defaultPlugins) PluginCatalog.ValidateId(file, "game.default_plugins", pluginId);
        var pluginSources = game.Tables("plugin_sources").Select(row =>
        {
            row.OnlyKeys("id", "source");
            var pluginId = row.String("id", true);
            var sourceId = row.String("source", true);
            PluginCatalog.ValidateId(file, "game.plugin_sources.id", pluginId);
            PluginCatalog.ValidateId(file, "game.plugin_sources.source", sourceId);
            if (!storeManagedPlugins.Contains(pluginId, StringComparer.Ordinal))
                throw row.Error("id", "must be declared in store_managed_plugins");
            return new KeyValuePair<string, string>(pluginId, sourceId);
        }).ToArray();
        if (pluginSources.Select(pair => pair.Key).Distinct(StringComparer.Ordinal).Count() != pluginSources.Length)
            throw game.Error("plugin_sources", "must contain unique plugin identities");
        var fields = game.Strings("profile_fields");
        if (fields.Count > 8 || fields.Distinct(StringComparer.Ordinal).Count() != fields.Count || fields.Any(f => f is not ("level" or "lucci" or "cash" or "exp" or "tr")))
            throw game.Error("profile_fields", "must contain unique supported account statistic keys");
        var module = game.String("module");
        if (status == GameStatus.Enabled && module.Length == 0)
            throw new DefinitionException(file, "game.module", "required for an enabled game");

        var clientPolicy = ReadClientPolicy(root);
        var identities = LoadClientIdentities(Path.Combine(folder, "client-versions.toml"));
        var builds = new List<BuildDefinition>();
        var buildFolder = Path.Combine(folder, "builds");
        if (Directory.Exists(buildFolder))
            foreach (var buildFile in Directory.EnumerateFiles(buildFolder, "*.toml").Order(StringComparer.Ordinal))
            {
                var build = LoadBuild(buildFile, id);
                try { build = build with { ClientPolicy = Plugins.ProfileClientPolicy.Combine(clientPolicy, build.ClientPolicy) }; }
                catch (ArgumentException error) { throw new DefinitionException(buildFile, "client_policy_rules", error.Message); }
                if (identities.TryGetValue(build.Id, out var identity)) build = build with
                {
                    PeTimestamp = identity.PeTimestamp ?? build.PeTimestamp,
                    SignerCertificateSha256 = identity.Certificate ?? build.SignerCertificateSha256,
                    SignedAt = identity.SignedAt ?? build.SignedAt,
                    ExecutableAliases = identity.Aliases.Count == 0 ? build.ExecutableAliases : identity.Aliases,
                };
                builds.Add(build);
            }
        var duplicate = builds.GroupBy(b => b.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new DefinitionException(buildFolder, "build.id", $"'{duplicate.Key}' is defined more than once");

        return new GameDefinition(id, status, game.Localized("name"), module, game.String("hook_host", required: true),
            game.String("accent"), videos.Length == 0 ? null : videos[0], folder, builds)
        {
            ClientLanguages = Languages(game, "client_languages"), Levels = levels,
            NavigationEntries = navigation,
            RoomsEnabled = game.Boolean("rooms_enabled", true),
            LocalHostingEnabled = game.Boolean("local_hosting_enabled", true),
            SupportsInstanceSelection = game.Boolean("supports_instances"),
            UnsupportedRegions = unsupportedRegions,
            SupportsPlugins = game.Boolean("supports_plugins", true),
            StoreManagedPlugins = storeManagedPlugins,
            DefaultPlugins = defaultPlugins, PluginSources = pluginSources.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), ClientPolicy = clientPolicy,
            LauncherLogin = LoginPolicy(game, "launcher_login", "optional"),
            ProfileFields = fields, LevelIcon = game.Boolean("level_icon"),
            BundledInstances = game.Boolean("bundled_instances"), BackgroundAudioTracks = audioTracks,
            HomeOrder = (int?)game.Integer("home_order", min: 0, max: 10000),
            Preparing = game.Boolean("preparing", status == GameStatus.Placeholder),
            ServiceNotice = game.Has("service_notice") ? game.Localized("service_notice") : null,
            HeroTitle = heroTitle,
            BackgroundVideos = videos,
            BackgroundImage = backgroundImage,
        };
    }

    private sealed record ClientIdentity(uint? PeTimestamp, string? Certificate, DateTimeOffset? SignedAt, IReadOnlyList<string> Aliases);

    private static Dictionary<string, ClientIdentity> LoadClientIdentities(string file)
    {
        if (!File.Exists(file)) return new Dictionary<string, ClientIdentity>(StringComparer.Ordinal);
        var root = TomlReader.Load(file);
        root.OnlyKeys("schema", "versions");
        if (root.Integer("schema", required: true) != 1) throw new DefinitionException(file, "schema", "must be 1");
        var result = new Dictionary<string, ClientIdentity>(StringComparer.Ordinal);
        foreach (var row in root.Tables("versions", 512))
        {
            row.OnlyKeys("id", "pe_timestamp", "signer_certificate_sha256", "signed_at", "executable_aliases");
            var id = row.String("id", required: true);
            if (!IsWireBuildId(id))
                throw row.Error("id", "must be a region-wire version id such as kr-1256 or cn-406");
            var certificate = row.Has("signer_certificate_sha256") ? row.String("signer_certificate_sha256", required: true) : null;
            if (certificate is not null && (certificate.Length != 64 || certificate.Any(c => !char.IsAsciiHexDigit(c)))) throw row.Error("signer_certificate_sha256", "must be a 64-digit certificate SHA-256");
            DateTimeOffset? signedAt = null;
            if (row.Has("signed_at"))
            {
                var value = row.String("signed_at", required: true);
                if (!value.EndsWith('Z') || !DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)) throw row.Error("signed_at", "must be UTC yyyy-MM-ddTHH:mm:ssZ");
                signedAt = parsed.ToOffset(KoreaStandardTime);
            }
            var aliases = row.Strings("executable_aliases");
            if (aliases.Count > 16 || aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count() != aliases.Count || aliases.Any(value => value.Length is 0 or > 128
                || value.Contains('/') || value.Contains('\\') || value.Contains(':') || !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                throw row.Error("executable_aliases", "must contain at most 16 unique leaf executable names");
            if (!result.TryAdd(id, new ClientIdentity((uint?)row.Integer("pe_timestamp", max: uint.MaxValue), certificate, signedAt, aliases)))
                throw row.Error("id", "must be unique");
        }
        return result;
    }

    private static bool IsWireBuildId(string id)
    {
        var parts = id.Split('-', StringSplitOptions.None);
        if (parts.Length < 2 || parts[0].Length is < 2 or > 8 || parts[0].Any(c => c is < 'a' or > 'z')) return false;
        // This checks syntax only. A numeric year or resource version also passes;
        // assigning a wire identity still requires independent client evidence.
        return parts[1].Length > 0 && parts[1].All(char.IsAsciiDigit) && parts.Skip(2).All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }

    /// <summary>Loads one build definition belonging to <paramref name="gameId"/>.</summary>
    public static BuildDefinition LoadBuild(string file, string gameId)
    {
        return ReadBuild(TomlReader.Load(file), gameId);
    }

    /// <summary>Validates an inline store profile using the same contract as a bundled client definition.</summary>
    public static BuildDefinition ParseBuild(string file, string text, string gameId) => ReadBuild(TomlReader.Parse(file, text), gameId);

    private static BuildDefinition ReadBuild(TomlReader root, string gameId)
    {
        var file = root.File;
        root.OnlyKeys("schema", "build", "fingerprint", "launch", "plugins", "overlay", "client_policy_sources", "client_policy_rules");
        if (root.Integer("schema", required: true) != 1) throw new DefinitionException(file, "schema", "must be 1");
        var build = root.Table("build", required: true)!;
        build.OnlyKeys("id", "game", "branch", "build_date", "protocol_version", "executable", "executable_aliases", "messenger", "server_profile", "hosting_adapter", "default_port", "name", "release_version", "patch_version", "description", "enabled", "hidden", "region");
        var id = build.String("id", required: true);
        if (Path.GetFileNameWithoutExtension(file) != id) throw new DefinitionException(file, "build.id", "must match the file name");
        if (build.String("game", required: true) != gameId) throw new DefinitionException(file, "build.game", $"must be '{gameId}'");

        uint? timestamp = null;
        string? signer = null;
        DateTimeOffset? signedAt = null;
        if (root.Table("fingerprint") is { } fingerprint)
        {
            fingerprint.OnlyKeys("pe_timestamp", "signer_certificate_sha256", "signed_at");
            timestamp = (uint?)fingerprint.Integer("pe_timestamp", min: 0, max: uint.MaxValue);
            if (fingerprint.Has("signer_certificate_sha256") || fingerprint.Has("signed_at"))
            {
                signer = fingerprint.String("signer_certificate_sha256", required: true);
                if (signer.Length != 64 || signer.Any(c => !char.IsAsciiHexDigit(c))) throw fingerprint.Error("signer_certificate_sha256", "must be a 64-digit certificate SHA-256");
                var time = fingerprint.String("signed_at", required: true);
                if (!time.EndsWith('Z') || !DateTimeOffset.TryParseExact(time, "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)) throw fingerprint.Error("signed_at", "must be a UTC timestamp in yyyy-MM-ddTHH:mm:ssZ format");
                // Keep the public identity in KST as well. Without this conversion,
                // a build whose external identity row is absent would be classified
                // by the machine's UTC date while all table-backed builds use KST.
                signedAt = parsed.ToOffset(KoreaStandardTime);
            }
        }
        var aliases = build.Strings("executable_aliases");
        if (aliases.Count > 16 || aliases.Distinct(StringComparer.OrdinalIgnoreCase).Count() != aliases.Count || aliases.Any(value => value.Length is 0 or > 128
            || value.Contains('/') || value.Contains('\\') || value.Contains(':') || !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            throw build.Error("executable_aliases", "must contain at most 16 unique leaf executable names");
        var launch = root.Table("launch", required: true)!;
        launch.OnlyKeys("login", "arguments", "launcher_login", "native_language", "additional_languages", "login_module", "requires_elevation");
        var nativeLanguage = launch.String("native_language");
        if (nativeLanguage.Length > 0 && !IsLanguage(nativeLanguage)) throw launch.Error("native_language", "must be a supported language code");
        var additionalLanguages = new Dictionary<string, string>(StringComparer.Ordinal);
        if (launch.Table("additional_languages") is { } packs)
            foreach (var pair in packs.Scalars())
            {
                if (!IsLanguage(pair.Key) || pair.Key == nativeLanguage) throw packs.Error(pair.Key, "must be an additional supported language code distinct from native_language");
                if (pair.Value is not string plugin) throw packs.Error(pair.Key, "must name a file-plugin identity");
                PluginCatalog.ValidateId(file, "launch.additional_languages." + pair.Key, plugin);
                if (additionalLanguages.ContainsValue(plugin)) throw packs.Error(pair.Key, "each language requires a separate file-plugin identity");
                additionalLanguages.Add(pair.Key, plugin);
            }
        if (additionalLanguages.Count > 0 && nativeLanguage.Length == 0)
            throw launch.Error("native_language", "required when additional_languages is declared");
        var loginModule = launch.Has("login_module") ? launch.String("login_module", required: true) : null;
        if (loginModule is not null && (loginModule.Length > 64 || !loginModule.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || loginModule[..^4].Length == 0 || loginModule[..^4].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))))
            throw launch.Error("login_module", "must be a leaf DLL name using letters, digits, underscores or hyphens");
        var plugins = root.Table("plugins");
        plugins?.OnlyKeys("default");
        var region = build.String("region");
        if (region.Length > 0 && (region.Length != 2 || region.Any(c => c is < 'A' or > 'Z')))
            throw build.Error("region", "must be a two-letter uppercase client region");

        return new BuildDefinition(
            id,
            build.String("branch", required: true),
            (int)build.Integer("protocol_version", required: true, min: 0, max: int.MaxValue)!.Value,
            build.Localized("name"),
            build.String("executable", required: true),
            build.String("messenger"),
            build.String("server_profile"),
            (int?)build.Integer("default_port", min: 1, max: 65535),
            timestamp,
            launch.String("login", required: true),
            launch.Strings("arguments"),
            plugins?.Strings("default") ?? [])
        {
            BuildDate = ReadBuildDate(build),
            Enabled = build.Boolean("enabled", true), Hidden = build.Boolean("hidden"),
            ExecutableAliases = aliases, SignerCertificateSha256 = signer, SignedAt = signedAt,
            ReleaseVersion = build.String("release_version"), PatchVersion = build.String("patch_version"), Description = build.Has("description") ? build.Localized("description") : null,
            NativeLanguage = nativeLanguage, AdditionalLanguages = additionalLanguages,
            LauncherLogin = launch.Has("launcher_login") ? LoginPolicy(launch, "launcher_login", "optional") : null,
            LoginModule = loginModule,
            RequiresElevation = launch.Boolean("requires_elevation"),
            HostingAdapter = build.String("hosting_adapter"),
            ClientPolicy = ReadClientPolicy(root), Region = region,
            Overlay = ReadOverlayProfile(root.Table("overlay")),
        };
    }

    private static ClientPolicy ReadClientPolicy(TomlReader root)
    {
        try
        {
            var policy = Stores.StoreCatalogFile.ClientPolicy(root);
            Servers.ServerMetadataPolicy.Validate(policy);
            return policy;
        }
        catch (ArgumentException error) { throw root.Error("client_policy_rules", error.Message); }
    }

    private static OverlayBuildProfile? ReadOverlayProfile(TomlReader? table)
    {
        if (table is null) return null;
        table.OnlyKeys("theme", "capabilities", "targets");
        var theme = table.String("theme", true);
        if (theme is not ("v1" or "v2" or "v3")) throw table.Error("theme", "must be v1, v2 or v3");
        var capabilities = table.Strings("capabilities");
        if (capabilities.Count > 128 || capabilities.Distinct(StringComparer.Ordinal).Count() != capabilities.Count)
            throw table.Error("capabilities", "requires at most 128 unique capability names");
        foreach (var capability in capabilities) PluginCatalog.ValidateId(table.File, "overlay.capabilities", capability);
        var targets = table.Tables("targets", 128).Select(row =>
        {
            row.OnlyKeys("id", "locator", "resource", "control", "parent_target");
            var id = row.String("id", true);
            PluginCatalog.ValidateId(row.File, "overlay.targets.id", id);
            var locator = row.String("locator", true);
            if (locator is not ("hook" or "image" or "ui-resource")) throw row.Error("locator", "must be hook, image or ui-resource");
            var resource = row.String("resource");
            var control = row.String("control");
            var parent = row.String("parent_target");
            if (resource.Length > 256 || control.Length > 128 || parent.Length > 128)
                throw row.Error("resource", "locator metadata exceeds its bounds");
            if (resource.Length > 0 && (Path.IsPathRooted(resource) || resource.Contains(':') || resource.Split('/', '\\').Any(p => p == "..")))
                throw row.Error("resource", "must be a relative package or game resource identity");
            if (locator is "image" or "ui-resource" && resource.Length == 0) throw row.Error("resource", "required for image and UI-resource locators");
            return new OverlayTargetDefinition(id, locator, resource.Length == 0 ? null : resource,
                control.Length == 0 ? null : control, parent.Length == 0 ? null : parent);
        }).ToArray();
        if (targets.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != targets.Length)
            throw table.Error("targets", "target identities must be unique");
        var byId = targets.ToDictionary(t => t.Id, StringComparer.Ordinal);
        foreach (var target in targets)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { target.Id };
            var parent = target.ParentTarget;
            while (parent is not null)
            {
                if (!byId.TryGetValue(parent, out var definition)) throw table.Error("targets", $"parent target '{parent}' is not declared");
                if (!visited.Add(parent)) throw table.Error("targets", "parent targets cannot contain a cycle");
                parent = definition.ParentTarget;
            }
        }
        return new OverlayBuildProfile(theme, capabilities, targets);
    }

    private static DateTime? ReadBuildDate(TomlReader build)
    {
        if (!build.Has("build_date")) return null;
        var value = build.String("build_date", required: true);
        if (value.Length != 19 || !DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var date))
            throw build.Error("build_date", "must use yyyy-MM-dd HH:mm:ss");
        return DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
    }

    private static bool IsLanguage(string value) => value is "zh-CN" or "zh-TW" or "ko" or "en" or "id" or "vi";

    private static IReadOnlyList<string> Languages(TomlReader table, string key)
    {
        var values = table.Strings(key);
        if (values.Count > 6 || values.Distinct(StringComparer.Ordinal).Count() != values.Count || values.Any(v => !IsLanguage(v)))
            throw table.Error(key, "must contain unique supported language codes");
        return values;
    }

    private static string LoginPolicy(TomlReader table, string key, string fallback)
    {
        var value = table.String(key, fallback: fallback);
        return value is "required" or "optional" or "none" ? value : throw table.Error(key, "must be required, optional or none");
    }
}
