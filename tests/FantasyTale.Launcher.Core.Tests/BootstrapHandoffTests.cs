using FantasyTale.Launcher.Core.Hooks;
using FantasyTale.Launcher.Core.Instances;

namespace FantasyTale.Launcher.Core.Tests;

public sealed class BootstrapHandoffTests
{
    [Fact]
    public async Task CompletedHandoffRetiresBootstrapAndContinuesWatchingTheGame()
    {
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var monitor = BootstrapHandoff.WaitForExitAsync(exited.Task,
            () => Task.FromResult(new HookLaunchStatus("ready", "", []) { BootstrapComplete = true }),
            _ => { Interlocked.Increment(ref calls); stopped.TrySetResult(); return ValueTask.CompletedTask; },
            _ => throw new InvalidOperationException("Valid handoff must not fail observation."), TimeSpan.FromMilliseconds(1));
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(monitor.IsCompleted);
        exited.SetResult();
        await monitor;
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnfinishedOrInvalidReportsNeverStopBootstrapBeforeGameExit(bool invalid)
    {
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var monitor = BootstrapHandoff.WaitForExitAsync(exited.Task,
            () =>
            {
                observed.TrySetResult();
                return invalid ? Task.FromException<HookLaunchStatus>(new IOException("Untrusted status"))
                    : Task.FromResult(new HookLaunchStatus("ready", "", []));
            },
            _ => { calls++; return ValueTask.CompletedTask; }, _ => { }, TimeSpan.FromMilliseconds(1));
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(monitor.IsCompleted);
        exited.SetResult();
        await monitor;
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task GameExitCancelsPendingRetirementBeforeReturningToDisposal()
    {
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = false;
        var monitor = BootstrapHandoff.WaitForExitAsync(exited.Task,
            () => Task.FromResult(new HookLaunchStatus("ready", "", []) { BootstrapComplete = true }),
            async token =>
            {
                started.SetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { canceled = true; throw; }
            }, _ => throw new InvalidOperationException("Game exit is not an observation failure."), TimeSpan.FromMilliseconds(1));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(monitor.IsCompleted);
        exited.SetResult();
        await monitor.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(canceled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetirementFailureRetainsGameObservation(bool synchronous)
    {
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = BootstrapHandoff.WaitForExitAsync(exited.Task,
            () => Task.FromResult(new HookLaunchStatus("ready", "", []) { BootstrapComplete = true }),
            _ => synchronous ? throw new IOException("Retirement failed") : ValueTask.FromException(new IOException("Retirement failed")),
            error => failed.SetResult(error), TimeSpan.FromMilliseconds(1));
        Assert.Equal(nameof(IOException), await failed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(monitor.IsCompleted);
        exited.SetResult();
        await monitor;
    }
}
