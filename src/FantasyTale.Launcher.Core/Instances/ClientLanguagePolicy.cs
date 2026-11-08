using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Instances;

/// <summary>Resolves distribution languages and verified translation selections without changing the native identity.</summary>
public static class ClientLanguagePolicy
{
    /// <summary>Returns the distribution's native language, with a fallback for earlier profiles.</summary>
    public static string Native(GameDefinition game, BuildDefinition build) => build.NativeLanguage.Length > 0
        ? build.NativeLanguage : game.ClientLanguages.Count > 0 ? game.ClientLanguages[0] : "";

    /// <summary>Returns the native language followed by additional languages declared by this distribution.</summary>
    public static IReadOnlyList<string> Supported(GameDefinition game, BuildDefinition build) =>
        new[] { Native(game, build) }
            .Concat(build.AdditionalLanguages.Keys).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>Tests whether a translation supplies restorable file replacements for this client and launcher.</summary>
    public static bool IsTranslationPackage(BuildDefinition build, PluginDefinition package) =>
        package.Kind == "files" && package.RestoreFiles &&
        package.Files.Any(file => file.Builds.Count == 0 || PluginCatalog.Matches(file.Builds, build)) &&
        PluginCatalog.IsAvailable(package, build) && LauncherCompatibility.Supports(package.MinimumLauncherVersion);

    /// <summary>Rejects undeclared or unavailable translations before requesting credentials or modifying client files.</summary>
    public static void RequireAvailable(GameDefinition game, BuildDefinition build, InstanceDefinition instance, IReadOnlyList<PluginDefinition> packages)
    {
        var language = instance.Language ?? Native(game, build);
        if (language.Length == 0) return;
        if (!Supported(game, build).Contains(language, StringComparer.Ordinal)) throw new InvalidOperationException("Unsupported client language.");
        if (!build.AdditionalLanguages.TryGetValue(language, out var id)) return;
        if (!instance.Plugins.Any(selection => selection.Id == id && selection.Enabled && packages.Any(package =>
            package.Id == id && package.Version == selection.Version && package.Game == game.Id && IsTranslationPackage(build, package))))
            throw new InvalidOperationException("client_language_pack_unavailable");
    }

    /// <summary>Excludes translations for other languages while retaining unrelated selected plugins.</summary>
    public static InstanceDefinition ForLaunch(GameDefinition game, BuildDefinition build, InstanceDefinition instance) => instance with
    {
        Plugins = instance.Plugins.Where(selection => !build.AdditionalLanguages.Values.Contains(selection.Id, StringComparer.Ordinal) ||
            build.AdditionalLanguages.GetValueOrDefault(instance.Language ?? Native(game, build)) == selection.Id).ToArray(),
    };
}
