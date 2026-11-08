using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Downloads;
using Microsoft.Win32.SafeHandles;

namespace FantasyTale.Launcher.Core.Updates;

/// <summary>Owns the external update process and the exact-process startup acknowledgement protocol.</summary>
public static partial class LauncherUpdateHandoff
{
    private sealed record Request(int Schema, string Installation, string DataRoot, int ParentId, long ParentStart, string Pipe, string ManifestHash,
        string Operation = "install", string? JournalHash = null, bool ShowErrors = true);
    internal sealed record Outcome(string Status, string? SourceIdentity = null, string? ManifestSha256 = null,
        string? Failure = null, string? Phase = null, int? ErrorCode = null);
    private const string HelperExecutable = "FantasyTale.Launcher.Updater.exe";

    /// <summary>Copies the installed helper outside the installation and waits for its validation before the host may exit.</summary>
    public static async Task PrepareAsync(string installation, string dataRoot, string updateRoot, string manifestSha256, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Launcher restart updates currently require Windows.");
        installation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation));
        var expectedRoot = UpdateFiles.Safe(dataRoot, "launcher-update");
        if (!string.Equals(Path.GetFullPath(updateRoot), expectedRoot, StringComparison.OrdinalIgnoreCase) || InstanceStore.IsWithin(installation, expectedRoot))
            throw new InvalidOperationException("Update staging must be outside the launcher installation.");
        using var current = Process.GetCurrentProcess();
        if (!SamePath(current.MainModule!.FileName, UpdateFiles.Safe(installation, LauncherRelease.Executable)))
            throw new InvalidOperationException("Restart updates require the installed launcher executable.");
        var policy = LauncherUpdatePolicy.LoadSelected(installation, updateRoot) ?? throw new InvalidDataException("Launcher update source is missing.");
        var release = policy.ReadStagedRelease(updateRoot);
        if (release.ManifestSha256 != manifestSha256) throw new InvalidDataException("The staged release changed after download completion.");
        var helperRoot = UpdateFiles.Safe(updateRoot, "helper"); Directory.CreateDirectory(helperRoot);
        var source = UpdateFiles.Safe(installation, "updater");
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToArray();
        if (files.Length == 0) throw new IOException("Installed updater files are missing.");
        foreach (var file in files)
        {
            InstanceStore.RejectLinks(file);
            var target = UpdateFiles.Safe(helperRoot, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
        var pipeName = "ft-update-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        using var pipe = Server(pipeName);
        var requestPath = UpdateFiles.Safe(updateRoot, "handoff.json");
        UpdateFiles.Write(requestPath, new Request(1, installation, Path.GetFullPath(dataRoot), current.Id, current.StartTime.ToUniversalTime().Ticks, pipeName, release.ManifestSha256));
        var start = new ProcessStartInfo(UpdateFiles.Safe(helperRoot, HelperExecutable)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = helperRoot };
        start.ArgumentList.Add(requestPath);
        using var helper = Process.Start(start) ?? throw new IOException("Unable to start the launcher updater.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            var connected = pipe.WaitForConnectionAsync(deadline.Token);
            if (await Task.WhenAny(connected, helper.WaitForExitAsync(deadline.Token)).ConfigureAwait(false) != connected)
                throw new IOException("The updater exited before accepting the handoff.");
            await connected.ConfigureAwait(false); VerifyPeer(pipe, helper.Id);
            var ready = new byte[1]; await pipe.ReadExactlyAsync(ready, deadline.Token).ConfigureAwait(false);
            if (ready[0] != 1) throw new IOException("The updater rejected the handoff.");
        }
        catch
        {
            // The parent remains alive, so this owned helper cannot have started replacement.
            if (!helper.HasExited) { helper.Kill(); await helper.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            throw;
        }
    }

    /// <summary>Called by the copied helper; validates ownership, waits for exit, installs and rolls back an unhealthy child.</summary>
    public static async Task<int> RunAsync(string requestPath)
    {
        if (!OperatingSystem.IsWindows()) return 1;
        requestPath = Path.GetFullPath(requestPath); InstanceStore.RejectLinks(requestPath);
        var root = Path.GetDirectoryName(requestPath)!;
        Process? child = null; var parentExited = false; var replacing = false; var committed = false; Request? request = null;
        LauncherUpdatePolicy? policy = null;
        var phase = "handoff";
        try
        {
            request = UpdateFiles.Read<Request>(requestPath) ?? throw new InvalidDataException("Missing update handoff.");
            if (request.Operation == "repair") return await RunRecoveryHandoffAsync(requestPath, request).ConfigureAwait(false);
            if (request.Operation != "install") throw new InvalidDataException("Unknown updater operation.");
            if (request.Schema != 1 || !SamePath(root, UpdateFiles.Safe(request.DataRoot, "launcher-update")) ||
                InstanceStore.IsWithin(request.Installation, root) || !request.Pipe.StartsWith("ft-update-", StringComparison.Ordinal) || request.Pipe.Length != 58 ||
                !request.Pipe.AsSpan(10).ToString().All(char.IsAsciiHexDigit)) throw new InvalidDataException("Invalid updater handoff.");
            using var exclusive = new FileStream(UpdateFiles.Safe(root, "helper.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using var parent = Process.GetProcessById(request.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStart || !SamePath(parent.MainModule!.FileName, UpdateFiles.Safe(request.Installation, LauncherRelease.Executable)))
                throw new InvalidDataException("Updater parent identity changed.");
            policy = LauncherUpdatePolicy.LoadSelected(request.Installation, root) ?? throw new InvalidDataException("Missing launcher update source.");
            policy.ValidateRuntime();
            var release = policy.ReadStagedRelease(root);
            if (release.ManifestSha256 != request.ManifestHash) throw new InvalidDataException("Update selection changed.");
            var installed = ClientInstallTransaction.ReadReceipt(request.Installation);
            if (release.Revision <= Math.Max(policy.MinimumRevision, installed?.Revision ?? 0)) throw new InvalidDataException("Update revision did not advance.");
            var fileCache = new ClientFileCache(request.Installation);
            phase = "staged-verification";
            await VerifyObjectsAsync(root, release, fileCache).ConfigureAwait(false);
            UpdateFiles.Write(UpdateFiles.Safe(root, "outcome.json"), new Outcome("installing", policy.Identity, release.ManifestSha256));
            using (var pipe = new NamedPipeClientStream(".", request.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false); VerifyServer(pipe, parent.Id);
                await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false); await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            phase = "parent-exit";
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90))) await parent.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            parentExited = true;
            phase = "installation-lock";
            Directory.CreateDirectory(ClientInstallTransaction.Workspace(request.Installation));
            using var installationLease = new FileStream(UpdateFiles.Safe(ClientInstallTransaction.Workspace(request.Installation), "self-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            replacing = true;
            await InstallAsync(request.Installation, root, policy, release, async () =>
            {
                var health = Server(request.Pipe + "-health");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                try
                {
                    child = StartLauncher(request, request.Pipe + "-health");
                    var connected = health.WaitForConnectionAsync(timeout.Token);
                    if (await Task.WhenAny(connected, child.WaitForExitAsync(timeout.Token)).ConfigureAwait(false) != connected)
                        throw new IOException("Updated launcher exited before frontend readiness.");
                    await connected.ConfigureAwait(false); VerifyPeer(health, child.Id);
                    var ready = new byte[1]; await health.ReadExactlyAsync(ready, timeout.Token).ConfigureAwait(false);
                    if (ready[0] != 1 || child.HasExited) throw new IOException("Updated launcher did not become ready.");
                    return new HealthAcknowledgement(health, child);
                }
                catch { health.Dispose(); throw; }
            }, () => committed = true, fileCache, next => phase = next).ConfigureAwait(false);
            UpdateFiles.Write(UpdateFiles.Safe(root, "outcome.json"), new Outcome("completed", policy.Identity, release.ManifestSha256));
            return 0;
        }
        catch (Exception error) when (LauncherUpdateService.Expected(error) || error is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            var failure = FailedOutcome(committed ? "completed" : replacing ? "rolled-back" : "failed", policy?.Identity, request?.ManifestHash, phase, error);
            try
            {
                if (child is not null && !child.HasExited)
                { child.Kill(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)); await child.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                if (parentExited && request is not null)
                {
                    if (replacing) await ClientInstallTransaction.RecoverAsync(request.Installation).ConfigureAwait(false);
                }
                if (replacing && !committed && failure.Failure != "disk" && policy is not null && request is not null)
                    UpdateFiles.Write(UpdateFiles.Safe(root, "rejected.json"), failure);
                // Publish the source-bound result before restarting, so its monitor cannot restore a failed candidate as ready.
                UpdateFiles.Write(UpdateFiles.Safe(root, "outcome.json"), failure);
                if (parentExited && request is not null) { using var restored = StartLauncher(request, null); }
            }
            catch (Exception recovery) when (LauncherUpdateService.Expected(recovery) || recovery is InvalidOperationException or System.ComponentModel.Win32Exception)
            { UpdateFiles.Write(UpdateFiles.Safe(root, "outcome.json"), FailedOutcome("recovery-required", policy?.Identity, request?.ManifestHash, "recovery", recovery)); }
            return 1;
        }
        finally { child?.Dispose(); }
    }

    internal static Outcome FailedOutcome(string status, string? sourceIdentity, string? manifestSha256, string phase, Exception error)
    {
        var failure = phase switch
        {
            "frontend-startup" => "startup",
            "installed-verification" => "integrity",
            _ => LauncherUpdateService.CheckFailure(error).Failure
        };
        // Storage failures describe this installation attempt, not a defective release.
        if (status == "rolled-back" && failure == "disk") status = "failed";
        return new(status, sourceIdentity, manifestSha256, failure, phase, error.HResult);
    }

    // The acknowledgement remains open until the durable commit; the new host blocks launches meanwhile.
    private sealed class HealthAcknowledgement(NamedPipeServerStream pipe, Process process) : IAsyncDisposable
    {
        internal async Task CommitAsync()
        {
            if (process.HasExited) throw new IOException("Updated launcher exited before commit.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await pipe.WriteAsync(new byte[] { 2 }, timeout.Token).ConfigureAwait(false); await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
        }
        public ValueTask DisposeAsync() => pipe.DisposeAsync();
    }

    private static async Task InstallAsync(string installation, string root, LauncherUpdatePolicy policy, LauncherRelease release, Func<Task<HealthAcknowledgement>> health, Action committed, ClientFileCache cache, Action<string> phase)
    {
        phase("recovery");
        await ClientInstallTransaction.RecoverAsync(installation).ConfigureAwait(false);
        var receipt = LauncherUpdateService.Receipt(policy, release);
        var previous = ClientInstallTransaction.ReadReceipt(installation);
        if (previous is not null && (previous.SourceId != receipt.SourceId || previous.PackageId != receipt.PackageId ||
            release.Revision <= Math.Max(policy.MinimumRevision, previous.Revision)))
            throw new InvalidDataException("Installed launcher identity or revision changed before replacement.");
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(installation));
        phase("staging");
        foreach (var file in release.Files)
            if (!await cache.MatchesAsync(file, CancellationToken.None).ConfigureAwait(false))
                File.Copy(UpdateFiles.Safe(root, "objects/" + file.Sha256 + ".verified"), ClientInstallTransaction.Staged(installation, file.Sha256), true);
        phase("replacement");
        // The selected configured source was validated at handoff; pin the old
        // receipt so fallback can transfer ownership without weakening client installs.
        await ClientInstallTransaction.ApplyAsync(installation, release.Files, _ => { }, CancellationToken.None, receipt, cache,
            deferCommit: true, pruneOwned: true, sourceTransitionFrom: previous?.SourceIdentity != receipt.SourceIdentity ? previous : null).ConfigureAwait(false);
        phase("frontend-startup");
        await using var ready = await health().ConfigureAwait(false);
        phase("installed-verification");
        await Parallel.ForEachAsync(release.Files, VerificationOptions(), async (file, token) =>
        {
            if (!await HttpDownloadEngine.MatchesAsync(UpdateFiles.Safe(installation, file.Path), file.Size, file.Sha256, token).ConfigureAwait(false))
                throw new IOException("Updated installation changed during the startup health check.");
        }).ConfigureAwait(false);
        phase("commit");
        cache.RecordInstalled(release.Files);
        await ClientInstallTransaction.CommitAsync(installation, committed, cache).ConfigureAwait(false);
        await ready.CommitAsync().ConfigureAwait(false);
    }

    private static async Task VerifyObjectsAsync(string root, LauncherRelease release, ClientFileCache cache)
    {
        await Parallel.ForEachAsync(release.Files, VerificationOptions(), async (file, token) =>
        {
            if (!await cache.MatchesAsync(file, token).ConfigureAwait(false) &&
                !await HttpDownloadEngine.MatchesAsync(UpdateFiles.Safe(root, "objects/" + file.Sha256 + ".verified"), file.Size, file.Sha256, token).ConfigureAwait(false))
                throw new InvalidDataException("A staged launcher file changed.");
        }).ConfigureAwait(false);
    }

    // Bound disk pressure while allowing independent hashes to overlap, as during staging.
    private static ParallelOptions VerificationOptions() => new() { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8) };

    private static Process StartLauncher(Request request, string? health)
    {
        var start = new ProcessStartInfo(UpdateFiles.Safe(request.Installation, LauncherRelease.Executable)) { UseShellExecute = false, WorkingDirectory = request.Installation };
        start.ArgumentList.Add("--data-root"); start.ArgumentList.Add(request.DataRoot);
        if (health is not null)
        {
            start.ArgumentList.Add("--update-health"); start.ArgumentList.Add(health);
            start.ArgumentList.Add("--update-helper"); start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        return Process.Start(start) ?? throw new IOException("Unable to restart the launcher.");
    }

    /// <summary>Reports painted frontend readiness to the exact helper process, then waits for durable installation commit.</summary>
    public static async Task AcknowledgeAsync(string pipeName, int helperId, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(100));
        await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false); VerifyServer(pipe, helperId);
        await pipe.WriteAsync(new byte[] { 1 }, deadline.Token).ConfigureAwait(false); await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
        var committed = new byte[1]; await pipe.ReadExactlyAsync(committed, deadline.Token).ConfigureAwait(false);
        if (committed[0] != 2) throw new IOException("The updater did not commit the release.");
    }
    private static NamedPipeServerStream Server(string name) => new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 256, 256);
    private static bool SamePath(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);
    private static void VerifyPeer(NamedPipeServerStream pipe, int pid)
    { if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var actual) || actual != pid) throw new IOException("Unexpected updater pipe client."); }
    private static void VerifyServer(NamedPipeClientStream pipe, int pid)
    { if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var actual) || actual != pid) throw new IOException("Unexpected updater pipe server."); }
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
