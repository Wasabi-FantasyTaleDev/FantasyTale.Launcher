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
/// <param name="Kind">Patch, native, file replacement, Overlay or launcher-owned service plugin.</param>
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
}

/// <summary>Data-selected built-in provider for one optional game-session service plugin.</summary>
/// <param name="Provider">Registered launcher provider identity; no assembly path or network address is accepted.</param>
/// <param name="Messages">Explicit companion message types and schema versions granted to this package.</param>
public sealed record LauncherServiceDefinition(string Provider, IReadOnlyDictionary<string, int> Messages);

/// <summary>Validated metadata for an optional game-window overlay package.</summary>
/// <param name="Entry">Optional package-relative HTML entry file used by the overlay broker.</param>
public sealed record OverlayPluginDefinition(string? Entry);

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
