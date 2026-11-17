using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>Fingerprints the grants a package asks of the launcher, so that a version which changes them needs fresh consent.</summary>
public static class PluginGrants
{
    /// <summary>
    /// Returns the lower-case SHA-256 of the package's kind, its launcher service provider and messages, its companion
    /// message grants and its requirements. Options, files, art and labels are not grants and do not change it.
    /// </summary>
    public static string Fingerprint(PluginDefinition package)
    {
        var lines = new List<string> { "kind=" + package.Kind };
        if (package.Service is { } service)
        {
            lines.Add("service=" + service.Provider);
            lines.AddRange(service.Messages.Select(pair => "service-message=" + pair.Key + ":" + pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        lines.AddRange(package.CompanionMessages.Select(pair => "companion-message=" + pair.Key + ":" + pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        lines.AddRange(package.Requires.Select(requirement => "requires=" + requirement.Id + " " + requirement.Range));
        lines.Sort(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines)))).ToLowerInvariant();
    }
}
