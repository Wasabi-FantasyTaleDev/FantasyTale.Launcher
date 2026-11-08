using System.Reflection;

namespace FantasyTale.Launcher.Core.Definitions;

/// <summary>Uses the packaged application SemVer consistently for API reporting and minimum-version gates.</summary>
public static class LauncherCompatibility
{
    /// <summary>The running release version, excluding informational source metadata.</summary>
    public static string CurrentVersion { get; } = typeof(LauncherCompatibility).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? throw new InvalidOperationException("The launcher release version is unavailable.");

    /// <summary>Validates a bounded optional minimum; omitted requirements impose no gate.</summary>
    public static void ValidateMinimum(string? minimum)
    {
        if (minimum is null or "") return;
        try
        {
            if (minimum.Length > 128) throw new FormatException();
            _ = PluginVersion.Parse(minimum);
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        { throw new InvalidDataException("Invalid minimum launcher semantic version.", error); }
    }

    /// <summary>Compares numeric SemVer components and prerelease precedence, ignoring build metadata.</summary>
    public static bool Supports(string? minimum, string? current = null)
    {
        ValidateMinimum(minimum);
        return string.IsNullOrEmpty(minimum) || PluginVersion.Parse(current ?? CurrentVersion).CompareTo(PluginVersion.Parse(minimum)) >= 0;
    }

    /// <summary>Blocks an operation whose declared minimum is newer than the running release.</summary>
    public static void Require(string? minimum, string subject, string? current = null)
    {
        if (!Supports(minimum, current)) throw new LauncherUpgradeRequiredException(minimum!, current ?? CurrentVersion, subject);
    }
}

/// <summary>A structured, non-secret compatibility rejection that the frontend can route into updates.</summary>
public sealed class LauncherUpgradeRequiredException(string minimum, string current, string subject)
    : InvalidOperationException($"Launcher {minimum} or newer is required; the current version is {current}.")
{
    /// <summary>The validated minimum version requested by the plugin or server.</summary>
    public string MinimumVersion { get; } = minimum;
    /// <summary>The running release used for the compatibility check.</summary>
    public string CurrentVersion { get; } = current;
    /// <summary>The plugin id or server name whose operation was blocked.</summary>
    public string Subject { get; } = subject;
}
