using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>The common readable TOML contract for one persistent server, independent of its file name or transport.</summary>
public static class ServerProfileFile
{
    /// <summary>Reads a user-selected UTF-8 TOML file without importing or modifying it.</summary>
    public static async Task<string> ReadAsync(string file, CancellationToken ct = default)
    {
        InstanceStore.RejectLinks(file);
        return await File.ReadAllTextAsync(file, new UTF8Encoding(false, true), ct).ConfigureAwait(false);
    }

    /// <summary>Atomically exports validated TOML to the user's chosen file, preserving an existing file on failure.</summary>
    public static async Task ExportAsync(string file, string text, CancellationToken ct = default)
    {
        _ = Parse(file, text);
        file = Path.GetFullPath(file); InstanceStore.RejectLinks(file);
        var temporary = file + ".saving-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested(); AtomicFile.Replace(temporary, file);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Reads the same TOML schema for bundled, local, imported and store-delivered destinations.</summary>
    public static ServerDefinition Parse(string file, string text, bool builtIn = false)
        => Parse(TomlReader.Parse(file, text), builtIn);

    internal static ServerDefinition Load(string file, bool builtIn) => Parse(TomlReader.Load(file), builtIn);

    private static ServerDefinition Parse(TomlReader table, bool builtIn)
    {
        table.OnlyKeys("schema", "server", "presentation", "client_policy_sources", "client_policy_rules");
        if (table.Integer("schema", true) != 1) throw table.Error("schema", "must be 1");
        var row = table.Table("server", true)!;
        row.OnlyKeys(builtIn ? ["id", "game", "name", "api_url", "host", "port", "bundled_instances", "prefer_ip", "use_https", "builds", "icon_asset", "native_directory"]
            : ["id", "game", "name", "api_url", "host", "port", "bundled_instances", "prefer_ip", "use_https", "builds", "native_directory"]);
        var host = row.String("host");
        if (row.Has("host") != row.Has("port") || row.Has("host") && host.Length == 0) throw row.Error("host", "host and port must be supplied together");
        var server = new ServerDefinition(row.String("id", true), row.String("game", true), row.String("name", true),
            row.String("api_url") is { Length: > 0 } api ? api : null,
            host.Length == 0 ? null : new ServerEndpoint(host, (int)row.Integer("port", true, 1, 65535)!.Value), row.Boolean("bundled_instances"), builtIn)
        {
            PreferIp = row.Has("prefer_ip") ? row.Boolean("prefer_ip") : null,
            UseHttps = row.Has("use_https") && row.Boolean("use_https"),
            IconAsset = builtIn && row.Has("icon_asset") ? row.String("icon_asset", true) : null,
            Presentation = StoreCatalogFile.Presentation(table.Table("presentation")),
            Builds = row.Strings("builds"),
            NativeDirectory = ReadNativeDirectory(row.Table("native_directory")),
            ClientPolicy = StoreCatalogFile.ClientPolicy(table),
        };
        try { ServerStore.Validate(server); }
        catch (ArgumentException error) { throw table.Error("server", error.Message); }
        return server;
    }

    /// <summary>Uses the stable profile identity as the suggested TOML filename, independently of its current address.</summary>
    public static string FileName(ServerDefinition server)
    {
        ServerStore.ValidateId(server.Id);
        return server.Id + ".toml";
    }

    /// <summary>Writes a portable server profile without account credentials or bundled-only asset authority.</summary>
    public static string Format(ServerDefinition server)
    {
        ServerStore.Validate(server);
        var text = new StringBuilder("schema = 1\n[server]\n");
        void Line(string key, string value) => text.Append(key).Append(" = ").Append(SessionWriter.Quote(value)).Append('\n');
        Line("id", server.Id); Line("game", server.Game); Line("name", server.Name); Line("api_url", server.ApiUrl ?? "");
        text.Append("bundled_instances = ").Append(server.BundledInstances ? "true\n" : "false\n");
        if (server.PreferIp is { } preferIp) text.Append("prefer_ip = ").Append(preferIp ? "true\n" : "false\n");
        if (server.UseHttps) text.Append("use_https = true\n");
        if (server.Endpoint is { } endpoint) { Line("host", endpoint.Host); text.Append(System.Globalization.CultureInfo.InvariantCulture, $"port = {endpoint.Port}\n"); }
        text.Append("builds = [").AppendJoin(", ", server.Builds.Select(SessionWriter.Quote)).Append("]\n");
        if (server.NativeDirectory is { } directory)
        {
            text.Append("\n[server.native_directory]\n");
            text.Append("enabled = ").Append(directory.Enabled ? "true\n" : "false\n");
            Line("mode", directory.Mode);
            text.Append("builds = [").AppendJoin(", ", directory.Builds.Select(SessionWriter.Quote)).Append("]\n");
            foreach (var target in directory.Targets)
            {
                text.Append("\n[[server.native_directory.targets]]\n");
                Line("id", target.Id); Line("name", target.Name); if (target.ShortName is { } shortName) Line("short_name", shortName); Line("host", target.Endpoint.Host);
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"port = {target.Endpoint.Port}\n");
            }
        }
        StoreCatalogFile.WriteClientPolicy(text, server.ClientPolicy);
        if (server.Presentation is { } presentation)
        {
            text.Append("\n[presentation]\n");
            Line("motd", presentation.Motd);
            Line("summary", presentation.Summary);
            if (presentation.OnlinePlayers is { } online) text.Append(System.Globalization.CultureInfo.InvariantCulture, $"online_players = {online}\n");
            if (presentation.PeakOnlinePlayers is { } peak) text.Append(System.Globalization.CultureInfo.InvariantCulture, $"peak_online_players = {peak}\n");
            if (presentation.PlayerLimit is { } limit) text.Append(System.Globalization.CultureInfo.InvariantCulture, $"player_limit = {limit}\n");
            if (presentation.State is { } state) Line("state", state);
            text.Append("whitelist = ").Append(presentation.Whitelist ? "true\n" : "false\n");
            text.Append("roster_enabled = ").Append(presentation.RosterEnabled ? "true\n" : "false\n");
            if (presentation.IconUrl is { } icon) Line("icon_url", icon);
            text.Append("images = [").AppendJoin(", ", presentation.Images.Select(SessionWriter.Quote)).Append("]\n");
            if (presentation.MotdTranslations.Count != 0)
            {
                text.Append("\n[presentation.motd_translations]\n");
                foreach (var (language, description) in presentation.MotdTranslations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    Line(SessionWriter.Quote(language), description);
            }
            if (presentation.SummaryTranslations.Count != 0)
            {
                text.Append("\n[presentation.summary_translations]\n");
                foreach (var (language, summary) in presentation.SummaryTranslations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    Line(SessionWriter.Quote(language), summary);
            }
            if (presentation.IconUrlTranslations.Count != 0)
            {
                text.Append("\n[presentation.icon_url_translations]\n");
                foreach (var (language, translatedIcon) in presentation.IconUrlTranslations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    Line(SessionWriter.Quote(language), translatedIcon);
            }
            if (presentation.ImagesTranslations.Count != 0)
            {
                text.Append("\n[presentation.images_translations]\n");
                foreach (var (language, gallery) in presentation.ImagesTranslations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    text.Append(SessionWriter.Quote(language)).Append(" = [").AppendJoin(", ", gallery.Select(SessionWriter.Quote)).Append("]\n");
            }
        }
        return text.ToString();
    }

    private static NativeServerDirectory? ReadNativeDirectory(TomlReader? table)
    {
        if (table is null) return null;
        table.OnlyKeys("enabled", "mode", "builds", "targets");
        var targets = table.Tables("targets", 32).Select(row =>
        {
            row.OnlyKeys("id", "name", "short_name", "host", "port");
            return new GameServerTarget(row.String("id", true), row.String("name", true),
                new ServerEndpoint(row.String("host", true), (int)row.Integer("port", true, 1, 65535)!.Value))
            { ShortName = row.Has("short_name") ? row.String("short_name", true) : null };
        }).ToArray();
        return new NativeServerDirectory(table.Has("mode") ? table.String("mode", true) : "gateway", table.Strings("builds"), targets)
        {
            Enabled = table.Boolean("enabled"),
        };
    }
}
