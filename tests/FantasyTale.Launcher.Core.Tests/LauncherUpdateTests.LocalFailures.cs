using System.Text.Json;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Theory]
    [InlineData("failed", "disk", "staging", true)]
    [InlineData("rolled-back", "startup", "frontend-startup", true)]
    [InlineData("recovery-required", "disk", "recovery", true)]
    [InlineData("completed", null, null, false)]
    [InlineData("installing", null, null, false)]
    [InlineData("https://private.example/?token=secret", null, null, false)]
    public void InstallationDiagnosticsOmitSuccessAndUnrelatedSelections(string status, string? failure, string? phase, bool visible)
    {
        var outcome = new LauncherUpdateHandoff.Outcome(status, "source", "hash", failure, phase, -2147024784);
        var attempt = LauncherUpdateAttempt.From(outcome, "source", "hash");
        Assert.Equal(visible, attempt is not null);
        if (visible)
        {
            Assert.Equal(status, attempt!.Status);
            Assert.Equal(failure, attempt.Failure);
            Assert.Equal(phase, attempt.Phase);
            Assert.Equal(-2147024784, attempt.ErrorCode);
        }
        Assert.Null(LauncherUpdateAttempt.From(outcome, "other-source", "hash"));
        Assert.Null(LauncherUpdateAttempt.From(outcome, "source", "other-hash"));
        Assert.Null(LauncherUpdateAttempt.From(null, "source", "hash"));
    }

    [Fact]
    public void InstallationDiagnosticsDiscardUnrecognizedTextAndAcceptLegacyResults()
    {
        var outcome = new LauncherUpdateHandoff.Outcome("rolled-back", "source", "hash", "token=secret", "https://private.example/");
        var attempt = LauncherUpdateAttempt.From(outcome, "source", "hash");
        Assert.Equal(new LauncherUpdateAttempt("rolled-back", null, null, null), attempt);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(attempt));
        Assert.DoesNotContain("private.example", JsonSerializer.Serialize(attempt));
        Assert.Equal(attempt, LauncherUpdateAttempt.From(new("rolled-back", "source", "hash"), "source", "hash"));
    }

    [Fact]
    public async Task InstallationDiagnosticsAreCachedAndClearedBySuccessOrSourceEditing()
    {
        var policy = Policy();
        var index = IndexBytes(Manifest());
        var hash = Hash(index);
        var updateRoot = Path.Combine(Data, "launcher-update");
        Directory.CreateDirectory(updateRoot);
        var outcomePath = Path.Combine(updateRoot, "outcome.json");
        File.WriteAllText(outcomePath, JsonSerializer.Serialize(new LauncherUpdateHandoff.Outcome("failed", policy.Identity, hash,
            "disk", "staging", -2147024784)));
        using var handler = new FeedHandler(index);
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        Assert.Null(service.LastAttempt);
        Assert.Equal("available", (await service.CheckAsync()).Status);
        var attempt = Assert.IsType<LauncherUpdateAttempt>(service.LastAttempt);
        Assert.Equal("staging", attempt.Phase);

        File.WriteAllText(outcomePath, JsonSerializer.Serialize(new LauncherUpdateHandoff.Outcome("completed", policy.Identity, hash)));
        Assert.Same(attempt, service.LastAttempt);
        await service.CheckAsync();
        Assert.Null(service.LastAttempt);

        File.WriteAllText(outcomePath, JsonSerializer.Serialize(new LauncherUpdateHandoff.Outcome("failed", policy.Identity, hash, "disk", "replacement")));
        await service.CheckAsync();
        Assert.NotNull(service.LastAttempt);
        await service.SaveSourcesAsync(service.Sources());
        Assert.Null(service.LastAttempt);
    }

    [Theory]
    [InlineData("staging", "failed", "disk")]
    [InlineData("replacement", "failed", "disk")]
    [InlineData("frontend-startup", "rolled-back", "startup")]
    [InlineData("installed-verification", "rolled-back", "integrity")]
    public void HelperFailureDiagnosticsRetainOnlyPhaseAndNumericError(string phase, string status, string failure)
    {
        var code = unchecked((int)0x80070070);
        var outcome = LauncherUpdateHandoff.FailedOutcome("rolled-back", "source", "hash", phase,
            new IOException("https://private.example/?token=secret", code));

        Assert.Equal(status, outcome.Status);
        Assert.Equal(failure, outcome.Failure);
        Assert.Equal(phase, outcome.Phase);
        Assert.Equal(code, outcome.ErrorCode);
        var saved = JsonSerializer.Serialize(outcome);
        Assert.DoesNotContain("private.example", saved);
        Assert.DoesNotContain("secret", saved);
    }

    [Fact]
    public async Task TemporaryStorageRollbackDoesNotPermanentlyRejectTheCandidate()
    {
        var policy = Policy();
        using var handler = new FeedHandler(IndexBytes(Manifest()));
        using var http = new HttpClient(handler);
        string hash;
        await using (var first = new LauncherUpdateService(Installation, Data, http))
        {
            first.SavePreferences(new(false, false));
            hash = (await first.CheckAsync()).ManifestSha256!;
            await first.DownloadAsync(hash);
            await UntilAsync(() => first.List()[0].Stage == "ready");
        }
        var updateRoot = Path.Combine(Data, "launcher-update");
        File.WriteAllText(Path.Combine(updateRoot, "outcome.json"), JsonSerializer.Serialize(new
        {
            Status = "rolled-back", SourceIdentity = policy.Identity, ManifestSha256 = hash,
            Failure = "disk", Phase = "staging", ErrorCode = unchecked((int)0x80070070)
        }));

        await using var restarted = new LauncherUpdateService(Installation, Data, http);
        Assert.Equal("available", (await restarted.CheckAsync()).Status);
        Assert.Equal("staging", restarted.LastAttempt?.Phase);
        Assert.Equal(unchecked((int)0x80070070), restarted.LastAttempt?.ErrorCode);
        restarted.StartMonitoring();
        await UntilAsync(() => restarted.List().Count > 0 && restarted.List()[0].Stage == "ready");
        Assert.Null(restarted.State.Failure);
        Assert.Equal("disk", restarted.LastAttempt?.Failure);
        Assert.Equal(hash, restarted.State.ManifestSha256);
        Assert.True(Directory.Exists(restarted.ReadyDirectory()));
        Assert.False(File.Exists(Path.Combine(updateRoot, "rejected.json")));
    }
}
