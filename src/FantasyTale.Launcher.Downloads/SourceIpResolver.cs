using System.Net;
using System.Net.Sockets;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Downloads;

internal static class SourceIpResolver
{
    public static async Task<ResolvedDownload> ResolveAsync(Uri uri, bool preferIp, CancellationToken cancellationToken)
    {
        if (!preferIp || uri.Scheme != Uri.UriSchemeHttp || IPAddress.TryParse(uri.Host, out _))
            return new ResolvedDownload(uri, new Dictionary<string, string>(), null);

        var addresses = await HostAddressResolver.ResolveAsync(uri.DnsSafeHost, cancellationToken).ConfigureAwait(false);
        var address = addresses.FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.FirstOrDefault() ?? throw new HttpRequestException("The source hostname has no IP addresses.");
        var resolved = new UriBuilder(uri) { Host = address.ToString() }.Uri;
        return new ResolvedDownload(resolved, new Dictionary<string, string>(), null);
    }
}
