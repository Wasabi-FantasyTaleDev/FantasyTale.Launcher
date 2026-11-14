namespace FantasyTale.Launcher.Core.Hooks;

/// <summary>Reports rejection before a hook request acquired the channel; no bytes from that request were submitted.</summary>
public sealed class HookChannelBusyException : InvalidOperationException
{
    /// <summary>Creates an admission rejection for an already occupied hook channel.</summary>
    public HookChannelBusyException() : base("A hook request is already in progress.") { }
}
