namespace FantasyTale.Launcher.Core.Updates;

/// <summary>Editable ordered update source configuration, without credentials.</summary>
/// <param name="Name">Printable display name.</param>
/// <param name="Kind">HTTP or AList transport identifier.</param>
/// <param name="BaseUrl">Public root address.</param>
/// <param name="FeedPath">Relative release index path.</param>
/// <param name="MinimumRevision">Installation baseline revision that discovery cannot downgrade.</param>
/// <param name="SignaturePath">Optional detached index signature path.</param>
/// <param name="SignaturePublicKey">Optional trusted RSA public key.</param>
public sealed record LauncherUpdateSourceSettings(string Name, string Kind, string BaseUrl, string FeedPath,
    long MinimumRevision = 0, string? SignaturePath = null, string? SignaturePublicKey = null)
{
    /// <summary>Connects plain HTTP requests through DNS-resolved IP addresses when enabled.</summary>
    public bool PreferIp { get; init; }
}

/// <summary>Safe outcome of checking one source, including partial failures when another source succeeds.</summary>
/// <param name="Name">Configured display name, without a private address.</param>
/// <param name="State">Discovery outcome and allowlisted diagnostic fields.</param>
public sealed record LauncherUpdateSourceResult(string Name, LauncherUpdateState State);
