using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Multiplayer;

/// <summary>Bounded directory transport with server-directed IP policy and no credential-bearing redirects.</summary>
public sealed class DirectoryClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 12 };
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly NetworkPolicy _network;
    private readonly object _gate = new();
    private readonly Dictionary<string, DirectoryServiceInfo> _info = new(StringComparer.Ordinal);

    /// <summary>Creates an API client; an injected HTTP client must reject redirects and remains caller-owned.</summary>
    public DirectoryClient(HttpClient? http = null, NetworkPolicy? network = null)
    {
        _ownsHttp = http is null;
        _http = http ?? new HttpClient(NetworkPolicy.CreateHttpHandler()) { Timeout = TimeSpan.FromSeconds(10) };
        _network = network ?? new NetworkPolicy();
    }

    /// <summary>Reads source capabilities without credentials and remembers an explicitly required access policy.</summary>
    public async Task<DirectoryServiceInfo> InfoAsync(DirectorySource source, CancellationToken ct = default)
    {
        var url = Base(source); DirectoryServiceInfo? previous;
        lock (_gate) previous = _info.GetValueOrDefault(url);
        DirectoryServiceInfo info;
        try { info = await SendAsync<DirectoryServiceInfo>(source, HttpMethod.Get, "v1/info", null, null, previous?.HostPolicy == "ip" ? "ip" : "domain", ct).ConfigureAwait(false); }
        catch (Exception e) when (!ct.IsCancellationRequested && previous?.HostPolicy is not ("ip" or "domain") &&
            new Uri(url).Scheme == "http" && !IPAddress.TryParse(new Uri(url).DnsSafeHost, out _) && e is HttpRequestException or InvalidDataException or TaskCanceledException)
        { info = await SendAsync<DirectoryServiceInfo>(source, HttpMethod.Get, "v1/info", null, null, "ip", ct).ConfigureAwait(false); }
        if (info.Schema != 1 || info.RoomLeaseSeconds is < 30 or > 300 || info.HostPolicy is not ("auto" or "ip" or "domain") ||
            info.Region is not null && (info.Region.Length != 2 || !info.Region.All(char.IsAsciiLetterUpper))) throw new InvalidDataException("Invalid directory capabilities.");
        DirectoryValidation.Text(info.Name, 128);
        lock (_gate)
        {
            _info[url] = info;
        }
        return info;
    }

    /// <summary>Loads and validates public entries; expired rooms are removed even from a stale response.</summary>
    public async Task<DirectoryListing> ListAsync(DirectorySource source, CancellationToken ct = default)
    {
        _ = await InfoAsync(source, ct).ConfigureAwait(false);
        var listing = await SendAsync<DirectoryListing>(source, HttpMethod.Get, "v1/entries", null, null, null, ct).ConfigureAwait(false);
        if (listing.Schema != 1 || listing.Entries is null) throw new InvalidDataException("Invalid directory listing.");
        foreach (var entry in listing.Entries)
        {
            if (entry is null) throw new InvalidDataException("Missing directory entry.");
            DirectoryValidation.Id(entry.Id); _ = DirectoryValidation.Normalize(entry.Details);
            if (entry.Kind is not ("server" or "room") || entry.Kind == "room" && (entry.ExpiresAt is null || entry.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(6)) || entry.Kind == "server" && entry.ExpiresAt is not null)
                throw new InvalidDataException("Invalid directory entry lifetime.");
        }
        if (listing.Entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != listing.Entries.Count) throw new InvalidDataException("Duplicate directory identities.");
        return listing with { Entries = listing.Entries.Where(e => e.ExpiresAt is null || e.ExpiresAt > DateTimeOffset.UtcNow).ToArray() };
    }

    /// <summary>Submits metadata for review; success does not mean the server is publicly approved.</summary>
    public async Task<DirectorySubmission> SubmitAsync(DirectorySource source, DirectoryAdvertisement details, CancellationToken ct = default)
    {
        details = DirectoryValidation.Normalize(details); _ = await InfoAsync(source, ct).ConfigureAwait(false);
        var result = await SendAsync<DirectorySubmission>(source, HttpMethod.Post, "v1/submissions", details, null, null, ct).ConfigureAwait(false);
        DirectoryValidation.Id(result.Id); _ = DirectoryValidation.Normalize(result.Details);
        return result;
    }

    /// <summary>Publishes a temporary room, returning a private management token to backend code only.</summary>
    public async Task<DirectoryLease> PublishAsync(DirectorySource source, DirectoryAdvertisement details, CancellationToken ct = default)
    {
        details = DirectoryValidation.Normalize(details); _ = await InfoAsync(source, ct).ConfigureAwait(false);
        return Lease(await SendAsync<DirectoryLease>(source, HttpMethod.Post, "v1/rooms", details, null, null, ct).ConfigureAwait(false));
    }

    /// <summary>Renews one source-bound room without changing its public metadata or management identity.</summary>
    public async Task<DirectoryLease> RenewAsync(DirectorySource source, DirectoryLease lease, CancellationToken ct = default)
    {
        var result = Lease(await SendAsync<DirectoryLease>(source, HttpMethod.Post, "v1/rooms/" + EntryId(lease.Id) + "/heartbeat", null, lease.Token, null, ct).ConfigureAwait(false));
        if (result.Id != lease.Id || result.Token != lease.Token) throw new InvalidDataException("The directory changed room lease identity.");
        return result;
    }

    /// <summary>Withdraws one room; expiry or an earlier successful withdrawal counts as already absent.</summary>
    public async Task WithdrawAsync(DirectorySource source, DirectoryLease lease, CancellationToken ct = default)
    {
        try { _ = await SendAsync<JsonElement>(source, HttpMethod.Delete, "v1/rooms/" + EntryId(lease.Id), null, lease.Token, null, ct).ConfigureAwait(false); }
        catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound) { }
    }

    private static DirectoryLease Lease(DirectoryLease value)
    {
        DirectoryValidation.Id(value.Id);
        if (value.Token is null || value.Token.Length != 64 || !value.Token.All(char.IsAsciiHexDigit) || value.RenewAfterSeconds is < 1 or > 100 ||
            value.ExpiresAt <= DateTimeOffset.UtcNow || value.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(6)) throw new InvalidDataException("Invalid room lease response.");
        return value;
    }
    private static string EntryId(string id) { DirectoryValidation.Id(id); return id; }
    private static string Base(DirectorySource source) => ServerStore.ApiBase(source.BaseUrl ?? throw new InvalidOperationException("Configure this directory source address first.")).AbsoluteUri;
    private async Task<T> SendAsync<T>(DirectorySource source, HttpMethod method, string path, object? body, string? token, string? discoveryPolicy, CancellationToken ct)
    {
        var url = Base(source); DirectoryServiceInfo? policy;
        lock (_gate) policy = _info.GetValueOrDefault(url);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10)); ct = timeout.Token;
        var server = new ServerDefinition(source.Id, "directory", source.Name, url, null, false, source.BuiltIn);
        var uri = await _network.RequestUriAsync(new Uri(new Uri(url), path), server, discoveryPolicy ?? policy?.HostPolicy, policy?.Region, ct).ConfigureAwait(false);
        using var request = new HttpRequestMessage(method, uri);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Directory request failed (HTTP {(int)response.StatusCode}).", null, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.NoContent && typeof(T) == typeof(JsonElement)) return default!;
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        try { return await JsonSerializer.DeserializeAsync<T>(stream, Json, ct).ConfigureAwait(false) ?? throw new InvalidDataException("Empty directory response."); }
        catch (JsonException) { throw new InvalidDataException("Malformed directory response."); }
    }

    /// <summary>Releases the owned HTTP transport; no room credentials are written to disk.</summary>
    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
