using System.Net;
using FantasyTale.Launcher.Downloads;

namespace FantasyTale.Launcher.Core.Servers;

internal static class DnsResolution
{
    internal static Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => HostAddressResolver.ResolveAsync(host, ct);
}
