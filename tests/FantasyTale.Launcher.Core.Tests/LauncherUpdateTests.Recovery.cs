using System.Diagnostics;
using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    private string PrepareRecoveryProcessFixture()
    {
        Policy();
        var client = Path.Combine(AppContext.BaseDirectory, "test-client");
        foreach (var file in Directory.GetFiles(client)) File.Copy(file, Path.Combine(Installation, Path.GetFileName(file)), true);
        File.Copy(Path.Combine(client, "FantasyTale.Launcher.TestClient.exe"), Path.Combine(Installation, LauncherRelease.Executable), true);
        File.WriteAllText(Path.Combine(Installation, "update-probe"), "healthy");
        var source = Path.Combine(AppContext.BaseDirectory, "test-updater");
        var helper = Path.Combine(Data, "launcher-update", "helper"); Directory.CreateDirectory(helper);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(helper, Path.GetFileName(file)), true);
        return Path.Combine(helper, "FantasyTale.Launcher.Updater.exe");
    }

    private async Task SeedInterruptedRecoveryAsync(bool committed)
    {
        var old = new ClientReleaseFile("payload.dll", "objects/old", 3, Hash("old"u8.ToArray()));
        var next = new ClientReleaseFile("payload.dll", "objects/new", 3, Hash("new"u8.ToArray()));
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(Installation));
        File.WriteAllText(ClientInstallTransaction.Staged(Installation, old.Sha256), "old");
        File.WriteAllText(ClientInstallTransaction.Staged(Installation, next.Sha256), "new");
        File.WriteAllText(Path.Combine(Installation, "payload.dll"), "old");
        await ClientInstallTransaction.ApplyAsync(Installation, [next], _ => { }, default, deferCommit: true);
        if (committed)
        {
            var journalPath = Path.Combine(ClientInstallTransaction.Workspace(Installation), "journal.json");
            var journal = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(journalPath))!;
            journal["Committed"] = true; File.WriteAllText(journalPath, journal.ToJsonString());
        }
    }

    private async Task<int> RunRepairProcessAsync(string helper)
    {
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(helper)! };
        foreach (var value in new[] { "--repair", Installation, Data, "--silent" }) start.ArgumentList.Add(value);
        using var child = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await child.WaitForExitAsync(timeout.Token); return child.ExitCode;
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("replaced")]
    [InlineData("added")]
    [InlineData("committed")]
    public async Task OfflineHelperRecoversAfterRealProcessTerminationAtTransactionBoundaries(string checkpoint)
    {
        if (!OperatingSystem.IsWindows()) return;
        var helper = PrepareRecoveryProcessFixture();
        var workspace = ClientInstallTransaction.Workspace(Installation);
        Directory.CreateDirectory(workspace);
        var oldHash = Hash("old"u8.ToArray());
        File.WriteAllText(ClientInstallTransaction.Staged(Installation, oldHash), "old");
        var initial = new ClientInstallReceipt("fixture", "fixture", "", Hash("old-index"u8.ToArray()), Hash("source"u8.ToArray()), 1);
        await ClientInstallTransaction.ApplyAsync(Installation,
            [new("payload.dll", "payload", 3, oldHash), new("obsolete.dll", "obsolete", 3, oldHash)],
            _ => { }, default, initial, pruneOwned: true);
        File.WriteAllText(ClientInstallTransaction.Staged(Installation, Hash("new"u8.ToArray())), "new");
        File.WriteAllText(ClientInstallTransaction.Staged(Installation, Hash("added"u8.ToArray())), "added");
        File.WriteAllText(Path.Combine(Installation, "personal.txt"), "user data");
        var marker = Path.Combine(Data, "transaction-checkpoint");
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "test-client", "FantasyTale.Launcher.TestClient.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Installation };
        foreach (var value in new[] { "--transaction-probe", Installation, checkpoint, marker }) start.ArgumentList.Add(value);
        using var child = Process.Start(start)!;
        try
        {
            await UntilAsync(() => File.Exists(marker) || child.HasExited);
            Assert.False(child.HasExited, "The transaction probe exited before reaching its checkpoint.");
            Assert.Equal(checkpoint, File.ReadAllText(marker));
            Assert.False(File.Exists(Path.Combine(Installation, "obsolete.dll")));
            Assert.Equal(checkpoint == "removed" ? "old" : "new", File.ReadAllText(Path.Combine(Installation, "payload.dll")));
            Assert.Equal(checkpoint is "added" or "committed", File.Exists(Path.Combine(Installation, "added.dll")));
        }
        finally
        {
            if (!child.HasExited) child.Kill();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.True(LauncherUpdateHandoff.RequiresRecovery(Installation));
        Assert.Equal(0, await RunRepairProcessAsync(helper));
        var committed = checkpoint == "committed";
        Assert.Equal(committed ? "new" : "old", File.ReadAllText(Path.Combine(Installation, "payload.dll")));
        Assert.Equal(!committed, File.Exists(Path.Combine(Installation, "obsolete.dll")));
        Assert.Equal(committed, File.Exists(Path.Combine(Installation, "added.dll")));
        Assert.Equal("user data", File.ReadAllText(Path.Combine(Installation, "personal.txt")));
        Assert.Equal(committed ? 2 : 1, ClientInstallTransaction.ReadReceipt(Installation)!.Revision);
        Assert.False(LauncherUpdateHandoff.RequiresRecovery(Installation));
        Assert.Empty(Directory.GetFiles(workspace, "original-*"));
        Assert.Equal(0, await RunRepairProcessAsync(helper));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedExternalHelperRepairsUncommittedOrCommittedOfflineJournal(bool committed)
    {
        if (!OperatingSystem.IsWindows()) return;
        var helper = PrepareRecoveryProcessFixture(); await SeedInterruptedRecoveryAsync(committed);
        var rejectionPath = Path.Combine(Data, "launcher-update", "rejected.json");
        var rejection = new LauncherUpdateHandoff.Outcome("rolled-back", Policy().Identity, Hash(IndexBytes(Manifest())));
        UpdateFiles.Write(rejectionPath, rejection);
        Assert.True(LauncherUpdateHandoff.RequiresRecovery(Installation));
        Assert.Equal(0, await RunRepairProcessAsync(helper));
        Assert.False(LauncherUpdateHandoff.RequiresRecovery(Installation));
        Assert.Equal(committed ? "new" : "old", File.ReadAllText(Path.Combine(Installation, "payload.dll")));
        Assert.Equal(0, await RunRepairProcessAsync(helper));
        Assert.Contains("no-recovery-needed", File.ReadAllText(Path.Combine(Data, "launcher-update", "outcome.json")), StringComparison.Ordinal);
        Assert.Equal(rejection, UpdateFiles.Read<LauncherUpdateHandoff.Outcome>(rejectionPath));
    }

    [Fact]
    public async Task RecoveryBootstrapWaitsForExactParentExitThenRestartsRestoredLauncher()
    {
        if (!OperatingSystem.IsWindows()) return;
        PrepareRecoveryProcessFixture(); await SeedInterruptedRecoveryAsync(false);
        var start = new ProcessStartInfo(Path.Combine(Installation, LauncherRelease.Executable)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Installation };
        foreach (var value in new[] { "--data-root", Data, "--prepare-recovery" }) start.ArgumentList.Add(value);
        using var parent = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await parent.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, parent.ExitCode);
        var restoredPid = 0;
        await UntilAsync(() =>
        {
            var marker = Path.Combine(Data, "restored");
            if (!File.Exists(marker)) return false;
            // File creation precedes the probe closing its exclusive writer.
            try { return int.TryParse(File.ReadAllText(marker), System.Globalization.CultureInfo.InvariantCulture, out restoredPid); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { return false; }
        }, 30000);
        using (var restoredTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            while (true)
            {
                try
                {
                    using var restored = Process.GetProcessById(restoredPid);
                    await restored.WaitForExitAsync(restoredTimeout.Token);
                    break;
                }
                catch (ArgumentException) { break; }
            }
        }
        Assert.False(LauncherUpdateHandoff.RequiresRecovery(Installation));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, "payload.dll")));
        Assert.Contains("repaired", File.ReadAllText(Path.Combine(Data, "launcher-update", "outcome.json")), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.Combine(Data, "launcher-update"), "repair-*.json"));
    }

    [Fact]
    public async Task OfflineRecoveryPreservesExternallyEditedFilesAndBackups()
    {
        if (!OperatingSystem.IsWindows()) return;
        var helper = PrepareRecoveryProcessFixture(); await SeedInterruptedRecoveryAsync(false);
        File.WriteAllText(Path.Combine(Installation, "payload.dll"), "external edit");
        Assert.Equal(1, await RunRepairProcessAsync(helper));
        Assert.Equal("external edit", File.ReadAllText(Path.Combine(Installation, "payload.dll")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(ClientInstallTransaction.Workspace(Installation), "original-0")));
        Assert.True(LauncherUpdateHandoff.RequiresRecovery(Installation));
        Assert.Contains("recovery-required", File.ReadAllText(Path.Combine(Data, "launcher-update", "outcome.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfflineRecoveryRefusesConcurrentHelperLeaseWithoutMutatingInstallation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var helper = PrepareRecoveryProcessFixture(); await SeedInterruptedRecoveryAsync(false);
        using var lease = new FileStream(Path.Combine(Data, "launcher-update", "helper.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Assert.Equal(1, await RunRepairProcessAsync(helper));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Installation, "payload.dll")));
        Assert.True(LauncherUpdateHandoff.RequiresRecovery(Installation));
    }

    [Fact]
    public async Task OfflineRecoveryRefusesInstallationLeaseAndCanRetryAfterRelease()
    {
        if (!OperatingSystem.IsWindows()) return;
        var helper = PrepareRecoveryProcessFixture(); await SeedInterruptedRecoveryAsync(false);
        using (var lease = new FileStream(Path.Combine(ClientInstallTransaction.Workspace(Installation), "self-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(1, await RunRepairProcessAsync(helper));
            Assert.Equal("new", File.ReadAllText(Path.Combine(Installation, "payload.dll")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(ClientInstallTransaction.Workspace(Installation), "original-0")));
            Assert.True(LauncherUpdateHandoff.RequiresRecovery(Installation));
        }
        Assert.Equal(0, await RunRepairProcessAsync(helper));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, "payload.dll")));
        Assert.False(LauncherUpdateHandoff.RequiresRecovery(Installation));
    }

    [Fact]
    public async Task OfflineRecoveryPreservesMalformedJournalAndBackupForRepair()
    {
        if (!OperatingSystem.IsWindows()) return;
        var helper = PrepareRecoveryProcessFixture(); await SeedInterruptedRecoveryAsync(false);
        var journal = Path.Combine(ClientInstallTransaction.Workspace(Installation), "journal.json");
        File.WriteAllText(journal, "{\"Schema\":");
        Assert.Equal(1, await RunRepairProcessAsync(helper));
        Assert.Equal("{\"Schema\":", File.ReadAllText(journal));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Installation, "payload.dll")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(ClientInstallTransaction.Workspace(Installation), "original-0")));
        Assert.Contains("recovery-required", File.ReadAllText(Path.Combine(Data, "launcher-update", "outcome.json")), StringComparison.Ordinal);
    }
}
