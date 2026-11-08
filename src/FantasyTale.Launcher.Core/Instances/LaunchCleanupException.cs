namespace FantasyTale.Launcher.Core.Instances;

/// <summary>Identifies the failed cleanup stage without exposing process or session contents to the UI.</summary>
public sealed class LaunchCleanupException : IOException
{
    /// <summary>Creates a failure with the last confirmed process state and a stable cleanup stage.</summary>
    public LaunchCleanupException(string phase, bool exitConfirmed, Exception innerException, int? exitCode = null)
        : base("Client cleanup failed during " + phase + ".", innerException)
    {
        Phase = phase;
        ExitConfirmed = exitConfirmed;
        ExitCode = exitConfirmed ? exitCode : null;
    }

    /// <summary>The cleanup operation that failed, separate from native activation readiness.</summary>
    public string Phase { get; }

    /// <summary>Whether the owning game process was confirmed to have exited before cleanup.</summary>
    public bool ExitConfirmed { get; }

    /// <summary>The observed game process exit code, when available, independent of the cleanup failure.</summary>
    public int? ExitCode { get; }
}
