namespace FantasyTale.Launcher.Core.Instances;

/// <summary>A live operation owns the client's exclusive cross-process lease.</summary>
public sealed class InstanceBusyException : IOException
{
    /// <summary>Preserves the operating system's sharing failure and the local lease context for the details dialog.</summary>
    public InstanceBusyException(IOException cause, string? lockPath = null, string? instanceId = null, string? clientPath = null) : base("instance_in_use", cause)
    { HResult = cause.HResult; LockPath = lockPath; InstanceId = instanceId; ClientPath = clientPath; }

    /// <summary>The exclusive lease file whose sharing mode rejected this operation.</summary>
    public string? LockPath { get; }

    /// <summary>The client registration requested by the blocked operation, when known.</summary>
    public string? InstanceId { get; }

    /// <summary>The physical client directory protected by this lease, when known.</summary>
    public string? ClientPath { get; }
}
