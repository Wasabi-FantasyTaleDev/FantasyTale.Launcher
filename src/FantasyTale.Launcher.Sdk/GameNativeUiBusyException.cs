namespace FantasyTale.Launcher.Sdk;

/// <summary>Reports a native UI capability or result query that was not submitted because its channel was occupied.</summary>
/// <remarks>A rejected result query does not change the outcome of the earlier operation being queried; only that query may be retried.</remarks>
public sealed class GameNativeUiBusyException : InvalidOperationException
{
    /// <summary>Creates a transient native UI query admission rejection without reporting an operation outcome.</summary>
    public GameNativeUiBusyException() : base("The native UI channel is busy; this query was not submitted.") { }
}
