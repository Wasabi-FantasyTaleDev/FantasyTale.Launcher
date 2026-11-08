namespace FantasyTale.Launcher.Sdk;

/// <summary>A connection destination independent of any installed client directory.</summary>
/// <param name="Id">Stable local server identifier.</param>
/// <param name="Game">Game whose accounts and instances this server accepts.</param>
/// <param name="Name">User-facing server name.</param>
/// <param name="ApiUrl">Launcher API base URL, or null until configured.</param>
/// <param name="Endpoint">Manual game endpoint when no catalog endpoint is supplied.</param>
/// <param name="BundledInstances">Whether the server advertises client instance presets.</param>
/// <param name="BuiltIn">Whether this destination is supplied by bundled data or a store, with a local editable override.</param>
public sealed record ServerDefinition(string Id, string Game, string Name, string? ApiUrl, ServerEndpoint? Endpoint, bool BundledInstances, bool BuiltIn)
{
    /// <summary>Connection destination type: a persistent server or a hosted room, including local-only play.</summary>
    public string Kind { get; init; } = "server";
    /// <summary>Optional per-server IP preference; null inherits the global preference.</summary>
    public bool? PreferIp { get; init; }
    /// <summary>
    /// Whether requests after discovery use HTTPS: an http API address is upgraded to https on the same host and
    /// port. Off by default; a catalog secure URL is used only when this preference is enabled.
    /// </summary>
    public bool UseHttps { get; init; }
    /// <summary>Optional locally saved description and artwork, used when the public catalog omits those fields.</summary>
    public ServerPresentation? Presentation { get; init; }
    /// <summary>Optional trusted icon asset relative to the launcher's web root, supplied only by a bundled server profile.</summary>
    public string? IconAsset { get; init; }
    /// <summary>Supported client builds retained from an explicitly imported server profile.</summary>
    public IReadOnlyList<string> Builds { get; init; } = [];
    /// <summary>Optional per-build native server-center directory supplied by this server profile.</summary>
    public NativeServerDirectory? NativeDirectory { get; init; }
    /// <summary>The client policy retained for display and launches when the live catalog is unavailable.</summary>
    public ClientPolicy ClientPolicy { get; init; } = ClientPolicy.Empty;
}

/// <summary>A validated native center list for builds that let the game choose its destination.</summary>
/// <param name="Mode">Server-owned Center handling: <c>native</c> retains the server endpoint; <c>gateway</c> requests local bootstrap.</param>
/// <param name="Builds">Client builds on which this directory is active.</param>
/// <param name="Targets">The real endpoints shown in the native center selector.</param>
public sealed record NativeServerDirectory(string Mode, IReadOnlyList<string> Builds, IReadOnlyList<GameServerTarget> Targets)
{
    /// <summary>Explicit server-profile Center activation; directories do not enable Center merely by being present.</summary>
    public bool Enabled { get; init; }
}

/// <summary>A server-advertised client choice; it does not imply client files are installed.</summary>
/// <param name="Id">Preset id within this server's catalog.</param>
/// <param name="Build">Required client build.</param>
/// <param name="Name">Localized preset title.</param>
/// <param name="Endpoint">Game connection address for this preset.</param>
/// <param name="LauncherLogin">Server policy; required cannot be weakened by a client preference.</param>
public sealed record ServerInstance(string Id, string Build, IReadOnlyDictionary<string, string> Name, ServerEndpoint? Endpoint, string? LauncherLogin)
{
    /// <summary>Optional release subversion below the localized title.</summary>
    public string? ReleaseVersion { get; init; }
    /// <summary>Optional numeric game protocol declared by this preset; descriptive release labels do not replace it.</summary>
    public int? ProtocolVersion { get; init; }
    /// <summary>Optional installed patch label, independent of the game protocol.</summary>
    public string? PatchVersion { get; init; }
    /// <summary>Optional language list overriding the local build's preferences, including an empty list.</summary>
    public IReadOnlyList<string>? ClientLanguages { get; init; }
    /// <summary>Optional client distribution recommendation, accepted only through an explicit installation.</summary>
    public ServerClientSource? DownloadSource { get; init; }
}

