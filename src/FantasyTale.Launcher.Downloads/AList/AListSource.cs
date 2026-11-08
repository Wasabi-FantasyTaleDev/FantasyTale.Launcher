using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Downloads.AList;

/// <summary>
/// Resolves files on an AList server. <c>POST /api/fs/get</c> returns the file's direct
/// <c>raw_url</c>; when a storage does not provide one, the signed proxy URL
/// <c>/d{path}?sign={sign}</c> is used instead.
/// </summary>
public sealed class AListSource : IDownloadSource
{
    private readonly HttpClient _http;
    private readonly Uri _baseUrl;
    private readonly string? _token;
    private readonly string? _password;
    private readonly bool _preferIp;

    /// <summary>Creates a source for the AList server at <paramref name="baseUrl"/>.</summary>
    /// <param name="http">HTTP client used for API calls.</param>
    /// <param name="baseUrl">Server root, for example <c>https://files.example.org/</c>.</param>
    /// <param name="token">API token for private storages, or null.</param>
    /// <param name="password">Folder password, or null.</param>
    /// <param name="preferIp">Connects to a resolved IP for plain HTTP.</param>
    public AListSource(HttpClient http, Uri baseUrl, string? token = null, string? password = null, bool preferIp = false)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(baseUrl);
        _http = http;
        _baseUrl = SourceAddress.BaseUrl(baseUrl.AbsoluteUri);
        _token = token;
        _password = password;
        _preferIp = preferIp;
    }

    /// <inheritdoc />
    public string Kind => "alist";

    /// <inheritdoc />
    public async Task<ResolvedDownload> ResolveAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        path = "/" + SourceAddress.RelativePath(path.StartsWith('/') ? path[1..] : path);
        // fs/get is read-only. Retry provider failures as well as HTTP failures:
        // AList may report an upstream outage inside a successful HTTP envelope.
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await ResolveOnceAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException error) when (attempt < 3 &&
                !cancellationToken.IsCancellationRequested &&
                (error.StatusCode is null || IsTransient((int)error.StatusCode)))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (attempt < 3 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(int code) => code is 408 or 429 or 500 or 502 or 503 or 504;

    private async Task<ResolvedDownload> ResolveOnceAsync(string path, CancellationToken cancellationToken)
    {
        // File transfers may use an infinite HTTP timeout; metadata must still bound headers and body reads.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        cancellationToken = deadline.Token;
        var apiRequest = await SourceIpResolver.ResolveAsync(new Uri(_baseUrl, "api/fs/get"), _preferIp, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, apiRequest.Url)
        {
            Content = JsonContent.Create(new GetRequest(path, _password ?? "")),
        };
        if (_token is not null) request.Headers.TryAddWithoutValidation("Authorization", _token);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = JsonSerializer.Deserialize<Envelope>(await SourceDocumentReader.ReadContentAsync(response.Content, cancellationToken).ConfigureAwait(false))
            ?? throw new InvalidDataException($"AList returned an empty reply for {path}.");
        if (body.Code == 500 && body.Message is { } message)
        {
            var parts = message.Trim().ToLowerInvariant().Split(": ", StringSplitOptions.None);
            if (parts[^1] == "object not found" && parts[..^1].All(part => part is "failed get parent list" or "failed get dir"))
                throw new HttpRequestException("AList source object was not found.", null, HttpStatusCode.NotFound);
        }
        if (IsTransient(body.Code))
            throw new HttpRequestException($"AList temporarily could not resolve {path}: {body.Code}.", null, (HttpStatusCode)body.Code);
        if (body.Code is >= 400 and <= 599)
            throw new HttpRequestException("AList rejected the metadata request.", null, (HttpStatusCode)body.Code);
        if (body.Code != 200 || body.Data is null)
            throw new InvalidDataException($"AList could not resolve {path}: {body.Code} {body.Message}");
        if (body.Data.IsDir) throw new InvalidDataException($"AList path {path} is a folder.");

        Uri url = !string.IsNullOrEmpty(body.Data.RawUrl)
            ? new Uri(body.Data.RawUrl, UriKind.Absolute)
            : new Uri(_baseUrl, "d" + Uri.EscapeDataString(path).Replace("%2F", "/", StringComparison.Ordinal)
                + (string.IsNullOrEmpty(body.Data.Sign) ? "" : "?sign=" + Uri.EscapeDataString(body.Data.Sign)));
        if (url.Scheme is not ("http" or "https") || url.UserInfo.Length != 0 || body.Data.Size < 0)
            throw new InvalidDataException("AList returned invalid download metadata.");
        var download = await SourceIpResolver.ResolveAsync(url, _preferIp, cancellationToken).ConfigureAwait(false);
        return new ResolvedDownload(download.Url, download.Headers, body.Data.Size);
    }

    private sealed record GetRequest([property: JsonPropertyName("path")] string Path, [property: JsonPropertyName("password")] string Password);

    private sealed record Envelope(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("data")] FileInfo? Data);

    private sealed record FileInfo(
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("is_dir")] bool IsDir,
        [property: JsonPropertyName("raw_url")] string? RawUrl,
        [property: JsonPropertyName("sign")] string? Sign);
}
