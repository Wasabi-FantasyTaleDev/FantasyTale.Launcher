using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter;

public sealed partial class BubbleFighterModule
{
    /// <inheritdoc />
    public async ValueTask<ILaunchBootstrapLease?> PrepareBootstrapAsync(LaunchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.AutomaticSelection is { } automatic)
        {
            if (!SupportsAutomaticSelection(context.Build) || context.GameEndpoint is not null || context.GameChoices.Count != 0)
                throw new InvalidDataException("Native automatic selection cannot use a direct endpoint or presentation adapter.");
            return await GatewayBootstrap.StartAsync(GatewayBootstrap.ExecutablePath(), context.Build.Id, automatic.Targets,
                cancellationToken, context.Diagnostics, automatic: automatic, launchId: context.LaunchId,
                metadata: NativeCenterNames.Create(context.Build.Id, automatic.Targets, automatic)).ConfigureAwait(false);
        }
        if (context.GameTargets.Count == 0 && context.GameChoices.Count == 0) return null;
        if (context.GameEndpoint is not null) throw new InvalidOperationException("Choose either a direct game endpoint or an in-game server directory.");
        if (context.GameTargets.Count == 0) throw new InvalidOperationException("The in-game server directory has no available destination.");
        var targets = GatewayBootstrap.ProjectOrdinaryTargets(context.GameTargets);
        if (targets.Length == 1 && targets[0].UnavailableCapacity is null)
            return new DirectBootstrapLease(targets[0].Endpoint);
        if (!GatewayBuildCapabilities.SupportsBootstrap(context.Build.Id))
            throw new InvalidDataException("This client build has no audited native Gateway adapter.");
        var executable = GatewayBootstrap.ExecutablePath();
        return await GatewayBootstrap.StartAsync(executable, context.Build.Id, targets, cancellationToken, context.Diagnostics,
            metadata: NativeCenterNames.Create(context.Build.Id, context.GameTargets)).ConfigureAwait(false);
    }

    private sealed class DirectBootstrapLease(ServerEndpoint endpoint) : ILaunchBootstrapLease
    {
        public ServerEndpoint? GameEndpoint { get; } = endpoint;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal static partial class GatewayBootstrap
{
    internal const int ConnectionTraceMaximumBytes = 64 * 1024;
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out int processId);

    private const int MaximumFrameBytes = 65536;
    // Confirm listener readiness before starting the game, within the shared launch deadline.
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    internal static GameServerTarget[] ValidateTargets(IReadOnlyList<GameServerTarget> targets)
    {
        if (targets.Count < 1) throw new InvalidOperationException("The in-game server directory must contain an entry.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var endpoints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (target.Id.Length is < 1 or > 128 || !target.Id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
                || target.Name.Length is < 1 or > 128 || target.Name.Any(char.IsControl)
                || !ValidEndpointHost(target)
                || target.UnavailableCapacity is < 1 or > int.MaxValue / 2
                || target.StatusCapacity is < 1 or > int.MaxValue / 2
                || target.UnavailableCapacity is not null && target.NativeCenterId is not > 0
                || target.Endpoint.Port is < 1 or > 65535 || !ids.Add(target.Id)
                || !endpoints.Add(target.Endpoint.Host.ToLowerInvariant() + ":" + target.Endpoint.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                throw new InvalidOperationException("The in-game server directory contains an invalid or duplicate target.");
        }
        return targets.ToArray();
    }

    private static bool ValidEndpointHost(GameServerTarget target) =>
        IPAddress.TryParse(target.Endpoint.Host, out var address) && address.AddressFamily == AddressFamily.InterNetwork
        || target.UnavailableCapacity is not null && target.Endpoint.Host is { Length: > 0 and <= 253 } host
        && host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-') && Uri.CheckHostName(host) == UriHostNameType.Dns;

    internal static GameServerTarget[] ProjectOrdinaryTargets(IReadOnlyList<GameServerTarget> targets)
    {
        if (targets.Count < 1)
            throw new InvalidOperationException("The in-game server directory must contain an entry.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var physical = new Dictionary<ServerEndpoint, GameServerTarget>();
        foreach (var target in targets)
        {
            ValidateTargets([target]);
            if (!ids.Add(target.Id)) throw new InvalidOperationException("The in-game server directory repeats a selector identity.");
            var endpoint = target.Endpoint with { Host = IPAddress.TryParse(target.Endpoint.Host, out var address) ? address.ToString() : target.Endpoint.Host };
            if (physical.TryGetValue(endpoint, out var previous))
            {
                if (string.IsNullOrEmpty(target.BackendId) || previous.BackendId != target.BackendId)
                    throw new InvalidOperationException("A physical server endpoint has conflicting backend identities.");
                throw new InvalidOperationException("Distinct native selector entries cannot be silently collapsed onto one physical endpoint.");
            }
            physical.Add(endpoint, target with { Endpoint = endpoint });
        }
        // Original native choosers connect to physical servers; automatic admission supplies its own native Center.
        return ValidateTargets(physical.Values.Select(target => targets.Any(row => row.UnavailableCapacity is not null)
            || targets.All(row => row.StatusCapacity is not null && row.NativeCenterId is not null)
            ? target : target with { BackendId = null, NativeCenterId = null }).ToArray());
    }

    internal static string ExecutablePath()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Fantasy Tale Gateway is available only on Windows.");
        var rid = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            Architecture.X86 => "win-x86",
            _ => throw new PlatformNotSupportedException("This Windows architecture has no Fantasy Tale Gateway build."),
        };
        var path = Path.Combine(AppContext.BaseDirectory, "gateway", rid, "FantasyTale.Gateway.exe");
        return File.Exists(path) ? path : throw new FileNotFoundException("Fantasy Tale Gateway is missing from the Launcher package.", path);
    }

    internal static async Task<ILaunchBootstrapLease> StartAsync(string executable, string build,
        IReadOnlyList<GameServerTarget> targets, CancellationToken cancellationToken, ILaunchDiagnosticSink? diagnostics = null,
        IAutomaticServerSelection? automatic = null, string? launchId = null, LaunchBootstrapMetadata? metadata = null)
    {
        byte[]? automaticConfiguration = null;
        if (automatic is not null)
        {
            var initial = await automatic.RefreshAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Native server admission closed before Gateway startup.");
            automaticConfiguration = BuildAutomaticConfiguration(build, launchId, automatic, initial);
        }
        var configuration = automaticConfiguration ?? BuildConfiguration(build, targets);
        var controlBudget = ControlMessageBudget(configuration, automatic, launchId);
        Record(diagnostics, "gateway-start-requested", "build-" + build + "-targets-" + targets.Count);
        var pipeName = "fantasy-tale-gateway-" + Guid.NewGuid().ToString("N");
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--pipe"); start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add("--control-max-bytes"); start.ArgumentList.Add(controlBudget.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Process? process = null;
        Task? stderrDrain = null;
        Task? exitObservation = null;
        var stderr = new StringBuilder(2048);
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("Fantasy Tale Gateway did not start.");
            Record(diagnostics, "gateway-process-started", "pid-" + process.Id);
            exitObservation = ObserveExitAsync(process, diagnostics);
            stderrDrain = DrainStandardErrorAsync(process.StandardError, stderr, diagnostics);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ReadyTimeout);
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            // The pipe name is visible on the Gateway command line, and CurrentUserOnly still admits any
            // same-user process. Send the admission configuration only to the Gateway this call started,
            // as the hook and broker pipes already require.
            if (!OperatingSystem.IsWindows() || !GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var peer) || peer != process.Id)
                throw new InvalidDataException("The Gateway control pipe was connected by a different process.");
            Record(diagnostics, "gateway-pipe-connected");
            await WriteFrameAsync(pipe, configuration, timeout.Token).ConfigureAwait(false);
            Record(diagnostics, "gateway-config-sent", "bytes-" + configuration.Length + "-targets-" + targets.Count);
            using var ready = await ReadFrameAsync(pipe, timeout.Token).ConfigureAwait(false);
            var endpoint = ParseReady(ready.RootElement, build);
            Record(diagnostics, "gateway-ready", "port-" + endpoint.Port);
            var lease = new GatewayBootstrapLease(process, pipe, endpoint, stderr, stderrDrain, exitObservation, diagnostics, metadata);
            if (automatic is not null) lease.StartRefresh(automatic, launchId!);
            return lease;
        }
        catch (OperationCanceledException error)
        {
            await StopFailedStartAsync(process, pipe, stderrDrain, exitObservation).ConfigureAwait(false);
            Record(diagnostics, "gateway-start-failed", error.GetType().Name + "-" + StderrDetail(stderr));
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException("Fantasy Tale Gateway did not become ready within thirty seconds. " + Diagnostic(stderr), error);
        }
        catch (EndOfStreamException error)
        {
            await StopFailedStartAsync(process, pipe, stderrDrain, exitObservation).ConfigureAwait(false);
            Record(diagnostics, "gateway-start-failed", error.GetType().Name + "-" + StderrDetail(stderr));
            throw new InvalidDataException("Fantasy Tale Gateway closed the control pipe before READY. " + Diagnostic(stderr), error);
        }
        catch (Exception error)
        {
            await StopFailedStartAsync(process, pipe, stderrDrain, exitObservation).ConfigureAwait(false);
            Record(diagnostics, "gateway-start-failed", error.GetType().Name + "-" + StderrDetail(stderr));
            throw;
        }
    }

    internal static byte[] BuildConfiguration(string build, IReadOnlyList<GameServerTarget> targets) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema_version = 1,
            rows = new[]
            {
                new
                {
                    id = "selector",
                    plugin = "bf-center-selector",
                    config = new
                    {
                        build,
                        connection_trace = true,
                        connection_trace_max_bytes = ConnectionTraceMaximumBytes,
                        native_probe_timeout_ms = targets.All(target => target.StatusCapacity is not null) ? 3000 : 0,
                        native_probe_parallelism = 8,
                        connect_timeout_ms = 120000,
                        centers = targets.Select(target => new
                        {
                            id = target.Id,
                            name = target.Name,
                            short_name = target.ShortName,
                            host = target.Endpoint.Host,
                            port = target.Endpoint.Port,
                            backend_id = target.BackendId,
                            native_center_id = target.NativeCenterId,
                            unavailable_capacity = target.UnavailableCapacity,
                            status_capacity = target.StatusCapacity,
                        }),
                    },
                },
            },
        });

    private static async Task WriteFrameAsync(Stream stream, byte[] body, CancellationToken cancellationToken)
    {
        if (body.Length < 1) throw new InvalidDataException("Gateway configuration is empty.");
        var header = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)body.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length is < 1 or > MaximumFrameBytes) throw new InvalidDataException("Gateway returned an invalid control frame length.");
        var body = new byte[checked((int)length)]; await stream.ReadExactlyAsync(body, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    private static ServerEndpoint ParseReady(JsonElement root, string build)
    {
        if (root.GetProperty("schema_version").GetInt32() != 1 || root.GetProperty("type").GetString() != "ready")
            throw new InvalidDataException("Gateway returned an unsupported readiness message.");
        var rows = root.GetProperty("rows");
        if (rows.GetArrayLength() != 1) throw new InvalidDataException("Gateway returned an unexpected readiness row count.");
        var row = rows[0];
        if (row.GetProperty("id").GetString() != "selector" || row.GetProperty("build").GetString() != build)
            throw new InvalidDataException("Gateway readiness does not match the selected client build.");
        var mode = row.GetProperty("mode").GetString();
        if (mode != "bootstrap") throw new InvalidDataException("Gateway did not open a native selector endpoint for multiple servers.");
        var host = row.GetProperty("host").GetString();
        if (host != "127.0.0.1" || !row.GetProperty("port").TryGetInt32(out var port) || port is < 1 or > 65535)
            throw new InvalidDataException("Gateway returned an invalid loopback endpoint.");
        return new ServerEndpoint(host, port);
    }

    internal static async Task DrainStandardErrorAsync(TextReader reader, StringBuilder captured, ILaunchDiagnosticSink? diagnostics)
    {
        var buffer = new char[512];
        var line = new StringBuilder(512);
        var overflow = false;
        var recordedBytes = 0;
        var limitReported = false;
        var placeholders = new HashSet<string>(StringComparer.Ordinal);
        void Flush()
        {
            if (line.Length == 0 && !overflow) return;
            var safe = overflow ? "suppressed-overlong-line" : GatewayDiagnostic.Sanitize(line.ToString());
            lock (captured)
            {
                // Retain the latest phases even when stderr continues after READY.
                if (captured.Length + safe.Length + 1 > 2048) captured.Remove(0, safe.Length + 1);
                captured.Append(safe).Append('\n');
            }
            // Repeated untrusted lines must not consume the connection-lifecycle evidence budget.
            var placeholder = safe.StartsWith("suppressed-", StringComparison.Ordinal) || safe == "empty";
            if (!placeholder || placeholders.Add(safe))
            {
                if (safe.Length + 1 <= ConnectionTraceMaximumBytes - recordedBytes)
                {
                    Record(diagnostics, "gateway-stderr", safe);
                    recordedBytes += safe.Length + 1;
                }
                else if (!limitReported)
                {
                    Record(diagnostics, "gateway-stderr-limit", "bytes-65536");
                    limitReported = true;
                }
            }
            line.Clear(); overflow = false;
        }
        while (true)
        {
            var read = await reader.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0) { Flush(); return; }
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == '\n') Flush();
                else if (line.Length < 512) line.Append(buffer[i]);
                else overflow = true;
            }
        }
    }

    private static string Diagnostic(StringBuilder stderr)
    {
        lock (stderr) return stderr.Length == 0 ? string.Empty : "Gateway: " + stderr.ToString();
    }

    private static string StderrDetail(StringBuilder stderr)
    {
        lock (stderr) return "stderr-chars-" + stderr.Length;
    }

    private static void Record(ILaunchDiagnosticSink? diagnostics, string name, string? detail = null)
    {
        try { diagnostics?.Record(name, detail); }
        catch (Exception) { /* Diagnostics cannot change launch ownership or cleanup. */ }
    }

    private static async Task ObserveExitAsync(Process process, ILaunchDiagnosticSink? diagnostics)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            Record(diagnostics, "gateway-process-exited", "pid-" + process.Id + "-code-" + process.ExitCode);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
        { Record(diagnostics, "gateway-exit-observation-failed", error.GetType().Name); }
    }

    private static async Task StopFailedStartAsync(Process? process, NamedPipeServerStream pipe, Task? stderrDrain, Task? exitObservation)
    {
        await pipe.DisposeAsync().ConfigureAwait(false);
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            if (stderrDrain is not null) await stderrDrain.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            if (exitObservation is not null) await exitObservation.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException or ObjectDisposedException) { }
        finally { process.Dispose(); }
    }

    private sealed class GatewayBootstrapLease(Process process, NamedPipeServerStream pipe, ServerEndpoint endpoint,
        StringBuilder stderr, Task stderrDrain, Task exitObservation, ILaunchDiagnosticSink? diagnostics,
        LaunchBootstrapMetadata? metadata) : ILaunchBootstrapLease
    {
        private int _disposed;
        private readonly SemaphoreSlim _writes = new(1, 1);
        private CancellationTokenSource? _refreshCancellation;
        private Task? _refreshTask;
        public ServerEndpoint? GameEndpoint { get; } = endpoint;
        public LaunchBootstrapMetadata? Metadata { get; } = metadata;

        public async ValueTask CompleteHandoffAsync(CancellationToken cancellationToken)
        {
            // The sidecar bounds its native handshakes and auxiliary retirement. Keep refresh
            // and the pipe alive until it exits; canceling refresh would send STOP early.
            Record(diagnostics, "gateway-handoff-waiting");
            await exitObservation.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!process.HasExited) throw new IOException("Gateway retirement could not confirm process exit.");
            Record(diagnostics, "gateway-handoff-retired");
            await DisposeAsync().ConfigureAwait(false);
        }

        internal void StartRefresh(IAutomaticServerSelection selection, string launchId)
        {
            _refreshCancellation = new CancellationTokenSource();
            // Admission expiry withdraws new automatic choices. Only lease disposal
            // cancels native cleanup connections that the running game still owns.
            _refreshTask = RefreshAutomaticAsync(selection, launchId, pipe, exitObservation, _writes,
                diagnostics, _refreshCancellation.Token);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_refreshCancellation is not null)
            {
                await _refreshCancellation.CancelAsync().ConfigureAwait(false);
                if (_refreshTask is not null) await _refreshTask.ConfigureAwait(false);
                _refreshCancellation.Dispose();
            }
            try
            {
                if (pipe.IsConnected && !process.HasExited)
                {
                    Record(diagnostics, "gateway-stop-requested");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await WriteSerializedAsync(pipe, "{\"type\":\"stop\"}"u8.ToArray(), _writes, timeout.Token).ConfigureAwait(false);
                    Record(diagnostics, "gateway-control-sent", "stop");
                }
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException or OperationCanceledException)
            { Record(diagnostics, "gateway-control-failed", error.GetType().Name); }
            await pipe.DisposeAsync().ConfigureAwait(false);
            Record(diagnostics, "gateway-pipe-closed");
            try
            {
                if (!process.HasExited)
                {
                    var exited = process.WaitForExitAsync();
                    if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false) != exited)
                    {
                        Record(diagnostics, "gateway-kill-requested", "stop-timeout");
                        process.Kill(entireProcessTree: true);
                    }
                }
                if (!process.HasExited) await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                await exitObservation.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                await stderrDrain.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                var exitCode = process.ExitCode;
                Record(diagnostics, exitCode == 0 ? "gateway-exited" : "gateway-exit-failed", "code-" + exitCode + "-" + StderrDetail(stderr));
                if (exitCode != 0) throw new IOException("Fantasy Tale Gateway exited with code " + exitCode + ". " + Diagnostic(stderr));
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException or ObjectDisposedException)
            { Record(diagnostics, "gateway-exit-failed", error.GetType().Name + "-" + StderrDetail(stderr)); throw new IOException("Fantasy Tale Gateway did not stop cleanly. " + Diagnostic(stderr), error); }
            finally { process.Dispose(); _writes.Dispose(); }
        }
    }
}

