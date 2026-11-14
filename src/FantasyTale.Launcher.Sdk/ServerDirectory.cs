using System.Text.Json.Serialization;

namespace FantasyTale.Launcher.Sdk;

/// <summary>Public bootstrap metadata for one game build, independent of Launcher client presets.</summary>
/// <param name="Schema">Directory contract version.</param>
/// <param name="Build">Client build whose endpoints are described.</param>
/// <param name="Servers">Ordered choices displayed exclusively inside the game.</param>
public sealed record ServerDirectory(int Schema, string Build, [property: JsonPropertyName("servers")] IReadOnlyList<ServerSelector> Servers)
{
    /// <summary>Explicit server-owned Center activation; absent metadata keeps the ordinary server launch unchanged.</summary>
    [JsonPropertyName("center")]
    public ServerCenterConfiguration? Center { get; init; }

    /// <summary>Groups the servers are offered in, the first being the default; null or empty when the server does not group them.</summary>
    [JsonPropertyName("sections")]
    public IReadOnlyList<ServerDirectorySection>? Sections { get; init; }
}

/// <summary>A named group of servers offered as one choice.</summary>
/// <param name="Id">Stable identity that selectors refer to.</param>
/// <param name="Name">Display name.</param>
/// <param name="ApiUrl">The section's own Launcher API when it signs players in separately; null when it shares the publishing API's sign-in.</param>
public sealed record ServerDirectorySection(string Id, string Name, [property: JsonPropertyName("api_url")] string? ApiUrl = null);

/// <summary>A node's maintenance period while it is in effect.</summary>
/// <param name="Start">When it began; null when not announced.</param>
/// <param name="End">When it is expected to end; null when unknown.</param>
/// <param name="Description">The operator's explanation.</param>
public sealed record ServerDirectoryMaintenance(DateTimeOffset? Start, DateTimeOffset? End, string Description);

/// <summary>Server authorization to enable Center handling independently of client adapter support.</summary>
/// <param name="Enabled">Whether this server explicitly enables Center; omission leaves it disabled.</param>
/// <param name="Mode">Defaults to gateway when omitted; explicit native uses the server's own Center implementation.</param>
public sealed record ServerCenterConfiguration(bool Enabled, string? Mode = "gateway");

/// <summary>An independent native selector identity with zero or one manual target or eligible automatic candidates.</summary>
/// <param name="Id">Stable native selector identity, independent of physical endpoints.</param>
/// <param name="Name">Operator-supplied selector label.</param>
/// <param name="Automatic">Whether the Launcher measures eligible candidates before bootstrap.</param>
/// <param name="Targets">Physical endpoints and their admission metadata.</param>
public sealed record ServerSelector(int Id, string Name, bool Automatic, IReadOnlyList<ServerDirectoryTarget> Targets)
{
    /// <summary>Optional short label projected into the Gateway Center entry; the Launcher preserves it verbatim.</summary>
    [JsonPropertyName("short_name")]
    public string? ShortName { get; init; }
    /// <summary>The section this selector is offered in; null when the server does not group its selectors.</summary>
    [JsonPropertyName("section")]
    public string? Section { get; init; }
}

/// <summary>A physical node declaration; scope names do not grant authentication or cross-node joining.</summary>
/// <param name="NodeId">Stable physical backend identity.</param>
/// <param name="Host">Unresolved hostname or IP address.</param>
/// <param name="Port">Game listener port.</param>
/// <param name="DataScope">Opaque declared account-data scope; empty means no sharing.</param>
/// <param name="JoinScope">Opaque declared routing scope; empty grants no joining permission.</param>
/// <param name="Online">Whether a fresh lease reports the node ready.</param>
/// <param name="Draining">Whether the node refuses new placements.</param>
/// <param name="Players">Current authenticated game connections.</param>
/// <param name="Capacity">Configured admission limit.</param>
public sealed record ServerDirectoryTarget(string NodeId, string Host, int Port, string DataScope, string JoinScope,
    bool Online, bool Draining, int Players, int Capacity)
{
    /// <summary>Explicit authentication and catalog authority; absent metadata grants no cross-authority access.</summary>
    public ServerDirectoryAuthentication? Authentication { get; init; }
    /// <summary>Actual source heartbeat time, absent before the first observation.</summary>
    [JsonPropertyName("observed_at_utc")]
    public DateTimeOffset? ObservedAtUtc { get; init; }
    /// <summary>Actual source heartbeat expiry; fetching the directory does not renew it.</summary>
    [JsonPropertyName("expires_at_utc")]
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    /// <summary>Opaque source revision; lexical or numeric ordering has no meaning.</summary>
    [JsonPropertyName("status_revision")]
    public string? StatusRevision { get; init; }
    /// <summary>Every client build the node serves; null from a server that does not say.</summary>
    [JsonPropertyName("client_builds")]
    public IReadOnlyList<string>? ClientBuilds { get; init; }
    /// <summary>The node's maintenance period while it is in effect; null otherwise.</summary>
    [JsonPropertyName("maintenance")]
    public ServerDirectoryMaintenance? Maintenance { get; init; }
}

/// <summary>Identifies the ticket issuer and policy catalog independently of data and routing scopes.</summary>
/// <param name="AuthorityId">Opaque authority identity, bound to one API origin.</param>
/// <param name="ApiUrl">Exact authority API URL; it cannot redirect credentials from another session.</param>
/// <param name="Mode">master-issued for a shared issuer or own-authority for independent authentication.</param>
/// <param name="PolicyMode">authority requires that issuer's catalog and build policy without implicit inheritance.</param>
public sealed record ServerDirectoryAuthentication(string AuthorityId, string ApiUrl, string Mode, string PolicyMode);

/// <summary>An operator notice published by a server.</summary>
/// <param name="Id">Stable identity; the Launcher shows each one on its own only once.</param>
/// <param name="Title">Title.</param>
/// <param name="Body">Body in Markdown, rendered without raw HTML.</param>
/// <param name="Level">info, warning or important.</param>
/// <param name="PublishedAt">When the notice was published.</param>
public sealed record ServerNotice(string Id, string Title, string Body, string Level, DateTimeOffset? PublishedAt);
