namespace FantasyTale.Launcher.Sdk;

/// <summary>Observed lifecycle of the local Gateway lease owned by one server-bound launch.</summary>
public enum GatewayControlState
{
    /// <summary>No lifecycle provider is installed or authorized for this launch.</summary>
    Unavailable,
    /// <summary>This launch currently owns no running Gateway process.</summary>
    Stopped,
    /// <summary>The owned Gateway is starting and has not confirmed readiness.</summary>
    Starting,
    /// <summary>The owned Gateway has confirmed readiness; this alone does not prove game routing or login.</summary>
    Running,
    /// <summary>The owned Gateway is completing its bounded stop handshake.</summary>
    Stopping,
    /// <summary>The owned process failed and requires an explicit new authorized operation.</summary>
    Failed,
}

/// <summary>Known or uncertain outcome of a launch-scoped Gateway lifecycle request.</summary>
public enum GatewayControlStatus
{
    /// <summary>The requested lifecycle change completed with the returned state.</summary>
    Completed,
    /// <summary>The same operation or desired state was already applied without creating another process.</summary>
    AlreadyApplied,
    /// <summary>The optional lifecycle capability is absent.</summary>
    Unavailable,
    /// <summary>The request is not bound to the currently authenticated server launch.</summary>
    NotBound,
    /// <summary>The request's authorization, expiry or Gateway ownership could not be validated.</summary>
    Rejected,
    /// <summary>Another serialized lifecycle operation still owns this launch's Gateway.</summary>
    Busy,
    /// <summary>The operation was canceled before any lifecycle change could occur.</summary>
    Canceled,
    /// <summary>A lifecycle change may have occurred; inspect current state instead of blindly retrying.</summary>
    OutcomeUnknown,
    /// <summary>The process failed with a known terminal lifecycle state.</summary>
    Failed,
}

/// <summary>A request to start Gateway from host-retained, current server authorization rather than arbitrary process arguments.</summary>
/// <param name="OperationId">Idempotency identity within this launch and its current companion epoch.</param>
/// <param name="AuthorizationId">Opaque reference to host-validated server authorization for this launch and permitted directory.</param>
public sealed record GatewayStartRequest(string OperationId, string AuthorizationId);

/// <summary>A request to close exactly the local Gateway lease owned by this launch.</summary>
/// <param name="OperationId">Idempotency identity within this launch and its current companion epoch.</param>
/// <param name="GatewayId">Opaque lease identity returned by start; never an arbitrary operating-system process id.</param>
public sealed record GatewayStopRequest(string OperationId, string GatewayId);

/// <summary>Credential-free Gateway lifecycle outcome; a running process does not assert that the game changed destinations.</summary>
/// <param name="Status">Whether the operation completed, was refused, or has an uncertain outcome.</param>
/// <param name="State">Last confirmed state of the Gateway owned by this controller.</param>
/// <param name="GatewayId">Opaque owned lease identity, absent when no lease was established.</param>
public sealed record GatewayControlResult(GatewayControlStatus Status, GatewayControlState State, string? GatewayId = null);

/// <summary>Reserved optional Gateway lifecycle service for a server-bound launch, including future Nirinsou Link consumers.</summary>
public interface IGatewayControl
{
    /// <summary>Server-issued launch identity to which every operation, authorization and owned process is bound.</summary>
    string LaunchId { get; }

    /// <summary>Last confirmed state of this launch's Gateway; other launches are never inspected or controlled.</summary>
    GatewayControlState State { get; }

    /// <summary>Starts or reuses the owned Gateway after validating current server authorization, without widening its candidate pool.</summary>
    ValueTask<GatewayControlResult> StartAsync(GatewayStartRequest request, CancellationToken cancellationToken = default);

    /// <summary>Stops the exact owned lease through its normal control handshake; stale lease identities cannot stop a replacement.</summary>
    ValueTask<GatewayControlResult> StopAsync(GatewayStopRequest request, CancellationToken cancellationToken = default);
}
