using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using FantasyTale.Launcher.Downloads;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Servers;

/// <summary>A selector retains its identity even when no eligible or reachable backend is available.</summary>
/// <param name="Id">Native selector identity.</param>
/// <param name="Name">Native selector label.</param>
/// <param name="Target">Selected physical node, or null for an unconfigured selector or unavailable automatic pool.</param>
/// <param name="Endpoint">Resolved IPv4 endpoint, or null when the selector is unavailable.</param>
/// <param name="RoundTrip">Measured player-side TCP connection time for an automatic candidate.</param>
public sealed record ResolvedServerSelector(int Id, string Name, ServerDirectoryTarget? Target, ServerEndpoint? Endpoint, TimeSpan? RoundTrip);

/// <summary>Bounds DNS and player-side endpoint measurements without changing manual identities or granting authentication.</summary>
public sealed class ServerDirectoryResolver
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly Func<ServerEndpoint, CancellationToken, Task<TimeSpan?>> _measure;
    private readonly int _parallelism;
    private readonly TimeSpan _deadline;
    private readonly TimeSpan _probeTimeout;
    private readonly int _nativeProbeAttempts;
    private readonly ILaunchDiagnosticSink? _diagnostics;

    /// <summary>Creates a bounded chooser resolver; native retries share one deadline before endpoint bindings are frozen.</summary>
    public ServerDirectoryResolver(Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
        Func<ServerEndpoint, CancellationToken, Task<TimeSpan?>>? measure = null, int parallelism = 4,
        TimeSpan? deadline = null, TimeSpan? probeTimeout = null, int nativeProbeAttempts = 3, ILaunchDiagnosticSink? diagnostics = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parallelism, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parallelism, 16);
        ArgumentOutOfRangeException.ThrowIfLessThan(nativeProbeAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nativeProbeAttempts, 8);
        _nativeProbeAttempts = nativeProbeAttempts;
        _diagnostics = diagnostics;
        _parallelism = parallelism;
        _deadline = deadline ?? TimeSpan.FromSeconds(8);
        _probeTimeout = probeTimeout ?? TimeSpan.FromSeconds(1);
        if (_deadline <= TimeSpan.Zero || _deadline > TimeSpan.FromSeconds(30) || _probeTimeout <= TimeSpan.Zero || _probeTimeout > _deadline)
            throw new ArgumentOutOfRangeException(nameof(deadline));
        _resolve = resolve ?? DnsResolution.ResolveAsync;
        _measure = measure ?? MeasureAsync;
    }

    /// <summary>Resolves and probes every configured manual destination, preserving failed endpoints as explicit native status-only entries.</summary>
    public async Task<IReadOnlyList<GameServerTarget>> ResolveNativeAsync(ServerDirectory directory, CancellationToken ct = default)
    {
        LauncherApiClient.ValidateDirectory(directory, directory.Build, configuredMembership: true);
        var manual = directory.Servers.Where(row => !row.Automatic).ToArray();
        if (manual.Any(row => row.Targets.Count != 1))
            throw new InvalidDataException("Every native manual selector requires its configured target, including unavailable destinations.");
        var resolved = new System.Collections.Concurrent.ConcurrentDictionary<int, GameServerTarget>();
        await Parallel.ForEachAsync(manual, new ParallelOptions { MaxDegreeOfParallelism = _parallelism, CancellationToken = ct }, async (row, token) =>
        {
            var node = row.Targets[0];
            var endpoint = new ServerEndpoint(node.Host, node.Port);
            var unavailable = true;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(_deadline);
            for (var attempt = 1; attempt <= _nativeProbeAttempts; attempt++)
            {
                var phase = "dns";
                var reason = "no-ipv4";
                try
                {
                    budget.Token.ThrowIfCancellationRequested();
                    var addresses = await _resolve(node.Host, budget.Token).ConfigureAwait(false);
                    var address = addresses.FirstOrDefault(value => value.AddressFamily == AddressFamily.InterNetwork);
                    if (address is not null)
                    {
                        endpoint = new(address.ToString(), node.Port);
                        phase = "connect";
                        using var probe = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
                        probe.CancelAfter(_probeTimeout);
                        var elapsed = await _measure(endpoint, probe.Token).ConfigureAwait(false);
                        unavailable = elapsed is null || elapsed < TimeSpan.Zero;
                        reason = unavailable ? "connect-unavailable" : "connected";
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { reason = phase + "-timeout"; }
                catch (Exception error) when (error is SocketException or HttpRequestException or IOException) { reason = phase + "-failed"; }
                RecordNativeProbe(row.Id, attempt, reason);
                if (!unavailable || budget.IsCancellationRequested) break;
            }
            if (unavailable && node.Capacity > int.MaxValue / 2)
                throw new InvalidDataException("Unavailable server capacity cannot be represented by the native status.");
            resolved[row.Id] = new(row.Id.ToString(CultureInfo.InvariantCulture), row.Name, endpoint)
            {
                ShortName = row.ShortName, BackendId = node.NodeId, NativeCenterId = row.Id,
                StatusCapacity = node.Capacity,
                UnavailableCapacity = unavailable ? node.Capacity : null,
            };
        }).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var targets = manual.Select(row => resolved[row.Id]).ToArray();
        if (targets.Select(row => row.Endpoint).Distinct().Count() != targets.Length)
            throw new InvalidDataException("Distinct native workers resolve to the same physical endpoint.");
        return targets;
    }

    private void RecordNativeProbe(int centerId, int attempt, string reason)
    {
        try
        {
            _diagnostics?.Record("gateway-native-probe", "center-" + centerId.ToString(CultureInfo.InvariantCulture)
                + "-" + reason + "-attempt-" + attempt.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception) { /* Optional diagnostics cannot change native endpoint admission. */ }
    }

    /// <summary>Preserves directory order and manual entries, selecting only admitted automatic candidates by measured RTT.</summary>
    public async Task<IReadOnlyList<ResolvedServerSelector>> ResolveAsync(ServerDirectory directory, CancellationToken ct = default)
    {
        LauncherApiClient.ValidateDirectory(directory, directory.Build);
        var nodes = directory.Servers.SelectMany(selector => selector.Targets)
            .Where(target => target.Online && !target.Draining && target.Players < target.Capacity)
            .DistinctBy(target => target.NodeId).ToArray();
        var automatic = directory.Servers.Where(selector => selector.Automatic).SelectMany(selector => selector.Targets)
            .Select(target => target.NodeId).ToHashSet(StringComparer.Ordinal);
        var resolved = new System.Collections.Concurrent.ConcurrentDictionary<string, (ServerEndpoint Endpoint, TimeSpan? RoundTrip)>(StringComparer.Ordinal);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_deadline);
        try
        {
            await Parallel.ForEachAsync(nodes, new ParallelOptions { MaxDegreeOfParallelism = _parallelism, CancellationToken = deadline.Token }, async (target, token) =>
            {
                using var probe = CancellationTokenSource.CreateLinkedTokenSource(token);
                probe.CancelAfter(_probeTimeout);
                try
                {
                    var addresses = await _resolve(target.Host, probe.Token).ConfigureAwait(false);
                    var address = addresses.FirstOrDefault(value => value.AddressFamily == AddressFamily.InterNetwork);
                    if (address is null) return;
                    var endpoint = new ServerEndpoint(address.ToString(), target.Port);
                    resolved[target.NodeId] = (endpoint, null);
                    var elapsed = automatic.Contains(target.NodeId) ? await _measure(endpoint, probe.Token).ConfigureAwait(false) : null;
                    if (elapsed < TimeSpan.Zero) throw new InvalidOperationException("Endpoint measurement returned a negative duration.");
                    resolved[target.NodeId] = (endpoint, elapsed);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch (Exception error) when (error is SocketException or HttpRequestException or IOException) { }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        ct.ThrowIfCancellationRequested();
        var endpoints = new Dictionary<ServerEndpoint, string>();
        foreach (var (nodeId, result) in resolved)
        {
            if (endpoints.TryGetValue(result.Endpoint, out var previous) && previous != nodeId)
                throw new InvalidDataException("One physical endpoint claims different backend identities.");
            endpoints[result.Endpoint] = nodeId;
        }
        return directory.Servers.Select(selector =>
        {
            var target = selector.Automatic
                ? selector.Targets.Where(value => resolved.TryGetValue(value.NodeId, out var result) && result.RoundTrip is not null)
                    .OrderBy(value => resolved[value.NodeId].RoundTrip).ThenBy(value => value.NodeId, StringComparer.Ordinal).FirstOrDefault()
                : selector.Targets.SingleOrDefault();
            if (target is null || !target.Online || target.Draining || target.Players >= target.Capacity
                || !resolved.TryGetValue(target.NodeId, out var result))
                return new ResolvedServerSelector(selector.Id, selector.Name, target, null, null);
            return new ResolvedServerSelector(selector.Id, selector.Name, target, result.Endpoint, result.RoundTrip);
        }).ToArray();
    }

    private static async Task<TimeSpan?> MeasureAsync(ServerEndpoint endpoint, CancellationToken ct)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var started = Stopwatch.GetTimestamp();
        await socket.ConnectAsync(IPAddress.Parse(endpoint.Host), endpoint.Port, ct).ConfigureAwait(false);
        return Stopwatch.GetElapsedTime(started);
    }
}
