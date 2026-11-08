using System.Reflection;
using System.Net;
using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Fact]
    public async Task PublishedReadyStateAllowsImmediateHandoffBeforeWorkerLifetimeCompletes()
    {
        var policy = Policy();
        using var handler = new ArchiveFallbackHandler("valid") { IncrementalAvailable = true };
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        await service.DownloadAsync((await service.CheckAsync()).ManifestSha256!);
        await UntilAsync(() => service.List()[0].Stage == "ready");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await DeferUpdateWorkerCompletionAsync(service, release.Task);
            Assert.Equal("ready", service.State.Status);
            var objects = Path.Combine(service.ReadyDirectory(), "objects");
            foreach (var file in LauncherRelease.Parse(policy, IndexBytes(Manifest())).Files)
                Assert.Equal(file.Sha256, Hash(await File.ReadAllBytesAsync(Path.Combine(objects, file.Sha256 + ".verified"))));
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task PublishedFailureAllowsImmediateRetryBeforeWorkerLifetimeCompletes()
    {
        Policy();
        using var handler = new ArchiveFallbackHandler("valid") { ArchiveUnavailable = true };
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        await service.DownloadAsync((await service.CheckAsync()).ManifestSha256!);
        await UntilAsync(() => service.List()[0].Stage == "failed");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await DeferUpdateWorkerCompletionAsync(service, release.Task);
            Assert.Equal("failed", service.State.Status);
            handler.IncrementalAvailable = true;
            await service.ActionAsync("retry");
            await UntilAsync(() => service.List()[0].Stage is "ready" or "failed");
            Assert.Equal("ready", service.List()[0].Stage);
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(service.ReadyDirectory(), "objects"), "*.verified"));
        }
        finally { release.TrySetResult(); }
    }

    private static async Task DeferUpdateWorkerCompletionAsync(LauncherUpdateService service, Task completion)
    {
        // Extend only lifetime bookkeeping after real staging ended, reproducing a delayed Task completion deterministically.
        var field = typeof(LauncherUpdateService).GetField("_worker", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var worker = (Task)field.GetValue(service)!;
        await worker;
        field.SetValue(service, Task.WhenAll(worker, completion));
    }

    [Fact]
    public async Task CanceledRowRemainsUntilItsTransferCleanupFinishes()
    {
        Policy();
        using var handler = new CancellationCleanupHandler();
        using var http = new HttpClient(handler);
        await using var service = new LauncherUpdateService(Installation, Data, http);
        await service.DownloadAsync((await service.CheckAsync()).ManifestSha256!);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await handler.PayloadStarted.Task.WaitAsync(deadline.Token);
            var cancel = service.ActionAsync("cancel");
            await handler.CancellationObserved.Task.WaitAsync(deadline.Token);
            service.ClearFinished();
            Assert.Equal("canceled", Assert.Single(service.List()).Stage);
            Assert.False(cancel.IsCompleted);
            handler.ReleaseCleanup.TrySetResult();
            await cancel.WaitAsync(deadline.Token);
            service.ClearFinished();
            Assert.Empty(service.List());
        }
        finally { handler.ReleaseCleanup.TrySetResult(); }
    }

    private sealed class CancellationCleanupHandler : HttpMessageHandler
    {
        internal readonly TaskCompletionSource PayloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource CancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseCleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("stable.toml", StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content = new StringContent(Manifest()) };
            PayloadStarted.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult();
                await ReleaseCleanup.Task;
                throw;
            }
            throw new InvalidOperationException("Payload staging must be canceled.");
        }
    }
}
