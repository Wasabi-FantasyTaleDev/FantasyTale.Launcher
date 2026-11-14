using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>Evaluates the same typed policy for local game/build profiles and server destinations.</summary>
public static class ProfileClientPolicy
{
    /// <summary>Returns the local rules applicable to the selected build and the current launcher language.</summary>
    public static IReadOnlyList<ServerPluginRule> Rules(BuildDefinition build) => Rules(build, LauncherLanguage.Current);

    /// <summary>Returns the local rules applicable to the selected build and launcher language.</summary>
    public static IReadOnlyList<ServerPluginRule> Rules(BuildDefinition build, string? language) => ServerPluginPolicy.ForBuild(build.ClientPolicy, build.Id, language);

    /// <summary>Whether a local rule of this type applies to the plugin on this build and the current launcher language.</summary>
    public static bool Has(BuildDefinition build, string plugin, string type)
    {
        var language = LauncherLanguage.Current;
        return build.ClientPolicy.Plugins.Where(item => item.Id == plugin).SelectMany(item => item.Rules)
            .Any(rule => rule.Type == type && (rule.Builds.Count == 0 || rule.Builds.Contains(build.Id, StringComparer.Ordinal))
                && (rule.Languages is not { Count: > 0 } || language is { Length: > 0 } && rule.Languages.Any(tag => PluginPolicy.LanguageMatches(tag, language))));
    }

    /// <summary>Retains a local constraint when its component has an explicitly declared former identifier.</summary>
    public static bool Has(BuildDefinition build, PluginDefinition plugin, string type) =>
        Has(build, plugin.Id, type) || plugin.Aliases.Any(alias => Has(build, alias, type));

    /// <summary>Maps former identifiers before intersecting constraints, so a rename cannot bypass a policy.</summary>
    public static IReadOnlyList<ServerPluginRule> CanonicalRules(IEnumerable<ServerPluginRule> rules, IReadOnlyList<PluginDefinition> packages) =>
        Merge(rules.Select(rule => rule with { Id = Definitions.PluginCatalog.CanonicalId(packages, rule.Id) }).ToArray(), []);

    /// <summary>Plugin identities offered by local require or recommend rules, in definition order.</summary>
    public static IReadOnlyList<string> Offered(BuildDefinition build) => Offered(build, LauncherLanguage.Current);

    /// <summary>Plugin identities offered by local require or recommend rules for one launcher language, in definition order.</summary>
    public static IReadOnlyList<string> Offered(BuildDefinition build, string? language) =>
        Rules(build, language).Where(rule => rule.Offered && !rule.Disabled).Select(rule => rule.Id).ToArray();

    /// <summary>Combines game and build declarations without overrides or silently discarded constraints.</summary>
    public static ClientPolicy Combine(ClientPolicy game, ClientPolicy build)
    {
        var sources = game.Sources.Concat(build.Sources).GroupBy(source => source.Name, StringComparer.Ordinal).Select(group =>
        {
            if (group.Distinct().Count() != 1) throw new ArgumentException("Conflicting client policy source: " + group.Key);
            return group.First();
        }).ToArray();
        var result = new ClientPolicy(1, sources, game.Plugins.Concat(build.Plugins).GroupBy(plugin => plugin.Id, StringComparer.Ordinal)
            .Select(group => new ClientPluginRules(group.Key, group.SelectMany(plugin => plugin.Rules).ToArray())).ToArray());
        ServerMetadataPolicy.Validate(result);
        return result;
    }

    /// <summary>Intersects local and server constraints, rejecting a destination that contradicts a local rule.</summary>
    public static IReadOnlyList<ServerPluginRule> ForDestination(BuildDefinition build, ClientPolicy server) =>
        Merge(Rules(build), ServerPluginPolicy.ForBuild(server, build.Id));

    private static ServerPluginRule[] Merge(IReadOnlyList<ServerPluginRule> local, IReadOnlyList<ServerPluginRule> remote) =>
        local.Concat(remote).GroupBy(rule => rule.Id, StringComparer.Ordinal).Select(group =>
        {
            var items = group.ToArray();
            var minimum = items.Select(rule => rule.Minimum).OfType<string>().OrderByDescending(value => value, Comparer<string>.Create(Definitions.PluginCatalog.CompareVersions)).FirstOrDefault();
            var maximum = items.Select(rule => rule.Maximum).OfType<string>().OrderBy(value => value, Comparer<string>.Create(Definitions.PluginCatalog.CompareVersions)).FirstOrDefault();
            if (items.Any(rule => rule.Disabled) && items.Any(rule => !rule.Disabled) ||
                items.Any(rule => rule.Required) && items.Any(rule => rule.Recommendation is not null) ||
                items.Any(rule => rule.Latest) && maximum is not null || minimum is not null && maximum is not null && Definitions.PluginCatalog.CompareVersions(minimum, maximum) > 0)
                throw new InvalidOperationException("client_policy_conflict:" + group.Key);
            var named = items.Where(rule => rule.Sources is not null).Select(rule => rule.Sources!).ToArray();
            var sources = named.Length == 0 ? null : named[0].Where(source => named.All(list => list.Any(other => other.Kind == source.Kind &&
                other.BaseUrl.TrimEnd('/').Equals(source.BaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) && other.CatalogPath == source.CatalogPath))).ToArray();
            if (sources is { Length: 0 }) throw new InvalidOperationException("client_policy_source_conflict:" + group.Key);
            return new ServerPluginRule(group.Key)
            {
                Required = items.Any(rule => rule.Required), Disabled = items.Any(rule => rule.Disabled),
                Recommendation = items.Any(rule => rule.Recommendation is not null)
                    ? string.Join('+', items.Select(rule => rule.Recommendation).OfType<string>().Distinct(StringComparer.Ordinal)) : null,
                DefaultSelected = items.Any(rule => rule.Recommendation is not null)
                    && items.Where(rule => rule.Recommendation is not null).All(rule => rule.DefaultSelected),
                Sources = sources, Minimum = minimum, Maximum = maximum, Latest = items.Any(rule => rule.Latest),
            };
        }).ToArray();
}
