using FantasyTale.Launcher.Core.Updates;

namespace FantasyTale.Launcher.Core.Tests;

public sealed partial class LauncherUpdateTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShutdownDrainsDiscoveryWhenTransportReportsFailureInsteadOfCancellation(bool monitor)
    {
        Policy();
        using var handler = new InterruptedDiscoveryHandler(); using var http = new HttpClient(handler);
        var service = new LauncherUpdateService(Installation, Data, http);
        if (monitor) service.StartMonitoring(); else _ = service.BeginStartupCheck();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(handler.Interrupted);
    }

    private sealed class InterruptedDiscoveryHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Interrupted { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { Interrupted = true; throw new HttpRequestException("Transport closed during cancellation"); }
            throw new InvalidOperationException("The fixture must be interrupted");
        }
    }
}
