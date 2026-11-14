using System.Text;
using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Multiplayer;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Stores;

/// <summary>Strict parser for the common HTTP/AList store catalog.</summary>
public static class StoreCatalogFile
{
    /// <summary>Parses legacy or unified metadata without resolving paths or installing any package.</summary>
    public static StoreCatalog Parse(string file, string text)
    {
        var root = TomlReader.Parse(file, text);
        var schema = root.Integer("schema", true);
        if (schema is not (1 or 2)) throw root.Error("schema", "must be 1 or 2");
        root.OnlyKeys(schema == 1 ? ["schema", "catalog", "packages"] : ["schema", "catalog", "packages", "profiles", "servers", "directory_sources", "github_releases", "deprecated_plugins"]);
        var catalog = root.Table("catalog", true)!; catalog.OnlyKeys("id", "name");
        var id = catalog.String("id", true); PluginCatalog.ValidateId(file, "catalog.id", id);
        var packages = new List<StorePackage>(); var releases = new HashSet<(string, string)>(); var clientDigests = new HashSet<(string, string)>();
        foreach (var p in root.Tables("packages"))
        {
            var allowed = new[] { "id", "kind", "game", "builds", "version", "name", "description", "publisher", "size", "manifest", "manifest_sha256", "revision", "update_mode", "authors", "uploader" };
            p.OnlyKeys(schema == 2 ? [.. allowed, "plugin_definition", "minimum_launcher_version", "github_asset", "plugin_archive"] : [.. allowed, "plugin_archive"]);
            var packageId = p.String("id", true); PluginCatalog.ValidateId(file, p.Error("id", "").Key, packageId);
            var kind = p.String("kind", true);
            if (kind is not ("client" or "plugin" or "runtime" or "launcher" or "server")) throw p.Error("kind", "unknown package kind");
            var game = p.String("game");
            if (kind != "launcher" || game.Length > 0) InstanceFile.ValidateId(file, game);
            var builds = p.Strings("builds");
            if (builds.Distinct(StringComparer.Ordinal).Count() != builds.Count || kind == "client" && builds.Count != 1 || kind == "server" && builds.Count == 0)
                throw p.Error("builds", "requires unique build ids; a client requires exactly one and a server at least one");
            foreach (var build in builds) InstanceFile.ValidateId(file, build);
            var version = kind == "client" ? p.String("version") : Printable(p, "version", 64);
            if (version.Length > 64 || version.Any(char.IsControl) || version.Length > 0 && string.IsNullOrWhiteSpace(version)) throw p.Error("version", "invalid optional update label");
            var updateMode = p.Has("update_mode") ? p.String("update_mode", true) : kind == "client" && version.Length == 0 ? "files" : "release";
            if (updateMode is not ("files" or "release") || updateMode == "files" && kind != "client") throw p.Error("update_mode", "files applies only to clients");
            if (kind is "plugin" or "runtime" or "launcher" or "server") PluginCatalog.ValidateVersion(file, p.Error("version", "").Key, version);
            if (version.Length > 0 && !releases.Add((packageId, version))) throw p.Error("version", "duplicate package release");
            var hash = p.String("manifest_sha256", true);
            if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c))) throw p.Error("manifest_sha256", "must be a SHA-256 digest");
            if (kind == "client" && !clientDigests.Add((packageId, hash.ToLowerInvariant()))) throw p.Error("manifest_sha256", "duplicate file snapshot");
            var path = p.String("manifest", true);
            try { SourceAddress.RelativePath(path); } catch (ArgumentException e) { throw p.Error("manifest", e.Message); }
            PluginDefinition? plugin = null;
            var minimumLauncher = p.String("minimum_launcher_version");
            try { LauncherCompatibility.ValidateMinimum(minimumLauncher); }
            catch (InvalidDataException) { throw p.Error("minimum_launcher_version", "must be a SemVer version"); }
            if (p.Has("plugin_definition"))
            {
                if (kind != "plugin") throw p.Error("plugin_definition", "only applies to plugin packages");
                var definition = p.String("plugin_definition", true);
                plugin = PluginCatalog.Parse(Path.Combine("store-metadata", game, packageId, version, "plugin.toml"), definition);
                if (plugin.Id != packageId || plugin.Game != game || plugin.Version != version)
                    throw p.Error("plugin_definition", "identity disagrees with the package");
                if (p.Has("minimum_launcher_version") && (plugin.MinimumLauncherVersion ?? "") != minimumLauncher)
                    throw p.Error("minimum_launcher_version", "disagrees with inline plugin metadata");
                minimumLauncher = plugin.MinimumLauncherVersion ?? "";
            }
            var authors = p.Has("authors") ? p.Strings("authors") : plugin?.Authors ?? [];
            if (authors.Count > 32 || authors.Any(author => string.IsNullOrWhiteSpace(author) || author.Length > 128 || author.Any(char.IsControl)))
                throw p.Error("authors", "requires at most 32 nonempty printable author names of at most 128 characters");
            var uploader = p.String("uploader");
            if (uploader.Length > 128 || uploader.Any(char.IsControl) || uploader.Length > 0 && string.IsNullOrWhiteSpace(uploader))
                throw p.Error("uploader", "must be empty or a printable name of at most 128 characters");
            StoreGitHubAsset? github = null;
            if (p.Table("github_asset") is { } asset)
            {
                asset.OnlyKeys("repository", "tag", "asset", "sha256", "executable");
                github = new(asset.String("repository", true), asset.String("tag", true), asset.String("asset", true),
                    asset.String("sha256", true), asset.String("executable", true));
                try { GitHubStoreReleases.Validate(github); }
                catch (ArgumentException error) { throw asset.Error("github_asset", error.Message); }
                if (kind != "server") throw p.Error("github_asset", "requires a server archive package");
            }
            StorePluginArchive? pluginArchive = null;
            if (p.Table("plugin_archive") is { } zip)
            {
                zip.OnlyKeys("source_path", "size", "sha256");
                if (kind != "plugin") throw p.Error("plugin_archive", "requires a plugin package");
                var zipPath = zip.String("source_path", true);
                try { SourceAddress.RelativePath(zipPath); } catch (ArgumentException error) { throw zip.Error("source_path", error.Message); }
                if (!zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) throw zip.Error("source_path", "must name a ZIP file");
                var zipHash = zip.String("sha256", true);
                if (zipHash.Length != 64 || zipHash.Any(c => !char.IsAsciiHexDigit(c))) throw zip.Error("sha256", "must be a SHA-256 digest");
                pluginArchive = new(zipPath, zip.Integer("size", true, 1)!.Value, zipHash.ToLowerInvariant());
            }
            var package = new StorePackage(packageId, kind, game, builds, version, Localized(p, "name", 128),
                p.Has("description") ? Localized(p, "description", 4096) : new Dictionary<string, string>(),
                Printable(p, "publisher", 128), p.Integer("size", true, 0)!.Value, path, hash.ToLowerInvariant(), p.Integer("revision", false, 0, 9007199254740991) ?? 0, updateMode)
                { PluginDefinition = plugin, MinimumLauncherVersion = minimumLauncher, Authors = authors, Uploader = uploader, GitHubAsset = github, PluginArchive = pluginArchive };
            if (github is not null) _ = ClientRelease.Parse(package, GitHubStoreReleases.Manifest(package));
            packages.Add(package);
        }
        if (packages.GroupBy(p => p.Id).Any(g => g.Any(p => p.UpdateMode == "files") && g.Count() != 1))
            throw root.Error("packages", "a file-sync package must have exactly one current snapshot");
        if (packages.Where(p => p.Revision > 0).GroupBy(p => (p.Id, p.Revision)).Any(g => g.Count() > 1))
            throw root.Error("packages", "release revisions must be unique within each package");
        var inlinePlugins = packages.Where(package => package.PluginDefinition is not null).Select(package => package.PluginDefinition!).ToArray();
        PluginCatalog.ValidateIdentityConflicts(inlinePlugins, file, allowInstalledPredecessors: true);
        var deprecated = new List<StoreDeprecatedPlugin>();
        foreach (var row in root.Tables("deprecated_plugins"))
        {
            row.OnlyKeys("id", "game", "name", "reason");
            var retiredId = row.String("id", true); var game = row.String("game", true);
            PluginCatalog.ValidateId(file, "deprecated_plugins.id", retiredId); InstanceFile.ValidateId(file, game);
            if (deprecated.Any(item => item.Id == retiredId)) throw row.Error("id", "duplicate retired plugin");
            if (packages.Any(package => package.Id == retiredId && (package.Kind != "plugin" || package.Game != game))
                || inlinePlugins.Any(plugin => plugin.Aliases.Contains(retiredId, StringComparer.Ordinal)))
                throw row.Error("id", "retirement conflicts with a package identity or replacement alias");
            deprecated.Add(new(retiredId, game, Localized(row, "name", 128),
                row.Has("reason") ? Localized(row, "reason", 4096) : new Dictionary<string, string>()));
        }
        packages.RemoveAll(package => deprecated.Any(item => item.Id == package.Id && item.Game == package.Game));
        var profiles = new List<StoreClientProfile>();
        foreach (var row in root.Tables("profiles"))
        {
            row.OnlyKeys("game", "build", "revision", "definition");
            var game = row.String("game", true); var build = row.String("build", true);
            InstanceFile.ValidateId(file, game); InstanceFile.ValidateId(file, build);
            var definition = row.String("definition", true);
            var parsed = GameCatalog.ParseBuild(build + ".toml", definition, game);
            if (parsed.Id != build) throw row.Error("build", "does not match inline definition");
            if (profiles.Any(p => p.Game == game && p.Definition.Id == build)) throw row.Error("build", "duplicate profile");
            profiles.Add(new(game, row.Integer("revision", true, 1, 9007199254740991)!.Value, parsed,
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(definition)))));
        }
        var servers = new List<DirectoryEntry>();
        foreach (var row in root.Tables("servers"))
        {
            row.OnlyKeys("id", "kind", "game", "name", "api_url", "host", "port", "builds", "description", "expires_at", "presentation", "client_policy_sources", "client_policy_rules", "profile");
            var serverId = row.String("id", true); var game = row.String("game", true);
            var host = row.String("host"); var api = row.String("api_url");
            if (row.Has("host") != row.Has("port") || row.Has("host") && host.Length == 0)
                throw row.Error("host", "host and port must be supplied together");
            var kind = row.String("kind", fallback: "server");
            if (kind is not ("server" or "room")) throw row.Error("kind", "must be server or room");
            DateTimeOffset? expires = null;
            if (row.Has("expires_at"))
            {
                if (!DateTimeOffset.TryParseExact(row.String("expires_at", true), "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                    throw row.Error("expires_at", "must use UTC yyyy-MM-ddTHH:mm:ssZ");
                expires = parsed;
            }
            if (kind == "room" && expires is null || kind == "server" && expires is not null)
                throw row.Error("expires_at", "required only for rooms");
            var details = new DirectoryAdvertisement(game, row.String("name", true), api, row.Strings("builds"), row.String("description"))
            {
                Endpoint = host.Length == 0 ? null : new ServerEndpoint(host, (int)row.Integer("port", true, 1, 65535)!.Value),
                Presentation = Presentation(row.Table("presentation")),
                ClientPolicy = ClientPolicy(row),
            };
            try { if (kind == "server") ServerStore.ValidateId(serverId); else DirectoryValidation.Id(serverId); details = DirectoryValidation.Normalize(details); }
            catch (ArgumentException error) { throw row.Error("servers", error.Message); }
            if (servers.Any(s => s.Id.Equals(serverId, StringComparison.OrdinalIgnoreCase))) throw row.Error("id", "duplicate server identity");
            var profile = row.Has("profile") ? row.String("profile", true) : null;
            if (profile is not null)
            {
                if (kind != "server") throw row.Error("profile", "only persistent servers have downloadable profiles");
                try { SourceAddress.RelativePath(profile); } catch (ArgumentException error) { throw row.Error("profile", error.Message); }
            }
            servers.Add(new(serverId, kind, details, expires) { ProfilePath = profile });
        }
        var directories = new List<DirectorySource>();
        foreach (var row in root.Tables("directory_sources"))
        {
            row.OnlyKeys("id", "name", "base_url", "enabled");
            var directoryId = row.String("id", true); var name = row.String("name", true); var url = row.String("base_url", true);
            try { DirectoryValidation.Id(directoryId); DirectoryValidation.Text(name, 128); _ = ServerStore.ApiBase(url); }
            catch (ArgumentException error) { throw row.Error("directory_sources", error.Message); }
            if (directories.Any(s => s.Id == directoryId)) throw row.Error("id", "duplicate directory identity");
            directories.Add(new(directoryId, name, url, row.Boolean("enabled", true), true));
        }
        var subscriptions = new List<StoreGitHubRelease>();
        foreach (var row in root.Tables("github_releases"))
        {
            row.OnlyKeys("id", "game", "builds", "repository", "asset", "name", "description", "publisher", "executable");
            var packageId = row.String("id", true); PluginCatalog.ValidateId(file, "github_releases.id", packageId);
            var game = row.String("game", true); InstanceFile.ValidateId(file, game);
            var builds = row.Strings("builds");
            if (builds.Count == 0 || builds.Distinct(StringComparer.Ordinal).Count() != builds.Count) throw row.Error("builds", "requires unique client builds");
            foreach (var build in builds) InstanceFile.ValidateId(file, build);
            var reference = new StoreGitHubRelease(packageId, game, builds, row.String("repository", true), row.String("asset", true),
                Localized(row, "name", 128), row.Has("description") ? Localized(row, "description", 4096) : new Dictionary<string, string>(),
                Printable(row, "publisher", 128), row.String("executable", true));
            try { GitHubStoreReleases.Validate(new(reference.Repository, "validation", reference.Asset, new string('0', 64), reference.Executable)); }
            catch (ArgumentException error) { throw row.Error("github_releases", error.Message); }
            if (subscriptions.Any(item => item.Id == packageId) || packages.Any(item => item.Id == packageId && item.GitHubAsset is null))
                throw row.Error("id", "duplicate subscription or conflicting static package");
            subscriptions.Add(reference);
        }
        return new StoreCatalog(id, Localized(catalog, "name", 128), packages)
            { Profiles = profiles, Servers = servers, DirectorySources = directories, GitHubReleases = subscriptions, DeprecatedPlugins = deprecated };
    }

    internal static ServerPresentation? Presentation(TomlReader? row)
    {
        if (row is null) return null;
        row.OnlyKeys("motd", "motd_translations", "summary", "summary_translations", "icon_url", "icon_url_translations", "images", "images_translations", "online_players", "peak_online_players", "player_limit", "state", "whitelist", "roster_enabled");
        return new()
        {
            Motd = row.String("motd"), IconUrl = row.Has("icon_url") ? row.String("icon_url", true) : null, Images = row.Strings("images"),
            MotdTranslations = row.StringMap("motd_translations"),
            Summary = row.String("summary"), SummaryTranslations = row.StringMap("summary_translations"),
            IconUrlTranslations = row.StringMap("icon_url_translations"), ImagesTranslations = row.StringListMap("images_translations"),
            OnlinePlayers = row.Integer("online_players", min: -1), PeakOnlinePlayers = row.Integer("peak_online_players", min: -1),
            PlayerLimit = row.Integer("player_limit", min: 1),
            State = row.Has("state") ? row.String("state", true) : null, Whitelist = row.Boolean("whitelist"),
            RosterEnabled = row.Boolean("roster_enabled", true),
        };
    }

    /// <summary>
    /// Reads a client policy written flat: <c>[[client_policy_sources]]</c> tables and <c>[[client_policy_rules]]</c>
    /// tables that name their plugin. Rules keep their written order within each plugin.
    /// </summary>
    internal static ClientPolicy ClientPolicy(TomlReader row)
    {
        var sources = row.Tables("client_policy_sources").Select(item =>
        {
            item.OnlyKeys("name", "kind", "base_url", "catalog_path");
            return new ServerStoreSource(item.String("name", true), item.String("kind", true), item.String("base_url", true), item.String("catalog_path", fallback: "source.toml"));
        }).ToArray();
        var rules = row.Tables("client_policy_rules").Select(item =>
        {
            item.OnlyKeys("plugin", "id", "type", "builds", "version", "sources", "default_selected", "languages");
            var languages = item.Has("languages") ? item.Strings("languages") : null;
            if (languages is not null && (languages.Count == 0 || languages.Any(language => language.Length is 0 or > 35
                || !language.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) || languages.Distinct(StringComparer.OrdinalIgnoreCase).Count() != languages.Count))
                throw item.Error("languages", "must be distinct launcher language tags such as zh-CN");
            return (Plugin: item.String("plugin", true), Rule: new ClientPolicyRule(item.String("id", true), item.String("type", true), item.Strings("builds"),
                item.Has("version") ? item.String("version", true) : null, item.Has("sources") ? item.Strings("sources") : null)
                { DefaultSelected = item.Has("default_selected") ? item.Boolean("default_selected") : null, Languages = languages });
        }).ToArray();
        return new ClientPolicy(1, sources, rules.Select(r => r.Plugin).Distinct(StringComparer.Ordinal)
            .Select(plugin => new ClientPluginRules(plugin, rules.Where(r => r.Plugin == plugin).Select(r => r.Rule).ToArray())).ToArray());
    }

    /// <summary>Writes a client policy in the flat form <see cref="ClientPolicy(TomlReader)"/> reads.</summary>
    internal static void WriteClientPolicy(StringBuilder text, ClientPolicy policy)
    {
        void Line(string key, string value) => text.Append(key).Append(" = ").Append(Hooks.SessionWriter.Quote(value)).Append('\n');
        void List(string key, IEnumerable<string> values) => text.Append(key).Append(" = [").AppendJoin(", ", values.Select(Hooks.SessionWriter.Quote)).Append("]\n");
        foreach (var source in policy.Sources)
        {
            text.Append("\n[[client_policy_sources]]\n"); Line("name", source.Name); Line("kind", source.Kind);
            Line("base_url", source.BaseUrl); Line("catalog_path", source.CatalogPath);
        }
        foreach (var plugin in policy.Plugins)
        foreach (var rule in plugin.Rules)
        {
            text.Append("\n[[client_policy_rules]]\n"); Line("plugin", plugin.Id); Line("id", rule.Id); Line("type", rule.Type); List("builds", rule.Builds);
            if (rule.Version is not null) Line("version", rule.Version);
            if (rule.Sources is not null) List("sources", rule.Sources);
            if (rule.DefaultSelected is { } selected) text.Append("default_selected = ").Append(selected ? "true" : "false").Append('\n');
            if (rule.Languages is not null) List("languages", rule.Languages);
        }
    }

    private static string Printable(TomlReader table, string key, int maximum)
    {
        var text = table.String(key, true);
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum || text.Any(char.IsControl)) throw table.Error(key, $"must contain 1..{maximum} printable characters");
        return text;
    }

    private static IReadOnlyDictionary<string, string> Localized(TomlReader table, string key, int maximum)
    {
        var values = table.Localized(key).Values;
        if (values.Count > 32 || values.Any(v => v.Key.Length > 32 || string.IsNullOrWhiteSpace(v.Value) || v.Value.Length > maximum || v.Value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))))
            throw table.Error(key, "localized text exceeds its limits or contains invalid characters");
        return values;
    }
}
