using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace FantasyTale.Launcher.Downloads;

/// <summary>Resolves real endpoint addresses, replacing synthetic proxy DNS answers with public DNS results.</summary>
public static class HostAddressResolver
{
    private static readonly HttpClient Resolver = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly IPNetwork Synthetic = IPNetwork.Parse("198.18.0.0/15");

    /// <summary>Returns usable endpoint IP addresses with a bounded fallback for synthetic DNS answers.</summary>
    /// <remarks>
    /// When the system resolver returns only addresses in the benchmark range used by proxy fake-IP
    /// modes (198.18.0.0/15), the host is resolved again over DNS-over-HTTPS. Cancellation is always
    /// reported as <see cref="OperationCanceledException"/>.
    /// </remarks>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="IOException">The public resolver failed or returned no usable address.</exception>
    public static async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
        }
        catch (SocketException) when (ct.IsCancellationRequested)
        {
            // Windows can surface a cancelled GetAddrInfoEx lookup as WSA_E_CANCELLED or a
            // WSALookupServiceEnd race instead of a cancellation exception.
            throw new OperationCanceledException(ct);
        }
        if (!addresses.Any(Synthetic.Contains)) return addresses;
        // Proxy-generated benchmark addresses cannot be sent to a game as its public destination.
        foreach (var type in new[] { 1, 28 })
        {
            using var response = await Resolver.GetAsync($"https://dns.alidns.com/resolve?name={Uri.EscapeDataString(host)}&type={type}", HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(32768, ct).ConfigureAwait(false);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), new JsonDocumentOptions { MaxDepth = 8 });
            if (!json.RootElement.TryGetProperty("Status", out var status) || status.GetInt32() != 0) throw new IOException("Public DNS resolution failed.");
            if (!json.RootElement.TryGetProperty("Answer", out var answers)) continue;
            var result = answers.EnumerateArray().Take(64).Where(a => a.GetProperty("type").GetInt32() == type)
                .Select(a => IPAddress.Parse(a.GetProperty("data").GetString()!)).Where(a => !Synthetic.Contains(a)).ToArray();
            if (result.Length > 0) return result;
        }
        throw new IOException("Public DNS returned no usable server address.");
    }
}
