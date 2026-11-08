namespace FantasyTale.Launcher.Sdk;

/// <summary>A locally configured catalog endpoint, independent of game servers and accounts.</summary>
/// <param name="Id">Stable local source identifier.</param>
/// <param name="Name">User-visible source label.</param>
/// <param name="Kind">Transport adapter: alist, http or github.</param>
/// <param name="BaseUrl">Server root; null until the bundled source is configured.</param>
/// <param name="CatalogPath">Relative virtual path of the shared TOML catalog.</param>
/// <param name="Enabled">Whether the source participates in browsing.</param>
/// <param name="BuiltIn">Whether this entry is a bundled source slot.</param>
public sealed record StoreSource(string Id, string Name, string Kind, string? BaseUrl, string CatalogPath, bool Enabled, bool BuiltIn)
{
    /// <summary>Whether plain HTTP requests use a resolved IP; null follows global network settings.</summary>
    public bool? PreferIp { get; init; }
}

/// <summary>A catalog resolved through either HTTP or AList using the same schema.</summary>
/// <param name="Id">Publisher's catalog identifier, scoped to its configured source.</param>
/// <param name="Name">Localized catalog title.</param>
/// <param name="Packages">Available immutable releases; identities remain scoped to the source.</param>
public sealed record StoreCatalog(string Id, IReadOnlyDictionary<string, string> Name, IReadOnlyList<StorePackage> Packages)
{
    /// <summary>Source-owned retirement records retained even after their package releases are withdrawn.</summary>
    public IReadOnlyList<StoreDeprecatedPlugin> DeprecatedPlugins { get; init; } = [];

    /// <summary>Validated client profiles carried with packages in the same catalog response.</summary>
    public IReadOnlyList<StoreClientProfile> Profiles { get; init; } = [];

    /// <summary>Discoverable servers and rooms; reading the catalog never adds them to the user's saved destinations.</summary>
    public IReadOnlyList<DirectoryEntry> Servers { get; init; } = [];

    /// <summary>Public server and room directory endpoints supplied by this catalog.</summary>
    public IReadOnlyList<DirectorySource> DirectorySources { get; init; } = [];

    /// <summary>Publisher-selected GitHub release subscriptions resolved when this catalog refreshes.</summary>
    public IReadOnlyList<StoreGitHubRelease> GitHubReleases { get; init; } = [];
}

/// <summary>A catalog publisher's instruction to remove an obsolete plugin from future launches.</summary>
/// <param name="Id">Retired canonical plugin identifier; aliases of a replacement are not retirements.</param>
/// <param name="Game">Game that owns the retired plugin.</param>
/// <param name="Name">Localized plugin title retained after its package rows are withdrawn.</param>
/// <param name="Reason">Localized plain-text explanation displayed outside the plugin information card.</param>
public sealed record StoreDeprecatedPlugin(string Id, string Game, IReadOnlyDictionary<string, string> Name,
    IReadOnlyDictionary<string, string> Reason);

/// <summary>A server package whose current stable release and archive identity come from a GitHub repository.</summary>
/// <param name="Id">Package identity retained within the parent store.</param>
/// <param name="Game">Owning game module.</param>
/// <param name="Builds">Client builds supported by the subscribed package.</param>
/// <param name="Repository">GitHub owner/repository, without a branch or URL suffix.</param>
/// <param name="Asset">Exact release archive filename.</param>
/// <param name="Name">Localized store title.</param>
/// <param name="Description">Localized store description.</param>
/// <param name="Publisher">Publisher display label.</param>
/// <param name="Executable">Relative server executable used to discover the installation root inside the archive.</param>
public sealed record StoreGitHubRelease(string Id, string Game, IReadOnlyList<string> Builds, string Repository,
    string Asset, IReadOnlyDictionary<string, string> Name, IReadOnlyDictionary<string, string> Description,
    string Publisher, string Executable);

