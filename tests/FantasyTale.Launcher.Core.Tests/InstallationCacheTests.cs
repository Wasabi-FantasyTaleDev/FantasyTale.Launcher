using System.Security.Cryptography;
using System.Text.Json.Nodes;
using FantasyTale.Launcher.Core.Stores;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class InstallationCacheTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ft-install-cache-").FullName;
    private readonly byte[] _payload = "new payload"u8.ToArray();

    private string Workspace => ClientInstallTransaction.Workspace(_root);
    private string Hash => Convert.ToHexString(SHA256.HashData(_payload));
    private static string OtherHash => Convert.ToHexString(SHA256.HashData("another pending payload"u8));

    private ClientReleaseFile[] Prepare()
    {
        Directory.CreateDirectory(Workspace);
        File.WriteAllBytes(ClientInstallTransaction.Staged(_root, Hash), _payload);
        File.WriteAllBytes(ClientInstallTransaction.Staged(_root, OtherHash), "another pending payload"u8.ToArray());
        File.WriteAllText(Path.Combine(Workspace, "notes.txt"), "independent file");
        File.WriteAllText(Path.Combine(_root, "client.dll"), "old payload");
        return [new("client.dll", "payload", _payload.Length, Hash), new("nested/copy.dll", "payload", _payload.Length, Hash)];
    }

    private void AssertUnrelatedFilesRemain()
    {
        Assert.Equal("another pending payload", File.ReadAllText(ClientInstallTransaction.Staged(_root, OtherHash)));
        Assert.Equal("independent file", File.ReadAllText(Path.Combine(Workspace, "notes.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulCommitRemovesConsumedSharedObjectsAndPreservesUnrelatedFiles(bool deferred)
    {
        var files = Prepare();
        await ClientInstallTransaction.ApplyAsync(_root, files, _ => { }, default, deferCommit: deferred);
        if (deferred)
        {
            Assert.True(File.Exists(ClientInstallTransaction.Staged(_root, Hash)));
            await ClientInstallTransaction.CommitAsync(_root);
        }
        Assert.False(File.Exists(ClientInstallTransaction.Staged(_root, Hash)));
        Assert.Equal(_payload, File.ReadAllBytes(Path.Combine(_root, "client.dll")));
        Assert.Equal(_payload, File.ReadAllBytes(Path.Combine(_root, "nested/copy.dll")));
        AssertUnrelatedFilesRemain();
        Assert.False(File.Exists(Path.Combine(Workspace, "journal.json")));
        await ClientInstallTransaction.RecoverAsync(_root);
        AssertUnrelatedFilesRemain();
    }

    [Fact]
    public async Task RollbackRetainsVerifiedObjectsForRetryAndRestoresOriginalFiles()
    {
        var files = Prepare();
        await ClientInstallTransaction.ApplyAsync(_root, files, _ => { }, default, deferCommit: true);
        await ClientInstallTransaction.RecoverAsync(_root);
        Assert.Equal("old payload", File.ReadAllText(Path.Combine(_root, "client.dll")));
        Assert.False(File.Exists(Path.Combine(_root, "nested/copy.dll")));
        Assert.Equal(_payload, File.ReadAllBytes(ClientInstallTransaction.Staged(_root, Hash)));
        AssertUnrelatedFilesRemain();
    }

    [Fact]
    public async Task OfflineRecoveryFinishesCacheCleanupAfterTheCommitWasPersisted()
    {
        var files = Prepare();
        await ClientInstallTransaction.ApplyAsync(_root, files, _ => { }, default, deferCommit: true);
        var journalPath = Path.Combine(Workspace, "journal.json");
        var journal = JsonNode.Parse(File.ReadAllText(journalPath))!;
        journal["Committed"] = true;
        File.WriteAllText(journalPath, journal.ToJsonString());
        await ClientInstallTransaction.RecoverAsync(_root);
        Assert.False(File.Exists(ClientInstallTransaction.Staged(_root, Hash)));
        Assert.Equal(_payload, File.ReadAllBytes(Path.Combine(_root, "client.dll")));
        Assert.False(File.Exists(journalPath));
        Assert.Empty(Directory.GetFiles(Workspace, "original-*"));
        AssertUnrelatedFilesRemain();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
