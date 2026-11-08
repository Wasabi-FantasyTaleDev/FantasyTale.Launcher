using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Multiplayer;

/// <summary>Shared bounds for directory hosts, clients and saved source definitions.</summary>
public static class DirectoryValidation
{
    /// <summary>Rejects malformed metadata and returns a detached advertisement with its canonical API root.</summary>
    public static DirectoryAdvertisement Normalize(DirectoryAdvertisement value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Id(value.Game); Text(value.Name, 128);
        if (value.Description is null || value.Description.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) throw new ArgumentException("Invalid directory introduction.");
        if (value.Builds is null || value.Builds.Count == 0 || value.Builds.Distinct(StringComparer.Ordinal).Count() != value.Builds.Count)
            throw new ArgumentException("Directory entries require distinct supported builds.");
        foreach (var build in value.Builds) Id(build);
        if (string.IsNullOrWhiteSpace(value.ApiUrl)) throw new ArgumentException("A launcher API address is required.");
        var api = ServerStore.ApiBase(value.ApiUrl).AbsoluteUri;
        ServerMetadataPolicy.Validate(value.Presentation);
        ServerMetadataPolicy.Validate(value.ClientPolicy);
        var normalized = value with { ApiUrl = api, Builds = value.Builds.ToArray() };
        if (value.Endpoint is { } endpoint) ServerStore.Validate(new("discovery", value.Game, value.Name, api, endpoint, false, false));
        return normalized;
    }

    /// <summary>Accepts a bounded local identity without file-system metacharacters.</summary>
    public static void Id(string value)
    {
        if (value is null) throw new ArgumentException("Missing directory identity.");
        try { InstanceFile.ValidateId("directory", value); }
        catch (DefinitionException error) { throw new ArgumentException("Invalid directory identity.", nameof(value), error); }
    }

    /// <summary>Accepts only nonempty printable display text within the specified length.</summary>
    public static void Text(string value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > limit || value.Any(char.IsControl)) throw new ArgumentException("Invalid directory display text.");
    }
}
