using System.Globalization;
using System.Text;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Definitions;

/// <summary>Strict schema-one persistence for an instance, excluding all login secrets.</summary>
public static class InstanceFile
{
    /// <summary>Loads and validates an instance document.</summary>
    public static InstanceDefinition Load(string file) => Read(TomlReader.Load(file));

    private static InstanceDefinition Read(TomlReader root)
    {
        root.OnlyKeys("schema", "instance", "endpoint", "plugins");
        if (root.Integer("schema", true) != 1) throw root.Error("schema", "must be 1");
        var t = root.Table("instance", true)!;
        t.OnlyKeys("id", "name", "game", "build", "language", "installation_path", "check_integrity");
        var id = t.String("id", true);
        ValidateId(root.File, id);
        var name = t.String("name", true);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl)) throw t.Error("name", "must contain 1..128 printable characters");
        var game = t.String("game", true); var build = t.String("build", true);
        PluginCatalog.ValidateId(root.File, "instance.game", game);
        PluginCatalog.ValidateId(root.File, "instance.build", build);
        ServerEndpoint? endpoint = null;
        if (root.Table("endpoint") is { } e)
        {
            e.OnlyKeys("host", "port");
            var host = e.String("host", true);
            if (host.Length > 253 || Uri.CheckHostName(host) == UriHostNameType.Unknown) throw e.Error("host", "must be a hostname or IP address");
            endpoint = new ServerEndpoint(host, (int)e.Integer("port", true, 1, 65535)!.Value);
        }
        var plugins = root.Tables("plugins", 64).Select(p =>
        {
            p.OnlyKeys("id", "version", "enabled", "required", "options");
            var pluginId = p.String("id", true); var version = p.String("version", true);
            PluginCatalog.ValidateId(root.File, p.Error("id", "").Key, pluginId);
            PluginCatalog.ValidateVersion(root.File, p.Error("version", "").Key, version);
            var enabled = p.Boolean("enabled", true); var required = p.Boolean("required");
            if (required && !enabled) throw p.Error("enabled", "a required plugin cannot be disabled");
            return new InstancePlugin(pluginId, version, enabled, required, p.Table("options")?.Scalars() ?? new Dictionary<string, object>());
        }).ToArray();
        if (plugins.GroupBy(p => p.Id).Any(g => g.Count() > 1)) throw root.Error("plugins", "duplicate plugin id");
        var language = t.String("language");
        if (language.Length != 0 && language is not ("zh-CN" or "zh-TW" or "ko" or "en" or "id" or "vi")) throw t.Error("language", "unknown language code");
        var installationPath = t.String("installation_path");
        if (installationPath.Length != 0 && (!Path.IsPathFullyQualified(installationPath) || installationPath.Any(char.IsControl)))
            throw t.Error("installation_path", "must be an absolute filesystem path without control characters");
        return new InstanceDefinition(id, name, game, build, endpoint, plugins)
        {
            Language = language.Length == 0 ? null : language,
            CheckIntegrity = t.Boolean("check_integrity", true),
            InstallationPath = installationPath.Length == 0 ? null : Path.GetFullPath(installationPath),
        };
    }

    /// <summary>Renders an instance and validates the result before it can reach disk.</summary>
    public static string Render(InstanceDefinition instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var text = new StringBuilder("schema = 1\n\n[instance]\n");
        Line(text, "id", instance.Id); Line(text, "name", instance.Name); Line(text, "game", instance.Game); Line(text, "build", instance.Build);
        if (instance.Language is not null) Line(text, "language", instance.Language);
        text.Append("check_integrity = ").Append(instance.CheckIntegrity ? "true" : "false").Append('\n');
        if (instance.InstallationPath is not null) Line(text, "installation_path", instance.InstallationPath);
        if (instance.Endpoint is { } endpoint)
        {
            text.Append("\n[endpoint]\n"); Line(text, "host", endpoint.Host);
            text.Append(CultureInfo.InvariantCulture, $"port = {endpoint.Port}\n");
        }
        foreach (var p in instance.Plugins)
        {
            text.Append("\n[[plugins]]\n"); Line(text, "id", p.Id); Line(text, "version", p.Version);
            text.Append("enabled = ").Append(p.Enabled ? "true" : "false").Append('\n');
            text.Append("required = ").Append(p.Required ? "true" : "false").Append('\n');
            if (p.Options.Count == 0) continue;
            text.Append("[plugins.options]\n");
            foreach (var (key, value) in p.Options.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                text.Append(SessionWriter.Quote(key)).Append(" = ").Append(value switch
                {
                    bool b => b ? "true" : "false", long n => n.ToString(CultureInfo.InvariantCulture), string s => SessionWriter.Quote(s),
                    _ => throw new DefinitionException("instance.toml", "plugins.options." + key, "must be a boolean, integer or string"),
                }).Append('\n');
            }
        }
        var result = text.ToString();
        _ = Read(TomlReader.Parse("instance.toml", result));
        return result;
    }

    internal static void ValidateId(string file, string id)
    {
        if (id.Length is < 1 or > 64 || !id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') ||
            id is "con" or "prn" or "aux" or "nul" || (id.Length == 4 && (id.StartsWith("com", StringComparison.Ordinal) || id.StartsWith("lpt", StringComparison.Ordinal)) && id[3] is >= '0' and <= '9'))
            throw new DefinitionException(file, "instance.id", "must be 1..64 lowercase letters, digits or hyphens, excluding reserved device names");
    }

    private static void Line(StringBuilder text, string key, string value) => text.Append(key).Append(" = ").Append(SessionWriter.Quote(value)).Append('\n');
}
