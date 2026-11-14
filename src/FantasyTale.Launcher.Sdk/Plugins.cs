namespace FantasyTale.Launcher.Sdk;

/// <summary>A typed plugin setting, including the constraints enforced before client launch.</summary>
/// <param name="Key">Key written to the session options table.</param>
/// <param name="Type">One of bool, int, string or enum.</param>
/// <param name="Default">Value used when the instance has no override.</param>
/// <param name="Minimum">Inclusive integer lower bound.</param>
/// <param name="Maximum">Inclusive integer upper bound.</param>
/// <param name="MaxLength">Maximum UTF-8 byte count for strings, matching the native runtime.</param>
/// <param name="Values">Allowed strings for an enum.</param>
/// <param name="Label">Localized setting label.</param>
/// <param name="Description">Optional localized explanation.</param>
public sealed record PluginOption(string Key, string Type, object Default, long Minimum, long Maximum,
    int MaxLength, IReadOnlyList<string> Values, LocalizedText Label, LocalizedText? Description);

/// <summary>A declarative memory patch whose bytes and location are validated at load time.</summary>
/// <param name="Id">Patch identifier, shared only by disjoint build variants.</param>
/// <param name="Builds">Build selectors; empty applies to every compatible build.</param>
/// <param name="When">Boolean option gating this patch, or empty.</param>
/// <param name="Pattern">Search bytes, or null for an RVA or named locator.</param>
/// <param name="Rva">Fixed relative address, or null.</param>
/// <param name="Locator">Named locator, or null.</param>
/// <param name="Offset">Displacement from the located address.</param>
/// <param name="Match">Pattern selection policy: unique or first.</param>
/// <param name="Expect">Expected hexadecimal bytes, with optional wildcards.</param>
/// <param name="Write">Exact replacement hexadecimal bytes.</param>
/// <param name="Timing">Immediate or unpacked application timing.</param>
/// <param name="DeadlineMs">Maximum unpacking wait in milliseconds.</param>
public sealed record PluginPatch(string Id, IReadOnlyList<string> Builds, string When, string? Pattern,
    uint? Rva, string? Locator, int Offset, string Match, string Expect, string Write, string Timing, int DeadlineMs)
{
    /// <summary>Selected game languages on which this patch applies; empty permits every language.</summary>
    public IReadOnlyList<string> ClientLanguages { get; init; } = [];

    /// <summary>Selected game languages on which this patch is suppressed, regardless of its option gate.</summary>
    public IReadOnlyList<string> ExcludeClientLanguages { get; init; } = [];
}

/// <summary>A validated hook package and its launcher-facing metadata.</summary>
/// <param name="Id">Globally unique plugin identifier.</param>
/// <param name="Version">Pinned semantic version.</param>
/// <param name="Game">Owning game identifier.</param>
/// <param name="Kind">Patch, native, file replacement, resource, Overlay, launcher-owned service or data-only assets package.</param>
/// <param name="Runtime">Required hook runtime version range, or empty.</param>
/// <param name="Folder">Absolute package directory.</param>
/// <param name="Name">Localized plugin title.</param>
/// <param name="Description">Optional localized description.</param>
/// <param name="Authors">Package authors.</param>
/// <param name="License">License identifier, or empty.</param>
/// <param name="Homepage">Project URL, or empty.</param>
/// <param name="Builds">Supported build selectors.</param>
/// <param name="Exclude">Excluded build selectors.</param>
/// <param name="Options">Ordered option definitions.</param>
/// <param name="Patches">Ordered declarative patches.</param>
/// <param name="Library">Native DLL filename, or null.</param>
/// <param name="Entry">Native export name, or null.</param>
public sealed record PluginDefinition(string Id, string Version, string Game, string Kind, string Runtime,
    string Folder, LocalizedText Name, LocalizedText? Description, IReadOnlyList<string> Authors, string License,
    string Homepage, IReadOnlyList<string> Builds, IReadOnlyList<string> Exclude,
    IReadOnlyList<PluginOption> Options, IReadOnlyList<PluginPatch> Patches, string? Library, string? Entry)
{
    /// <summary>Former plugin identifiers that may be migrated to this canonical identifier.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Optional minimum Launcher SemVer required before this package may be enabled.</summary>
    public string? MinimumLauncherVersion { get; init; }

    /// <summary>Localized confirmation shown when a warning component is disabled.</summary>
    public LocalizedText? Warning { get; init; }

    /// <summary>Verified file replacements applied by the launcher, never passed to the in-process hook.</summary>
    public IReadOnlyList<PluginFile> Files { get; init; } = [];

    /// <summary>Whether file replacements revert after the game exits; false retains a completed installation.</summary>
    public bool RestoreFiles { get; init; } = true;

    /// <summary>Whether enabling this package exposes the client's diagnostic records in the launcher.</summary>
    public bool Diagnostics { get; init; }

    /// <summary>Configured source identifier for a bundled component; store installations otherwise use their original receipt.</summary>
    public string UpdateSource { get; init; } = "";

    /// <summary>Optional game-module resource transformer; never loaded by the native hook.</summary>
    public GameResourceDefinition? Resources { get; init; }

    /// <summary>Packages that cannot be active alongside this package; explicit selection retains their settings but disables them.</summary>
    public IReadOnlyList<string> ExclusiveWith { get; init; } = [];

    /// <summary>Optional HTML overlay capability declared by an overlay package.</summary>
    public OverlayPluginDefinition? Overlay { get; init; }

    /// <summary>Optional launcher-owned service provider; never loaded into the in-process hook runtime.</summary>
    public LauncherServiceDefinition? Service { get; init; }

    /// <summary>Optional companion message/schema grants requested by an in-process native plugin.</summary>
    public IReadOnlyDictionary<string, int> CompanionMessages { get; init; } = new Dictionary<string, int>();

    /// <summary>Data folder declared by a data-only assets package; null for every other kind.</summary>
    public AssetsPluginDefinition? Assets { get; init; }

    /// <summary>
    /// Other packages this package needs on some build or launcher language: every <c>requires</c> entry and every
    /// <c>require</c> policy rule. Use the launcher's plugin policy to evaluate them for one client and language.
    /// </summary>
    public IReadOnlyList<PluginRequirement> Requires { get; init; } = [];

    /// <summary>
    /// The package's rules about other packages (<c>[[plugin_policy]]</c>), in manifest order. A legacy <c>requires</c>
    /// entry is listed here as an unscoped <c>require</c> rule.
    /// </summary>
    public IReadOnlyList<PluginPolicyRule> Policy { get; init; } = [];
}

