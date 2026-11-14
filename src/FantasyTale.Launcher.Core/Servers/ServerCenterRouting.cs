using System.Globalization;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Local bootstrap work authorized by a server directory, without replacing a native Center endpoint.</summary>
/// <param name="AutomaticRequested">Whether the server enabled Automatic and the module can consume authenticated admission.</param>
/// <param name="Targets">Physical destinations to provide to a local Gateway.</param>
/// <param name="Choices">Manual chooser entries retained independently of destination availability.</param>
public sealed record ServerCenterRoute(bool AutomaticRequested, IReadOnlyList<GameServerTarget> Targets, IReadOnlyList<GameServerChoice> Choices)
{
    /// <summary>Gets the canonical chooser directory independently of the ticket's narrower Automatic admission.</summary>
    public ServerDirectory? Directory { get; init; }
}

/// <summary>Separates server-owned native Center handling from explicitly requested local Gateway support.</summary>
public static class ServerCenterRouting
{
    /// <summary>Reads a directory only when the catalog explicitly enables gateway mode; other servers need no directory API.</summary>
    public static async Task<ServerCenterRoute> PrepareAsync(ServerCenterConfiguration? center,
        Func<CancellationToken, Task<ServerDirectory?>> readDirectory, ServerDirectoryResolver resolver,
        bool supportsAutomatic, string launcherLogin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(readDirectory);
        ArgumentNullException.ThrowIfNull(resolver);
        ct.ThrowIfCancellationRequested();
        ValidateConfiguration(center);
        if (center is not { Enabled: true, Mode: "gateway" }) return new(false, [], []);
        var directory = await readDirectory(ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("The server enabled Gateway Center without supplying its directory.");
        if (directory.Center != center)
            throw new InvalidDataException("The Center directory does not match the server catalog's activation.");
        return await PrepareAsync(directory, resolver, supportsAutomatic, launcherLogin, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Keeps only nodes that serve the directory's build, so the Center list a Gateway shows never offers a node the
    /// client cannot play on. A node that does not state its builds is kept, and a manual server left without any
    /// node is dropped; a server that listed no node to begin with (a reserved entry) is kept as it came.
    /// </summary>
    internal static ServerDirectory ServingBuild(ServerDirectory directory) => directory with
    {
        Servers = directory.Servers
            .Select(row => row with { Targets = row.Targets.Where(target => target.ClientBuilds is null
                || target.ClientBuilds.Contains(directory.Build, StringComparer.Ordinal)).ToArray() })
            .Where((row, index) => row.Automatic || row.Targets.Count > 0 || directory.Servers[index].Targets.Count == 0)
            .ToArray(),
    };

    /// <summary>Rejects an enabled Center whose mode this launcher cannot honour.</summary>
    /// <remarks>
    /// A disabled Center is inert: its mode is never read, so an unknown or future value must not reject
    /// the catalog and block ordinary server login.
    /// </remarks>
    internal static void ValidateConfiguration(ServerCenterConfiguration? center)
    {
        if (center is { Enabled: true, Mode: not ("native" or "gateway") })
            throw new InvalidDataException("Invalid server Center configuration.");
    }

    /// <summary>Prepares only explicitly enabled gateway directories; disabled or native handling retains the original server endpoint.</summary>
    public static async Task<ServerCenterRoute> PrepareAsync(ServerDirectory? directory, ServerDirectoryResolver resolver,
        bool supportsAutomatic, string launcherLogin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ct.ThrowIfCancellationRequested();
        if (directory is null) return new(false, [], []);
        LauncherApiClient.ValidateDirectory(directory, directory.Build, configuredMembership: true);
        if (directory.Center is not { Enabled: true, Mode: "gateway" }) return new(false, [], []);
        directory = ServingBuild(directory);
        if (supportsAutomatic && launcherLogin is not ("none" or "game") && directory.Servers.Any(row => row.Automatic))
            return new(true, [], []) { Directory = directory };

        // A bootstrap-only adapter cannot turn an Automatic alias into an unauthenticated preselected endpoint.
        var manual = directory with { Servers = directory.Servers.Where(row => !row.Automatic).ToArray() };
        var targets = await resolver.ResolveNativeAsync(manual, ct).ConfigureAwait(false);
        if (targets.Count == 0)
            throw new InvalidOperationException("The selected Gateway directory has no available game servers.");
        var choices = targets.Select(item => new GameServerChoice(item.Id, item.Name, item.NativeCenterId!.Value,
            item.UnavailableCapacity is null ? item.Id : null, item.UnavailableCapacity is null ? null : "unavailable")).ToArray();
        return new(false, targets, choices);
    }
}
