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
    /// <summary>The current transport or its owner has closed; a surviving launch-scoped view may later reconnect.</summary>
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
public sealed record CompanionOperationResult(CompanionOperationStatus Status, string? RequestId = null, object? Response = null)
{
    /// <summary>Gets the originating transport epoch of a received response, or null for a local outcome without a response.</summary>
    public string? ConnectionEpoch { get; init; }

    /// <summary>Gets this response frame's position within its connection epoch; zero means no received-frame identity is available.</summary>
    /// <remarks>Compare with event receive sequences from the same epoch, never with callback arrival order or another epoch.</remarks>
    public long ReceiveSequence { get; init; }
}

/// <summary>A structured event delivered by a registered companion message type.</summary>
public sealed record CompanionEvent(string Type, int Version, string MessageId, object? Payload)
{
    /// <summary>Gets the transport epoch captured when this event was received, independent of later reconnects.</summary>
    public string? ConnectionEpoch { get; init; }

    /// <summary>Gets this event frame's position within its connection epoch, captured before queuing its callback; zero means unavailable.</summary>
    /// <remarks>Events and responses share one increasing sequence, which may contain gaps for control frames.</remarks>
    public long ReceiveSequence { get; init; }
}

/// <summary>Describes a channel's current connection identity, binding state and owner-granted capabilities.</summary>
/// <param name="Epoch">The opaque negotiated transport epoch, or null before a transport has been attached.</param>
/// <param name="State">The transport and game-binding state at this revision.</param>
/// <param name="Capabilities">Negotiated message types restricted to this channel's grants.</param>
public sealed record CompanionConnectionSnapshot(string? Epoch, CompanionChannelState State, IReadOnlySet<string> Capabilities)
{
    /// <summary>Gets the monotonically increasing revision of this stable channel view.</summary>
    public long Revision { get; init; }
}

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

    /// <summary>Gets a coherent connection snapshot; implementations without observable lifecycle support have no epoch.</summary>
    CompanionConnectionSnapshot ConnectionSnapshot => new(null, State, Capabilities);

    /// <summary>Subscribes to ordered lifecycle snapshots, immediately replaying the latest state; callbacks must return promptly.</summary>
    /// <remarks>Callbacks run outside transport locks. A slow callback may receive the latest revision after intermediate states were superseded.</remarks>
    /// <exception cref="NotSupportedException">This channel does not expose lifecycle notifications; consumers requiring them must remain inactive.</exception>
    IDisposable SubscribeState(Action<CompanionConnectionSnapshot> handler) =>
        throw new NotSupportedException("The companion channel does not expose lifecycle notifications.");

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
