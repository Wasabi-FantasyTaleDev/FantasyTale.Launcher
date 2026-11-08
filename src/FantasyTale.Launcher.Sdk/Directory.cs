namespace FantasyTale.Launcher.Sdk;

/// <summary>Metadata advertised by a server operator or the owner of a temporary room.</summary>
/// <param name="Game">Stable game module identity.</param>
/// <param name="Name">Plain-text name presented before connection.</param>
/// <param name="ApiUrl">Public launcher API; account credentials are sent there only after explicit login.</param>
/// <param name="Builds">Supported client build identities, checked again against the target catalog on join.</param>
/// <param name="Description">Optional plain-text introduction, without executable markup.</param>
public sealed record DirectoryAdvertisement(string Game, string Name, string ApiUrl, IReadOnlyList<string> Builds, string Description)
{
    /// <summary>Optional advertised game endpoint used to identify the same server across independent catalogs.</summary>
    public ServerEndpoint? Endpoint { get; init; }
    /// <summary>Optional public status, icon, player counts and message of the day.</summary>
    public ServerPresentation? Presentation { get; init; }
    /// <summary>The server's client policy, scoped to its supported client builds.</summary>
    public ClientPolicy ClientPolicy { get; init; } = ClientPolicy.Empty;
}

/// <summary>One approved persistent server or unexpired temporary room from a directory source.</summary>
/// <param name="Id">Opaque identity within this directory.</param>
/// <param name="Kind">server or room; a room is never an approved persistent server.</param>
/// <param name="Details">Operator-supplied display and connection information.</param>
/// <param name="ExpiresAt">Room lease deadline, or null for an approved persistent server.</param>
public sealed record DirectoryEntry(string Id, string Kind, DirectoryAdvertisement Details, DateTimeOffset? ExpiresAt)
{
    /// <summary>Optional relative store path fetched only after the player explicitly adds this server.</summary>
    public string? ProfilePath { get; init; }
    /// <summary>Configured store supplying this advertisement, assigned locally during discovery.</summary>
    public string? StoreSourceId { get; init; }
}

/// <summary>A bounded version-one public directory response.</summary>
/// <param name="Schema">Protocol version, currently one.</param>
/// <param name="Entries">Approved servers and currently leased rooms.</param>
public sealed record DirectoryListing(int Schema, IReadOnlyList<DirectoryEntry> Entries);

/// <summary>Public source capabilities obtained before making directory mutations.</summary>
/// <param name="Schema">Protocol version, currently one.</param>
/// <param name="Name">Source display name.</param>
/// <param name="RoomLeaseSeconds">Maximum time a room remains listed without renewal.</param>
/// <param name="HostPolicy">auto, ip or domain; explicit source policy overrides local IP preferences.</param>
/// <param name="Region">Optional two-letter deployment region.</param>
public sealed record DirectoryServiceInfo(int Schema, string Name, int RoomLeaseSeconds, string HostPolicy, string? Region);

/// <summary>Private room management authority, retained only in the publisher's process memory.</summary>
/// <param name="Id">Exact room identity; it changes when a room is republished.</param>
/// <param name="Token">Opaque bearer secret for renew/delete only, never a game account credential.</param>
/// <param name="ExpiresAt">Current expiry confirmed by the directory.</param>
/// <param name="RenewAfterSeconds">Suggested heartbeat interval.</param>
public sealed record DirectoryLease(string Id, string Token, DateTimeOffset ExpiresAt, int RenewAfterSeconds);

/// <summary>A persistent server submission awaiting source-owner review.</summary>
/// <param name="Id">Submission reference returned to the operator.</param>
/// <param name="Details">Proposed server information.</param>
/// <param name="SubmittedAt">Source receipt time.</param>
public sealed record DirectorySubmission(string Id, DirectoryAdvertisement Details, DateTimeOffset SubmittedAt);

/// <summary>A server-discovery directory supplied by a store, bundled fallback or local configuration.</summary>
/// <param name="Id">Local source identity.</param>
/// <param name="Name">Display name chosen by the player.</param>
/// <param name="BaseUrl">HTTP API root, or null for an unconfigured bundled slot.</param>
/// <param name="Enabled">Whether the source appears in discovery.</param>
/// <param name="BuiltIn">Whether bundled data or a store supplies this source's default.</param>
public sealed record DirectorySource(string Id, string Name, string? BaseUrl, bool Enabled, bool BuiltIn)
{
    /// <summary>Owning configured store identity; an asterisk represents all enabled stores, and null denotes a live directory API.</summary>
    public string? StoreSourceId { get; init; }
}
