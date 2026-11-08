using System.Net;
using FantasyTale.Launcher.Downloads;

namespace FantasyTale.Launcher.Core.Stores;

public sealed partial class ClientDownloadService
{
    private async Task PreparePluginArchiveAsync(Job job, HttpDownloadEngine engine, CancellationToken ct)
    {
        if (ClientRelease.PluginZip(job.Saved.Package) is not { } volume || job.Files.Any(file => file.PluginArchive)) return;
        var client = job.Snapshot.Target.Directory;
        // Resume verified members without probing an optional transport again.
        var complete = true;
        foreach (var file in job.Files)
            if (!await HttpDownloadEngine.MatchesAsync(ClientInstallTransaction.Staged(client, file.Sha256), file.Size, file.Sha256, ct).ConfigureAwait(false))
            { complete = false; break; }
        if (complete) return;
        var archive = volume;
        var staged = ClientInstallTransaction.Staged(client, archive.Sha256);
        try
        {
            if (!await HttpDownloadEngine.MatchesAsync(staged, archive.Size, archive.Sha256, ct).ConfigureAwait(false))
            {
                var resolved = await Source(job).ResolveAsync(archive.SourcePath, ct).ConfigureAwait(false);
                Change(job, s => s with { CurrentPath = volume.Path, Files = [new(volume.Path, archive.Size, 0, "queued")] });
                await engine.DownloadAsync(new(resolved, staged, archive.Size, archive.Sha256),
                    new InlineProgress(p => Change(job, s => s with
                    {
                        Stage = p.Received == p.Total ? "verifying" : "downloading", BytesPerSecond = p.BytesPerSecond,
                        SpeedSampleCount = p.BytesPerSecond > 0 ? s.SpeedSampleCount + 1 : 0,
                        Files = [new(volume.Path, archive.Size, p.Received, "downloading")]
                    })), ct).ConfigureAwait(false);
            }
        }
        catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // Only an absent optional ZIP falls back; integrity and transport failures stay actionable.
            Change(job, s => s with { Stage = "checking", CurrentPath = null, BytesPerSecond = 0, SpeedSampleCount = 0 });
            return;
        }
        Change(job, s => s with { Stage = "installing", BytesPerSecond = 0, StageProgress = 0 });
        await ClientArchiveExtractor.ExtractAsync(client, [volume], null,
            value => Change(job, s => s with { StageProgress = value }), ct, expectedFiles: job.Files).ConfigureAwait(false);
        _ = Source(job);
        ct.ThrowIfCancellationRequested();
        Change(job, s => s with { Stage = "checking", StageProgress = null, CurrentPath = null });
    }
}
