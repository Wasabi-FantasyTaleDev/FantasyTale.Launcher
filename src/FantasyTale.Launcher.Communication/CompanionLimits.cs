namespace FantasyTale.Launcher.Communication;

/// <summary>Validated resource budgets for one optional companion connection.</summary>
public sealed record CompanionLimits
{
    /// <summary>Maximum reassembled UTF-8 message size, checked before buffering each fragment.</summary>
    public int MessageBytes { get; init; } = 64 * 1024;
    /// <summary>Maximum JSON nesting including the envelope.</summary>
    public int JsonDepth { get; init; } = 16;
    /// <summary>Maximum queued application messages in each direction.</summary>
    public int QueueMessages { get; init; } = 128;
    /// <summary>Maximum queued application bytes in each direction.</summary>
    public int QueueBytes { get; init; } = 1024 * 1024;
    /// <summary>Maximum simultaneous requests waiting for a response.</summary>
    public int PendingRequests { get; init; } = 32;
    /// <summary>Maximum registered message routes per connection.</summary>
    public int Routes { get; init; } = 128;
    /// <summary>Maximum fragments for one application message, including empty fragments.</summary>
    public int MessageFragments { get; init; } = 4096;
    /// <summary>Maximum received messages in a one-second observation window, including controls.</summary>
    public int MessagesPerSecond { get; init; } = 128;
    /// <summary>Time allowed for a complete WebSocket upgrade and hello/welcome exchange.</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Default wait for a request response.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Interval between application heartbeat probes.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(20);
    /// <summary>Maximum time to wait for the matching heartbeat acknowledgement.</summary>
    public TimeSpan HeartbeatTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Maximum time allowed to finish transport cleanup after cancellation.</summary>
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(3);

    internal void Validate()
    {
        if (MessageBytes is < 256 or > 1024 * 1024 || JsonDepth is < 4 or > 64
            || QueueMessages is < 1 or > 1024 || QueueBytes < MessageBytes || QueueBytes > 16 * 1024 * 1024
            || PendingRequests is < 1 or > 256 || Routes is < 1 or > 1024
            || MessageFragments is < 1 or > 65536 || MessagesPerSecond is < 1 or > 4096)
            throw new ArgumentException("Invalid companion resource budgets.");
        foreach (var timeout in new[] { HandshakeTimeout, RequestTimeout, HeartbeatInterval, HeartbeatTimeout, CloseTimeout })
            if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
                throw new ArgumentException("Companion timeouts must be positive and at most five minutes.");
    }
}