/// <summary>A server's client distribution recommendation without credentials or launcher update authority.</summary>
/// <param name="Kind">Shared transport adapter: http, alist or github.</param>
/// <param name="BaseUrl">Public source root.</param>
/// <param name="CatalogPath">Relative path of the shared store catalog.</param>
/// <param name="PackageId">Client package identity within that catalog.</param>
public sealed record ServerClientSource(string Kind, string BaseUrl, string CatalogPath, string PackageId);

/// <summary>A validated server recommendation whose source and exact release are shown before installation.</summary>
/// <param name="Source">Source identity scoped to the server and distribution endpoint.</param>
/// <param name="Package">Selected compatible client release.</param>
public sealed record ServerClientRelease(StoreSource Source, StorePackage Package);

/// <summary>A bounded, declarative extra registration field supplied by the server.</summary>
/// <param name="Key">Stable form key distinct from username and password.</param>
/// <param name="Label">Localized label rendered as text.</param>
/// <param name="Type">One of text, password, email, number, select or checkbox.</param>
/// <param name="Required">Whether registration must supply a value.</param>
/// <param name="MaxLength">Maximum serialized field length.</param>
/// <param name="Choices">Allowed values for a select field.</param>
public sealed record RegistrationField(string Key, IReadOnlyDictionary<string, string> Label, string Type, bool Required, int MaxLength, IReadOnlyList<string> Choices);

/// <summary>The version-one launcher service's public capabilities and client choices.</summary>
/// <param name="Schema">Contract version, currently one.</param>
/// <param name="Game">Game identity used to reject mismatched servers.</param>
/// <param name="Name">Server display name.</param>
/// <param name="RegistrationEnabled">Whether new accounts can be created.</param>
/// <param name="RegistrationFields">Extra fields in addition to username and password.</param>
/// <param name="Instances">Client presets available from the server.</param>
public sealed record LauncherCatalog(int Schema, string Game, string Name, bool RegistrationEnabled, IReadOnlyList<RegistrationField> RegistrationFields, IReadOnlyList<ServerInstance> Instances)
{
    /// <summary>Explicit Center activation supplied by this server; absent metadata preserves ordinary launch behavior.</summary>
    public ServerCenterConfiguration? Center { get; init; }
    /// <summary>Optional display information supplied with this same public catalog response.</summary>
    public ServerPresentation? Presentation { get; init; }
    /// <summary>Optional minimum Launcher SemVer accepted by authentication and launch operations.</summary>
    public string? MinimumLauncherVersion { get; init; }
    /// <summary>The server's HTTPS base URL used after discovery when the local HTTPS preference is enabled.</summary>
    public string? SecureUrl { get; init; }
    /// <summary>The server's client policy; its sources are also offered to the player for explicit addition.</summary>
    public ClientPolicy ClientPolicy { get; init; } = ClientPolicy.Empty;
    /// <summary>Explicit ip or domain overrides every local preference; auto or null inherits it.</summary>
    public string? HostPolicy { get; init; }
    /// <summary>Deployment region; CN denotes mainland China.</summary>
    public string? Region { get; init; }
    /// <summary>Optional game-wide authentication override.</summary>
    public string? LauncherLogin { get; init; }
    /// <summary>Optional override for the instance selector.</summary>
    public bool? SupportsInstances { get; init; }
    /// <summary>Optional override for plugin availability.</summary>
    public bool? SupportsPlugins { get; init; }
    /// <summary>Optional override for client language choices.</summary>
    public IReadOnlyList<string>? ClientLanguages { get; init; }
    /// <summary>Optional ordered account statistic keys.</summary>
    public IReadOnlyList<string>? ProfileFields { get; init; }
    /// <summary>Optional override for rank icon presentation.</summary>
    public bool? LevelIcon { get; init; }
}

/// <summary>Public account information; bearer tokens never cross the frontend bridge.</summary>
/// <param name="Username">Account login identifier.</param>
/// <param name="DisplayName">Player display name.</param>
/// <param name="Stats">Available level and balance values; absent values remain unknown.</param>
/// <param name="LevelIconUrl">Optional server-supplied HTTPS rank icon.</param>
public sealed record AccountProfile(string Username, string DisplayName, IReadOnlyDictionary<string, long?> Stats, string? LevelIconUrl)
{
    /// <summary>Whether the server confirms a created game character; null means the endpoint has not supplied this status.</summary>
    public bool? HasCharacter { get; init; }

