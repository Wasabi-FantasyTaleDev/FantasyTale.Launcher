using System.Text;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Downloads;

/// <summary>Fetches metadata through any source adapter without forwarding API credentials.</summary>
public static class SourceDocumentReader
{
    /// <summary>Reads a UTF-8 catalog using GET; temporary direct URLs stay inside the transport.</summary>
    public static async Task<string> ReadAsync(HttpClient http, IDownloadSource source, string path, CancellationToken cancellationToken)
        => new UTF8Encoding(false, true).GetString(await ReadBytesAsync(http, source, path, cancellationToken).ConfigureAwait(false)).TrimStart('\uFEFF');

    /// <summary>Reads exact metadata bytes with bounded transport retries and one shared thirty-second deadline.</summary>
    public static async Task<byte[]> ReadBytesAsync(HttpClient http, IDownloadSource source, string path, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        cancellationToken = deadline.Token;
        for (var attempt = 0; ; attempt++)
        {
            try { return await ReadOnceAsync(http, source, path, cancellationToken).ConfigureAwait(false); }
            catch (HttpRequestException error) when (attempt < 3 && !cancellationToken.IsCancellationRequested &&
                (error.StatusCode is null || (int)error.StatusCode is 408 or 429 or 500 or 502 or 503 or 504))
            {
                // Re-resolve each attempt: storage URLs can expire or move after an upstream failure.
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<byte[]> ReadOnceAsync(HttpClient http, IDownloadSource source, string path, CancellationToken cancellationToken)
    {
        var resolved = await source.ResolveAsync(path, cancellationToken).ConfigureAwait(false);
        if (resolved.Url.Scheme is not ("http" or "https") || resolved.Url.UserInfo.Length != 0)
            throw new InvalidDataException("Source returned an invalid download URL.");
        // Metadata is public. Never copy an AList API token to the storage provider.
        using var request = new HttpRequestMessage(HttpMethod.Get, resolved.Url);
        foreach (var header in resolved.Headers)
        {
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) request.Headers.Host = header.Value;
            else if (header.Key.Equals("Referer", StringComparison.OrdinalIgnoreCase)) request.Headers.Referrer = new Uri(header.Value);
            else throw new InvalidDataException("Unsupported source request header.");
        }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await ReadContentAsync(response.Content, cancellationToken).ConfigureAwait(false);
        if (resolved.Size is { } size && bytes.LongLength != size)
            throw new InvalidDataException("Source document length does not match its metadata.");
        return bytes;
    }

    internal static async Task<byte[]> ReadContentAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        await stream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return output.ToArray();
    }
}
