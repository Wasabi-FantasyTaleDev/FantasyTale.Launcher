namespace FantasyTale.Launcher.Sdk;

/// <summary>Lifecycle state of the optional server companion channel.</summary>
public enum CompanionChannelState
{
    /// <summary>The feature is disabled by the selected catalog or plugin policy.</summary>
    Disabled,
    /// <summary>No compatible server capability or connection is available.</summary>
    Unavailable,
    /// <summary>The component is obtaining a short-lived connection authorization.</summary>
    Connecting,
    /// <summary>The connection is authorized but game login has not bound the launch yet.</summary>
    PendingGame,
    /// <summary>The channel negotiated successfully and can exchange messages.</summary>
    Connected,
    /// <summary>The transport is retrying a bounded reconnect.</summary>
    Reconnecting,
    /// <summary>The owner or game launch has ended and the channel will not reconnect.</summary>
    Closed,
}

/// <summary>Outcome of an attempted companion send or request.</summary>
public enum CompanionOperationStatus
{
    /// <summary>The message was accepted by the bounded send queue.</summary>
    Accepted,
    /// <summary>The request completed and its response is available.</summary>
    Completed,
    /// <summary>The channel is absent or its server capability is not advertised.</summary>
    Unavailable,
    /// <summary>The launch is not bound to a logged-in game session.</summary>
    NotBound,
    /// <summary>The bounded queue or request budget is full.</summary>
    Busy,
    /// <summary>The operation was canceled before completion.</summary>
    Canceled,
    /// <summary>The connection ended after a side-effecting request may have reached the server.</summary>
    OutcomeUnknown,
    /// <summary>The server returned a structured error for this request.</summary>
    Rejected,
}

/// <summary>Result returned by a companion send or request.</summary>
public sealed record CompanionOperationResult(CompanionOperationStatus Status, string? RequestId = null, object? Response = null);

/// <summary>A structured event delivered by a registered companion message type.</summary>
public sealed record CompanionEvent(string Type, int Version, string MessageId, object? Payload);

/// <summary>Options for a request that may have a bounded response timeout.</summary>
public sealed record CompanionRequestOptions
{
    /// <summary>Maximum wait in milliseconds; zero uses the channel default.</summary>
    public int TimeoutMilliseconds { get; init; }

    /// <summary>Optional caller-owned idempotency key for a server-defined retry contract.</summary>
    public string? OperationId { get; init; }
}

/// <summary>Owner-scoped public API for the optional bidirectional server companion channel.</summary>
public interface ICompanionChannel : IDisposable
{
    /// <summary>Current transport and game-binding state.</summary>
    CompanionChannelState State { get; }

    /// <summary>Capabilities negotiated for the current build and server session.</summary>
    IReadOnlySet<string> Capabilities { get; }

    /// <summary>Sends a low-frequency event without exposing the underlying transport.</summary>
    ValueTask<CompanionOperationResult> SendAsync(string type, int version, object? payload = null,
        CancellationToken cancellationToken = default);

    /// <summary>Sends a request and returns a known response or an explicit uncertain outcome.</summary>
    ValueTask<CompanionOperationResult> RequestAsync(string type, int version, object? payload = null,
        CompanionRequestOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to one registered event type; disposal removes the owner-scoped handler.</summary>
    IDisposable Subscribe(string type, int version, Func<CompanionEvent, ValueTask> handler);

    /// <summary>Subscribes with a cancellation token that ends when the owner or session closes.</summary>
    IDisposable Subscribe(string type, int version, Func<CompanionEvent, CancellationToken, ValueTask> handler) =>
        Subscribe(type, version, message => handler(message, CancellationToken.None));
}
