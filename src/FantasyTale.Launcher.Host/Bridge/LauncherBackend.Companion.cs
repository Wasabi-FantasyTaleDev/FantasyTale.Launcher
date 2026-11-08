using System.Collections.Concurrent;
using FantasyTale.Launcher.Communication;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Host.Bridge;

// Fantasy Tale Nirinsou Link: the server issues the launch identity and the first one-time ticket with /v1/launch;
// every reconnect asks the server for a fresh ticket of the same launch.
internal sealed partial class LauncherBackend
{
    private readonly ConcurrentDictionary<string, CompanionLaunch> _companionLaunches = new(StringComparer.Ordinal);

    private sealed record CompanionLaunch(ServerDefinition Server, LaunchCompanion? Grant, string Game, string Locale, GameLoginLaunch? GameLogin = null);

    /// <summary>Holds the launch's grant until the client's launcher services ask for a session.</summary>
    private void OfferCompanion(string instanceId, ServerDefinition server, LaunchCredentials? credentials, string game, string locale,
        GameLoginLaunch? gameLogin = null)
    {
        if (credentials?.Companion is { } grant) _companionLaunches[instanceId] = new(server, grant, game, locale);
        else if (gameLogin is not null) _companionLaunches[instanceId] = new(server, null, game, locale, gameLogin);
        else _companionLaunches.TryRemove(instanceId, out _);
    }

    private Task<CompanionSession?> CreateCompanionAsync(InstanceDefinition instance, BuildDefinition build,
        IReadOnlyDictionary<string, int> messages, CompanionLimits limits, CancellationToken cancellationToken)
    {
        if (!_companionLaunches.TryRemove(instance.Id, out var launch)) return Task.FromResult<CompanionSession?>(null);
        // A plain-http launcher API already carries the bearer token in the clear; the companion ticket is no more secret.
        var plainText = Uri.TryCreate(launch.Server.ApiUrl, UriKind.Absolute, out var api) && api.Scheme == Uri.UriSchemeHttp;
        // An in-game sign-in launch has no grant until the game has signed in; its launch key fetches every grant.
        var identity = new CompanionIdentity(launch.Game, build.Id, launch.Grant?.LaunchId ?? launch.GameLogin!.LaunchId, launch.Locale, messages);
        var first = 1;
        var session = new CompanionSession(identity, async token =>
        {
            var grant = launch.GameLogin is { } gameLogin ? await _api.GameLoginCompanionAsync(launch.Server, gameLogin, token).ConfigureAwait(false)
                : Interlocked.Exchange(ref first, 0) == 1 ? launch.Grant
                : await _api.RenewCompanionAsync(launch.Server, launch.Grant!.LaunchId, token).ConfigureAwait(false);
            if (grant is null || !Uri.TryCreate(grant.Url, UriKind.Absolute, out var endpoint)) return null;
            try { return new CompanionAuthorization(endpoint, grant.Ticket, plainText); }
            catch (ArgumentException) { return null; }
        }, limits);
        return Task.FromResult<CompanionSession?>(session);
    }
}
