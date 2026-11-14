using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>What one plugin policy rule of an enabled package means for a client right now.</summary>
/// <param name="Plugin">The package that declares the rule.</param>
/// <param name="Version">Its selected version.</param>
/// <param name="Rule">The rule.</param>
/// <param name="Status">
/// <c>satisfied</c>; for a requirement or recommendation <c>missing</c> (no installed version fits),
/// <c>disabled</c> (installed and available but not enabled), <c>version</c> (only other versions are installed) or
/// <c>unavailable</c> (installed but not for this build); for a conflict <c>conflict</c> (the other package is enabled).
/// </param>
/// <param name="Target">The installed package the rule resolves to, when one is installed.</param>
public sealed record PluginPolicyFinding(string Plugin, string Version, PluginPolicyRule Rule, string Status, PluginDefinition? Target)
{
    /// <summary>Whether the finding prevents a launch: an unmet requirement or an enabled conflict.</summary>
    public bool Blocks => Status != "satisfied" && Rule.Type is "require" or "conflict";
}

/// <summary>
/// Evaluates the <c>[[plugin_policy]]</c> rules packages declare about each other, as client policy does for builds:
/// a requirement must be enabled before launch, a recommendation is offered once, and a conflict cannot be enabled at
/// the same time. A rule applies on its builds and launcher languages only; a language never forbids a package.
/// </summary>
public static class PluginPolicy
{
    /// <summary>Whether a rule applies to this build and launcher language.</summary>
    public static bool Applies(PluginPolicyRule rule, BuildDefinition build, string? language) =>
        (rule.Builds.Count == 0 || rule.Builds.Contains(build.Id, StringComparer.Ordinal))
        && (rule.Languages.Count == 0 || language is { Length: > 0 } && rule.Languages.Any(tag => LanguageMatches(tag, language)));

