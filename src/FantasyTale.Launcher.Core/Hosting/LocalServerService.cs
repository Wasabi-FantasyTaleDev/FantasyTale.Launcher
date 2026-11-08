using System.Diagnostics;
using System.Text;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Servers;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Hosting;

/// <summary>Owns local server processes, readiness, bounded logs and graceful or forced cleanup.</summary>
public sealed class LocalServerService : IAsyncDisposable
{
    private sealed class Run(LocalServerProfile profile, LocalServerPlan plan, Process process, ServerProcessJob job, FileStream lease, LocalServerData.Lease data, string log)
    {
        internal readonly LocalServerProfile Profile = profile;
        internal readonly LocalServerPlan Plan = plan;
        internal readonly Process Process = process;
        internal readonly ServerProcessJob Job = job;
        internal readonly FileStream Lease = lease;
        internal readonly LocalServerData.Lease Data = data;
        internal readonly object Sync = new();
        internal readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Fault = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly Queue<string> Lines = new();
        internal LocalServerStatus Status = new(profile.Id, Guid.NewGuid().ToString("N"), "starting", process.Id,
            DateTimeOffset.UtcNow, null, null, false, null, null, log);
        internal readonly TaskCompletionSource Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Completion => Completed.Task;
    }

    private readonly string _root;
    private readonly LocalServerData _data;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Run> _runs = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>Opens separate bounded invocation and stable save roots; no server is automatically started.</summary>
    public LocalServerService(string root, string? dataRoot = null)
    {
        _root = Path.GetFullPath(root); InstanceStore.RejectLinks(_root);
        var data = Path.GetFullPath(dataRoot ?? Path.TrimEndingDirectorySeparator(_root) + "-data");
        if (LocalServerData.Overlap(_root, data)) throw new ArgumentException("Hosting logs and saved data require separate directories.");
        Directory.CreateDirectory(_root); _data = new(data);
    }

    /// <summary>Returns snapshots of the most recent invocation of each profile.</summary>
    public IReadOnlyList<LocalServerStatus> List()
    {
        lock (_runs) return _runs.Values.Select(r => { lock (r.Sync) return r.Status; }).ToArray();
    }

    /// <summary>Returns the immutable settings used to start one exact invocation, independently of saved edits.</summary>
    public LocalServerProfile StartedProfile(string id, string runId) => Find(id, runId).Profile;

    /// <summary>Returns bounded recent process output for one exact invocation.</summary>
    public IReadOnlyList<string> Log(string id, string runId)
    { var run = Find(id, runId); lock (run.Sync) return run.Lines.ToArray(); }

    /// <summary>Returns a ready transient destination, optionally restricted to one exact owned invocation.</summary>
    public ServerDefinition? Destination(string id, string? runId = null)
    {
        lock (_runs)
        {
            if (!_runs.TryGetValue(id, out var run)) return null;
            lock (run.Sync)
                return (runId is null || run.Status.RunId == runId) && run.Status.State == "ready" &&
                    !run.Stop.Task.IsCompleted && !run.Fault.Task.IsCompleted && !run.Process.HasExited
                    ? new("local-" + id, run.Profile.Game, run.Profile.Name, run.Plan.ApiUrl,
                        new(run.Profile.Mode == "lan" ? run.Profile.AdvertisedHost : "127.0.0.1", run.Profile.GamePort), true, true) { Kind = "room" }
                    : null;
        }
    }

    /// <summary>Starts one module-produced plan and returns immediately while its readiness check runs.</summary>
    public Task<LocalServerStatus> StartAsync(LocalServerProfile profile, GameDefinition game, ILocalServerModule module, CancellationToken ct = default)
        => StartAsync(profile, game, module, null, ct);