    /// <summary>Optional explicit icon variant, such as star, resolved only through the game's rank data.</summary>
    public string? RankVariant { get; init; }
    /// <summary>Optional explicit special-rank key, such as piero; this is presentation metadata, not an authorization grant.</summary>
    public string? RankBadge { get; init; }
    /// <summary>Optional numeric presentation override from the configured badge table; it does not replace level or experience.</summary>
    public int? LevelIconId { get; init; }
}

/// <summary>Short-lived credentials for one launch, retained only by the host.</summary>
/// <param name="Account">Authenticated username.</param>
/// <param name="Ticket">Ephemeral launch ticket.</param>
/// <param name="ExpiresAt">UTC expiry of the ticket.</param>
public sealed record LaunchCredentials(string Account, string Ticket, DateTimeOffset ExpiresAt)
{
    /// <summary>Optional issuer-scoped native server admission retained only by the launch host.</summary>
    public LaunchServerSelection? ServerSelection { get; init; }
    /// <summary>Messenger sign-in for this launch, when the server runs a messenger; retained only by the host.</summary>
    public LaunchMessenger? Messenger { get; init; }

    /// <summary>Fantasy Tale Nirinsou Link grant for this launch, when the server runs it; retained only by the host.</summary>
    public LaunchCompanion? Companion { get; init; }
}

/// <summary>Where the launcher connects Fantasy Tale Nirinsou Link for one launch.</summary>
/// <param name="Url">Absolute ws or wss URL of the server's companion endpoint.</param>
/// <param name="Ticket">One-time connection ticket, presented once in the WebSocket upgrade.</param>
/// <param name="LaunchId">Server-issued launch identity; the game ticket of the same launch carries it.</param>
/// <param name="ExpiresAt">UTC expiry of the connection ticket.</param>
public sealed record LaunchCompanion(string Url, string Ticket, string LaunchId, DateTimeOffset ExpiresAt);

/// <summary>
/// A launch opened before anyone signed in, for a build whose own login screen signs in (server preset
/// <c>launcher_login = "game"</c>): the game binds it with the player's sign-in, and the key lets the launcher fetch its
/// companion grant afterwards.
/// </summary>
/// <param name="LaunchId">The server's launch id.</param>
/// <param name="LaunchKey">The secret that binds and reads the launch; never shown to the user.</param>
/// <param name="ExpiresAt">When an unbound launch expires.</param>
public sealed record GameLoginLaunch(string LaunchId, string LaunchKey, DateTimeOffset ExpiresAt);

/// <summary>Where and how the client signs in to the server's messenger for one launch.</summary>
/// <param name="Url">Absolute http(s) base URL of the messenger service.</param>
/// <param name="Ticket">Signed, single-use messenger ticket; separate from the game launch ticket.</param>
/// <param name="ExpiresAt">UTC expiry of the messenger ticket.</param>
public sealed record LaunchMessenger(string Url, string Ticket, DateTimeOffset ExpiresAt);

/// <summary>A public view of a server account session, without its bearer token.</summary>
/// <param name="Account">Last validated account profile.</param>
/// <param name="ExpiresAt">UTC time when the current access token expires; the launcher refreshes it automatically while the protected refresh session remains valid.</param>
public sealed record AccountSessionInfo(AccountProfile Account, DateTimeOffset ExpiresAt)
{
    /// <summary>UTC expiry of the renewable session, when available; this deadline does not expose the refresh credential.</summary>
    public DateTimeOffset? RefreshExpiresAt { get; init; }
}

/// <summary>An atomic account list and active selection for one server.</summary>
/// <param name="Current">Selected profile, or null when no valid session is selected.</param>
/// <param name="Accounts">Retained sessions for this server, renewed before authenticated requests.</param>
public sealed record AccountSessionList(AccountProfile? Current, IReadOnlyList<AccountSessionInfo> Accounts);