    /// <summary>A rule's tag matches the launcher language exactly or as its primary subtag (<c>zh</c> matches <c>zh-TW</c>).</summary>
    public static bool LanguageMatches(string tag, string language) =>
        tag.Equals(language, StringComparison.OrdinalIgnoreCase)
        || !tag.Contains('-') && language.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase);

    /// <summary>The requirements of <paramref name="package"/> that apply to this build and language.</summary>
    public static IReadOnlyList<PluginRequirement> Requirements(PluginDefinition package, BuildDefinition build, string? language) =>
        package.Policy.Count == 0 && package.Requires.Count > 0
            ? package.Requires // Definitions built in code without a policy list keep their plain requirements.
            : package.Policy.Where(rule => rule.Type == "require" && Applies(rule, build, language))
                .GroupBy(rule => (rule.Plugin, rule.Range)).Select(group => new PluginRequirement(group.Key.Plugin, group.Key.Range)).ToArray();

    /// <summary>
    /// Evaluates every applicable rule of every enabled, build-compatible package. <paramref name="enabling"/> is treated as
    /// enabled, so a caller can check a package before turning it on.
    /// </summary>
    public static IReadOnlyList<PluginPolicyFinding> Evaluate(InstanceDefinition instance, BuildDefinition build, string? language,
        IReadOnlyList<PluginDefinition> packages, PluginDefinition? enabling = null, PluginDefinition? disabling = null)
    {
        var enabled = EnabledList(instance, build, packages, enabling, disabling);
        var findings = new List<PluginPolicyFinding>();
        foreach (var package in enabled)
            foreach (var rule in package.Policy.Where(rule => Applies(rule, build, language)))
                findings.Add(new(package.Id, package.Version, rule, Status(rule, package, build, enabled, packages, out var target), target));
        if (enabling is not null && disabling is null)
        {
            var inspected = new HashSet<(string Id, string Version)>(enabled.Select(p => (p.Id, p.Version)));
            var pending = new Queue<PluginDefinition>();
            EnqueueRequireTargets(findings, enabling.Id, build, enabled, inspected, pending);
            while (pending.TryDequeue(out var dep))
            {
                var depFindings = new List<PluginPolicyFinding>();
                foreach (var rule in dep.Policy.Where(rule => rule.Type == "require" && Applies(rule, build, language)))
                {
                    var finding = new PluginPolicyFinding(dep.Id, dep.Version, rule, Status(rule, dep, build, enabled, packages, out var target), target);
                    depFindings.Add(finding);
                    findings.Add(finding);
                }
                EnqueueRequireTargets(depFindings, dep.Id, build, enabled, inspected, pending);
            }
        }
        return findings;
    }

    private static void EnqueueRequireTargets(IEnumerable<PluginPolicyFinding> source, string ownerId, BuildDefinition build,
        IReadOnlyList<PluginDefinition> enabled, HashSet<(string Id, string Version)> inspected, Queue<PluginDefinition> pending)
    {
        foreach (var finding in source)
        {
            if (finding.Plugin != ownerId || finding.Rule.Type != "require" || finding.Status == "satisfied" || finding.Target is not { } target) continue;
            if (!PluginCatalog.IsAvailable(target, build) || enabled.Any(p => p.Id == target.Id && p.Version == target.Version)) continue;
            if (inspected.Add((target.Id, target.Version))) pending.Enqueue(target);
        }
    }

    /// <summary>Whether <paramref name="rule"/> declared on <paramref name="owner"/> is currently satisfied.</summary>
    public static bool IsRuleSatisfied(PluginPolicyRule rule, PluginDefinition owner, BuildDefinition build,
        IReadOnlyList<PluginDefinition> enabled, IReadOnlyList<PluginDefinition> packages) =>
        Status(rule, owner, build, enabled, packages, out _) == "satisfied";

    /// <summary>
    /// Language-independent <c>require</c> rules for <paramref name="package"/>, optionally narrowed to rules
    /// matching at least one build in <paramref name="targetBuilds"/>.
    /// </summary>
    public static IEnumerable<PluginRequirement> PlanRequirements(PluginDefinition package, IReadOnlyList<string>? targetBuilds = null)
    {
        if (package.Policy.Count == 0)
            return package.Requires;
        return package.Policy
            .Where(rule => rule.Type == "require" && rule.Languages.Count == 0
                && (rule.Builds.Count == 0 || targetBuilds is not { Count: > 0 }
                    || rule.Builds.Any(build => targetBuilds.Contains(build, StringComparer.Ordinal))))
            .Select(rule => new PluginRequirement(rule.Plugin, rule.Range));
    }

    /// <summary>Throws an explanatory coded error if enabling or disabling <paramref name="target"/> violates blocking plugin policy rules.</summary>
    public static void RequireConfigured(PluginDefinition target, bool enablingTarget, InstanceDefinition instance, BuildDefinition build,
        string? language, IReadOnlyList<PluginDefinition> packages)
    {
        var findings = Evaluate(instance, build, language, packages);
        if (enablingTarget)
        {
            var blocking = findings.FirstOrDefault(finding => finding.Blocks
                && (PluginCatalog.MatchesIdentity(target, finding.Plugin)
                    || finding.Rule.Type == "conflict" && PluginCatalog.MatchesIdentity(target, finding.Rule.Plugin)));
            if (blocking is null) return;
            if (blocking.Rule.Type == "conflict")
            {
                var other = PluginCatalog.MatchesIdentity(target, blocking.Plugin)
                    ? blocking.Target
                    : packages.FirstOrDefault(p => p.Id == blocking.Plugin && p.Version == blocking.Version);
                throw new InvalidOperationException("plugin_policy_conflict_active:" + (other?.Name.Resolve(language ?? "en") ?? blocking.Rule.Plugin));
            }
            var needed = blocking.Target ?? packages.FirstOrDefault(p => PluginCatalog.MatchesIdentity(p, blocking.Rule.Plugin) && PluginCatalog.IsAvailable(p, build))
                ?? packages.FirstOrDefault(p => PluginCatalog.MatchesIdentity(p, blocking.Rule.Plugin));
            throw new InvalidOperationException("plugin_policy_require_unmet:" + (needed?.Name.Resolve(language ?? "en") ?? blocking.Rule.Plugin));
        }

        var dependent = findings.FirstOrDefault(finding => finding.Blocks && finding.Rule.Type == "require"
            && PluginCatalog.MatchesIdentity(target, finding.Rule.Plugin));
        if (dependent is null) return;
        var owner = packages.FirstOrDefault(p => p.Id == dependent.Plugin && p.Version == dependent.Version);
        throw new InvalidOperationException("plugin_policy_required_by:" + (owner?.Name.Resolve(language ?? "en") ?? dependent.Plugin));
    }

    /// <summary>Active build-compatible packages for <paramref name="instance"/>, applying optional pending <paramref name="enabling"/> and <paramref name="disabling"/> changes.</summary>
    public static IReadOnlyList<PluginDefinition> Enabled(InstanceDefinition instance, BuildDefinition build,
        IReadOnlyList<PluginDefinition> packages, PluginDefinition? enabling = null, PluginDefinition? disabling = null) =>
        EnabledList(instance, build, packages, enabling, disabling);

    /// <summary>The once-per-client claim key of a recommendation finding: the recommending package and the recommended one.</summary>
    public static string RecommendationKey(PluginPolicyFinding finding) => "plugin-policy:" + finding.Plugin + "@" + finding.Rule.Plugin;

    private static List<PluginDefinition> EnabledList(InstanceDefinition instance, BuildDefinition build,
        IReadOnlyList<PluginDefinition> packages, PluginDefinition? enabling = null, PluginDefinition? disabling = null)
    {
        var result = instance.Plugins.Where(selection => selection.Enabled
                && (enabling is null || !PluginCatalog.MatchesIdentity(enabling, selection.Id))
                && (disabling is null || !PluginCatalog.MatchesIdentity(disabling, selection.Id)))
            .Select(selection => packages.SingleOrDefault(package => package.Id == selection.Id && package.Version == selection.Version))
            .OfType<PluginDefinition>()
            .Where(package => package.Game == instance.Game && PluginCatalog.IsAvailable(package, build)
                && (disabling is null || !PluginCatalog.MatchesIdentity(package, disabling.Id)))
            .ToList();
        if (enabling is not null && PluginCatalog.IsAvailable(enabling, build)
            && (disabling is null || !PluginCatalog.MatchesIdentity(enabling, disabling.Id)))
            result.Add(enabling);
        return result;
    }

    private static string Status(PluginPolicyRule rule, PluginDefinition owner, BuildDefinition build, IReadOnlyList<PluginDefinition> enabled,
        IReadOnlyList<PluginDefinition> packages, out PluginDefinition? target)
    {
        bool Fits(PluginDefinition item) => rule.Range.Length == 0 || PluginCatalog.Satisfies(item.Version, rule.Range);
        var candidates = packages.Where(item => item.Game == owner.Game && PluginCatalog.MatchesIdentity(item, rule.Plugin))
            .OrderByDescending(item => PluginVersion.Parse(item.Version)).ToArray();
        if (rule.Type == "conflict")
        {
            target = enabled.FirstOrDefault(item => PluginCatalog.MatchesIdentity(item, rule.Plugin) && Fits(item));
            return target is null ? "satisfied" : "conflict";
        }
        // An assets package is shared data: installed and available is enough, it is never enabled on its own.
        target = enabled.FirstOrDefault(item => PluginCatalog.MatchesIdentity(item, rule.Plugin) && Fits(item))
            ?? candidates.FirstOrDefault(item => item.Kind == "assets" && Fits(item) && PluginCatalog.IsAvailable(item, build));
        if (target is not null) return "satisfied";
        target = candidates.FirstOrDefault(item => Fits(item) && PluginCatalog.IsAvailable(item, build));
        if (target is not null) return "disabled";
        target = candidates.FirstOrDefault(Fits);
        if (target is not null) return "unavailable";
        target = candidates.FirstOrDefault();
        return target is null ? "missing" : "version";
    }
}
