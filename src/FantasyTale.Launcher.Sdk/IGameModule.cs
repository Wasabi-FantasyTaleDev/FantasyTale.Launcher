namespace FantasyTale.Launcher.Sdk;

/// <summary>
/// The per-game part of the launcher. Open games ship their module with the launcher; closed
/// games ship it as a separate package discovered in the <c>games</c> folder at run time. A module
/// only describes how its game starts; installing, updating and plugin handling are shared.
/// </summary>
public interface IGameModule
{
    /// <summary>The game id this module serves; matches <see cref="GameDefinition.Id"/>.</summary>
    string GameId { get; }

    /// <summary>Resolves an optional game-owned public capability by interface, without coupling callers to the module implementation.</summary>
    T? GetCapability<T>() where T : class => this as T;

    /// <summary>
    /// Describes how to start one instance. Implementations validate the build's launch data
    /// and throw <see cref="InvalidOperationException"/> when the build cannot be started.
    /// </summary>
    LaunchPlan CreateLaunchPlan(LaunchContext context);

    /// <summary>Checks game-specific installed files before the shared launcher writes credentials or starts a process.</summary>
    void ValidateInstallation(LaunchContext context) { }

    /// <summary>Synchronizes persistent game-specific launch settings after validation, while the launcher owns the client lease and before a process is started.</summary>
    void PrepareLaunch(LaunchContext context) { }

    /// <summary>Reads optional installed version labels without requiring version files or changing protocol selection.</summary>
    ClientVersionMetadata ReadVersionMetadata(BuildDefinition build, string instanceFolder) => new(build.ReleaseVersion, build.PatchVersion);

    /// <summary>Resolves the game's UI revision from its client build while retaining declared capabilities and targets.</summary>
    OverlayBuildProfile? GetOverlayProfile(BuildDefinition build) => build.Overlay;
}

/// <summary>Optional installed release and patch labels, separate from the protocol identity.</summary>
/// <param name="ReleaseVersion">Distribution release label, or null when unknown.</param>
/// <param name="PatchVersion">Distribution patch label, or null when unknown.</param>
public sealed record ClientVersionMetadata(string? ReleaseVersion, string? PatchVersion);

/// <summary>Everything a module needs to start one instance.</summary>
/// <param name="Game">The game definition.</param>
/// <param name="Build">The instance's build.</param>
/// <param name="InstanceFolder">Absolute folder holding the client files.</param>
/// <param name="Server">The server to connect to, or null for the client's own default.</param>
/// <param name="Account">Account name for clients that receive it on the command line, or null.</param>
public sealed record LaunchContext(
    GameDefinition Game,
    BuildDefinition Build,
    string InstanceFolder,
    ServerEndpoint? Server,
    string? Account)
{
    /// <summary>Process-local launch identity shared with bootstrap preparation and any resulting native session.</summary>
    public string? LaunchId { get; init; }

    /// <summary>Optional sink for safe, launch-scoped diagnostics.</summary>
    public ILaunchDiagnosticSink? Diagnostics { get; init; }

    /// <summary>Persistent instance-owned cache for validated prepared resource archives, outside the game directory.</summary>
    public string? ResourceCacheFolder { get; init; }

    /// <summary>Progress sink for resource preparation; it must not access UI controls directly.</summary>
    public IProgress<GameResourceProgress>? ResourceProgress { get; init; }

    /// <summary>Cancellation is honored during preparation and immediately before deployment.</summary>
    public CancellationToken PreparationCancellation { get; init; }
    /// <summary>Game connection endpoint, which may differ from the authentication server endpoint.</summary>
    public ServerEndpoint? GameEndpoint { get; init; }
    /// <summary>Real destinations offered by an in-game native server selector.</summary>
    public IReadOnlyList<GameServerTarget> GameTargets { get; init; } = [];
    /// <summary>Visible native selector choices, including unavailable entries without network destinations.</summary>
    public IReadOnlyList<GameServerChoice> GameChoices { get; init; } = [];
    /// <summary>Optional issuer-bound native automatic selection owned by the bootstrap lifetime.</summary>
    public IAutomaticServerSelection? AutomaticSelection { get; init; }
    /// <summary>Whether an explicitly selected room accepts a plain player name without a server ticket.</summary>
    public bool NameLogin { get; init; }

    /// <summary>Selected in-game language, which the module applies when its language implementation is available.</summary>
    public string? ClientLanguage { get; init; }

    /// <summary>User choices answering a previous resource conflict report; stale snapshots are ignored.</summary>
    public IReadOnlyList<GameResourceConflictChoice> ResourceConflictChoices { get; init; } = [];

    /// <summary>Client destinations owned by declared file plugins, including components explicitly disabled for this launch.</summary>
    public IReadOnlySet<string> FilePluginTargets { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A process to start.</summary>
/// <param name="ExecutablePath">Absolute path of the executable.</param>
/// <param name="Arguments">Command-line arguments, unquoted.</param>
/// <param name="WorkingDirectory">Working directory of the process.</param>
public sealed record LaunchPlan(string ExecutablePath, IReadOnlyList<string> Arguments, string WorkingDirectory)
{
    /// <summary>Whether the module explicitly requests Windows elevation for this executable.</summary>
    public bool RequiresElevation { get; init; }

    /// <summary>Verified native files to overlay for this launch, or null when the module needs no native host.</summary>
    public RuntimeDeploymentPlan? Runtime { get; init; }

    /// <summary>One selected native plugin explicitly authorized to copy this launch's ticket and endpoint during activation; null grants no plugin access.</summary>
    public string? AuthenticationPlugin { get; init; }

    /// <summary>Selected plugin versions required by a module capability such as plain-name native login.</summary>
    public IReadOnlyDictionary<string, string> MinimumPluginVersions { get; init; } = new Dictionary<string, string>();
}

/// <summary>A module's native package and its client-directory file bindings. Shared code owns deployment and restoration.</summary>
/// <param name="PackageFolder">Absolute directory containing runtime.toml and its hashed payloads.</param>
/// <param name="Files">Files installed for this process lifetime, in dependency order.</param>
public sealed record RuntimeDeploymentPlan(string PackageFolder, IReadOnlyList<RuntimeFileBinding> Files);

/// <summary>Maps a verified native payload to a file beside the client executable.</summary>
/// <param name="Payload">File name in the native package.</param>
/// <param name="Target">Destination file name in the client directory.</param>
/// <param name="OriginalAlias">When absent, populate this optional forwarder alias from the original target before replacing it.</param>
public sealed record RuntimeFileBinding(string Payload, string Target, string? OriginalAlias = null);
