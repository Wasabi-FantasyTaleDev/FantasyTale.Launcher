using System.Text.Json.Serialization;

namespace FantasyTale.Launcher.Sdk;

/// <summary>
/// The launch policy declared by game/build profiles or sent by a server with its catalog and sign-in. Plugins are the
/// first, and a later section is a new property here with its own rule types. Unknown members are refused rather
/// than ignored, so a Launcher never silently skips a section or rule field it does not understand.
/// </summary>
/// <param name="Version">Policy format; this Launcher reads format 1 and refuses others.</param>
/// <param name="Sources">Named store catalogs that recommendation rules may name.</param>
/// <param name="Plugins">One entry per plugin identity with its typed rules.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClientPolicy(int Version, IReadOnlyList<ServerStoreSource> Sources, IReadOnlyList<ClientPluginRules> Plugins)
{
    /// <summary>A policy without sources or rules.</summary>
    public static ClientPolicy Empty { get; } = new(1, [], []);
}

/// <summary>The rules that apply to one plugin identity.</summary>
/// <param name="Id">Plugin identity resolved through store catalogs; never a path or download address.</param>
/// <param name="Rules">Typed rules, each with its own build scope.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClientPluginRules(string Id, IReadOnlyList<ClientPolicyRule> Rules);

/// <summary>One typed rule: require, disable, recommend, min_version, max_version or latest.</summary>
/// <param name="Id">Stable identity within its plugin; a recommendation is shown once per client and rule.</param>
/// <param name="Type">The rule type name.</param>
/// <param name="Builds">Exact applicable build ids, or empty for every build.</param>
/// <param name="Version">The bound of a version rule.</param>
/// <param name="Sources">Source names a recommendation installs from; empty allows any declared source.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClientPolicyRule(string Id, string Type, IReadOnlyList<string> Builds, string? Version = null, IReadOnlyList<string>? Sources = null)
{
    /// <summary>Optional initial check state for a recommendation; omitted means unchecked and other rule types cannot specify it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DefaultSelected { get; init; }

    /// <summary>
    /// Launcher languages the rule applies to (for example <c>zh-CN</c>, or <c>zh</c> for every Chinese variant); null or
    /// empty applies to every language. A language never forbids a plugin: elsewhere the rule simply does not apply.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Languages { get; init; }
}
