using System.Text.RegularExpressions;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Instances;

/// <summary>Maps a stored client name that is a retired default build name to the build's current name.</summary>
public static partial class InstanceNames
{
    /// <summary>
    /// Returns the build's current name when <paramref name="storedName"/> equals a default name that carried the build's link-time (UTC)
    /// date instead of the displayed Korea-time date; any other name, including one the user typed, is returned unchanged.
    /// </summary>
    public static string Current(string storedName, BuildDefinition? build)
    {
        if (build?.BuildDate is not { } linked) return storedName;
        var utcDate = linked.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var current in build.Name.Values.Values)
        {
            var retired = DatePattern().Replace(current, utcDate);
            if (retired != current && retired.Equals(storedName, StringComparison.Ordinal)) return current;
        }
        return storedName;
    }

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
    private static partial Regex DatePattern();
}
