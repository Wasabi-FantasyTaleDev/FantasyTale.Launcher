using System.Text.Json.Serialization;

namespace FantasyTale.Launcher.Sdk;

/// <summary>Issuer-bound endpoint membership for one authenticated launch; refreshes change health only.</summary>
/// <param name="Schema">Selection contract revision.</param>
/// <param name="GrantId">Opaque immutable admission identity.</param>
/// <param name="Build">Authorized wire build.</param>
/// <param name="AuthorityId">Authenticated ticket issuer identity.</param>
/// <param name="PolicyRevision">Equality-only catalog and policy identity.</param>
/// <param name="DataScope">Authorized persistent account domain for the automatic pool; manual master-issued targets may declare an independent scope.</param>
/// <param name="ExpiresAtUtc">Immutable hard admission deadline.</param>
/// <param name="GeneratedAtUtc">Response generation time, independent of heartbeat observations.</param>
/// <param name="Servers">Original selector membership with current source observations.</param>
public sealed record LaunchServerSelection(int Schema, string GrantId, string Build, string AuthorityId,
    string PolicyRevision, string DataScope, DateTimeOffset ExpiresAtUtc,
    [property: JsonPropertyName("generated_at_utc")] DateTimeOffset GeneratedAtUtc, IReadOnlyList<ServerSelector> Servers);

/// <summary>Lets shared launch orchestration discover a module's audited native automatic-selection support.</summary>
public interface IAutomaticServerSelectionCapability
{
    /// <summary>Returns whether this build can select an automatic destination without a presentation Hook.</summary>
    bool SupportsAutomaticSelection(BuildDefinition build);
}

/// <summary>An expiring player-side measurement for an originally admitted physical center.</summary>
/// <param name="CenterId">Canonical manual center identity.</param>
/// <param name="Healthy">Whether current source readiness and admission permit placement.</param>
/// <param name="RttUs">Measured TCP round-trip time in microseconds.</param>
/// <param name="Users">Source-reported occupied capacity.</param>
/// <param name="Capacity">Source-reported total capacity.</param>
/// <param name="ObservedAtUtc">Local time at which source readiness and the player-side probe were confirmed.</param>
/// <param name="ExpiresAtUtc">Remaining immutable launch admission mapped to the local clock.</param>
/// <param name="StatusRevision">Opaque heartbeat revision, compared only for equality.</param>
public sealed record AutomaticServerCandidate(string CenterId, bool Healthy, int RttUs, int Users, int Capacity,
    DateTimeOffset ObservedAtUtc, DateTimeOffset ExpiresAtUtc, string StatusRevision);

/// <summary>Supplies one launch's fixed endpoint bindings and serialized health refreshes without exposing its ticket.</summary>
public interface IAutomaticServerSelection
{
    /// <summary>Gets the original issuer grant; no refresh can extend or expand it.</summary>
    LaunchServerSelection Grant { get; }
    /// <summary>Gets the immutable admission deadline mapped to the receiving machine's clock for the local Gateway.</summary>
    DateTimeOffset LocalExpiresAtUtc => Grant.ExpiresAtUtc;
    /// <summary>Gets canonical manual destinations, including independent authorities outside the Automatic grant.</summary>
    IReadOnlyList<GameServerTarget> Targets { get; }
    /// <summary>Gets the distinct native automatic selector.</summary>
    ServerSelector Automatic { get; }
    /// <summary>Gets canonical manual center identities belonging to the original automatic pool.</summary>
    IReadOnlyList<string> AdmittedCenterIds { get; }
    /// <summary>Refreshes a full snapshot; null means admission has closed and must never be reissued implicitly.</summary>
    Task<IReadOnlyList<AutomaticServerCandidate>?> RefreshAsync(CancellationToken cancellationToken);
}
