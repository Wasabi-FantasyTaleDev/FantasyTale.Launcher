using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Multiplayer;

/// <summary>Reads server and room advertisements from the shared store snapshots without saving any destinations.</summary>
public sealed class StoreServerDiscovery(StoreCatalogCache catalogs, Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null)
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve = resolve ?? Dns.GetHostAddressesAsync;

    /// <summary>Creates a discovery selector whose transport and metadata remain owned by the configured store.</summary>
    public static DirectorySource Source(StoreSource source) => new("catalog-" + StoreSourceIdentity.Of(source)[..24], source.Name,
        source.BaseUrl, source.Enabled, true) { StoreSourceId = source.Id };

    /// <summary>Combines enabled sources in configured order, retaining usable offline snapshots when a refresh fails.</summary>
    public async Task<DirectoryListing> ListAsync(IReadOnlyList<StoreSource> sources, CancellationToken ct = default)
    {
        var rows = await Task.WhenAll(sources.Where(source => source.Enabled && source.BaseUrl is not null).Select(async source =>
        {
            StoreCatalog? catalog;
            try { catalog = await catalogs.GetAsync(source, cancellationToken: ct).ConfigureAwait(false); }
            catch (Exception error) when (!ct.IsCancellationRequested && error is IOException or HttpRequestException
                or DefinitionException or OperationCanceledException or InvalidDataException or InvalidOperationException)
            { catalog = catalogs.Cached(source); }
            return catalog?.Servers.Select(entry => entry with
            {
                Id = EntryId(source, entry.Id),
                StoreSourceId = source.Id,
            }).ToArray() ?? [];
        })).ConfigureAwait(false);
        return new(1, await DeduplicateAsync(rows.SelectMany(row => row).ToArray(), ct).ConfigureAwait(false));
    }

    /// <summary>Reads a selected server profile only for an explicit add operation, through the owning store's transport.</summary>
    public static async Task<ServerDefinition> DownloadProfileAsync(HttpClient http, Func<StoreSource, IDownloadSource> open,
        StoreSource source, DirectoryEntry entry, CancellationToken ct = default)
    {
        if (!source.Enabled || entry.Kind != "server" || entry.StoreSourceId != source.Id) throw new ArgumentException("Unavailable server profile source.");
        var details = DirectoryValidation.Normalize(entry.Details);
        if (entry.ProfilePath is null)
            return new(entry.Id, details.Game, details.Name, details.ApiUrl, details.Endpoint, true, false)
            { Presentation = details.Presentation, Builds = details.Builds, ClientPolicy = details.ClientPolicy };
        var text = await SourceDocumentReader.ReadAsync(http, open(source), entry.ProfilePath, ct).ConfigureAwait(false);
        var profile = ServerProfileFile.Parse(entry.ProfilePath, text);
        return ValidateProfile(profile, entry, source);
    }

    /// <summary>Checks a portable profile against its advertised identity and connection before publication or explicit import.</summary>
    public static ServerDefinition ValidateProfile(ServerDefinition profile, DirectoryEntry entry, StoreSource? source = null)
    {
        if (entry.Kind != "server") throw new ArgumentException("Only persistent servers have downloadable profiles.");
        var details = DirectoryValidation.Normalize(entry.Details);
        ServerStore.Validate(profile);
        if (profile.BuiltIn || profile.IconAsset is not null) throw new ArgumentException("Store profiles cannot claim bundled authority.");
        ServerMetadataPolicy.Validate(profile.Presentation);
        if ((source is null ? profile.Id : EntryId(source, profile.Id)) != entry.Id || profile.Game != details.Game || profile.ApiUrl is null
            || ServerStore.ApiBase(profile.ApiUrl) != ServerStore.ApiBase(details.ApiUrl) || profile.Endpoint != details.Endpoint)
            throw new InvalidDataException("Server profile identity or connection differs from its advertisement.");
        return profile with { Builds = profile.Builds.Count == 0 ? details.Builds : profile.Builds };
    }

    private static string EntryId(StoreSource source, string id) => "store-" + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(StoreSourceIdentity.Of(source) + "\n" + id)))[..32];

    /// <summary>Removes expired rooms and duplicate game IP/port pairs; DNS failure keeps the hostname visible.</summary>
    public async Task<IReadOnlyList<DirectoryEntry>> DeduplicateAsync(IReadOnlyList<DirectoryEntry> entries, CancellationToken ct = default)
    {
        var live = entries.Where(entry => entry.ExpiresAt is null || entry.ExpiresAt > DateTimeOffset.UtcNow).ToArray();
        static string Host(DirectoryEntry entry) => entry.Details.Endpoint?.Host ?? new Uri(entry.Details.ApiUrl).DnsSafeHost;
        using var parallel = new SemaphoreSlim(4, 4);
        var addresses = await Task.WhenAll(live.Select(Host).Distinct(StringComparer.OrdinalIgnoreCase).Select(async host =>
        {
            if (IPAddress.TryParse(host, out var literal)) return (Host: host, Addresses: new[] { Canonical(literal) });
            await parallel.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(3));
                var resolved = await _resolve(host, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
                return (Host: host, Addresses: resolved.Select(Canonical).DefaultIfEmpty(host.TrimEnd('.').ToLowerInvariant()).ToArray());
            }
            catch (Exception error) when (!ct.IsCancellationRequested && error is SocketException or IOException or OperationCanceledException)
            { return (Host: host, Addresses: new[] { host.TrimEnd('.').ToLowerInvariant() }); }
            finally { parallel.Release(); }
        })).ConfigureAwait(false);
        var lookup = addresses.ToDictionary(row => row.Host, row => row.Addresses, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<(string Game, string Host, int Port, string Path)>();
        var result = new List<DirectoryEntry>();
        foreach (var entry in live)
        {
            ct.ThrowIfCancellationRequested();
            var api = new Uri(entry.Details.ApiUrl);
            var port = entry.Details.Endpoint?.Port ?? api.Port;
            // Without an advertised game endpoint, preserve distinct APIs hosted behind one reverse proxy.
            var path = entry.Details.Endpoint is null ? api.AbsolutePath.TrimEnd('/') : "";
            var keys = lookup[Host(entry)].Select(host => (entry.Details.Game, host, port, path)).ToArray();
            var duplicate = keys.Any(seen.Contains);
            foreach (var key in keys) seen.Add(key);
            if (!duplicate) result.Add(entry);
        }
        return result;
    }

    private static string Canonical(IPAddress address) => (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
}
