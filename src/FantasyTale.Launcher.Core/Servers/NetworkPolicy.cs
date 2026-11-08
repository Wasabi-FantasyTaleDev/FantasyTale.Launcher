using System.Net;
using System.Net.Sockets;
using FantasyTale.Launcher.Sdk;
using FantasyTale.Launcher.Downloads;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Applies explicit server routing requirements before local server and global preferences.</summary>
public sealed class NetworkPolicy
{
    private readonly Func<string> _preference;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private static readonly Lazy<IPNetwork[]> ChinaNetworks = new(() =>
    {
        using var stream = typeof(NetworkPolicy).Assembly.GetManifestResourceStream("FantasyTale.Launcher.Core.Servers.china-networks.txt")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('#')).Select(IPNetwork.Parse).ToArray();
    });

    /// <summary>Creates a policy with optional deterministic DNS resolution for transport tests.</summary>
    public NetworkPolicy(Func<string>? preference = null, Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
    {
        _preference = preference ?? (() => "china");
        _resolve = resolve ?? DnsResolution.ResolveAsync;
    }

    internal string InitialDiscoveryPolicy(ServerDefinition server) =>
        (server.PreferIp ?? _preference() == "all") ? "ip" : "domain";

    /// <summary>Creates a bounded API transport that directly connects HTTP IP authorities while retaining the configured proxy for domain names and HTTPS.</summary>
    public static SocketsHttpHandler CreateHttpHandler(IWebProxy? proxy = null) => CreateTransport(proxy, allowRedirect: false);

    /// <summary>Creates a source and download transport that permits bounded redirects while preserving IP routing.</summary>
    public static SocketsHttpHandler CreateDownloadHttpHandler(IWebProxy? proxy = null) => CreateTransport(proxy, allowRedirect: true);

    private static SocketsHttpHandler CreateTransport(IWebProxy? proxy, bool allowRedirect) => new()
    {
        AllowAutoRedirect = allowRedirect,
        MaxAutomaticRedirections = 5,
        UseCookies = false,
        MaxConnectionsPerServer = 4,
        ConnectTimeout = TimeSpan.FromSeconds(15),
        Proxy = new IpAccessProxy(proxy ?? HttpClient.DefaultProxy)
    };

    private sealed class IpAccessProxy(IWebProxy upstream) : IWebProxy
    {
        private static bool Direct(Uri destination) => destination.Scheme == "http" && IPAddress.TryParse(destination.DnsSafeHost, out _);
        public ICredentials? Credentials { get => upstream.Credentials; set => upstream.Credentials = value; }
        public Uri? GetProxy(Uri destination) => Direct(destination) ? destination : upstream.GetProxy(destination);
        public bool IsBypassed(Uri destination) => Direct(destination) || upstream.IsBypassed(destination);
    }

    /// <summary>Rewrites plain HTTP authority to the selected address; HTTPS preserves certificate name validation.</summary>
    public async Task<Uri> RequestUriAsync(Uri uri, ServerDefinition server, string? requirement, string? region, CancellationToken ct)
    {
        if (uri.Scheme != "http") return uri;
        var host = await HostAsync(uri.DnsSafeHost, server, requirement, region, ct).ConfigureAwait(false);
        return host == uri.DnsSafeHost ? uri : new UriBuilder(uri) { Host = host }.Uri;
    }

    /// <summary>Routes an HTTP address handed to the game like the launcher's own requests, keeping its exact text when the
    /// host stays; a mainland host answers plain HTTP for an unregistered domain with its own registration block.</summary>
    public async Task<string> RouteAsync(string url, ServerDefinition server, string? requirement, string? region, CancellationToken ct)
    {
        var original = new Uri(url);
        var routed = await RequestUriAsync(original, server, requirement, region, ct).ConfigureAwait(false);
        if (routed.Authority == original.Authority) return url;
        var authorityStart = url.IndexOf("://", StringComparison.Ordinal) + 3;
        var authorityEnd = url.IndexOfAny(['/', '?', '#'], authorityStart);
        if (authorityEnd < 0) authorityEnd = url.Length;
        return url[..authorityStart] + routed.Authority + url[authorityEnd..];
    }

    /// <summary>Resolves a game or HTTP hostname according to the effective policy, preferring IPv4 for legacy clients.</summary>
    public async Task<string> HostAsync(string host, ServerDefinition server, string? requirement, string? region, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out _)) return host;
        var preference = _preference();
        var forced = requirement switch { "ip" => true, "domain" => false, _ => server.PreferIp };
        if (forced == false || forced is null && preference == "disabled") return host;
        IPAddress[] addresses;
        try { addresses = await _resolve(host, ct).ConfigureAwait(false); }
        catch (ServerFailureException) { throw; }
        catch (SocketException error) { throw new ServerFailureException("network_error", transportCode: error.SocketErrorCode.ToString()); }
        catch (IOException) { throw new ServerFailureException("network_error", transportCode: "NameResolutionError"); }
        catch (HttpRequestException error) { throw new ServerFailureException("network_error", error.StatusCode, error.HttpRequestError.ToString()); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ServerFailureException("network_timeout", transportCode: "timeout"); }
        var eligible = addresses.Where(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray();
        if (eligible.Length == 0) throw new ServerFailureException("network_error", transportCode: "NameResolutionError");
        var knownRegion = region;
        if (forced == true || preference == "all" || knownRegion == "CN") return eligible[0].ToString();
        if (knownRegion is not null) return host;
        var china = eligible.FirstOrDefault(a => ChinaNetworks.Value.Any(n => n.Contains(a)));
        return china?.ToString() ?? host;
    }
}
