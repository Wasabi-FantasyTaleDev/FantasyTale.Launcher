using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Validates optional public server metadata without treating advertised status as authentication authority.</summary>
public static class ServerMetadataPolicy
{
    /// <summary>Rejects malformed display information while preserving absent and operator-hidden counters.</summary>
    public static void Validate(ServerPresentation? value)
    {
        if (value is null) return;
        if (value.Motd is null || value.Summary is null || new[] { value.Motd, value.Summary }.Any(text => text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))))
            throw new ArgumentException("Invalid server message of the day.");
        if (value.MotdTranslations is null || value.SummaryTranslations is null) throw new ArgumentException("Invalid server description translations.");
        foreach (var translations in new[] { value.MotdTranslations, value.SummaryTranslations })
        {
            ValidateLanguages(translations.Keys);
            foreach (var (language, text) in translations)
            {
                if (text is null || text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
                    throw new ArgumentException("Invalid translated server description.");
            }
        }
        if (value.State is not (null or "online" or "offline" or "maintenance")) throw new ArgumentException("Invalid server state.");
        if (value.OnlinePlayers < -1 || value.PeakOnlinePlayers < -1) throw new ArgumentException("Player counts must be nonnegative, minus one for hidden, or omitted.");
        if (value.PlayerLimit <= 0) throw new ArgumentException("Player limit must be positive or omitted for unlimited.");
        if (value.PresenceGroup is { } group && (string.IsNullOrWhiteSpace(group) || group.Any(char.IsControl)))
            throw new ArgumentException("Invalid population group identity.");
        if (value.IconUrl is not null) _ = ServerStore.ApiBase(value.IconUrl);
        if (value.Images is null) throw new ArgumentException("Invalid server image gallery.");
        foreach (var image in value.Images) _ = ServerStore.ApiBase(image);
        if (value.IconUrlTranslations is null || value.ImagesTranslations is null) throw new ArgumentException("Invalid translated server artwork.");
        ValidateLanguages(value.IconUrlTranslations.Keys); ValidateLanguages(value.ImagesTranslations.Keys);
        foreach (var icon in value.IconUrlTranslations.Values)
        {
            if (icon is null) throw new ArgumentException("Invalid translated server icon.");
            if (icon.Length != 0) _ = ServerStore.ApiBase(icon);
        }
        foreach (var gallery in value.ImagesTranslations.Values)
        {
            if (gallery is null) throw new ArgumentException("Invalid translated server gallery.");
            foreach (var image in gallery) _ = ServerStore.ApiBase(image);
        }
    }

    private static void ValidateLanguages(IEnumerable<string> tags)
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in tags)
            if (string.IsNullOrEmpty(language) || !System.Text.RegularExpressions.Regex.IsMatch(language, @"\A[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*\z") || !languages.Add(language))
                throw new ArgumentException("Invalid or duplicate server metadata language tag.");
    }

    /// <summary>The rule types of policy format 1, with the field each takes.</summary>
    public static IReadOnlyDictionary<string, string> RuleFields { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["require"] = "none", ["disable"] = "none", ["recommend"] = "sources", ["min_version"] = "version", ["max_version"] = "version", ["latest"] = "none",
    };

    /// <summary>
    /// Rejects a malformed policy, an unknown rule type or source, and rules of one plugin that conflict on an
    /// overlapping build; these are the same rules the server applies before it sends the policy.
    /// </summary>
    public static void Validate(ClientPolicy policy)
    {
        if (policy?.Sources is null || policy.Plugins is null) throw new ArgumentException("Invalid client policy.");
        if (policy.Version != 1) throw new ArgumentException("This server's client policy needs a newer Launcher.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in policy.Sources)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Name) || source.Name.Any(char.IsControl) || !names.Add(source.Name)) throw new ArgumentException("Invalid plugin source name.");
            SourceTransport.Validate(source.Kind, source.BaseUrl);
            _ = SourceAddress.RelativePath(source.CatalogPath);
        }
        var plugins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plugin in policy.Plugins)
        {
            if (plugin?.Rules is null || string.IsNullOrEmpty(plugin.Id) || !plugins.Add(plugin.Id)) throw new ArgumentException("Invalid client policy plugin.");
            try { Definitions.PluginCatalog.ValidateId("client policy", "plugin id", plugin.Id); }
            catch (Definitions.DefinitionException error) { throw new ArgumentException("Invalid client policy plugin identity.", error); }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in plugin.Rules)
            {
                if (rule is null || string.IsNullOrEmpty(rule.Id) || !rule.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') || !ids.Add(rule.Id) ||
                    rule.Builds is null || rule.Builds.Distinct(StringComparer.Ordinal).Count() != rule.Builds.Count)
                    throw new ArgumentException($"Invalid client policy rule for plugin '{plugin.Id}'.");
                try { foreach (var build in rule.Builds) Definitions.InstanceFile.ValidateId("client policy", build); }
                catch (Definitions.DefinitionException error) { throw new ArgumentException("Invalid client policy build.", error); }
                if (!RuleFields.TryGetValue(rule.Type ?? "", out var field))
                    throw new ArgumentException($"This server's client policy uses the rule type '{rule.Type}', which needs a newer Launcher.");
                var versionValid = field == "version" ? rule.Version is not null && ValidVersion(rule.Version) : rule.Version is null;
                var sourcesValid = field == "sources" ? rule.Sources is { } list && list.Distinct(StringComparer.Ordinal).Count() == list.Count && list.All(names.Contains) : rule.Sources is null;
                if (!versionValid || !sourcesValid || rule.DefaultSelected is not null && rule.Type != "recommend")
                    throw new ArgumentException($"Invalid client policy rule '{rule.Id}' for plugin '{plugin.Id}'.");
            }
            for (var i = 0; i < plugin.Rules.Count; i++)
            for (var j = i + 1; j < plugin.Rules.Count; j++)
                if (Overlap(plugin.Rules[i].Builds, plugin.Rules[j].Builds) && Conflict(plugin.Rules[i], plugin.Rules[j]))
                    throw new ArgumentException($"The client policy rules '{plugin.Rules[i].Id}' and '{plugin.Rules[j].Id}' of plugin '{plugin.Id}' conflict.");
        }
    }

    /// <summary>Whether two build scopes share a build; an empty scope means every build.</summary>
    public static bool Overlap(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Count == 0 || right.Count == 0 || left.Intersect(right, StringComparer.Ordinal).Any();

    // Availability choices are exclusive on overlapping builds; equal version bounds can pin one version.
    private static bool Conflict(ClientPolicyRule a, ClientPolicyRule b) => a.Type == b.Type || a.Type == "disable" || b.Type == "disable" ||
        a.Type == "require" && b.Type == "recommend" || a.Type == "recommend" && b.Type == "require" ||
        a.Type == "latest" && b.Type == "max_version" || a.Type == "max_version" && b.Type == "latest" ||
        a.Type == "min_version" && b.Type == "max_version" && Definitions.PluginCatalog.CompareVersions(a.Version!, b.Version!) > 0 ||
        a.Type == "max_version" && b.Type == "min_version" && Definitions.PluginCatalog.CompareVersions(b.Version!, a.Version!) > 0;

    private static bool ValidVersion(string version)
    {
        try { _ = Definitions.PluginCatalog.CompareVersions(version, version); return true; }
        catch (FormatException) { return false; }
    }
}
