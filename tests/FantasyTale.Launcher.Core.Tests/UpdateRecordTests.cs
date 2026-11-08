using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class UpdateRecordTests
{
    [Fact]
    public async Task RecordRemovedWhileReaderRetriesIsReportedAsAbsent()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ft-update-removed-").FullName;
        var path = Path.Combine(root, "outcome.json");
        FileStream? locked = null;
        Task<LauncherUpdateHandoff.Outcome?>? reading = null;
        using var entered = new ManualResetEventSlim();
        try
        {
            UpdateFiles.Write(path, new LauncherUpdateHandoff.Outcome("installing"));
            locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete);
            reading = Task.Factory.StartNew(() =>
            {
                entered.Set();
                return UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(path);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            Thread.Sleep(40);
            Assert.False(reading.IsCompleted);
            File.Delete(path);
            locked.Dispose(); locked = null;
            Assert.Null(await reading.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Null(UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(Path.Combine(root, "absent", "outcome.json")));
        }
        finally
        {
            locked?.Dispose();
            if (reading is not null)
                try { await reading.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (IOException) { }
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordWritesTolerateBriefLocksAndRemainRetryableAfterPersistentLocks(bool persistent)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ft-update-write-").FullName;
        var path = Path.Combine(root, "transfer.json");
        FileStream? locked = null;
        using var entered = new ManualResetEventSlim();
        Task? writing = null;
        try
        {
            UpdateFiles.Write(path, new LauncherUpdateHandoff.Outcome("downloading"));
            locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            writing = Task.Factory.StartNew(() =>
            {
                entered.Set();
                UpdateFiles.Write(path, new LauncherUpdateHandoff.Outcome("ready"));
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            if (persistent)
            {
                await Assert.ThrowsAsync<IOException>(() => writing.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal("downloading", UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(path)!.Status);
                Assert.False(File.Exists(path + ".tmp"));
            }
            else Thread.Sleep(40);
            locked.Dispose(); locked = null;
            if (persistent) UpdateFiles.Write(path, new LauncherUpdateHandoff.Outcome("ready"));
            else await writing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("ready", UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(path)!.Status);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            locked?.Dispose();
            if (writing is not null)
                try { await writing.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (IOException) { }
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordReadsTolerateBriefSharingConflictButRejectPersistentLocks(bool persistent)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Directory.CreateTempSubdirectory("ft-update-read-").FullName;
        var path = Path.Combine(root, "outcome.json");
        FileStream? locked = null;
        using var entered = new ManualResetEventSlim();
        Task<LauncherUpdateHandoff.Outcome?>? reading = null;
        try
        {
            UpdateFiles.Write(path, new LauncherUpdateHandoff.Outcome("completed"));
            locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            // Lock release must not queue behind parallel process fixtures on the thread pool.
            reading = Task.Factory.StartNew(() =>
            {
                entered.Set();
                return UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(path);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            if (persistent)
                await Assert.ThrowsAsync<IOException>(() => reading.WaitAsync(TimeSpan.FromSeconds(5)));
            else
            {
                Thread.Sleep(40);
                locked.Dispose(); locked = null;
                Assert.Equal("completed", (await reading.WaitAsync(TimeSpan.FromSeconds(5)))!.Status);
            }
        }
        finally
        {
            locked?.Dispose();
            if (reading is not null)
                try { await reading.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (IOException) { }
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ReaderRetainsItsSnapshotWhileHelperPublishesTheNextState()
    {
        var root = Directory.CreateTempSubdirectory("ft-update-record-").FullName;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        BlockingRecord.Entered = entered;
        BlockingRecord.Release = release;
        Task<BlockingRecord?>? reading = null;
        try
        {
            var path = Path.Combine(root, "outcome.json");
            UpdateFiles.Write(path, new LauncherUpdateHandoff.Outcome("installing"));
            reading = Task.Run(() => UpdateFiles.Read<BlockingRecord>(path));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            UpdateFiles.Write(path, new LauncherUpdateHandoff.Outcome("completed"));
            release.Set();
            Assert.Equal("installing", (await reading.WaitAsync(TimeSpan.FromSeconds(5)))!.Status);
            Assert.Equal("completed", UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(path)!.Status);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            release.Set();
            if (reading is not null) await reading.WaitAsync(TimeSpan.FromSeconds(5));
            BlockingRecord.Entered = null;
            BlockingRecord.Release = null;
            Directory.Delete(root, true);
        }
    }

    private sealed class BlockingRecord
    {
        internal static ManualResetEventSlim? Entered;
        internal static ManualResetEventSlim? Release;

        public BlockingRecord(string status)
        {
            Status = status;
            Entered!.Set();
            if (!Release!.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Update record reader was not released.");
        }

        public string Status { get; }
    }
}
