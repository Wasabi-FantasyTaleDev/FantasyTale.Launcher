using FantasyTale.Launcher.Core.Definitions;
using FantasyTale.Launcher.Core.Hooks;

namespace FantasyTale.Launcher.Core.Instances;

internal static class BootstrapHandoff
{
    internal static async Task WaitForExitAsync(Task exited, Func<Task<HookLaunchStatus>> status,
        Func<CancellationToken, ValueTask> complete, Action<string> observationFailed, TimeSpan? interval = null)
    {
        while (!exited.IsCompleted)
        {
            if (await Task.WhenAny(exited, Task.Delay(interval ?? TimeSpan.FromMilliseconds(500))).ConfigureAwait(false) == exited) break;
            HookLaunchStatus current;
            try { current = await status().ConfigureAwait(false); }
            catch (InvalidOperationException) { continue; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or DefinitionException or System.Text.DecoderFallbackException or OperationCanceledException)
            {
                observationFailed(error.GetType().Name);
                break;
            }
            if (exited.IsCompleted) break;
            if (current.State != "ready" || !current.BootstrapComplete) continue;
            // Native readiness does not prove that auxiliary sockets have retired. Game exit
            // cancels observation so the owner can still perform immediate disposal.
            using var handoffCancellation = new CancellationTokenSource();
            Task handoff;
            try { handoff = complete(handoffCancellation.Token).AsTask(); }
            catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
            { observationFailed(error.GetType().Name); break; }
            if (await Task.WhenAny(handoff, exited).ConfigureAwait(false) == exited)
                await handoffCancellation.CancelAsync().ConfigureAwait(false);
            try { await handoff.ConfigureAwait(false); }
            catch (OperationCanceledException) when (handoffCancellation.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
            { observationFailed(error.GetType().Name); }
            break;
        }
        await exited.ConfigureAwait(false);
    }
}