    /// <summary>Starts a plan using a registered client's read-only resources and an owned save directory.</summary>
    public async Task<LocalServerStatus> StartAsync(LocalServerProfile profile, GameDefinition game, ILocalServerModule module, string? clientDirectory, CancellationToken ct = default)
    {
        game.RequireLocalHostingEnabled();
        if (profile.Mode == "room") game.RequireRoomsEnabled();
        LocalServerNetwork.RequireAssigned(profile);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this); LocalServerProfiles.Validate(profile);
            if (game.Id != profile.Game) throw new ArgumentException("Hosting game does not match its module.");
            var build = game.Builds.SingleOrDefault(b => b.Id == profile.Build) ?? throw new ArgumentException("Unsupported hosting build.");
            build.RequireEnabled();
            lock (_runs)
            {
                if (_runs.TryGetValue(profile.Id, out var prior) && !prior.Completion.IsCompleted) throw new InvalidOperationException("This local server is already running.");
            }
            var package = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile.PackagePath));
            if (!Directory.Exists(package) || InstanceStore.IsWithin(package, _root) || InstanceStore.IsWithin(_root, package) || package.Equals(_root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Select an installed server package outside the hosting data directory.");
            _data.ValidatePackage(package);
            var leasePath = Path.Combine(package, ".fantasytale-host.lock"); InstanceStore.RejectLinks(leasePath);
            var lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            ServerProcessJob? job = null; Process? process = null; LocalServerData.Lease? data = null;
            try
            {
                data = _data.Acquire(profile);
                var session = Path.Combine(_root, profile.Id); InstanceStore.RejectLinks(session);
                Directory.CreateDirectory(session);
                var plan = module.CreateServerPlan(new(game, build, profile, session) { DataDirectory = data.DirectoryPath, ClientDirectory = clientDirectory });
                ValidatePlan(package, plan, profile);
                foreach (var file in plan.Files)
                {
                    var path = Path.Combine(session, file.Name); InstanceStore.RejectLinks(path);
                    File.WriteAllText(path, file.Text, new UTF8Encoding(false));
                }
                var log = Path.Combine(session, "server.log"); InstanceStore.RejectLinks(log); InstanceStore.RejectLinks(log + ".1");
                if (File.Exists(log)) File.Move(log, log + ".1", true);
                job = new ServerProcessJob();
                var start = new ProcessStartInfo(plan.Executable)
                {
                    WorkingDirectory = package, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                foreach (var argument in plan.Arguments) start.ArgumentList.Add(argument);
                foreach (var pair in plan.Environment) start.Environment[pair.Key] = pair.Value;
                ct.ThrowIfCancellationRequested();
                process = Process.Start(start) ?? throw new IOException("Unable to start the local server.");
                job.Assign(process);
                var run = new Run(profile, plan, process, job, lease, data, log);
                lock (_runs) _runs[profile.Id] = run;
                _ = CompleteAsync(run);
                lock (run.Sync) return run.Status;
            }
            catch
            {
                try
                {
                    if (process is not null)
                    {
                        try { process.StandardInput.Close(); if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } }
                        finally { process.Dispose(); }
                    }
                }
                finally { try { job?.Dispose(); } finally { data?.Dispose(); lease.Dispose(); } }
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Stops only the selected invocation; an old UI request cannot terminate a newer run.</summary>
    public async Task<LocalServerStatus> StopAsync(string id, string runId)
    {
        var run = Find(id, runId); run.Stop.TrySetResult(); await run.Completion.ConfigureAwait(false);
        lock (run.Sync) return run.Status;
    }

    /// <summary>Requests graceful shutdown for every owned server and waits for bounded cleanup.</summary>
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        Run[] runs;
        try { _disposed = true; lock (_runs) runs = _runs.Values.ToArray(); foreach (var run in runs) run.Stop.TrySetResult(); }
        finally { _gate.Release(); }
        await Task.WhenAll(runs.Select(r => r.Completion)).ConfigureAwait(false);
    }

    /// <summary>Stops owned invocations without disposing the service, so a failed application exit can be retried.</summary>
    public async Task StopAllAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Run[] runs;
            lock (_runs) runs = _runs.Values.ToArray();
            foreach (var run in runs) run.Stop.TrySetResult();
            await Task.WhenAll(runs.Select(r => r.Completion)).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private Run Find(string id, string runId)
    {
        lock (_runs)
            return _runs.TryGetValue(id, out var run) && run.Status.RunId == runId ? run : throw new InvalidOperationException("The local server invocation changed. Refresh its status.");
    }

    private static void ValidatePlan(string package, LocalServerPlan plan, LocalServerProfile profile)
    {
        if (!Path.IsPathFullyQualified(plan.Executable) || !InstanceStore.IsWithin(package, plan.Executable) || !File.Exists(plan.Executable))
            throw new InvalidOperationException("The server executable must exist inside its selected package.");
        InstanceStore.RejectLinks(plan.Executable);
        if (plan.StartupSeconds is < 1 or > 120 || plan.ShutdownSeconds is < 1 or > 60 ||
            plan.Arguments is null || plan.Arguments.Any(argument => argument is null || argument.Contains('\0')))
            throw new InvalidOperationException("Server process limits are invalid.");
        var admin = ServerStore.ApiBase(plan.AdminUrl); var api = ServerStore.ApiBase(plan.ApiUrl);
        if (!admin.IsLoopback || admin.Scheme != "http" || api.Scheme != "http" ||
            !api.IsLoopback && !(profile.Mode == "lan" && api.DnsSafeHost == profile.AdvertisedHost))
            throw new InvalidOperationException("Administration must use loopback HTTP; the host-player API must use loopback or the selected local LAN address.");
        if (plan.Files is null || plan.Files.Any(file => file is null || file.Name is null || file.Text is null) ||
            plan.Files.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Files.Count ||
            plan.Files.Any(f => f.Name.Length == 0 || f.Name is "." or ".." || f.Name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')) || f.Name.EndsWith('.') || f.Name.StartsWith("server.log", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Generated server configuration is invalid.");
        foreach (var file in plan.Files) _ = ClientRelease.ValidatePath(file.Name);
        if (plan.Environment is null ||
            plan.Environment.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Environment.Count ||
            plan.Environment.Any(p => string.IsNullOrEmpty(p.Key) || p.Key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') || p.Value is null || p.Value.Contains('\0')))
            throw new InvalidOperationException("Server environment overrides are invalid.");
    }

    private static async Task CompleteAsync(Run run)
    {
        try { await MonitorAsync(run).ConfigureAwait(false); run.Completed.TrySetResult(); }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException or UnauthorizedAccessException)
        {
            lock (run.Sync) run.Status = run.Status with { State = "failed", Failure = "process-failed", ApiUrl = null, AdminUrl = null };
            // Failed tree verification must not unlock a package or save still potentially used by a descendant.
            run.Job.Dispose(); run.Process.Dispose();
            run.Completed.TrySetException(error);
        }
    }

    private static async Task MonitorAsync(Run run)
    {
        using var reads = new CancellationTokenSource();
        var stdout = PumpAsync(run, run.Process.StandardOutput, true, reads.Token);
        var stderr = PumpAsync(run, run.Process.StandardError, false, reads.Token);
        var exited = run.Process.WaitForExitAsync();
        string? failure = null; var forced = false;
        using var startup = new CancellationTokenSource();
        var timeout = Task.Delay(TimeSpan.FromSeconds(run.Plan.StartupSeconds), startup.Token);
        try
        {
            var first = await Task.WhenAny(run.Ready.Task, exited, run.Stop.Task, run.Fault.Task, timeout).ConfigureAwait(false);
            if (first == run.Ready.Task && !run.Process.HasExited && !run.Stop.Task.IsCompleted && !run.Fault.Task.IsCompleted)
            {
                lock (run.Sync) run.Status = run.Status with { State = "ready", ApiUrl = run.Plan.ApiUrl, AdminUrl = run.Plan.AdminUrl };
                await Task.WhenAny(exited, run.Stop.Task, run.Fault.Task).ConfigureAwait(false);
            }
            else if (first == timeout) failure = "startup-timeout";
            else if (!run.Stop.Task.IsCompleted) failure = "startup-failed";
            if (run.Fault.Task.IsCompleted) failure = "log-failed";
            if (!run.Process.HasExited)
            {
                lock (run.Sync) run.Status = run.Status with { State = "stopping", ApiUrl = null, AdminUrl = null };
                using var write = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await run.Process.StandardInput.WriteLineAsync("stop".AsMemory(), write.Token).ConfigureAwait(false); await run.Process.StandardInput.FlushAsync(write.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
                run.Process.StandardInput.Close();
                try { await exited.WaitAsync(TimeSpan.FromSeconds(run.Plan.ShutdownSeconds)).ConfigureAwait(false); }
                catch (TimeoutException)
                { forced = true; run.Job.Terminate(); await exited.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
            }
            await exited.ConfigureAwait(false);
            if (failure is null && !run.Stop.Task.IsCompleted) failure = "unexpected-exit";
            if (failure is null && run.Process.ExitCode != 0 && !forced) failure = "shutdown-failed";
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            failure = "process-failed";
            if (!run.Process.HasExited) { forced = true; run.Job.Terminate(); await exited.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
        }
        finally
        {
            startup.Cancel();
            lock (run.Sync) run.Status = run.Status with { State = "stopping", ApiUrl = null, AdminUrl = null };
            forced |= await run.Job.DrainAsync().ConfigureAwait(false);
            run.Job.Dispose();
            reads.CancelAfter(TimeSpan.FromSeconds(2));
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            if (run.Fault.Task.IsCompleted && failure is null) failure = "log-failed";
            var exitCode = run.Process.HasExited ? (int?)run.Process.ExitCode : null;
            run.Process.Dispose(); run.Data.Dispose(); run.Lease.Dispose();
            lock (run.Sync) run.Status = run.Status with
            {
                State = failure is null ? "stopped" : "failed", ExitCode = exitCode,
                Failure = failure, ForcedStop = forced, ApiUrl = null, AdminUrl = null,
            };
        }
    }

    private static async Task PumpAsync(Run run, StreamReader reader, bool stdout, CancellationToken ct)
    {
        var chars = new char[1024]; var line = new StringBuilder(); var truncated = false;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(chars.AsMemory(), ct).ConfigureAwait(false)) != 0)
                for (var i = 0; i < count; i++)
                {
                    var c = chars[i];
                    if (c == '\n')
                    {
                        var value = line.ToString().TrimEnd('\r');
                        if (stdout && !truncated && value == "FT_HOST_READY 1") run.Ready.TrySetResult();
                        WriteLine(run, stdout, value + (truncated ? " [truncated]" : ""));
                        line.Clear(); truncated = false;
                    }
                    else if (line.Length < 4096) line.Append(c); else truncated = true;
                }
            if (line.Length > 0) WriteLine(run, stdout, line.ToString());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or ObjectDisposedException or UnauthorizedAccessException) { run.Fault.TrySetResult(); }
    }

    private static void WriteLine(Run run, bool stdout, string line)
    {
        lock (run.Sync)
        {
            var text = $"{DateTimeOffset.UtcNow:O} {(stdout ? "out" : "err")} {line}";
            if (run.Lines.Count == 128) run.Lines.Dequeue(); run.Lines.Enqueue(text);
            var bytes = Encoding.UTF8.GetBytes(text + "\n"); var path = run.Status.LogPath;
            InstanceStore.RejectLinks(path); InstanceStore.RejectLinks(path + ".1");
            if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > 2 * 1024 * 1024) File.Move(path, path + ".1", true);
            using var output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read); output.Write(bytes);
        }
    }
}
