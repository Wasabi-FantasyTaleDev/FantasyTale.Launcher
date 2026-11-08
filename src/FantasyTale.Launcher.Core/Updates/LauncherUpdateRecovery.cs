using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Core.Stores;

namespace FantasyTale.Launcher.Core.Updates;

public static partial class LauncherUpdateHandoff
{
    /// <summary>Checks for an unfinished installation journal without changing application files.</summary>
    public static bool RequiresRecovery(string installation) => File.Exists(JournalPath(installation));

    /// <summary>Hands an interrupted installation to its retained external helper before the current launcher exits.</summary>
    public static async Task<bool> PrepareRecoveryAsync(string installation, string dataRoot, CancellationToken ct, bool showErrors = true)
    {
        if (!OperatingSystem.IsWindows()) return false;
        installation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation));
        if (!RequiresRecovery(installation)) return false;
        using var parent = Process.GetCurrentProcess();
        if (!SamePath(parent.MainModule!.FileName, UpdateFiles.Safe(installation, LauncherRelease.Executable)))
            throw new InvalidOperationException("Recovery handoff requires the installed launcher executable.");
        var root = RecoveryRoot(installation, dataRoot);
        var helperRoot = UpdateFiles.Safe(root, "helper");
        var helperPath = UpdateFiles.Safe(helperRoot, HelperExecutable);
        if (!File.Exists(helperPath)) throw new IOException("The retained external updater is unavailable; recovery files were preserved.");
        var journalHash = await JournalHashAsync(installation, ct).ConfigureAwait(false);
        var pipeName = "ft-update-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        using var pipe = Server(pipeName);
        // Each parent owns its request until the helper has read it; competing bootstraps must not overwrite it.
        var path = UpdateFiles.Safe(root, "repair-" + pipeName[10..] + ".json");
        UpdateFiles.Write(path, new Request(1, installation, Path.GetFullPath(dataRoot), parent.Id,
            parent.StartTime.ToUniversalTime().Ticks, pipeName, "", "repair", journalHash, showErrors));
        var start = new ProcessStartInfo(helperPath) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = helperRoot };
        start.ArgumentList.Add(path);
        Process? helper = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            helper = Process.Start(start) ?? throw new IOException("Unable to start update recovery.");
            var connected = pipe.WaitForConnectionAsync(timeout.Token);
            if (await Task.WhenAny(connected, helper.WaitForExitAsync(timeout.Token)).ConfigureAwait(false) != connected)
                throw new IOException("The recovery helper rejected the handoff.");
            await connected.ConfigureAwait(false); VerifyPeer(pipe, helper.Id);
            var ready = new byte[1]; await pipe.ReadExactlyAsync(ready, timeout.Token).ConfigureAwait(false);
            if (ready[0] != 1) throw new IOException("The recovery helper rejected the handoff.");
            return true;
        }
        catch
        {
            // The owning launcher has not exited, so its helper cannot yet modify installation files.
            if (helper is not null && !helper.HasExited) { helper.Kill(); await helper.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            throw;
        }
        finally
        {
            helper?.Dispose();
            File.Delete(path);
        }
    }

    /// <summary>Repairs an offline installation from the retained helper outside it, preserving conflicts and reporting an exit code.</summary>
    public static async Task<int> RepairOfflineAsync(string installation, string dataRoot, bool showErrors = true)
    {
        if (!OperatingSystem.IsWindows()) return 1;
        string? root = null;
        try
        {
            installation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation));
            root = RecoveryRoot(installation, dataRoot);
            using var current = Process.GetCurrentProcess();
            if (!SamePath(current.MainModule!.FileName, UpdateFiles.Safe(root, "helper/" + HelperExecutable)))
                throw new InvalidOperationException("Offline repair must run from the retained external updater.");
            using var helperLease = new FileStream(UpdateFiles.Safe(root, "helper.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            RejectRunningInstallation(installation);
            using var installLease = InstallationRecoveryLease(installation);
            var pending = RequiresRecovery(installation);
            await ClientInstallTransaction.RecoverAsync(installation).ConfigureAwait(false);
            UpdateFiles.Write(UpdateFiles.Safe(root, "outcome.json"), new Outcome(pending ? "repaired" : "no-recovery-needed"));
            return 0;
        }
        catch (Exception error) when (RecoveryError(error))
        {
            if (root is not null) WriteRecoveryFailure(root);
            if (showErrors) ShowRecoveryFailure();
            return 1;
        }
    }

    private static async Task<int> RunRecoveryHandoffAsync(string requestPath, Request request)
    {
        string? root = null;
        try
        {
            root = RecoveryRoot(request.Installation, request.DataRoot);
            if (request.Schema != 1 || !SamePath(Path.GetDirectoryName(requestPath)!, root) || request.JournalHash is not { Length: 64 } ||
                !request.JournalHash.All(char.IsAsciiHexDigit) || request.Pipe.Length != 58 || !request.Pipe.StartsWith("ft-update-", StringComparison.Ordinal) ||
                !request.Pipe.AsSpan(10).ToString().All(char.IsAsciiHexDigit)) throw new InvalidDataException("Invalid recovery handoff.");
            using var helperLease = new FileStream(UpdateFiles.Safe(root, "helper.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using var parent = Process.GetProcessById(request.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStart || !SamePath(parent.MainModule!.FileName, UpdateFiles.Safe(request.Installation, LauncherRelease.Executable)))
                throw new InvalidDataException("Recovery parent identity changed.");
            if (await JournalHashAsync(request.Installation, default).ConfigureAwait(false) != request.JournalHash)
                throw new InvalidDataException("The recovery journal changed before handoff.");
            using (var pipe = new NamedPipeClientStream(".", request.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false); VerifyServer(pipe, parent.Id);
                await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false); await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            }
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90))) await parent.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            RejectRunningInstallation(request.Installation);
            using var installLease = InstallationRecoveryLease(request.Installation);
            if (await JournalHashAsync(request.Installation, default).ConfigureAwait(false) != request.JournalHash)
                throw new InvalidDataException("The recovery journal changed while waiting for launcher exit.");
            await ClientInstallTransaction.RecoverAsync(request.Installation).ConfigureAwait(false);
            UpdateFiles.Write(UpdateFiles.Safe(root, "outcome.json"), new Outcome("repaired"));
            using var restored = StartLauncher(request, null);
            return 0;
        }
        catch (Exception error) when (RecoveryError(error))
        {
            if (root is not null) WriteRecoveryFailure(root);
            if (request.ShowErrors) ShowRecoveryFailure();
            return 1;
        }
    }

    private static string RecoveryRoot(string installation, string dataRoot)
    {
        installation = Path.GetFullPath(installation); dataRoot = Path.GetFullPath(dataRoot);
        InstanceStore.RejectLinks(installation); InstanceStore.RejectLinks(dataRoot);
        var root = UpdateFiles.Safe(dataRoot, "launcher-update");
        if (InstanceStore.IsWithin(installation, root) || InstanceStore.IsWithin(root, installation) || SamePath(root, installation))
            throw new InvalidOperationException("Update recovery files must be outside the installation.");
        return root;
    }

    private static string JournalPath(string installation) => UpdateFiles.Safe(ClientInstallTransaction.Workspace(installation), "journal.json");
    private static FileStream InstallationRecoveryLease(string installation) => new(UpdateFiles.Safe(ClientInstallTransaction.Workspace(installation), "self-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private static async Task<string> JournalHashAsync(string installation, CancellationToken ct)
    {
        using var input = new FileStream(JournalPath(installation), FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false));
    }

    private static void RejectRunningInstallation(string installation)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(LauncherRelease.Executable)))
            using (process)
                if (!process.HasExited && SamePath(process.MainModule!.FileName, UpdateFiles.Safe(installation, LauncherRelease.Executable)))
                    throw new InvalidOperationException("Close the launcher before offline repair.");
    }

    private static bool RecoveryError(Exception error) => LauncherUpdateService.Expected(error) ||
        error is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or OperationCanceledException;
    private static void WriteRecoveryFailure(string root)
    {
        try { UpdateFiles.Write(UpdateFiles.Safe(root, "outcome.json"), new Outcome("recovery-required")); }
        catch (Exception error) when (RecoveryError(error)) { /* Preserve the installation journal even if reporting is unavailable. */ }
    }

    /// <summary>Shows a localized bootstrap error when the normal frontend cannot safely start after interrupted replacement.</summary>
    public static void ShowRecoveryFailure()
    {
        if (!OperatingSystem.IsWindows()) return;
        var language = CultureInfo.CurrentUICulture.Name;
        var text = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? language is "zh-TW" or "zh-HK" ? "無法完成啟動器更新恢復。備份已保留。請關閉其他啟動器，使用保留的外部更新程式重試修復。" : "无法完成启动器更新恢复。备份已保留。请关闭其他启动器，使用保留的外部更新程序重试修复。"
            : language.StartsWith("ko", StringComparison.OrdinalIgnoreCase) ? "런처 업데이트를 복구하지 못했습니다. 백업은 유지됩니다. 다른 런처를 닫고 보관된 외부 업데이트 프로그램으로 복구를 다시 시도하세요."
            : language.StartsWith("id", StringComparison.OrdinalIgnoreCase) ? "Pemulihan pembaruan launcher gagal. Cadangan disimpan. Tutup launcher lain dan coba perbaikan melalui updater eksternal yang disimpan."
            : language.StartsWith("vi", StringComparison.OrdinalIgnoreCase) ? "Không thể khôi phục bản cập nhật launcher. Bản sao lưu được giữ lại. Đóng launcher khác và thử sửa bằng trình cập nhật bên ngoài đã lưu."
            : "Launcher update recovery could not finish. Backups were preserved. Close other launcher processes and retry repair using the retained external updater.";
        RecoveryMessageBox(0, text, "Fantasy Tale Launcher", 0x10);
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RecoveryMessageBox(nint window, string text, string caption, uint flags);
}
