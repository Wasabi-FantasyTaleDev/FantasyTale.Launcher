namespace FantasyTale.Launcher.Sdk;

/// <summary>A declarative resource transformation delegated to the owning game module.</summary>
/// <param name="Provider">Game-owned transformation identity.</param>
/// <param name="Manifest">Package-relative data file describing the transformation.</param>
public sealed record GameResourceDefinition(string Provider, string Manifest);

/// <summary>A selected resource package with validated option values.</summary>
public sealed record GameResourceSelection(PluginDefinition Package, IReadOnlyDictionary<string, object> Options);

/// <summary>One plugin's virtual edit, retained as diagnostic provenance for a composed client archive.</summary>
public sealed record GameResourceChange(string PluginId, string Version, LocalizedText Name, string Member,
    string Action, string? OriginalSha256, string? Sha256);

/// <summary>Measured preparation progress reported before any client resources are replaced.</summary>
public sealed record GameResourceProgress(string Path, long Completed, long Total, string Phase, double ElapsedSeconds)
{
    /// <summary>Archives already prepared or reused in this preparation snapshot.</summary>
    public int CompletedArchives { get; init; }

    /// <summary>Total archives affected by the snapshot, used for stable overall progress.</summary>
    public int TotalArchives { get; init; }

    /// <summary>Monotonic overall completion percentage, including bounded concurrent archive work.</summary>
    public double Percentage { get; init; }
}

/// <summary>A prepared file whose bytes will be validated and included in the recoverable client transaction.</summary>
public sealed record PreparedGameResource(string Source, string Target, long Size, string Sha256)
{
    /// <summary>Original input digest checked again when staging the transaction, preventing stale archive compositions.</summary>
    public string? OriginalSha256 { get; init; }

    /// <summary>Requires the client destination to remain absent until deployment; cannot accompany an original digest.</summary>
    public bool ExpectedAbsent { get; init; }

    /// <summary>Contributing plugin edits grouped under this physical client file.</summary>
    public IReadOnlyList<GameResourceChange> Changes { get; init; } = [];

    /// <summary>Whether preparation reused an independently verified cached archive.</summary>
    public bool CacheReused { get; init; }
}

/// <summary>One plugin's side of a resource edit conflict, shown as a plugin card by the launcher.</summary>
/// <param name="PluginId">Contributing plugin identity.</param>
/// <param name="Version">Contributing plugin version.</param>
/// <param name="Name">Localized plugin display name.</param>
/// <param name="Action">Requested action: add, replace, patch, remove, or member-replace.</param>
/// <param name="Value">Requested value for field conflicts, or null when the value is not a short text.</param>
public sealed record GameResourceConflictParty(string PluginId, string Version, LocalizedText Name, string Action, string? Value);

/// <summary>A structured conflict between resource plugins, detected before any client file is replaced.</summary>
/// <param name="Id">Stable identity of the conflict group within its snapshot, used to submit a choice.</param>
/// <param name="Code">Stable error code, for example resource.conflict.field-value.</param>
/// <param name="Archive">Client-relative physical archive path.</param>
/// <param name="Member">Virtual member path inside the archive.</param>
/// <param name="Target">Logical target: sheet name, string table, or UI control path.</param>
/// <param name="Key">Canonical record key or control path, empty for whole-member conflicts.</param>
/// <param name="Field">Field or attribute name for field-level conflicts, otherwise null.</param>
/// <param name="Original">Original short value when it helps the user decide, otherwise null.</param>
/// <param name="Parties">Every plugin participating in the conflict, ordered by plugin identity.</param>
public sealed record GameResourceConflict(string Id, string Code, string Archive, string Member, string Target, string Key,
    string? Field, string? Original, IReadOnlyList<GameResourceConflictParty> Parties);

/// <summary>Serializable conflict report bound to the exact preparation inputs that produced it.</summary>
/// <param name="Snapshot">Digest of build, client language, plugins, manifests and original archives.</param>
/// <param name="Build">Client build identity.</param>
/// <param name="Language">Selected client language, or null.</param>
/// <param name="Conflicts">Conflicts that require a user choice.</param>
public sealed record GameResourceConflictReport(string Snapshot, string Build, string? Language, IReadOnlyList<GameResourceConflict> Conflicts);

/// <summary>A user's choice for one conflict; it is ignored unless its snapshot matches the current inputs.</summary>
/// <param name="Snapshot">Snapshot from the report that the user answered.</param>
/// <param name="ConflictId">Conflict group identity from the same report.</param>
/// <param name="KeepPluginId">Plugin whose operations survive; other parties' operations in this group are skipped.</param>
public sealed record GameResourceConflictChoice(string Snapshot, string ConflictId, string KeepPluginId);

/// <summary>
/// Raised during preparation, before any client file is replaced, when plugins conflict. It is an invalid operation for the
/// current selection rather than damaged data, so callers can tell it apart and resubmit with
/// <see cref="LaunchContext.ResourceConflictChoices"/>.
/// </summary>
public sealed class GameResourceConflictException(GameResourceConflictReport report)
    : InvalidOperationException("Resource plugins conflict: " + string.Join("; ", report.Conflicts.Select(c => c.Code + " " + c.Member + " " + c.Key)))
{
    /// <summary>Structured conflicts and the snapshot that a resubmitted choice must match.</summary>
    public GameResourceConflictReport Report { get; } = report;
}

/// <summary>Owns staged outputs until the shared deployment transaction has copied them.</summary>
public interface IPreparedGameResources : IDisposable
{
    /// <summary>Hashed client replacements; an empty list means no client modifications.</summary>
    IReadOnlyList<PreparedGameResource> Files { get; }
}

/// <summary>Optional module capability for settings-dependent transformations of game-owned archives.</summary>
public interface IGameResourceModule
{
    /// <summary>Builds all selected outputs without modifying client files; disposal removes staged outputs.</summary>
    IPreparedGameResources PrepareResources(LaunchContext context, IReadOnlyList<GameResourceSelection> selections);

    /// <summary>Returns read-only JSON-serializable context for a package-owned sandboxed settings page.</summary>
    IReadOnlyDictionary<string, object> ResourceSettings(LaunchContext context, PluginDefinition package);

    /// <summary>Validates package-owned settings before saving them, including disabled selections.</summary>
    void ValidateResourceSettings(LaunchContext context, GameResourceSelection selection);
}
