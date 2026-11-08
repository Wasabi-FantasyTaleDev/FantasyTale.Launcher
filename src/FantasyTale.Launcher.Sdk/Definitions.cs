namespace FantasyTale.Launcher.Sdk;

/// <summary>Whether a game's installed module supports instance management and launch.</summary>
public enum GameStatus
{
    /// <summary>The game's module is installed and supports instance management and launch.</summary>
    Enabled,

    /// <summary>Only a display definition exists; browsing it does not enable instance management or launch.</summary>
    Placeholder,
}

/// <summary>A game, read from <c>games/&lt;game&gt;/game.toml</c>.</summary>
/// <param name="Id">Lower-case game id used by builds and plugins.</param>
/// <param name="Status">Whether the game has an installed module or is only a display placeholder.</param>
/// <param name="Name">Localized display name.</param>
/// <param name="Module">File name of the managed game module assembly, empty for placeholders.</param>
/// <param name="HookHost">Native hook host kind (<c>nmcogame</c> for Bubble Fighter, <c>external</c> when provided by the module).</param>
/// <param name="Accent">Accent colour as <c>#RRGGBB</c>, empty for the system accent.</param>
/// <param name="BackgroundVideo">Absolute path of the home background video, or null.</param>
/// <param name="Folder">Absolute folder holding the definition.</param>
/// <param name="Builds">Client builds of this game.</param>
public sealed record GameDefinition(
    string Id,
    GameStatus Status,
    LocalizedText Name,
    string Module,
    string HookHost,
    string Accent,
    string? BackgroundVideo,
    string Folder,
    IReadOnlyList<BuildDefinition> Builds)
{
    /// <summary>Launcher locale tags in which the game is hidden from the game selector; empty keeps it visible everywhere.</summary>
    public IReadOnlyList<string> UnsupportedRegions { get; init; } = [];
    /// <summary>Menu route identifiers supported by this game; older definitions expose only shared pages.</summary>
    public IReadOnlyList<string> NavigationEntries { get; init; } = ["home", "downloads", "accounts"];

    /// <summary>Whether directory rooms may be joined or published; server discovery is independent.</summary>
    public bool RoomsEnabled { get; init; } = true;

    /// <summary>Whether users may configure and start local server processes.</summary>
    public bool LocalHostingEnabled { get; init; } = true;

    /// <summary>Rejects room operations while the game declaration keeps them under development.</summary>
    public void RequireRoomsEnabled()
    {
        if (!RoomsEnabled) throw new InvalidOperationException("feature_in_development");
    }

    /// <summary>Rejects local server preparation while the game declaration keeps it under development.</summary>
    public void RequireLocalHostingEnabled()
    {
        if (!LocalHostingEnabled) throw new InvalidOperationException("feature_in_development");
    }

    /// <summary>Game-wide client language codes that installed modules can apply.</summary>
    public IReadOnlyList<string> ClientLanguages { get; init; } = [];

    /// <summary>Whether users may select between installed client instances on Home.</summary>
    public bool SupportsInstanceSelection { get; init; }

    /// <summary>Optional replaceable rank data for account level, experience and icon presentation.</summary>
    public LevelCatalogDefinition? Levels { get; init; }

    /// <summary>Whether this game accepts launcher-managed hook plugins.</summary>
    public bool SupportsPlugins { get; init; } = true;

    /// <summary>Optional bundled plugin identities whose versions may also be installed from explicitly selected stores.</summary>
    public IReadOnlyList<string> StoreManagedPlugins { get; init; } = [];

    /// <summary>Game-wide plugin identities activated when first available for a client, without overriding an explicit disabled selection.</summary>
    public IReadOnlyList<string> DefaultPlugins { get; init; } = [];

    /// <summary>Trusted store-source identities for separately distributed components; activation rules live in ClientPolicy.</summary>
    public IReadOnlyDictionary<string, string> PluginSources { get; init; } = new Dictionary<string, string>();

    /// <summary>Typed rules shared by every build of this game; build declarations add further rules.</summary>
    public ClientPolicy ClientPolicy { get; init; } = ClientPolicy.Empty;

    /// <summary>Launcher authentication policy: required, optional or none; a build may override it.</summary>
    public string LauncherLogin { get; init; } = "optional";

    /// <summary>Ordered account statistics displayed when supplied by the server.</summary>
    public IReadOnlyList<string> ProfileFields { get; init; } = [];

    /// <summary>Whether a server-provided rank icon may accompany the numeric level.</summary>
    public bool LevelIcon { get; init; }

    /// <summary>Whether the default server publishes an instance catalog.</summary>
    public bool BundledInstances { get; init; }

    /// <summary>Optional audio paths aligned with the video playlist; null uses embedded audio.</summary>
    public IReadOnlyList<string?> BackgroundAudioTracks { get; init; } = [];

    /// <summary>Optional home carousel order, from 0 to 10000; setting it also makes a placeholder visible.</summary>
    public int? HomeOrder { get; init; }

    /// <summary>Whether the home page displays a preparation notice instead of client management.</summary>
    public bool Preparing { get; init; }

    /// <summary>Localized service availability notice displayed for this game, when provided.</summary>
    public LocalizedText? ServiceNotice { get; init; }

    /// <summary>Optional English title used only above the home background's localized game name.</summary>
    public string? HeroTitle { get; init; }

    /// <summary>Optional local home cover image displayed when no video is available.</summary>
    public string? BackgroundImage { get; init; }

    /// <summary>Ordered local video paths, limited to 16 entries; playback wraps after the last entry.</summary>
    public IReadOnlyList<string> BackgroundVideos { get; init; } = BackgroundVideo is null ? [] : [BackgroundVideo];
}