internal static class GatewayDiagnostic
{
    private const int MaximumLength = 120;

    internal static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "empty";
        if (value.Length > 512) return "suppressed-overlong-line";
        // Only known metadata grammar survives; arbitrary stderr may contain an unlabelled credential.
        var line = value.Trim();
        var start = line.IndexOf("bf-center-selector:", StringComparison.Ordinal);
        if (start >= 0 && start <= 48) line = line[start..];
        var auxiliary = System.Text.RegularExpressions.Regex.Match(line,
            @"\Abf-center-selector: native auxiliary request observed; opcode=([0-9]{1,5})\.\z",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        var automatic = System.Text.RegularExpressions.Regex.IsMatch(line,
            @"\Abf-center-selector: automatic (?:auxiliary accepted|native login received|native status sent; available=(?:True|False))\.\z",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        var connection = System.Text.RegularExpressions.Regex.Match(line,
            @"\Abf-center-selector: connection role=(?:bootstrap|automatic|unavailable) port=([0-9]{1,5}) event=(?:listening|accepted|status-sent|uniqueness-received|uniqueness-replied|eof|closed|accept-stopped|cancelled|failed|directory-sent|login-received|redirect-sent|promotion-acknowledged|unavailable|deadline)\.\z",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        var binding = System.Text.RegularExpressions.Regex.Match(line,
            @"\Abf-center-selector: binding role=(?:automatic|unavailable) port=([0-9]{1,5}) group=([0-9]{1,10}) sub=([0-9]{1,10})\.\z",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        var allowed = connection.Success && ushort.TryParse(connection.Groups[1].Value, out var localPort) && localPort != 0
            || binding.Success && ushort.TryParse(binding.Groups[1].Value, out var bindingPort) && bindingPort != 0
                && uint.TryParse(binding.Groups[2].Value, out _) && uint.TryParse(binding.Groups[3].Value, out _)
            || auxiliary.Success && ushort.TryParse(auxiliary.Groups[1].Value, out _) || automatic || System.Text.RegularExpressions.Regex.IsMatch(line,
            @"\Abf-center-selector: (?:bootstrap accepted|native greeting sent|bootstrap connection released|native directory sent|unavailable auxiliary retirement started|native startup exchange answered; opcode=[0-9]{1,10}|pre-handshake opcode accepted; opcode=[0-9]{1,10}|bootstrap failure; phase=(?:connect|handshake|selection), kind=(?:PacketFormatException|EndOfStreamException|IOException|SocketException|InvalidOperationException)|automatic failure; phase=(?:connect|status|selection|release|freshness), kind=(?:PacketFormatException|EndOfStreamException|IOException|SocketException|InvalidOperationException)|native marker accepted|native keep-alive accepted|automatic awaiting fresh snapshot|automatic native redirect sent|automatic source promotion acknowledged|automatic source released|automatic unavailable; manual bootstrap retained|incomplete bootstrap EOF; phase=(?:connect|handshake|selection), pending_bytes=[0-9]{1,10}|rejected bootstrap frame; phase=(?:connect|handshake|selection), opcode=[0-9]{1,10}, payload_bytes=[0-9]{1,10}|rejected automatic frame; opcode=[0-9]{1,10}, payload_bytes=[0-9]{1,10})\.\z",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        if (!allowed)
        {
            var deadline = System.Text.RegularExpressions.Regex.Match(line,
                @"\AFantasy Tale Gateway failed: TimeoutException: The game's (connect|handshake|selection) deadline expired\.\z",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            if (deadline.Success) return "deadline-" + deadline.Groups[1].Value;
            var automaticDeadline = System.Text.RegularExpressions.Regex.Match(line,
                @"\AFantasy Tale Gateway failed: TimeoutException: Automatic Center (connect|status|selection|release) deadline expired\.\z",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            if (automaticDeadline.Success) return "automatic-deadline-" + automaticDeadline.Groups[1].Value;
            return line is "Fantasy Tale Gateway ready." or "Fantasy Tale Gateway stopped." or "Fantasy Tale Gateway log budget exhausted."
                ? line.Replace(' ', '-') : "suppressed-unrecognized-line";
        }
        return string.Concat(line.Take(MaximumLength).Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));
    }
}

