namespace FantasyTale.Launcher.Sdk;

/// <summary>
/// Text in several UI languages, keyed by BCP 47 tag (<c>en</c>, <c>zh-CN</c>, <c>zh-TW</c>, <c>ko</c>,
/// <c>id</c>, <c>vi</c>). Definitions require an <c>en</c> entry, which is the fallback.
/// </summary>
public sealed class LocalizedText
{
    private readonly IReadOnlyDictionary<string, string> _values;

    /// <summary>Creates the text from its per-language values; <paramref name="values"/> must contain <c>en</c>.</summary>
    /// <exception cref="ArgumentException">The values have no <c>en</c> entry.</exception>
    public LocalizedText(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!values.ContainsKey("en"))
            throw new ArgumentException("Localized text needs an 'en' entry.", nameof(values));
        _values = values;
    }

    /// <summary>All languages and their text.</summary>
    public IReadOnlyDictionary<string, string> Values => _values;

    /// <summary>
    /// The text for <paramref name="language"/>: the exact tag, then the tag's language part
    /// (<c>zh-Hant-TW</c> tries <c>zh</c>), then <c>en</c>.
    /// </summary>
    public string Resolve(string language)
    {
        if (_values.TryGetValue(language, out var exact)) return exact;
        var dash = language.IndexOf('-', StringComparison.Ordinal);
        if (dash > 0 && _values.TryGetValue(language[..dash], out var neutral)) return neutral;
        return _values["en"];
    }
}
