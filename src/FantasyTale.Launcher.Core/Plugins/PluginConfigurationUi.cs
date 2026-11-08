using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>A package-declared settings page referencing validated manifest options.</summary>
/// <param name="Id">Unique page identifier within the package.</param>
/// <param name="Kind">Native form or sandboxed HTML presentation.</param>
/// <param name="Title">Localized tab caption when multiple pages are declared.</param>
/// <param name="Path">Packaged HTML path, absent for native forms.</param>
/// <param name="Options">Manifest option keys exposed on this page.</param>
public sealed record PluginSettingsPage(string Id, string Kind, LocalizedText Title, string? Path, IReadOnlyList<string> Options);

/// <summary>Optional package-owned presentation for validated plugin settings.</summary>
/// <param name="Page">Relative inline HTML page, absent for the standard form.</param>
/// <param name="Labels">Localized captions for enum values, keyed by option and value.</param>
public sealed record PluginConfigurationUi(string? Page, IReadOnlyDictionary<string, IReadOnlyDictionary<string, LocalizedText>> Labels)
{
    /// <summary>Explicit package-owned pages; absent when the package uses the default form presentation.</summary>
    public IReadOnlyList<PluginSettingsPage>? Pages { get; init; }

    /// <summary>Loads the optional launcher-only sidecar without changing the native manifest contract.</summary>
    public static PluginConfigurationUi Load(PluginDefinition plugin)
    {
        var file = Safe(plugin.Folder, "plugin-ui.toml");
        if (!File.Exists(file)) return new(null, new Dictionary<string, IReadOnlyDictionary<string, LocalizedText>>());
        var root = TomlReader.Load(file);
        root.OnlyKeys("schema", "ui", "pages", "enum_labels");
        if (root.Integer("schema", true) != 1) throw root.Error("schema", "must be 1");
        var ui = root.Table("ui"); ui?.OnlyKeys("page");
        var page = ui?.Has("page") == true ? ui.String("page", true) : null;
        if (page is not null)
        {
            if (page.Length == 0 || !page.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) throw root.Error("ui.page", "must name a packaged HTML file");
            _ = ReadPage(plugin.Folder, page);
        }
        List<PluginSettingsPage>? pages = null;
        if (root.Has("pages"))
        {
            if (page is not null) throw root.Error("pages", "cannot be combined with ui.page");
            pages = [];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in root.Tables("pages"))
            {
                row.OnlyKeys("id", "kind", "title", "path", "options");
                var id = row.String("id", true);
                PluginCatalog.ValidateId(file, "pages.id", id);
                if (!ids.Add(id)) throw row.Error("id", "duplicate page identifier");
                var kind = row.String("kind", true);
                if (kind is not ("native" or "html")) throw row.Error("kind", "must be native or html");
                var path = row.Has("path") ? row.String("path", true) : null;
                if (kind == "html")
                {
                    if (string.IsNullOrEmpty(path) || !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                        throw row.Error("path", "must name a packaged HTML file");
                    _ = ReadPage(plugin.Folder, path);
                }
                else if (path is not null) throw row.Error("path", "is only valid for HTML pages");
                if (!row.Has("options")) throw row.Error("options", "must list the manifest option keys exposed by the page");
                var options = row.Strings("options");
                if (options.Distinct(StringComparer.Ordinal).Count() != options.Count || options.Any(key => !plugin.Options.Any(option => option.Key == key)))
                    throw row.Error("options", "must contain unique declared option keys");
                pages.Add(new(id, kind, row.Localized("title"), path, options));
            }
            if (pages.Count == 0) throw root.Error("pages", "must contain at least one settings page");
        }
        var labels = new Dictionary<string, IReadOnlyDictionary<string, LocalizedText>>(StringComparer.Ordinal);
        foreach (var row in root.Tables("enum_labels"))
        {
            row.OnlyKeys("option", "value", "label");
            var key = row.String("option", true); var value = row.String("value", true);
            var option = plugin.Options.SingleOrDefault(o => o.Key == key && o.Type == "enum");
            if (option is null || !option.Values.Contains(value, StringComparer.Ordinal)) throw row.Error("value", "must name a declared enum value");
            if (!labels.TryGetValue(key, out var existing)) labels[key] = existing = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
            if (!((Dictionary<string, LocalizedText>)existing).TryAdd(value, row.Localized("label"))) throw row.Error("value", "duplicate enum label");
        }
        return new(page, labels) { Pages = pages };
    }

    /// <summary>Reads a UTF-8 page whose relative path and parents remain inside the package.</summary>
    public static string ReadPage(string folder, string page)
    {
        var file = Safe(folder, ClientRelease.ValidatePath(page));
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        return reader.ReadToEnd();
    }

    private static string Safe(string folder, string path)
    {
        var full = Path.GetFullPath(Path.Combine(folder, path));
        if (!InstanceStore.IsWithin(folder, full)) throw new InvalidDataException("Plugin configuration path escapes its package.");
        InstanceStore.RejectLinks(full); return full;
    }
}
