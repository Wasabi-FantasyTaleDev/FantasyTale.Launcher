using System.Net;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Servers;

public sealed partial class LauncherApiClient
{
    /// <summary>Reads native chooser bootstrap metadata; only HTTP 404 means a standalone server has no directory.</summary>
    public Task<ServerDirectory?> ServerDirectoryAsync(ServerDefinition server, string build, CancellationToken ct = default) =>
        ServerDirectoryAsync(server, build, configuredMembership: false, ct);

    /// <summary>Reads configured membership for audited automatic selection while preserving offline candidates.</summary>
    public async Task<ServerDirectory?> ServerDirectoryAsync(ServerDefinition server, string build, bool configuredMembership, CancellationToken ct = default)
    {
        Definitions.InstanceFile.ValidateId("server directory", build);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        ServerDirectory directory;
        try
        {
            // Bound transport bytes without silently dropping configured selector identities.
            directory = await SendAsync<ServerDirectory>(server, HttpMethod.Get, "v1/servers/" + Uri.EscapeDataString(build),
                null, null, timeout.Token, maximumResponseBytes: 8 * 1024 * 1024).ConfigureAwait(false);
        }
        catch (ServerFailureException error) when (error.StatusCode == HttpStatusCode.NotFound) { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ServerFailureException("network_timeout", transportCode: "timeout"); }
        ValidateDirectory(directory, build, configuredMembership);
        return directory;
    }

    internal static void ValidateDirectory(ServerDirectory directory, string build, bool configuredMembership = false)
    {
        static bool Identifier(string? value) => value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
        static bool Backend(string? value) => value is { Length: > 0 and <= 64 } && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
        static bool Printable(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                var unit = value[index];
                if (char.IsControl(unit) || char.IsLowSurrogate(unit)) return false;
                if (char.IsHighSurrogate(unit) && (++index >= value.Length || !char.IsLowSurrogate(value[index]))) return false;
            }
            return true;
        }
        static bool ShortNameValid(string? value) => value is null || value.Length is > 0 and <= 128 && Printable(value);
        static bool Scope(string? value) => value is { Length: <= 128 } && Printable(value);
        ServerCenterRouting.ValidateConfiguration(directory.Center);
        if (directory.Schema != 1 || directory.Build != build
            || directory.Servers is null)
            throw new InvalidDataException("Invalid native server directory contract.");
        var selectors = new HashSet<int>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, (string Host, int Port, string DataScope, string JoinScope, ServerDirectoryAuthentication? Authentication)>(StringComparer.Ordinal);
        var authorities = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var selector in directory.Servers)
        {
            if (selector is null || selector.Id <= 0 || !selectors.Add(selector.Id) || string.IsNullOrWhiteSpace(selector.Name)
                || selector.Name.Length > 128 || !Printable(selector.Name) || !ShortNameValid(selector.ShortName)
                || !labels.Add(selector.Name) || selector.Targets is null
                || !selector.Automatic && selector.Targets.Count > 1)
                throw new InvalidDataException("Invalid native server selector.");
            var candidateIds = new HashSet<string>(StringComparer.Ordinal);
            string? automaticScope = null;
            ServerDirectoryAuthentication? automaticAuthority = null;
            foreach (var target in selector.Targets)
            {
                if (target is null || !Backend(target.NodeId) || !candidateIds.Add(target.NodeId) || target.Host is not { Length: > 0 and <= 253 }
                    || Uri.CheckHostName(target.Host) == UriHostNameType.Unknown || target.Port is < 1 or > 65535
                    || !Scope(target.DataScope) || !Scope(target.JoinScope) || target.Players < 0 || target.Capacity < 1)
                    throw new InvalidDataException("Invalid physical server target.");
                if (target.Authentication is { } authentication)
                {
                    if (!Identifier(authentication.AuthorityId) || authentication.Mode is not ("master-issued" or "own-authority")
                        || authentication.PolicyMode != "authority" || authentication.ApiUrl is not { Length: > 0 and <= 2048 }
                        || !Uri.TryCreate(authentication.ApiUrl, UriKind.Absolute, out var authority) || authority.Scheme is not ("http" or "https")
                        || authority.UserInfo.Length != 0 || authority.Query.Length != 0 || authority.Fragment.Length != 0
                        || authentication.ApiUrl.Any(char.IsWhiteSpace) || authentication.ApiUrl.Any(char.IsControl))
                        throw new InvalidDataException("Invalid explicit server authentication authority.");
                    var origin = authority.GetLeftPart(UriPartial.Authority);
                    if (authorities.TryGetValue(authentication.AuthorityId, out var existing) && existing != origin)
                        throw new InvalidDataException("An authentication authority has conflicting API origins.");
                    authorities[authentication.AuthorityId] = origin;
                }
                var identity = (target.Host.ToLowerInvariant(), target.Port, target.DataScope, target.JoinScope, target.Authentication);
                if (nodes.TryGetValue(target.NodeId, out var previous) && previous != identity)
                    throw new InvalidDataException("A physical node has conflicting endpoint or scope declarations.");
                nodes[target.NodeId] = identity;
                if (!selector.Automatic || directory.Center is not { Enabled: true, Mode: "gateway" }) continue;
                if ((!configuredMembership && (!target.Online || target.Draining || target.Players >= target.Capacity)) || target.DataScope.Length == 0
                    || automaticScope is not null && automaticScope != target.DataScope)
                    throw new InvalidDataException("Automatic candidates have invalid admission or data scopes.");
                if (automaticScope is not null && automaticAuthority != target.Authentication)
                    throw new InvalidDataException("Automatic candidates must share the same explicit authentication authority.");
                automaticScope = target.DataScope;
                automaticAuthority = target.Authentication;
            }
        }
    }

    /// <summary>Refreshes health for the original account, ticket and grant; closed admission never creates another ticket.</summary>
    public async Task<LaunchServerSelection?> RefreshServerSelectionAsync(ServerDefinition server, AccountLaunchSelection account,
        LaunchCredentials credentials, string admittedApiUrl, CancellationToken ct = default)
    {
        if (new Uri(EffectiveApiUrl(server)) != new Uri(admittedApiUrl))
            throw new InvalidOperationException("The selection authority address changed after admission.");
        var grant = credentials.ServerSelection ?? throw new InvalidOperationException("The launch has no server selection grant.");
        var selection = FindForLaunch(server, account) ?? throw new InvalidOperationException("The launch account is no longer selected.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var reply = await SendWithSessionRetryAsync<SelectionRefreshReply>(server, HttpMethod.Post, "v1/launch/servers",
                new { ticket = credentials.Ticket, build = grant.Build, grantId = grant.GrantId }, selection, timeout.Token,
                maximumResponseBytes: 8 * 1024 * 1024).ConfigureAwait(false);
            RequireSelection(server, selection);
            var refreshed = reply.ServerSelection ?? throw new InvalidDataException("The server omitted refreshed admission.");
            AutomaticServerSelection.ValidateRefresh(grant, refreshed, admittedApiUrl);
            return refreshed;
        }
        catch (ServerFailureException error) when (error.StatusCode == HttpStatusCode.Conflict && error.Code == "admission_closed") { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ServerFailureException("network_timeout", transportCode: "timeout"); }
    }

    private sealed record SelectionRefreshReply(LaunchServerSelection? ServerSelection);
}
