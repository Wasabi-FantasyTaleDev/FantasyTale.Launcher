using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>Freezes issuer-admitted endpoints and measures health without expanding a ticket's membership.</summary>
public sealed class AutomaticServerSelection : IAutomaticServerSelection
{
    private readonly Func<CancellationToken, Task<LaunchServerSelection?>> _refresh;
    private readonly Func<ServerEndpoint, CancellationToken, Task<TimeSpan?>> _measure;
    private readonly TimeProvider _clock;
    private readonly string _apiUrl;
    private readonly ILaunchDiagnosticSink? _diagnostics;
    private readonly ConcurrentDictionary<int, string> _candidateStates = new();
    private readonly long _startedAt;
    private readonly TimeSpan _lifetime;
    private int _refreshing;
    private bool _initial = true;
    private bool _closed;

    private AutomaticServerSelection(LaunchServerSelection grant, string apiUrl, IReadOnlyList<GameServerTarget> targets,
        Func<CancellationToken, Task<LaunchServerSelection?>> refresh,
        Func<ServerEndpoint, CancellationToken, Task<TimeSpan?>> measure, TimeProvider clock, ILaunchDiagnosticSink? diagnostics,
        long startedAt, DateTimeOffset receivedAtUtc)
    {
        Grant = grant; Targets = targets; _refresh = refresh; _measure = measure; _clock = clock; _apiUrl = apiUrl;
        _diagnostics = diagnostics;
        _startedAt = startedAt;
        _lifetime = grant.ExpiresAtUtc - grant.GeneratedAtUtc;
        LocalExpiresAtUtc = receivedAtUtc + _lifetime;
        Automatic = grant.Servers.Single(row => row.Automatic);
        var admitted = Automatic.Targets.Select(row => row.NodeId).ToHashSet(StringComparer.Ordinal);
        AdmittedCenterIds = targets.Where(row => admitted.Contains(row.BackendId!)).Select(row => row.Id).ToArray();
    }

    /// <inheritdoc />
    public LaunchServerSelection Grant { get; }
    /// <inheritdoc />
    public DateTimeOffset LocalExpiresAtUtc { get; }
    /// <inheritdoc />
    public IReadOnlyList<GameServerTarget> Targets { get; }
    /// <inheritdoc />
    public ServerSelector Automatic { get; }
    /// <inheritdoc />
    public IReadOnlyList<string> AdmittedCenterIds { get; }

    /// <summary>Resolves canonical manual destinations separately from Automatic admission; refreshes cannot replace either binding.</summary>
    public static async Task<AutomaticServerSelection> CreateAsync(LaunchServerSelection grant, string apiUrl,
        Func<CancellationToken, Task<LaunchServerSelection?>> refresh,
        Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
        Func<ServerEndpoint, CancellationToken, Task<TimeSpan?>>? measure = null, TimeProvider? clock = null,
        ServerDirectory? directory = null, ILaunchDiagnosticSink? diagnostics = null,
        Func<ServerEndpoint, CancellationToken, Task<TimeSpan?>>? measureManual = null, CancellationToken ct = default)
    {
        clock ??= TimeProvider.System;
        var startedAt = clock.GetTimestamp();
        var receivedAtUtc = clock.GetUtcNow();
        ValidateGrant(grant, grant.Build, apiUrl);
        resolve ??= DnsResolution.ResolveAsync;
        if (directory is not null) ValidateDirectoryBinding(grant, directory);
        var canonical = directory ?? new ServerDirectory(1, grant.Build,
            grant.Servers.Where(row => row.Automatic || row.Targets.Count != 0).ToArray()) { Center = new(true, "gateway") };
        var ordered = await new ServerDirectoryResolver(resolve, measureManual, diagnostics: diagnostics).ResolveNativeAsync(canonical, ct).ConfigureAwait(false);
        return new(grant, apiUrl, ordered, refresh, measure ?? MeasureAsync, clock, diagnostics, startedAt, receivedAtUtc);
    }

