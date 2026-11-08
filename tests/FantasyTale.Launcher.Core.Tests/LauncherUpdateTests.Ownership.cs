using FantasyTale.Launcher.Core.Stores;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    private async Task<(LauncherUpdatePolicy Policy, LauncherRelease Next)> SeedOwnedFilesAsync(bool owned = true)
    {
        var policy = Policy();
        var retired = $$"""
            [[files]]
            path = "retired.dll"
            source_path = "objects/retired"
            size = 3
            sha256 = "{{Hash("old"u8.ToArray())}}"
            """;
        var previous = LauncherRelease.Parse(policy, IndexBytes(Manifest(1, retired, Hash("old"u8.ToArray()))));
        var next = LauncherRelease.Parse(policy, IndexBytes(Manifest()));
        Directory.CreateDirectory(ClientInstallTransaction.Workspace(Installation));
        foreach (var file in previous.Files)
            File.WriteAllText(ClientInstallTransaction.Staged(Installation, file.Sha256),
                file.Path == "wwwroot/index.html" ? "web" : file.Sha256 == Hash("old"u8.ToArray()) ? "old" : "new");
        await ClientInstallTransaction.ApplyAsync(Installation, previous.Files, _ => { }, default,
            LauncherUpdateService.Receipt(policy, previous), pruneOwned: owned);
        // A committed baseline consumes its cache; the next release stages again.
        foreach (var file in next.Files)
            File.WriteAllText(ClientInstallTransaction.Staged(Installation, file.Sha256),
                file.Path == "wwwroot/index.html" ? "web" : "new");
        File.WriteAllText(Path.Combine(Installation, "personal.txt"), "user data");
        return (policy, next);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovedOwnedFilesRollbackWithTheUpdateAndCommitOnlyAfterHealth(bool healthy)
    {
        var (policy, next) = await SeedOwnedFilesAsync();
        await ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default,
            LauncherUpdateService.Receipt(policy, next), deferCommit: true, pruneOwned: true);
        Assert.False(File.Exists(Path.Combine(Installation, "retired.dll")));
        Assert.Equal(1, ClientInstallTransaction.ReadReceipt(Installation)!.Revision);
        if (healthy) await ClientInstallTransaction.CommitAsync(Installation);
        await ClientInstallTransaction.RecoverAsync(Installation);
        await ClientInstallTransaction.RecoverAsync(Installation);
        Assert.Equal(!healthy, File.Exists(Path.Combine(Installation, "retired.dll")));
        Assert.Equal(healthy ? "new" : "old", File.ReadAllText(Path.Combine(Installation, LauncherRelease.Executable)));
        Assert.Equal(healthy ? 2 : 1, ClientInstallTransaction.ReadReceipt(Installation)!.Revision);
        Assert.Equal("user data", File.ReadAllText(Path.Combine(Installation, "personal.txt")));
        Assert.False(File.Exists(Path.Combine(ClientInstallTransaction.Workspace(Installation), "journal.json")));
    }

    [Fact]
    public async Task ModifiedObsoleteFileRefusesTheUpdateBeforeReplacement()
    {
        var (policy, next) = await SeedOwnedFilesAsync();
        File.WriteAllText(Path.Combine(Installation, "retired.dll"), "external edit");
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default,
            LauncherUpdateService.Receipt(policy, next), pruneOwned: true));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, LauncherRelease.Executable)));
        Assert.Equal("external edit", File.ReadAllText(Path.Combine(Installation, "retired.dll")));
        Assert.False(File.Exists(Path.Combine(ClientInstallTransaction.Workspace(Installation), "journal.json")));
    }

    [Fact]
    public async Task RecreatedObsoleteFileBlocksCommitAndRollbackWithoutDeletingTheEdit()
    {
        var (policy, next) = await SeedOwnedFilesAsync();
        await ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default,
            LauncherUpdateService.Receipt(policy, next), deferCommit: true, pruneOwned: true);
        File.WriteAllText(Path.Combine(Installation, "retired.dll"), "external edit");
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.CommitAsync(Installation));
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.RecoverAsync(Installation));
        Assert.Equal("external edit", File.ReadAllText(Path.Combine(Installation, "retired.dll")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(ClientInstallTransaction.Workspace(Installation), "original-0")));
        Assert.True(File.Exists(Path.Combine(ClientInstallTransaction.Workspace(Installation), "journal.json")));
    }

    [Fact]
    public async Task LegacyInstallationsDoNotGuessOwnershipOfAbsentFiles()
    {
        var (policy, next) = await SeedOwnedFilesAsync(owned: false);
        await ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default,
            LauncherUpdateService.Receipt(policy, next), pruneOwned: true);
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, "retired.dll")));
        Assert.True(File.Exists(Path.Combine(ClientInstallTransaction.Workspace(Installation), "owned-files.json")));
        Assert.Equal("user data", File.ReadAllText(Path.Combine(Installation, "personal.txt")));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ViteAssetsArePrunedTransactionallyWithOrWithoutPreviousOwnership(bool owned, bool healthy)
    {
        var (policy, _) = await SeedOwnedFilesAsync(owned);
        var extra = $$"""
            [[files]]
            path = "wwwroot/assets/nested/app-current.js"
            source_path = "objects/current"
            size = 3
            sha256 = "{{Hash("web"u8.ToArray())}}"
            """;
        var next = LauncherRelease.Parse(policy, IndexBytes(Manifest(extra: extra)));
        var assets = Path.Combine(Installation, "wwwroot", "assets");
        var nested = Path.Combine(assets, "nested"); Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(assets, "app-old-123.js"), "old bundle");
        File.WriteAllText(Path.Combine(nested, "app-old-123.css"), "old styles");
        File.WriteAllText(Path.Combine(nested, "app-current.js"), "web");
        File.WriteAllText(Path.Combine(Installation, "wwwroot", "custom.txt"), "user content");
        await ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default,
            LauncherUpdateService.Receipt(policy, next), deferCommit: true, pruneOwned: true);
        Assert.False(File.Exists(Path.Combine(assets, "app-old-123.js")));
        Assert.False(File.Exists(Path.Combine(nested, "app-old-123.css")));
        Assert.Equal("web", File.ReadAllText(Path.Combine(nested, "app-current.js")));
        if (healthy) await ClientInstallTransaction.CommitAsync(Installation);
        await ClientInstallTransaction.RecoverAsync(Installation);
        Assert.Equal(!healthy, File.Exists(Path.Combine(assets, "app-old-123.js")));
        Assert.Equal(!healthy, File.Exists(Path.Combine(nested, "app-old-123.css")));
        Assert.Equal("web", File.ReadAllText(Path.Combine(nested, "app-current.js")));
        Assert.Equal("user content", File.ReadAllText(Path.Combine(Installation, "wwwroot", "custom.txt")));
        Assert.Equal("user data", File.ReadAllText(Path.Combine(Installation, "personal.txt")));
    }

    [Fact]
    public async Task OwnershipCannotBeAdoptedByAnUpdateFromAnotherSource()
    {
        var (policy, next) = await SeedOwnedFilesAsync();
        var changed = LauncherUpdateService.Receipt(policy, next) with { SourceIdentity = new string('a', 64) };
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default, changed, pruneOwned: true));
        Assert.True(File.Exists(Path.Combine(Installation, "retired.dll")));
        Assert.Equal(1, ClientInstallTransaction.ReadReceipt(Installation)!.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitSourceTransitionKeepsOwnershipTransactional(bool healthy)
    {
        var (policy, next) = await SeedOwnedFilesAsync();
        var previous = ClientInstallTransaction.ReadReceipt(Installation)!;
        var changed = LauncherUpdateService.Receipt(policy, next) with { SourceIdentity = new string('a', 64) };
        await ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default, changed,
            deferCommit: true, pruneOwned: true, sourceTransitionFrom: previous);
        Assert.Equal(previous, ClientInstallTransaction.ReadReceipt(Installation));
        Assert.False(File.Exists(Path.Combine(Installation, "retired.dll")));
        if (healthy) await ClientInstallTransaction.CommitAsync(Installation);
        await ClientInstallTransaction.RecoverAsync(Installation);
        Assert.Equal(healthy ? changed : previous, ClientInstallTransaction.ReadReceipt(Installation));
        Assert.Equal(!healthy, File.Exists(Path.Combine(Installation, "retired.dll")));
        Assert.Equal(healthy ? "new" : "old", File.ReadAllText(Path.Combine(Installation, LauncherRelease.Executable)));
        Assert.Equal("user data", File.ReadAllText(Path.Combine(Installation, "personal.txt")));
        // A subsequent same-source update also validates the committed ownership record.
        var subsequent = healthy ? changed with { Revision = 3 } : LauncherUpdateService.Receipt(policy, next);
        await ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default, subsequent, pruneOwned: true);
        Assert.Equal(subsequent, ClientInstallTransaction.ReadReceipt(Installation));
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("manifest")]
    [InlineData("version")]
    [InlineData("source")]
    [InlineData("package")]
    [InlineData("product")]
    public async Task SourceTransitionRequiresExactPreviousReceiptAndSameProduct(string mismatch)
    {
        var (policy, next) = await SeedOwnedFilesAsync();
        var previous = ClientInstallTransaction.ReadReceipt(Installation)!;
        var expected = mismatch switch
        {
            "revision" => previous with { Revision = 0 },
            "manifest" => previous with { ManifestSha256 = new string('b', 64) },
            "version" => previous with { Version = "another-version" },
            "source" => previous with { SourceIdentity = new string('b', 64) },
            _ => previous,
        };
        var changed = LauncherUpdateService.Receipt(policy, next) with { SourceIdentity = new string('a', 64) };
        if (mismatch == "package") changed = changed with { PackageId = "another-package" };
        if (mismatch == "product") changed = changed with { SourceId = "another-product" };
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default,
            changed, pruneOwned: true, sourceTransitionFrom: expected));
        Assert.Equal(previous, ClientInstallTransaction.ReadReceipt(Installation));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, LauncherRelease.Executable)));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, "retired.dll")));
        Assert.False(File.Exists(Path.Combine(ClientInstallTransaction.Workspace(Installation), "journal.json")));
    }

    [Fact]
    public async Task AuthorizedSourceTransitionStillPreservesModifiedObsoleteFiles()
    {
        var (policy, next) = await SeedOwnedFilesAsync();
        var previous = ClientInstallTransaction.ReadReceipt(Installation)!;
        var changed = LauncherUpdateService.Receipt(policy, next) with { SourceIdentity = new string('a', 64) };
        File.WriteAllText(Path.Combine(Installation, "retired.dll"), "external edit");
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default,
            changed, pruneOwned: true, sourceTransitionFrom: previous));
        Assert.Equal("external edit", File.ReadAllText(Path.Combine(Installation, "retired.dll")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, LauncherRelease.Executable)));
        Assert.Equal(previous, ClientInstallTransaction.ReadReceipt(Installation));
        Assert.False(File.Exists(Path.Combine(ClientInstallTransaction.Workspace(Installation), "journal.json")));
    }

    [Fact]
    public async Task SourceTransitionDoesNotBypassInconsistentOwnership()
    {
        var (policy, next) = await SeedOwnedFilesAsync();
        var changedReceipt = ClientInstallTransaction.ReadReceipt(Installation)! with { Version = "changed" };
        UpdateFiles.Write(Path.Combine(ClientInstallTransaction.Workspace(Installation), "receipt.json"), changedReceipt);
        var changed = LauncherUpdateService.Receipt(policy, next) with { SourceIdentity = new string('a', 64) };
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default,
            changed, pruneOwned: true, sourceTransitionFrom: changedReceipt));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, "retired.dll")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, LauncherRelease.Executable)));
        Assert.Equal(changedReceipt, ClientInstallTransaction.ReadReceipt(Installation));
    }

    [Fact]
    public async Task SourceTransitionWithoutOwnershipDoesNotGuessObsoleteFiles()
    {
        var (policy, next) = await SeedOwnedFilesAsync(owned: false);
        var previous = ClientInstallTransaction.ReadReceipt(Installation)!;
        var changed = LauncherUpdateService.Receipt(policy, next) with { SourceIdentity = new string('a', 64) };
        await ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default, changed,
            pruneOwned: true, sourceTransitionFrom: previous);
        Assert.Equal(changed, ClientInstallTransaction.ReadReceipt(Installation));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, "retired.dll")));
        Assert.Equal("user data", File.ReadAllText(Path.Combine(Installation, "personal.txt")));
    }

    [Fact]
    public async Task InterruptionAfterRemovalRestoresTheRemovedFileAndOriginalReceipt()
    {
        var (policy, next) = await SeedOwnedFilesAsync();
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => throw new IOException("Interrupted after first mutation."), default,
            LauncherUpdateService.Receipt(policy, next), pruneOwned: true));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, "retired.dll")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, LauncherRelease.Executable)));
        Assert.Equal(1, ClientInstallTransaction.ReadReceipt(Installation)!.Revision);
        await ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default,
            LauncherUpdateService.Receipt(policy, next), pruneOwned: true);
        Assert.False(File.Exists(Path.Combine(Installation, "retired.dll")));
    }

    [Fact]
    public async Task UnchangedOwnedFilesAreReverifiedBeforeHealthyCommit()
    {
        var (policy, next) = await SeedOwnedFilesAsync();
        await ClientInstallTransaction.ApplyAsync(Installation, next.Files, _ => { }, default,
            LauncherUpdateService.Receipt(policy, next), deferCommit: true, pruneOwned: true);
        File.WriteAllText(Path.Combine(Installation, "wwwroot", "index.html"), "external edit");
        await Assert.ThrowsAsync<IOException>(() => ClientInstallTransaction.CommitAsync(Installation));
        await ClientInstallTransaction.RecoverAsync(Installation);
        Assert.Equal("external edit", File.ReadAllText(Path.Combine(Installation, "wwwroot", "index.html")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installation, "retired.dll")));
        Assert.Equal(1, ClientInstallTransaction.ReadReceipt(Installation)!.Revision);
    }
}
