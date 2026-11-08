using FantasyTale.Launcher.Core.Hooks;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class LaunchLogTests
{
    [Fact]
    public void LockedOldLogsDoNotBlockANewRunAndRotateAfterTheirReaderCloses()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ft-log-locked-").FullName;
        try
        {
            var first = LaunchLogs.Create(root, keep: 1, now: new DateTime(2026, 11, 3, 4, 47, 0));
            var log = Path.Combine(first, "fthook.log");
            File.WriteAllText(log, "preserved diagnostic evidence");
            var skipped = new List<string>();
            string second;
            using (var reader = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                second = LaunchLogs.Create(root, keep: 1, now: new DateTime(2026, 11, 3, 4, 48, 0), skipped: (path, _) => skipped.Add(path));
                Assert.True(Directory.Exists(second));
                Assert.True(File.Exists(log));
                Assert.Equal("preserved diagnostic evidence", File.ReadAllText(log));
                Assert.NotEmpty(skipped);
            }
            var third = LaunchLogs.Create(root, keep: 1, now: new DateTime(2026, 11, 3, 4, 49, 0));
            Assert.False(Directory.Exists(first));
            Assert.False(Directory.Exists(second));
            Assert.Equal(third, Assert.Single(Directory.GetDirectories(root)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AProcessHoldingTheOldDirectoryDoesNotLoseItsLogsOrBlockLaunchPreparation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ft-log-directory-locked-").FullName;
        System.Diagnostics.Process? holder = null;
        try
        {
            var first = LaunchLogs.Create(root, keep: 1, now: new DateTime(2026, 11, 3, 4, 47, 0));
            var log = Path.Combine(first, "fthook.log");
            File.WriteAllText(log, "preserved directory evidence");
            holder = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                Arguments = "/d /q /c \"echo ready & set /p unused=\"", WorkingDirectory = first,
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            }) ?? throw new InvalidOperationException("The diagnostic directory fixture did not start.");
            Assert.Equal("ready ", await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            var next = LaunchLogs.Create(root, keep: 1, now: new DateTime(2026, 11, 3, 4, 48, 0));
            Assert.True(Directory.Exists(next));
            Assert.Equal("preserved directory evidence", File.ReadAllText(log));
        }
        finally
        {
            if (holder is not null)
            {
                holder.StandardInput.WriteLine();
                if (!holder.WaitForExit(5000)) { holder.Kill(); holder.WaitForExit(); }
                holder.Dispose();
            }
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RunsAreNamedByTheirLocalStartTime()
    {
        var root = Directory.CreateTempSubdirectory("ft-log-name-").FullName;
        try
        {
            var at = new DateTime(2026, 11, 3, 4, 47, 0, DateTimeKind.Local);
            Assert.Equal("diag_20261103_044700", Path.GetFileName(LaunchLogs.Create(root, now: at)));
            Assert.Equal("diag_20261103_044700_2", Path.GetFileName(LaunchLogs.Create(root, now: at)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrashStatusRotatesAndRunsHoldingOtherFilesStayUntouched(bool unexpected)
    {
        var root = Directory.CreateTempSubdirectory("ft-log-rotation-").FullName;
        try
        {
            var first = LaunchLogs.Create(root, keep: 1, now: new DateTime(2026, 11, 3, 4, 47, 0));
            File.WriteAllText(Path.Combine(first, "exception.dmp.status.txt"), "written");
            var userFile = Path.Combine(first, "user.txt");
            if (unexpected) File.WriteAllText(userFile, "preserve");
            // A run holding a file the launcher did not write never blocks the next launch.
            var next = LaunchLogs.Create(root, keep: 1, now: new DateTime(2026, 11, 3, 4, 48, 0));
            Assert.True(Directory.Exists(next));
            Assert.Equal(unexpected, Directory.Exists(first));
            if (unexpected) Assert.Equal("preserve", File.ReadAllText(userFile));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void EarlierLaunchNamedRunsStillRotate()
    {
        var root = Directory.CreateTempSubdirectory("ft-log-legacy-").FullName;
        try
        {
            var legacy = Directory.CreateDirectory(Path.Combine(root, "launch-" + Guid.NewGuid().ToString("N"))).FullName;
            File.WriteAllText(Path.Combine(legacy, "fthook.log"), "old");
            var foreign = Directory.CreateDirectory(Path.Combine(root, "notes")).FullName;
            LaunchLogs.Create(root, keep: 1);
            Assert.False(Directory.Exists(legacy));
            Assert.True(Directory.Exists(foreign));
        }
        finally { Directory.Delete(root, true); }
    }
}
