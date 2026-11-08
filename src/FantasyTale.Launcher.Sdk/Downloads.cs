namespace FantasyTale.Launcher.Sdk;

/// <summary>
/// Turns a path inside a download source into a URL that can be fetched. Manifests name a
/// source id and a path, so files can move between HTTP servers, AList and other kinds of
/// source without changing the manifest format.
/// </summary>
public interface IDownloadSource
{
    /// <summary>Source kind as written in <c>sources.toml</c> (<c>http</c>, <c>alist</c>).</summary>
    string Kind { get; }

    /// <summary>Resolves <paramref name="path"/> to a fetchable URL.</summary>
    Task<ResolvedDownload> ResolveAsync(string path, CancellationToken cancellationToken);
}

/// <summary>A fetchable file.</summary>
/// <param name="Url">Absolute URL.</param>
/// <param name="Headers">Request headers the URL requires (for example a referer), possibly empty.</param>
/// <param name="Size">Size in bytes when the source reports it, or null.</param>
public sealed record ResolvedDownload(Uri Url, IReadOnlyDictionary<string, string> Headers, long? Size);

/// <summary>A file to download and verify.</summary>
/// <param name="Source">Where to fetch it.</param>
/// <param name="Destination">Absolute final path. Engines write to a temporary file and move it into place only after verification.</param>
/// <param name="Size">Expected size in bytes.</param>
/// <param name="Sha256">Expected SHA-256 as lower-case hex.</param>
public sealed record DownloadRequest(ResolvedDownload Source, string Destination, long Size, string Sha256);

/// <summary>Progress of one download.</summary>
/// <param name="Received">Bytes received so far, including bytes resumed from an earlier attempt.</param>
/// <param name="Total">Expected total bytes.</param>
/// <param name="BytesPerSecond">Recent transfer rate.</param>
public sealed record DownloadProgress(long Received, long Total, double BytesPerSecond);

/// <summary>
/// Downloads files with resume support. The default engine drives aria2 over JSON-RPC; the
/// fallback uses HTTP range requests.
/// </summary>
public interface IDownloadEngine
{
    /// <summary>Engine name shown in settings (<c>aria2</c>, <c>http</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Downloads and verifies <paramref name="request"/>. Throws <see cref="InvalidDataException"/>
    /// when the size or hash does not match; the destination is then left untouched.
    /// </summary>
    Task DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken);
}