/// <summary>
/// One plugin policy rule: how this package relates to another package on the builds and launcher languages it names.
/// </summary>
/// <param name="Plugin">Canonical or former identifier of the other package.</param>
/// <param name="Type"><c>require</c>, <c>recommend</c> or <c>conflict</c>.</param>
/// <param name="Range">Space-separated version comparisons the other package's version must satisfy, or empty for any version.</param>
/// <param name="Builds">Build identifiers the rule applies to; empty applies to every build this package supports.</param>
/// <param name="Languages">Launcher languages the rule applies to (for example <c>zh-CN</c>); empty applies to every language.
/// A language never forbids a package: outside its languages the rule simply does not apply.</param>
/// <param name="DefaultSelected">For a recommendation, whether the prompt pre-selects it.</param>
public sealed record PluginPolicyRule(string Plugin, string Type, string Range, IReadOnlyList<string> Builds,
    IReadOnlyList<string> Languages, bool DefaultSelected)
{
    /// <summary>The rule types a manifest may declare.</summary>
    public static readonly IReadOnlyList<string> Types = ["require", "recommend", "conflict"];
}

/// <summary>One declared package requirement.</summary>
/// <param name="Id">Canonical or former identifier of the required package.</param>
/// <param name="Range">Space-separated version comparisons that the installed version must satisfy, or empty for any version.</param>
public sealed record PluginRequirement(string Id, string Range)
{
    /// <summary>Formats the requirement in its manifest form, such as <c>id</c> or <c>id@&gt;=1.2.0</c>.</summary>
    public override string ToString() => Range.Length == 0 ? Id : Id + "@" + Range;
}

/// <summary>Validated metadata for a data-only package whose files are shared with other packages.</summary>
/// <param name="Root">Package-relative folder whose files are exposed to dependents; always a safe slash-separated path.</param>
public sealed record AssetsPluginDefinition(string Root);

/// <summary>Data-selected built-in provider for one optional game-session service plugin.</summary>
/// <param name="Provider">Registered launcher provider identity; no assembly path or network address is accepted.</param>
/// <param name="Messages">Explicit companion message types and schema versions granted to this package.</param>
public sealed record LauncherServiceDefinition(string Provider, IReadOnlyDictionary<string, int> Messages);

/// <summary>Validated metadata for an optional game-window overlay package.</summary>
/// <param name="Entry">Optional package-relative HTML entry file used by the overlay broker.</param>
public sealed record OverlayPluginDefinition(string? Entry)
{
    /// <summary>Gets the game-module controller identity that owns this panel's background behavior; null retains a presentation-only package.</summary>
    public string? Controller { get; init; }
}

/// <summary>A hashed package file temporarily replacing a relative client path for selected builds.</summary>
/// <param name="Source">Relative payload path inside the plugin package.</param>
/// <param name="Target">Relative destination path inside the client.</param>
/// <param name="Size">Exact payload byte length.</param>
/// <param name="Sha256">Lower-case SHA-256 of the payload.</param>
/// <param name="Builds">Build selectors, or empty for every compatible build.</param>
public sealed record PluginFile(string Source, string Target, long Size, string Sha256, IReadOnlyList<string> Builds)
{
    /// <summary>Selected game languages permitted to receive this file; empty permits every language.</summary>
    public IReadOnlyList<string> ClientLanguages { get; init; } = [];

    /// <summary>Selected game languages excluded from this file replacement.</summary>
    public IReadOnlyList<string> ExcludeClientLanguages { get; init; } = [];
}
