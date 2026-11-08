namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Opaque account-selection snapshot that binds asynchronous launch preparation to its original server session.</summary>
public sealed class AccountLaunchSelection
{
    internal AccountLaunchSelection(LauncherApiClient client, string serverId, object? owner, long revision, Guid? identity)
    { Client = client; ServerId = serverId; Owner = owner; Revision = revision; Identity = identity; }

    internal LauncherApiClient Client { get; }
    internal string ServerId { get; }
    internal object? Owner { get; }
    internal long Revision { get; }
    internal Guid? Identity { get; }
}
