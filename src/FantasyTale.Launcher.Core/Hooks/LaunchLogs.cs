using FantasyTale.Launcher.Core.Instances;
using FantasyTale.Launcher.Sdk;
using System.Text.Json;

namespace FantasyTale.Launcher.Core.Hooks;

internal static class LaunchLogs
{
    // Arguments and launch credentials are deliberately excluded from durable diagnostics.
    internal static void RecordExecutable(string folder, string launchId, string game, string build, string executable)
    {
        uint? peTimestamp = null;
        var fingerprintStatus = "read";
        try { peTimestamp = ClientExecutable.ReadIdentity(executable).PeTimestamp; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException)
        { fingerprintStatus = "unavailable"; }
        var path = Path.Combine(folder, "launch.json");
        InstanceStore.RejectLinks(path);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(file, new { schema = 1, launchId, game, build, executable = Path.GetFullPath(executable),
            peTimestamp, fingerprintStatus, recordedAt = DateTimeOffset.UtcNow });
    }

    // Only launcher-owned runs are rotated. A run that holds anything else is left untouched and is no longer
    // counted, so a file placed beside the logs never blocks a launch and is never removed.
    internal static string Create(string root, int keep = 10, DateTime? now = null, Action<string, Exception>? skipped = null)
    {
        InstanceStore.RejectLinks(root);
        Directory.CreateDirectory(root);
        var runs = Directory.EnumerateDirectories(root).Where(p => IsRunName(Path.GetFileName(p))
                || Path.GetFileName(p).EndsWith(".pruning", StringComparison.Ordinal) && IsRunName(Path.GetFileName(p)[..^8]))
            .Where(OnlyOwnedFiles).OrderBy(Directory.GetCreationTimeUtc).ToArray();
        var remaining = Math.Max(0, runs.Length - keep + 1);
        foreach (var run in runs)
        {
            if (remaining == 0) break;
            var rotating = run.EndsWith(".pruning", StringComparison.Ordinal) ? run : run + ".pruning";
            InstanceStore.RejectLinks(rotating);
            try
            {
                // Moving the run first preserves its files when another process holds the directory open.
                if (run != rotating) Directory.Move(run, rotating);
                if (!OnlyOwnedFiles(rotating))
                {
                    if (run != rotating) Directory.Move(rotating, run);
                    continue;
                }
                foreach (var file in Directory.GetFileSystemEntries(rotating)) File.Delete(file);
                Directory.Delete(rotating);
                remaining--;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (Directory.Exists(rotating) && !Directory.Exists(run))
                {
                    try { Directory.Move(rotating, run); }
                    catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
                    { skipped?.Invoke(rotating, restoreError); }
                }
                skipped?.Invoke(run, error);
            }
        }
        var stamp = "diag_" + (now ?? DateTime.Now).ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        for (var n = 1; n <= 99; n++)
        {
            var path = Path.Combine(root, n == 1 ? stamp : stamp + "_" + n);
            InstanceStore.RejectLinks(path);
            if (!Path.Exists(path)) return Directory.CreateDirectory(path).FullName;
        }
        throw new IOException("Too many launch diagnostic directories were created within one second.");
    }

    // diag_yyyyMMdd_HHmmss with an optional _2.._99 suffix, or the earlier launch-<32 hex digits> form.
    internal static bool IsRunName(string name) =>
        name.Length == 39 && name.StartsWith("launch-", StringComparison.Ordinal) && name[7..].All(char.IsAsciiHexDigitLower)
        || name.StartsWith("diag_", StringComparison.Ordinal) && name.Length is 20 or 22 or 23
            && name[5..13].All(char.IsAsciiDigit) && name[13] == '_' && name[14..20].All(char.IsAsciiDigit)
            && (name.Length == 20 || name[20] == '_' && name[21..].All(char.IsAsciiDigit) && name[21] != '0');

    private static bool OnlyOwnedFiles(string run)
    {
        InstanceStore.RejectLinks(run);
        foreach (var file in Directory.GetFileSystemEntries(run))
        {
            InstanceStore.RejectLinks(file);
            if (Directory.Exists(file) || Path.GetFileName(file) is not ("fthook.log" or "crash.log" or "exception.dmp"
                    or "exception.dmp.tmp" or "exception.dmp.status.txt" or "receive.ftnet" or "readiness.toml" or "launch.json"))
                return false;
        }
        return true;
    }
}
