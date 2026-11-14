namespace FantasyTale.Launcher.Core.Plugins;

/// <summary>
/// The launcher's current UI language, which language-scoped client policy rules follow. The desktop launcher has one UI
/// language; its page reports it at start and on every change. Null until reported, when no language-scoped rule applies.
/// </summary>
public static class LauncherLanguage
{
    private static string? _current;

    /// <summary>The reported language tag, such as <c>zh-CN</c>, or null.</summary>
    public static string? Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value is { Length: > 0 and <= 35 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? value : null);
    }
}
