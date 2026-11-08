using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Downloads;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Loads deployment-owned mirror endpoints and bounded network measurement settings.</summary>
public static class GitHubMirrorConfiguration
{
    /// <summary>Reads the packaged TOML list and rejects malformed endpoint or probe settings.</summary>
    public static GitHubMirrors Load(string file, out Uri countryLookup, out TimeSpan lookupTimeout)
    {
        var root = TomlReader.Load(file);
        root.OnlyKeys("probe_url", "probe_prefix", "sample_bytes", "timeout_seconds", "parallelism", "mirrors", "country_lookup_url", "country_lookup_timeout_seconds", "directory_url");
        countryLookup = new Uri(root.String("country_lookup_url", true));
        if (countryLookup.Scheme != "https" || countryLookup.UserInfo.Length > 0) throw new ArgumentException("Country lookup requires a public HTTPS endpoint.");
        lookupTimeout = TimeSpan.FromSeconds(root.Integer("country_lookup_timeout_seconds", true, min: 1)!.Value);
        var configured = root.Tables("mirrors").Select(row =>
        {
            row.OnlyKeys("id", "name", "url", "directory_snapshot");
            return (Mirror: new GitHubMirror(row.String("id", true), row.String("name", true), new Uri(row.String("url", true))), Snapshot: row.Boolean("directory_snapshot"));
        }).ToArray();
        var providers = configured.Select(row => row.Mirror).ToArray();
        var directory = new Uri(root.String("directory_url", true));
        if (directory.Scheme != "https" || directory.UserInfo.Length > 0) throw new ArgumentException("Mirror directory requires HTTPS.");
        var parallelism = checked((int)root.Integer("parallelism", true, min: 1)!.Value);
        var timeout = TimeSpan.FromSeconds(root.Integer("timeout_seconds", true, min: 1)!.Value);
        return new(providers, new Uri(root.String("probe_url", true)), Convert.FromHexString(root.String("probe_prefix", true)),
            checked((int)root.Integer("sample_bytes", true, min: 1)!.Value), timeout,
            parallelism,
            refreshProviders: async ct => configured.Where(row => !row.Snapshot).Select(row => row.Mirror)
                .Concat(await GitHubMirrorDirectory.FetchAsync(directory, timeout, parallelism, ct).ConfigureAwait(false))
                .DistinctBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
