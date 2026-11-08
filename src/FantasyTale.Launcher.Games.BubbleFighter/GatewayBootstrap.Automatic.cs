using System.Globalization;
using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter;

internal static partial class GatewayBootstrap
{
    internal static byte[] BuildAutomaticConfiguration(string build, string? launchId, IAutomaticServerSelection selection,
        IReadOnlyList<AutomaticServerCandidate> candidates)
    {
        if (build != selection.Grant.Build || launchId is not { Length: 32 } || !launchId.All(char.IsAsciiHexDigit)
            || selection.LocalExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("Automatic Gateway configuration has an invalid launch binding.");
        var unavailableCapacity = selection.Automatic.Targets.Max(node => node.Capacity);
        if (unavailableCapacity is < 1 or > int.MaxValue / 2)
            throw new InvalidDataException("Automatic source capacity cannot be represented by the native unavailable status.");
        var targets = ValidateTargets(selection.Targets);
        if (targets.Any(target => target.BackendId is null || target.NativeCenterId is null)
            || targets.Select(target => target.BackendId).Distinct(StringComparer.Ordinal).Count() != targets.Length
            || targets.Select(target => target.NativeCenterId).Distinct().Count() != targets.Length
            || targets.Any(target => target.NativeCenterId == selection.Automatic.Id)
            || selection.AdmittedCenterIds.Count < 1
            || selection.AdmittedCenterIds.Distinct(StringComparer.Ordinal).Count() != selection.AdmittedCenterIds.Count
            || selection.AdmittedCenterIds.Any(id => !targets.Any(target => target.Id == id)))
            throw new InvalidDataException("Automatic Gateway centers must retain distinct admitted physical identities.");
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 1,
            rows = new[] { new
            {
                id = "selector", plugin = "bf-center-selector", config = new
                {
                    build,
                    connection_trace = true,
                    connection_trace_max_bytes = ConnectionTraceMaximumBytes,
                    native_probe_timeout_ms = targets.All(target => target.StatusCapacity is not null) ? 3000 : 0,
                    native_probe_parallelism = 8,
                    // The parent owns the absolute grant/startup deadline, including slow resource preparation.
                    connect_timeout_ms = 0,
                    centers = targets.Select(target => new { id = target.Id, name = target.Name,
                        short_name = target.ShortName,
                        host = target.Endpoint.Host, port = target.Endpoint.Port, backend_id = target.BackendId,
                        native_center_id = target.NativeCenterId, unavailable_capacity = target.UnavailableCapacity,
                        status_capacity = target.StatusCapacity }),
                    automatic_entry = new
                    {
                        capability = "bf-native-automatic-center-v1",
                        id = selection.Automatic.Id.ToString(CultureInfo.InvariantCulture),
                        name = selection.Automatic.Name, short_name = selection.Automatic.ShortName, native_center_id = selection.Automatic.Id,
                        unavailable_capacity = unavailableCapacity,
                        selection_policy = "lowest-rtt-then-load", launch_id = launchId,
                        grant_id = selection.Grant.GrantId, grant_expires_at_utc = selection.LocalExpiresAtUtc,
                        admitted_center_ids = selection.AdmittedCenterIds, candidates = ProjectCandidates(candidates),
                    },
                },
            } },
        });
    }

    internal static int ControlMessageBudget(byte[] configuration, IAutomaticServerSelection? selection, string? launchId)
    {
        if (selection is null) return Math.Max(MaximumFrameBytes, configuration.Length);
        var largestRefresh = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "refresh-automatic", row_id = "selector", launch_id = launchId,
            grant_id = selection.Grant.GrantId, revision = long.MaxValue,
            candidates = ProjectCandidates(selection.AdmittedCenterIds.Select(id => new AutomaticServerCandidate(id, true,
                int.MaxValue, int.MaxValue, int.MaxValue, DateTimeOffset.MaxValue, DateTimeOffset.MaxValue,
                new string('<', 128))).ToArray()),
        });
        return Math.Max(MaximumFrameBytes, Math.Max(configuration.Length, largestRefresh.Length));
    }

    private static object[] ProjectCandidates(IReadOnlyList<AutomaticServerCandidate> candidates) => candidates.Select(candidate => (object)new
    {
        center_id = candidate.CenterId, healthy = candidate.Healthy, rtt_us = candidate.RttUs,
        users = candidate.Users, capacity = candidate.Capacity, observed_at_utc = candidate.ObservedAtUtc,
        expires_at_utc = candidate.ExpiresAtUtc, status_revision = candidate.StatusRevision,
    }).ToArray();

    private static async Task WriteSerializedAsync(Stream pipe, byte[] body, SemaphoreSlim writes, CancellationToken ct)
    {
        await writes.WaitAsync(ct).ConfigureAwait(false);
        try { await WriteFrameAsync(pipe, body, ct).ConfigureAwait(false); }
        finally { writes.Release(); }
    }

    internal static async Task RefreshAutomaticAsync(IAutomaticServerSelection selection, string launchId, Stream pipe,
        Task exited, SemaphoreSlim writes, ILaunchDiagnosticSink? diagnostics, CancellationToken ct, TimeSpan? interval = null)
    {
        long revision = 0;
        var stop = false;
        var selectionFinished = false;
        try
        {
            while (!ct.IsCancellationRequested && !exited.IsCompleted)
            {
                var delay = Task.Delay(interval ?? TimeSpan.FromSeconds(2), ct);
                if (await Task.WhenAny(delay, exited).ConfigureAwait(false) == exited) return;
                await delay.ConfigureAwait(false);
                IReadOnlyList<AutomaticServerCandidate>? candidates;
                try { candidates = await selection.RefreshAsync(ct).ConfigureAwait(false); }
                catch (Exception error) when ((error is HttpRequestException or IOException) && error is not InvalidDataException)
                {
                    Record(diagnostics, "gateway-health-refresh-unavailable", error.GetType().Name);
                    continue;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Record(diagnostics, "gateway-health-refresh-unavailable", "timeout");
                    continue;
                }
                var admissionClosed = candidates is null;
                candidates ??= [];
                revision = checked(revision + 1);
                using var ackDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                ackDeadline.CancelAfter(TimeSpan.FromSeconds(3));
                var body = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    type = "refresh-automatic", row_id = "selector", launch_id = launchId,
                    grant_id = selection.Grant.GrantId, revision, candidates = ProjectCandidates(candidates),
                });
                await WriteSerializedAsync(pipe, body, writes, ackDeadline.Token).ConfigureAwait(false);
                // This task is the sole reader after READY; no frontend request owns control-pipe reads.
                using var response = await ReadFrameAsync(pipe, ackDeadline.Token).ConfigureAwait(false);
                var status = ValidateAutomaticAck(response.RootElement, revision);
                Record(diagnostics, "gateway-health-refresh", status);
                if (admissionClosed || status == "expired")
                {
                    // Ticket redemption may close admission before native uniqueness
                    // cleanup. Empty candidates revoke selection without killing it.
                    selectionFinished = true;
                    Record(diagnostics, "gateway-admission-withdrawn", admissionClosed ? "admission-closed" : "grant-expired");
                    return;
                }
                if (status == "finished")
                {
                    // Selection is frozen, but the sidecar still owns its native redirect handshake until exit.
                    selectionFinished = true;
                    await exited.WaitAsync(ct).ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException) { stop = true; }
        catch (Exception error)
        {
            Record(diagnostics, "gateway-health-refresh-stopped", error.GetType().Name);
            stop = true;
        }
        finally
        {
            // A finished or withdrawn admission does not establish native retirement.
            // The lease still owns STOP if the game closes during native cleanup.
            if (!selectionFinished && !exited.IsCompleted && (stop || ct.IsCancellationRequested))
            {
                try
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await WriteSerializedAsync(pipe, "{\"type\":\"stop\"}"u8.ToArray(), writes, deadline.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException or OperationCanceledException)
                { Record(diagnostics, "gateway-health-stop-failed", error.GetType().Name); }
            }
        }
    }

    internal static string ValidateAutomaticAck(JsonElement response, long revision)
    {
        if (response.GetProperty("type").GetString() != "automatic-refresh-result"
            || response.GetProperty("row_id").GetString() != "selector" || response.GetProperty("revision").GetInt64() != revision)
            throw new InvalidDataException("Gateway returned a mismatched automatic-refresh acknowledgement.");
        var status = response.GetProperty("status").GetString();
        var reason = response.GetProperty("reason").GetString();
        if (status == "accepted" && reason == "updated" || status == "finished" && reason == "retired"
            || status == "rejected" && reason == "candidates") return status;
        if (status == "rejected" && reason == "grant-expired") return "expired";
        throw new InvalidDataException("Gateway rejected the immutable automatic-refresh binding.");
    }
}
