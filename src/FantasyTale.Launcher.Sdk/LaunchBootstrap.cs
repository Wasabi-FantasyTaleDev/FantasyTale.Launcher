using System.Text.Json.Serialization;

namespace FantasyTale.Launcher.Sdk;

/// <summary>A real server shown in a game's native server selector.</summary>
/// <param name="Id">Stable identity within the directory.</param>
/// <param name="Name">Directory label; native display projection requires an explicit verified capability.</param>
/// <param name="Endpoint">Real game endpoint selected by the client.</param>
public sealed record GameServerTarget(string Id, string Name, ServerEndpoint Endpoint)
{
    /// <summary>Optional upstream short label for the Gateway Center entry; null preserves the full display name.</summary>
    [JsonPropertyName("short_name")]
    public string? ShortName { get; init; }
    /// <summary>Physical node identity, independent of automatic and manual selector aliases.</summary>
    public string? BackendId { get; init; }
    /// <summary>Positive native selector identity when launch-bound native projection is explicitly enabled.</summary>
    public int? NativeCenterId { get; init; }
    /// <summary>Declared capacity for a confirmed unavailable destination; local native status remains visible without permitting login.</summary>
    public int? UnavailableCapacity { get; init; }
    /// <summary>Catalog capacity retained for protocol readiness checks and a local unavailable status if a target fails them.</summary>
    public int? StatusCapacity { get; init; }
}

/// <summary>A native selector choice, kept independently from the network destinations opened for a launch.</summary>
/// <param name="Id">Stable choice identity.</param>
/// <param name="Name">Label displayed by the game's selector.</param>
/// <param name="NativeId">Positive identity supplied to the game's presentation adapter.</param>
/// <param name="BindingId">Matching real target identity, or null for an unavailable choice.</param>
/// <param name="UnavailableReason">Machine-readable reason for an unavailable choice, absent for an active binding.</param>
public sealed record GameServerChoice(string Id, string Name, int NativeId, string? BindingId, string? UnavailableReason);

/// <summary>Validated game-module data passed from launch preparation to the matching native adapter.</summary>
/// <param name="Capability">Exact adapter contract selected by the game module.</param>
/// <param name="Payload">Module-validated payload; shared launch code preserves its contents without interpreting them.</param>
public sealed record LaunchBootstrapMetadata(string Capability, string Payload);

/// <summary>Starts optional local protocol support before the game launch plan is created.</summary>
public interface ILaunchBootstrapProvider
{
    /// <summary>Prepares a bootstrap process for this launch, or returns null when it is not required.</summary>
    ValueTask<ILaunchBootstrapLease?> PrepareBootstrapAsync(LaunchContext context, CancellationToken cancellationToken);
}

/// <summary>Receives bounded launch diagnostics that contain no credentials or protocol payloads.</summary>
public interface ILaunchDiagnosticSink
{
    /// <summary>Records one safe launch lifecycle event; implementations must keep recording best effort.</summary>
    void Record(string eventName, string? detail = null);
}

/// <summary>Owns local launch support for the lifetime of one game process.</summary>
public interface ILaunchBootstrapLease : IAsyncDisposable
{
    /// <summary>Endpoint passed to the game while authentication remains bound to the selected server.</summary>
    ServerEndpoint? GameEndpoint { get; }

    /// <summary>Optional validated module payload written into this launch's native session.</summary>
    LaunchBootstrapMetadata? Metadata => null;

    /// <summary>Observes a successful native handoff without aborting protocol cleanup; cancellation returns ownership to the caller for disposal.</summary>
    /// <remarks>The default retains support until game exit. Providers may wait for their protocol's own bounded retirement before releasing resources.</remarks>
    ValueTask CompleteHandoffAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
