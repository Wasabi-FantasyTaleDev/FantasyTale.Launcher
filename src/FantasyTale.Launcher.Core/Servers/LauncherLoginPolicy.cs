namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Combines server sign-in preferences with the selected client's credential requirement.</summary>
public static class LauncherLoginPolicy
{
    /// <summary>Preserves required sign-in except for a room explicitly advertising name-only login.</summary>
    public static string Resolve(string? buildPolicy, string gamePolicy, string? serverPolicy = null, bool room = false) =>
        room && serverPolicy == "none" ? "none" : buildPolicy == "required" ? "required" : serverPolicy ?? buildPolicy ?? gamePolicy;
}
