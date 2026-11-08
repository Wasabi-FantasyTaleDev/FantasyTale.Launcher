namespace FantasyTale.Launcher.Sdk;

/// <summary>Optional public display information shared by persistent servers and temporary rooms.</summary>
public sealed record ServerPresentation
{
    /// <summary>Optional public screenshots displayed as a manually navigated gallery.</summary>
    public IReadOnlyList<string> Images { get; init; } = [];
    /// <summary>Plain-text multiline message shown below the server name, without executable markup.</summary>
    public string Motd { get; init; } = "";
    /// <summary>Optional descriptions keyed by language tag; the launcher selects its UI language before falling back to English or another available description.</summary>
    public IReadOnlyDictionary<string, string> MotdTranslations { get; init; } = new Dictionary<string, string>();
    /// <summary>Optional concise introduction for server cards; the full description is used when absent.</summary>
    public string Summary { get; init; } = "";
    /// <summary>Optional card introductions keyed by language tag, independently editable from full descriptions.</summary>
    public IReadOnlyDictionary<string, string> SummaryTranslations { get; init; } = new Dictionary<string, string>();
    /// <summary>Optional public HTTP or HTTPS image URL; absent or unavailable images use the game-neutral icon.</summary>
    public string? IconUrl { get; init; }
    /// <summary>Optional public icon URLs keyed by language tag, with independent language fallback.</summary>
    public IReadOnlyDictionary<string, string> IconUrlTranslations { get; init; } = new Dictionary<string, string>();
    /// <summary>Optional screenshot galleries keyed by language tag; empty galleries fall back to another available language.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ImagesTranslations { get; init; } = new Dictionary<string, IReadOnlyList<string>>();
    /// <summary>Current connected-player count; minus one hides it, and null means the field was not supplied.</summary>
    public long? OnlinePlayers { get; init; }
    /// <summary>Optional population group identity advertised by an aggregate server directory.</summary>
    public string? PresenceGroup { get; init; }
    /// <summary>False when the advertised online count is a known lower bound because member reports are incomplete; null is an older server.</summary>
    public bool? PresenceComplete { get; init; }
    /// <summary>Historical peak connected-player count, not capacity; minus one hides it, and null means not supplied.</summary>
    public long? PeakOnlinePlayers { get; init; }
    /// <summary>Optional simultaneous-player admission limit; null is the default and means unlimited.</summary>
    public long? PlayerLimit { get; init; }
    /// <summary>Advertised online, offline or maintenance state; null means the server has not supplied its state.</summary>
    public string? State { get; init; }
    /// <summary>Whether admission uses an operator-managed whitelist; enforcement remains entirely server-side.</summary>
    public bool Whitelist { get; init; }
    /// <summary>Whether the public online-player list is visible; enabled by default and independently configurable from counters.</summary>
    public bool RosterEnabled { get; init; } = true;
}

/// <summary>A public online player, without account identifiers or private profile fields.</summary>
/// <param name="Name">Plain-text display name.</param>
/// <param name="Level">Optional game-supported level; null omits the level column for this player.</param>
public sealed record OnlinePlayer(string Name, long? Level)
{
    /// <summary>Optional client build currently being played; older servers omit this public display field.</summary>
    public string? BuildId { get; init; }
}

/// <summary>A server's on-demand public player list, independently switchable by its operator.</summary>
/// <param name="Schema">Response contract version, currently one.</param>
/// <param name="Enabled">Whether public roster disclosure is enabled.</param>
/// <param name="Players">Current public display names and optional levels; empty when disclosure is disabled.</param>
public sealed record OnlinePlayerList(int Schema, bool Enabled, IReadOnlyList<OnlinePlayer> Players)
{
    /// <summary>False when some population members cannot disclose current cards; null is an older server without completeness metadata.</summary>
    public bool? Complete { get; init; }
    /// <summary>Whether the operator's public roster limit omitted additional cards.</summary>
    public bool Truncated { get; init; }
}

/// <summary>A server-recommended public plugin catalog that the player can explicitly add to their stores.</summary>
/// <param name="Name">Display name shown before accepting this additional source.</param>
/// <param name="Kind">Shared source transport: alist, http or github.</param>
/// <param name="BaseUrl">Public metadata and payload root without embedded credentials.</param>
/// <param name="CatalogPath">Relative path to the same unified TOML catalog used by every store.</param>
public sealed record ServerStoreSource(string Name, string Kind, string BaseUrl, string CatalogPath);