    private static void ValidateDirectoryBinding(LaunchServerSelection grant, ServerDirectory directory)
    {
        LauncherApiClient.ValidateDirectory(directory, grant.Build, configuredMembership: true);
        if (directory.Center is not { Enabled: true, Mode: "gateway" })
            throw new InvalidDataException("Automatic selection requires an enabled Gateway directory.");
        foreach (var admitted in grant.Servers)
        {
            var canonical = directory.Servers.SingleOrDefault(row => row.Id == admitted.Id);
            if (canonical is null || canonical.Name != admitted.Name || canonical.ShortName != admitted.ShortName || canonical.Automatic != admitted.Automatic
                || admitted.Targets.Any(node => !canonical.Targets.Any(value => Binding(value) == Binding(node))))
                throw new InvalidDataException("The launch admission does not match the canonical server directory.");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AutomaticServerCandidate>?> RefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
            throw new InvalidOperationException("A native selection refresh is already running.");
        try
        {
            if (_closed || GrantRemaining <= TimeSpan.Zero) { _closed = true; return null; }
            LaunchServerSelection? snapshot;
            try { snapshot = _initial ? Grant : await _refresh(cancellationToken).ConfigureAwait(false); }
            catch (ServerFailureException error)
            {
                var category = error.Code switch
                {
                    "network_error" or "network_timeout" or "http_error" or "server_not_ready"
                        or "server_maintenance" or "server_full" or "server_selection_unavailable"
                        or "invalid_credentials" or "account_banned" or "not_whitelisted" => error.Code,
                    _ => "server_rejection",
                };
                var status = error.StatusCode is { } code ? ((int)code).ToString(CultureInfo.InvariantCulture) : "none";
                try { _diagnostics?.Record("gateway-selection-refresh-failed", "http-" + status + "-" + category); }
                catch (Exception) { /* Diagnostics must preserve the original refresh failure. */ }
                throw;
            }
            _initial = false;
            if (snapshot is null) { _closed = true; return null; }
            ValidateRefresh(Grant, snapshot, _apiUrl);
            var automatic = snapshot.Servers.Single(row => row.Automatic);
            var candidates = new ConcurrentDictionary<string, AutomaticServerCandidate>(StringComparer.Ordinal);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                await Parallel.ForEachAsync(automatic.Targets, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = deadline.Token }, async (node, token) =>
                {
                    var target = Targets.Single(row => row.BackendId == node.NodeId);
                    void State(string reason) => RecordCandidateState(target.NativeCenterId!.Value, reason);
                    if (target.UnavailableCapacity is not null) { State("unavailable-native-entry"); return; }
                    if (!node.Online) { State("offline"); return; }
                    if (node.Draining) { State("draining"); return; }
                    if (node.Players >= node.Capacity) { State("full"); return; }
                    // The issuer owns heartbeat health. Source timestamps cannot override its online state.
                    // The current canonical manual mapping must still exist; an automatic alias cannot replace it.
                    if (!snapshot.Servers.Any(row => !row.Automatic && row.Id == target.NativeCenterId && row.Targets.Any(value => value.NodeId == node.NodeId)))
                    { State("missing-manual-binding"); return; }
                    using var probe = CancellationTokenSource.CreateLinkedTokenSource(token);
                    probe.CancelAfter(TimeSpan.FromSeconds(1));
                    try
                    {
                        var rtt = await _measure(target.Endpoint, probe.Token).ConfigureAwait(false);
                        if (rtt is null || rtt < TimeSpan.Zero || rtt > TimeSpan.FromSeconds(60)) { State("probe-unavailable"); return; }
                        var validFor = GrantRemaining;
                        if (validFor <= TimeSpan.Zero) { State("admission-expired"); return; }
                        var localNow = _clock.GetUtcNow();
                        var observedAt = localNow < LocalExpiresAtUtc ? localNow : LocalExpiresAtUtc;
                        candidates[target.Id] = new(target.Id, true, checked((int)(rtt.Value.Ticks / 10)), node.Players,
                            node.Capacity, observedAt, LocalExpiresAtUtc, node.StatusRevision ?? snapshot.PolicyRevision);
                        State("eligible");
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { State("probe-timeout"); }
                    catch (Exception error) when (error is SocketException or IOException or HttpRequestException) { State("probe-failed"); }
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var target in Targets.Where(row => AdmittedCenterIds.Contains(row.Id)
                && !automatic.Targets.Any(node => node.NodeId == row.BackendId)))
                RecordCandidateState(target.NativeCenterId!.Value, "withdrawn");
            return AdmittedCenterIds.Where(candidates.ContainsKey).Select(id => candidates[id]).ToArray();
        }
        finally { Volatile.Write(ref _refreshing, 0); }
    }

    private TimeSpan GrantRemaining => _lifetime - _clock.GetElapsedTime(_startedAt);

    private void RecordCandidateState(int centerId, string state)
    {
        if (_candidateStates.TryGetValue(centerId, out var previous) && previous == state) return;
        _candidateStates[centerId] = state;
        try { _diagnostics?.Record("gateway-automatic-candidate", "center-" + centerId.ToString(CultureInfo.InvariantCulture) + "-" + state); }
        catch (Exception) { /* Optional diagnostics cannot prevent native selection. */ }
    }

    internal static void ValidateGrant(LaunchServerSelection grant, string build, string apiUrl, bool allowEmpty = false)
    {
        static bool Opaque(string? value) => value is { Length: > 0 and <= 128 } && value.All(c => c is >= '!' and <= '~');
        if (grant.Schema != 1 || grant.Build != build || !Opaque(grant.GrantId) || !Opaque(grant.AuthorityId)
            || !Opaque(grant.PolicyRevision) || string.IsNullOrWhiteSpace(grant.DataScope) || grant.GeneratedAtUtc == default
            || grant.ExpiresAtUtc <= grant.GeneratedAtUtc || grant.ExpiresAtUtc - grant.GeneratedAtUtc > TimeSpan.FromMinutes(15)
            || grant.Servers is null)
            throw new InvalidDataException("Invalid launch-bound server admission.");
        LauncherApiClient.ValidateDirectory(new(1, build, grant.Servers) { Center = new(true, "gateway") }, build, configuredMembership: true);
        var automatic = grant.Servers.Where(row => row.Automatic).ToArray();
        var manual = grant.Servers.Where(row => !row.Automatic && row.Targets.Count == 1).ToArray();
        if (automatic.Length != 1
            || !allowEmpty && (automatic[0].Targets.Count == 0 || manual.Length == 0)
            || manual.Select(row => row.Targets[0].NodeId).Distinct(StringComparer.Ordinal).Count() != manual.Length)
            throw new InvalidDataException("Automatic selection requires one automatic row and distinct canonical manual workers.");
        var origin = new Uri(apiUrl).GetLeftPart(UriPartial.Authority);
        var automaticNodeIds = automatic.SelectMany(row => row.Targets).Select(node => node.NodeId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var node in grant.Servers.SelectMany(row => row.Targets))
        {
            if (automaticNodeIds.Contains(node.NodeId) && node.DataScope != grant.DataScope)
                throw new InvalidDataException("An automatic target must belong to the launch grant data scope.");
            if (node.Authentication is not { Mode: "master-issued", PolicyMode: "authority" } auth
                || auth.AuthorityId != grant.AuthorityId || new Uri(auth.ApiUrl).GetLeftPart(UriPartial.Authority) != origin)
                throw new InvalidDataException("A selection target does not belong to this ticket issuer and authentication audience.");
            if (!manual.Any(row => Binding(row.Targets[0]) == Binding(node)))
                throw new InvalidDataException("An automatic worker has no unique canonical manual selector.");
            if ((node.ObservedAtUtc is null) != (node.ExpiresAtUtc is null) || (node.StatusRevision is null) != (node.ObservedAtUtc is null)
                || node.StatusRevision is { } revision && !Opaque(revision))
                throw new InvalidDataException("Invalid source observation metadata.");
        }
    }

    internal static void ValidateRefresh(LaunchServerSelection original, LaunchServerSelection current, string apiUrl)
    {
        ValidateGrant(current, original.Build, apiUrl, allowEmpty: true);
        if (current.GrantId != original.GrantId || current.AuthorityId != original.AuthorityId || current.PolicyRevision != original.PolicyRevision
            || current.DataScope != original.DataScope || current.ExpiresAtUtc != original.ExpiresAtUtc || current.Servers.Count != original.Servers.Count)
            throw new InvalidDataException("A selection refresh changed immutable launch admission.");
        foreach (var row in current.Servers)
        {
            var admitted = original.Servers.SingleOrDefault(value => value.Id == row.Id);
            if (admitted is null || admitted.Name != row.Name || admitted.ShortName != row.ShortName || admitted.Automatic != row.Automatic
                || row.Targets.Any(node => !admitted.Targets.Any(value => Binding(value) == Binding(node))))
                throw new InvalidDataException("A selection refresh expanded or replaced original endpoint membership.");
        }
    }

    private static (string, string, int, string, string, ServerDirectoryAuthentication?) Binding(ServerDirectoryTarget node) =>
        (node.NodeId, node.Host.ToLowerInvariant(), node.Port, node.DataScope, node.JoinScope, node.Authentication);

    private static async Task<TimeSpan?> MeasureAsync(ServerEndpoint endpoint, CancellationToken ct)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var start = Stopwatch.GetTimestamp();
        await socket.ConnectAsync(IPAddress.Parse(endpoint.Host), endpoint.Port, ct).ConfigureAwait(false);
        return Stopwatch.GetElapsedTime(start);
    }
}
