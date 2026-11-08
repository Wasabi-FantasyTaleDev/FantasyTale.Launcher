using System.Globalization;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>One plugin selection carried by an exported user scheme.</summary>
public sealed record PluginSchemePlugin(string Id, string Version, bool Enabled, IReadOnlyDictionary<string, object> Options);

/// <summary>Selections for one client build in an exported user scheme.</summary>
public sealed record PluginSchemeBuild(string Id, IReadOnlyList<PluginSchemePlugin> Plugins);

/// <summary>Validated TOML document used to exchange plugin schemes between launcher instances.</summary>
public sealed record PluginSchemeDocument(string Game, string Name, IReadOnlyList<PluginSchemeBuild> Builds);

/// <summary>Parses and writes the launcher-owned plugin scheme TOML format.</summary>
public static class PluginSchemeFile
{
    /// <summary>Resolves former identities against installed successors; unknown builds and missing packages remain portable.</summary>
    public static PluginSchemeDocument Migrate(PluginSchemeDocument document, GameDefinition game, IReadOnlyList<PluginDefinition> packages)
    {
        if (document.Game != game.Id) throw new ArgumentException("The game does not own this scheme.", nameof(game));
        return document with { Builds = document.Builds.Select(row =>
        {
            var build = game.Builds.SingleOrDefault(build => build.Id == row.Id);
            if (build is null) return row;
            var migrated = row.Plugins.Select(plugin =>
            {
                var selection = PluginIdentityMigration.Resolve(new(plugin.Id, plugin.Version, plugin.Enabled, false, plugin.Options), build, packages);
                return new PluginSchemePlugin(selection.Id, selection.Version, selection.Enabled, selection.Options);
            }).ToArray();
            if (migrated.Select(plugin => plugin.Id).Distinct(StringComparer.Ordinal).Count() != migrated.Length)
                throw new InvalidDataException("The scheme selects both a former plugin identity and its successor.");
            return row with { Plugins = migrated };
        }).ToArray() };
    }

    /// <summary>Parses and validates one scheme document.</summary>
    public static PluginSchemeDocument Parse(string file, string text)
    {
        var reader = TomlReader.Parse(file, text); reader.OnlyKeys("schema", "game", "name", "builds");
        if (reader.Integer("schema", true) != 1) throw reader.Error("schema", "must be 1");
        var game = reader.String("game", true); InstanceFile.ValidateId(file, game);
        var name = reader.String("name", true);
        if (name.Length is < 1 or > 64 || name.Any(char.IsControl)) throw reader.Error("name", "must contain 1..64 printable characters");
        var builds = new List<PluginSchemeBuild>();
        foreach (var row in reader.Tables("builds"))
        {
            row.OnlyKeys("id", "plugins"); var id = row.String("id", true); InstanceFile.ValidateId(file, id);
            if (builds.Any(item => item.Id == id)) throw row.Error("id", "duplicate build");
            var plugins = new List<PluginSchemePlugin>();
            foreach (var plugin in row.Tables("plugins"))
            {
                plugin.OnlyKeys("id", "version", "enabled", "options"); var pluginId = plugin.String("id", true); PluginCatalog.ValidateId(file, "plugins.id", pluginId);
                var version = plugin.String("version", true); PluginCatalog.ValidateVersion(file, "plugins.version", version);
                if (plugins.Any(item => item.Id == pluginId)) throw plugin.Error("id", "duplicate plugin");
                plugins.Add(new(pluginId, version, plugin.Boolean("enabled", true), plugin.Table("options")?.Scalars() ?? new Dictionary<string, object>()));
            }
            builds.Add(new(id, plugins));
        }
        return new(game, name, builds);
    }

    /// <summary>Renders a validated scheme document as UTF-8 TOML text.</summary>
    public static string Render(PluginSchemeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document); InstanceFile.ValidateId("plugin-scheme.toml", document.Game);
        if (document.Name.Length is < 1 or > 64 || document.Name.Any(char.IsControl)) throw new ArgumentException("Invalid scheme name.", nameof(document));
        var text = new StringBuilder("schema = 1\n");
        Line(text, "game", document.Game); Line(text, "name", document.Name);
        foreach (var build in document.Builds)
        {
            InstanceFile.ValidateId("plugin-scheme.toml", build.Id); text.Append("\n[[builds]]\n"); Line(text, "id", build.Id);
            foreach (var plugin in build.Plugins)
            {
                PluginCatalog.ValidateId("plugin-scheme.toml", "plugins.id", plugin.Id); PluginCatalog.ValidateVersion("plugin-scheme.toml", "plugins.version", plugin.Version);
                text.Append("\n[[builds.plugins]]\n"); Line(text, "id", plugin.Id); Line(text, "version", plugin.Version);
                text.Append("enabled = ").Append(plugin.Enabled ? "true\n" : "false\n");
                if (plugin.Options.Count > 0) text.Append("\n[builds.plugins.options]\n");
                foreach (var pair in plugin.Options.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    if (pair.Value is not (bool or long or string)) throw new ArgumentException("Plugin options must be boolean, integer or string values.", nameof(document));
                    if (pair.Key.Any(char.IsControl)) throw new ArgumentException("Plugin option keys cannot contain control characters.", nameof(document));
                    text.Append(SessionWriter.Quote(pair.Key)).Append(" = ").Append(pair.Value switch { bool value => value ? "true" : "false", long value => value.ToString(CultureInfo.InvariantCulture), string value => SessionWriter.Quote(value), _ => throw new InvalidOperationException() }).Append('\n');
                }
            }
        }
        var rendered = text.ToString();
        _ = Parse("plugin-scheme.toml", rendered);
        return rendered;
    }

    private static void Line(StringBuilder text, string key, string value) => text.Append(key).Append(" = ").Append(SessionWriter.Quote(value)).Append('\n');
}
