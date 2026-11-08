using FantasyTale.Launcher.Core.Plugins;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>What a server's client policy asks of one plugin on one build, after its rules are combined.</summary>
/// <param name="Id">Plugin identity.</param>
public sealed record ServerPluginRule(string Id)
{
    /// <summary>Whether the plugin must be installed and enabled before launch.</summary>
    public bool Required { get; init; }
    /// <summary>Whether launches to this server turn the plugin off without changing the saved selection.</summary>
    public bool Disabled { get; init; }
    /// <summary>The recommendation rule, shown once per client; null when the plugin is not recommended.</summary>
    public string? Recommendation { get; init; }
    /// <summary>Whether a recommendation explicitly requests an initially checked card; omitted rules default to false.</summary>
    public bool DefaultSelected { get; init; }
    /// <summary>The sources a recommendation installs from; null allows any declared or configured source.</summary>
    public IReadOnlyList<ServerStoreSource>? Sources { get; init; }
    /// <summary>The lowest version an enabled plugin may have.</summary>
    public string? Minimum { get; init; }
    /// <summary>The highest version an enabled plugin may have.</summary>
    public string? Maximum { get; init; }
    /// <summary>Whether an enabled plugin is updated to its newest version before launch.</summary>
    public bool Latest { get; init; }
    /// <summary>Whether the player is offered this plugin.</summary>
    public bool Offered => Required || Recommendation is not null;

    /// <summary>Whether <paramref name="version"/> satisfies the version rules.</summary>
    public bool Allows(string version) => (Minimum is null || PluginCatalog.CompareVersions(version, Minimum) >= 0) &&
        (Maximum is null || PluginCatalog.CompareVersions(version, Maximum) <= 0);
}

/// <summary>Applies a server's client policy to launches without turning it into a global client lock.</summary>
public static class ServerPluginPolicy
{
    /// <summary>Combines the rules that apply to one exact build into one entry per plugin.</summary>
    public static IReadOnlyList<ServerPluginRule> ForBuild(ClientPolicy policy, string build) => policy.Plugins.Select(plugin =>
    {
        var rules = plugin.Rules.Where(rule => rule.Builds.Count == 0 || rule.Builds.Contains(build, StringComparer.Ordinal)).ToArray();
        if (rules.Length == 0) return null;
        var recommend = rules.FirstOrDefault(rule => rule.Type == "recommend");
        return new ServerPluginRule(plugin.Id)
        {
            Required = rules.Any(rule => rule.Type == "require"),
            Disabled = rules.Any(rule => rule.Type == "disable"),
            Recommendation = recommend?.Id,
            DefaultSelected = recommend?.DefaultSelected == true,
            Sources = recommend?.Sources is { Count: > 0 } names ? policy.Sources.Where(source => names.Contains(source.Name, StringComparer.Ordinal)).ToArray() : null,
            Minimum = rules.FirstOrDefault(rule => rule.Type == "min_version")?.Version,
            Maximum = rules.FirstOrDefault(rule => rule.Type == "max_version")?.Version,
            Latest = rules.Any(rule => rule.Type == "latest"),
        };
    }).OfType<ServerPluginRule>().ToArray();

    /// <summary>Produces a launch-only selection; local requirements and irreversible file components cannot be suppressed.</summary>
    public static InstanceDefinition ForLaunch(InstanceDefinition instance, BuildDefinition build, IReadOnlyList<PluginDefinition> packages,
        IEnumerable<ServerPluginRule> rules)
    {
        rules = ProfileClientPolicy.CanonicalRules(rules, packages);
        var disabled = rules.Where(item => item.Disabled).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var selection in instance.Plugins.Where(item => disabled.Contains(item.Id)))
        {
            var package = packages.SingleOrDefault(item => item.Id == selection.Id && item.Version == selection.Version);
            if (selection.Required || ProfileClientPolicy.Rules(build).Where(rule => rule.Required).Select(rule => rule.Id).Contains(selection.Id, StringComparer.Ordinal) || package is not null &&
                (PluginCatalog.IsRequired(package, build) || package.Kind == "files" && !package.RestoreFiles))
                throw new InvalidOperationException("server_plugin_policy_conflict:" + selection.Id);
        }
        return disabled.Count == 0 ? instance : instance with { Plugins = instance.Plugins.Select(item => disabled.Contains(item.Id) ? item with { Enabled = false } : item).ToArray() };
    }

    /// <summary>Checks the installed enabled selection, package ownership, build and Launcher compatibility before launch.</summary>
    public static bool Enabled(InstanceDefinition instance, BuildDefinition build, IReadOnlyList<PluginDefinition> packages, string id)
    {
        id = PluginCatalog.CanonicalId(packages, id);
        return instance.Plugins.Any(selection => selection.Id == id && selection.Enabled && packages.Any(package =>
            package.Id == id && package.Version == selection.Version && package.Game == instance.Game &&
            PluginCatalog.IsAvailable(package, build) && LauncherCompatibility.Supports(package.MinimumLauncherVersion)));
    }

    /// <summary>
    /// Rejects bypassed or stale recommendation dialogs: a required plugin must be enabled, and every enabled plugin must
    /// satisfy its version rules. The required-update dialog offers the version that satisfies them.
    /// </summary>
    public static void RequireEnabled(InstanceDefinition instance, BuildDefinition build, IReadOnlyList<PluginDefinition> packages,
        IEnumerable<ServerPluginRule> rules)
    {
        foreach (var item in ProfileClientPolicy.CanonicalRules(rules, packages))
        {
            if (item.Required && !Enabled(instance, build, packages, item.Id)) throw new InvalidOperationException("server_plugin_required:" + item.Id);
            if (item.Disabled || instance.Plugins.SingleOrDefault(selection => selection.Id == item.Id && selection.Enabled) is not { } active) continue;
            if (item.Minimum is not null && PluginCatalog.CompareVersions(active.Version, item.Minimum) < 0)
                throw new InvalidOperationException("server_plugin_version_low:" + item.Id);
            if (item.Maximum is not null && PluginCatalog.CompareVersions(active.Version, item.Maximum) > 0)
                throw new InvalidOperationException("server_plugin_version_high:" + item.Id);
        }
    }
}
