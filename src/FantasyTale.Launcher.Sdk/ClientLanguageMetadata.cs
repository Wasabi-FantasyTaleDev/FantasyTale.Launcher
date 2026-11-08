namespace FantasyTale.Launcher.Sdk;

/// <summary>Display-language context attached to the existing launch-ticket request.</summary>
/// <param name="LauncherLanguage">Current launcher interface language.</param>
/// <param name="NativeLanguage">Language shipped in the unmodified client distribution.</param>
/// <param name="ClientLanguage">Language selected for this launch, including an installed translation.</param>
public sealed record ClientLanguageMetadata(string LauncherLanguage, string NativeLanguage, string ClientLanguage);
