namespace FantasyTale.Launcher.Web;

/// <summary>Matches browser event sources to one startup document while allowing its hash routes.</summary>
internal sealed class TrustedWebDocument
{
    private readonly Uri _document;

    internal TrustedWebDocument(string document)
    {
        if (!TryParse(document, out var parsed))
            throw new ArgumentException("The trusted document must be an unambiguous HTTP(S) URL without credentials.", nameof(document));
        _document = parsed!;
    }

    internal bool Allows(string? source) => TryParse(source, out var candidate) &&
        candidate!.Scheme == _document.Scheme &&
        string.Equals(candidate.Host, _document.Host, StringComparison.OrdinalIgnoreCase) &&
        candidate.Port == _document.Port &&
        candidate.AbsolutePath == _document.AbsolutePath && candidate.Query == _document.Query;

    private static bool TryParse(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrEmpty(value) || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || value.Contains('\\')) return false;
        // Reject separators before URI normalization can change the path checked against the startup document.
        var authority = value.IndexOf("://", StringComparison.Ordinal);
        if (authority < 0) return false;
        var end = value.IndexOfAny(['?', '#'], authority + 3);
        var path = end < 0 ? value.AsSpan(authority + 3) : value.AsSpan(authority + 3, end - authority - 3);
        if (path.Contains("%2f", StringComparison.OrdinalIgnoreCase) || path.Contains("%5c", StringComparison.OrdinalIgnoreCase)) return false;
        return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
            uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.Host.Length != 0;
    }
}
