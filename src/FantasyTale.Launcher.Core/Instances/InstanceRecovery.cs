using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Sdk;
using Microsoft.Win32.SafeHandles;

namespace FantasyTale.Launcher.Core.Instances;

/// <summary>Explicitly restores interrupted native overlays while retaining evidence if client ownership cannot be established.</summary>
public static partial class InstanceRecovery
{
    /// <summary>Whether a client contains a native transaction or a leftover per-launch session.</summary>
    public static bool Required(string folder) => Path.Exists(Path.Combine(folder, ".fantasytale-runtime")) || Path.Exists(Path.Combine(folder, SessionWriter.FileName));

    /// <summary>Attempts verified recovery after a launcher restart; uncertainty leaves evidence for the manual action.</summary>
    public static bool TryRecoverStopped(InstanceStore store, string id, GameDefinition game)
    {
        try
        {
            Recover(store, id, game, confirmedStopped: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Acquires the client lease, rejects live matching processes, verifies session identity and restores original native files before deleting the session.</summary>
    public static void Recover(InstanceStore store, string id, GameDefinition game, bool confirmedStopped)
    {
        if (!confirmedStopped) throw new InvalidOperationException("Confirm that the game is closed before recovering this client.");
        using var lease = store.Acquire(id);
        RecoverLeased(store, id, game);
    }

    internal static void RecoverLeased(InstanceStore store, string id, GameDefinition game)
    {
        var instance = store.Load(id);
        if (instance.Game != game.Id) throw new InvalidOperationException("The recovery game does not match the client.");
        var build = game.Builds.Single(b => b.Id == instance.Build);
        var folder = store.ClientFolder(id);
        var executables = ClientExecutable.CandidatePaths(folder, build);
        EnsureStopped(folder, build);
        var session = Path.Combine(folder, SessionWriter.FileName);
        InstanceStore.RejectLinks(session);
        if (Directory.Exists(session)) throw new IOException("The interrupted session was replaced by a directory; recovery files were retained.");
        FileStream? heldSession = null;
        try
        {
            if (File.Exists(session))
            {
                heldSession = new FileStream(session, FileMode.Open, FileAccess.Read, FileShare.None);
                using var reader = new StreamReader(heldSession, new UTF8Encoding(false, true), leaveOpen: true);
                try
                {
                    var document = TomlReader.Parse(session, reader.ReadToEnd());
                    if (document.Integer("schema", true) is not (1 or 2)) throw new IOException("Unsupported interrupted session schema.");
                    var identity = document.Table("session", true)!;
                    if (identity.String("game", true) != game.Id || identity.String("build", true) != build.Id)
                        throw new IOException("The interrupted session belongs to a different client.");
                    var previousId = identity.String("instance", true);
                    if (previousId != id)
                    {
                        var previous = store.List().SingleOrDefault(item => item.Id == previousId);
                        if (previous is not null && !store.ClientFolder(previous.Id).Equals(folder, StringComparison.OrdinalIgnoreCase)
                            || !File.Exists(Path.Combine(folder, ".fantasytale-runtime", "journal.json"))
                            || ClientExecutable.FindInstalled(folder, build) is null)
                            throw new IOException("The interrupted session belongs to a different client.");
                        // A stopped orphan may have a retired registration, but its deployment must still verify in place.
                        var owner = identity.Integer("launcher_pid");
                        if (owner is > 0 and <= int.MaxValue && owner != Environment.ProcessId)
                        {
                            try
                            {
                                using var process = Process.GetProcessById((int)owner.Value);
                                if (!process.HasExited) throw new IOException("The interrupted launch is still owned by another launcher.");
                            }
                            catch (ArgumentException) { }
                        }
                    }
                }
                catch (DefinitionException) { throw new IOException("The interrupted session identity could not be verified; recovery files were retained."); }
                catch (DecoderFallbackException) { throw new IOException("The interrupted session encoding could not be verified; recovery files were retained."); }
            }
            foreach (var executable in executables) RejectRunning(executable);
            var staging = Path.Combine(folder, ".fantasytale-runtime");
            InstanceStore.RejectLinks(staging);
            if (Path.Exists(staging)) RuntimeDeployment.RecoverStoppedClient(folder);
        }
        finally { heldSession?.Dispose(); }
        if (File.Exists(session)) File.Delete(session);
    }

    private static void RejectRunning(string executable)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable));
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited) continue;
                    var path = ImagePath(process);
                    if (path is null)
                    {
                        if (process.HasExited) continue;
                        throw new IOException("The matching game process could not be inspected.");
                    }
                    if (Path.GetFullPath(path).Equals(executable, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Close the game before recovering its client files.");
                }
                catch (Win32Exception) when (process.HasExited) { }
                catch (Win32Exception) { throw new IOException("The matching game process could not be inspected; recovery files were retained."); }
                catch (InvalidOperationException) when (process.HasExited) { }
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static unsafe string? ImagePath(Process process)
    {
        if (!OperatingSystem.IsWindows()) return process.MainModule?.FileName;
        // The kernel image path exists before the loader's module list and does not require module-read permissions.
        using var handle = OpenProcess(0x1000, false, process.Id);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var buffer = new char[32768];
        fixed (char* pointer = buffer)
        {
            var length = (uint)buffer.Length;
            if (!QueryFullProcessImageNameW(handle, 0, pointer, ref length))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            return new string(buffer, 0, (int)length);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, char* path, ref uint length);

    internal static void EnsureStopped(string folder, BuildDefinition build)
    {
        foreach (var executable in ClientExecutable.CandidatePaths(folder, build)) RejectRunning(executable);
    }
}