/// <summary>An exact GitHub asset selected during discovery; no mutable latest URL is retained by downloads.</summary>
/// <param name="Repository">Validated owner/repository.</param>
/// <param name="Tag">Exact published release tag.</param>
/// <param name="Asset">Archive filename under that release.</param>
/// <param name="Sha256">Expected archive SHA-256 supplied by release metadata.</param>
/// <param name="Executable">Relative server executable used to discover the archive root.</param>
public sealed record StoreGitHubAsset(string Repository, string Tag, string Asset, string Sha256, string Executable);

/// <summary>A source-scoped client definition whose revision changes independently of the launcher application.</summary>
/// <param name="Game">Owning game module identity.</param>
/// <param name="Revision">Monotonic profile revision within this source and build.</param>
/// <param name="Definition">Validated executable, login, plugin and presentation settings.</param>
/// <param name="Sha256">Digest of the exact inline definition for same-revision conflict detection.</param>
public sealed record StoreClientProfile(string Game, long Revision, BuildDefinition Definition, string Sha256);

/// <summary>Metadata for one release; browsing it never authorizes installation or launcher updates.</summary>
/// <param name="Id">Package identity within the source.</param>
/// <param name="Kind">client, plugin, runtime, launcher or server.</param>
/// <param name="Game">Game identifier, empty only for launcher releases.</param>
/// <param name="Builds">Compatible client builds; clients name exactly one and servers require at least one.</param>
/// <param name="Version">Optional client update label, independent of the game build version; other package kinds require SemVer.</param>
/// <param name="Name">Localized display title.</param>
/// <param name="Description">Localized plain-text description.</param>
/// <param name="Publisher">Display label, not an authenticated publisher identity.</param>
/// <param name="Size">Total transfer size in bytes.</param>
/// <param name="Manifest">Relative virtual path of the release manifest.</param>
/// <param name="ManifestSha256">SHA-256 of the manifest bytes; not a publisher signature.</param>
/// <param name="UpdateMode">files tracks the current folder snapshot; release uses numbered immutable releases.</param>
/// <param name="Revision">Publisher's monotonically increasing release number; zero disables automatic selection.</param>
public sealed record StorePackage(string Id, string Kind, string Game, IReadOnlyList<string> Builds, string Version,
    IReadOnlyDictionary<string, string> Name, IReadOnlyDictionary<string, string> Description, string Publisher,
    long Size, string Manifest, string ManifestSha256, long Revision = 0, string UpdateMode = "release")
{
    /// <summary>Optional complete ZIP transport; extracted members must still match the pinned file manifest.</summary>
    public StorePluginArchive? PluginArchive { get; init; }

    /// <summary>Plugin creators shown separately from the catalog publisher and uploader.</summary>
    public IReadOnlyList<string> Authors { get; init; } = [];

    /// <summary>Optional display name of the person who uploaded this store entry; empty when unspecified.</summary>
    public string Uploader { get; init; } = "";

    /// <summary>Optional complete plugin metadata supplied inline to avoid per-plugin discovery requests; never authorizes execution.</summary>
    public PluginDefinition? PluginDefinition { get; init; }

    /// <summary>Minimum application SemVer required to install this release; empty means no declared minimum.</summary>
    public string MinimumLauncherVersion { get; init; } = "";

    /// <summary>Optional host-computed compatibility flag for presentation; not publisher authority.</summary>
    public bool? LauncherCompatible { get; init; }

    /// <summary>Optional immutable external archive; the parent store still owns package selection and installation records.</summary>
    public StoreGitHubAsset? GitHubAsset { get; init; }
}

/// <summary>A complete importable plugin ZIP offered alongside the original immutable file release.</summary>
/// <param name="SourcePath">Source-root-relative path of the downloadable ZIP.</param>
/// <param name="Size">Exact compressed ZIP length in bytes.</param>
/// <param name="Sha256">SHA-256 of the complete ZIP bytes.</param>
public sealed record StorePluginArchive(string SourcePath, long Size, string Sha256);