/// <summary>One client build, read from <c>games/&lt;game&gt;/builds/&lt;build&gt;.toml</c>.</summary>
/// <param name="Id">Build id (<c>kr-812</c>); unique within the game.</param>
/// <param name="Branch">Branch id used by <c>branch:</c> selectors (<c>kr</c>, <c>cn</c>, <c>id</c>, <c>tw</c>).</param>
/// <param name="ProtocolVersion">Numeric protocol version, independent of the distribution's release and patch labels.</param>
/// <param name="Name">Localized display name.</param>
/// <param name="Executable">Executable started for this build, relative to the instance folder.</param>
/// <param name="Messenger">Mode of the game's native host (Bubble Fighter: <c>standin</c> or <c>forward</c>).</param>
/// <param name="ServerProfile">Matching server profile id, empty when unknown.</param>
/// <param name="DefaultPort">Default game server port, or null.</param>
/// <param name="PeTimestamp">PE header timestamp identifying the build, or null.</param>
/// <param name="Login">How the client logs in (<c>gamewidget</c>, <c>gamewidget-ticket</c>, <c>nmco</c>, <c>hook</c>, <c>unverified</c>).</param>
/// <param name="Arguments">Command-line template; <c>{account}</c>, <c>{host}</c>, <c>{port}</c> are substituted.</param>
/// <param name="DefaultPlugins">Plugin ids enabled for a new instance.</param>
public sealed record BuildDefinition(
    string Id,
    string Branch,
    int ProtocolVersion,
    LocalizedText Name,
    string Executable,
    string Messenger,
    string ServerProfile,
    int? DefaultPort,
    uint? PeTimestamp,
    string Login,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> DefaultPlugins)
{
    /// <summary>Optional researched distribution date, stored without system-locale or time-zone conversion.</summary>
    public DateTime? BuildDate { get; init; }

    /// <summary>Whether this build may create clients, install releases or start a game; disabled definitions retain their identity.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Whether this build is omitted from public version and download selectors without removing its definition.</summary>
    public bool Hidden { get; init; }

    /// <summary>Rejects operational use of a temporarily disabled build before changing files or obtaining login credentials.</summary>
    public void RequireEnabled()
    {
        if (!Enabled) throw new InvalidOperationException("build_disabled");
    }

    /// <summary>Typed local client policy, including game-wide rules when loaded through the game catalog.</summary>
    public ClientPolicy ClientPolicy { get; init; } = ClientPolicy.Empty;

    /// <summary>Client distribution region, distinct from a server deployment region.</summary>
    public string Region { get; init; } = "";

    /// <summary>Alternative executable names for the same build, searched after the primary executable.</summary>
    public IReadOnlyList<string> ExecutableAliases { get; init; } = [];

    /// <summary>Optional SHA-256 identity of the publisher certificate, paired with its release timestamp.</summary>
    public string? SignerCertificateSha256 { get; init; }

    /// <summary>Optional timestamp-authority time identifying a signed release; unsigned files use the PE fingerprint.</summary>
    public DateTimeOffset? SignedAt { get; init; }

    /// <summary>Externally sourced release date used for display and sorting when signed metadata is available.</summary>
    public DateTimeOffset? ReleaseDate => SignedAt;

    /// <summary>Optional game-module adapter for local hosting; empty when this build is not enabled.</summary>
    public string HostingAdapter { get; init; } = "";
    /// <summary>Optional leaf DLL name of the client-loaded authentication bridge.</summary>
    public string? LoginModule { get; init; }

    /// <summary>Optional release subversion displayed below the region-and-year title.</summary>
    public string ReleaseVersion { get; init; } = "";

    /// <summary>Optional distribution patch label; descriptive metadata independent of the wire dialect.</summary>
    public string PatchVersion { get; init; } = "";

    /// <summary>Optional localized build detail separate from the stable wire-version id.</summary>
    public LocalizedText? Description { get; init; }

    /// <summary>Language shipped in the unmodified distribution; independent of the selected translation.</summary>
    public string NativeLanguage { get; init; } = "";

    /// <summary>Additional language codes mapped to file-plugin identities that supply their translations.</summary>
    public IReadOnlyDictionary<string, string> AdditionalLanguages { get; init; } = new Dictionary<string, string>();

    /// <summary>Optional build-specific launcher login policy; null inherits the game policy.</summary>
    public string? LauncherLogin { get; init; }

    /// <summary>Whether this build explicitly requests Windows administrator permission when launched.</summary>
    public bool RequiresElevation { get; init; }

    /// <summary>Optional overlay theme, semantic capabilities and version-specific locator declarations.</summary>
    public OverlayBuildProfile? Overlay { get; init; }
}

/// <summary>A game server address.</summary>
/// <param name="Host">Host name or IP address.</param>
/// <param name="Port">TCP port of the game gate.</param>
public sealed record ServerEndpoint(string Host, int Port);
