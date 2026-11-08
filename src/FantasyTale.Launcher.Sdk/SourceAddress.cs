namespace FantasyTale.Launcher.Sdk;

/// <summary>Validates transport roots and unescaped virtual paths shared by source adapters.</summary>
public static class SourceAddress
{
    /// <summary>Returns an HTTP(S) directory URL without credentials, query or fragment.</summary>
    public static Uri BaseUrl(string value)
    {
        if (value.Length > 2048 || value.Any(char.IsControl) || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Source URL must use HTTP(S) without credentials, query or fragment.");
        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    /// <summary>Rejects traversal, URLs and ambiguous encoded paths before either adapter resolves a file.</summary>
    public static string RelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(char.IsControl) || value.IndexOfAny(['\\', ':', '?', '#', '%']) >= 0 ||
            value.Split('/').Any(part => part.Length == 0 || part is "." or ".." || part != part.Trim()))
            throw new ArgumentException("Source paths must be relative, unescaped paths without empty or traversal segments.");
        return value;
    }
}
