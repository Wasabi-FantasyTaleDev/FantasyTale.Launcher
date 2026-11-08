namespace FantasyTale.Launcher.Host.Window;

/// <summary>Shares a confirmed exit request and preserves a failed cleanup for the requesting dialog.</summary>
internal sealed class WindowExitOperation(Func<Task> stop, Action close)
{
    private readonly object _gate = new();
    private Task? _request;

    internal Task RequestAsync()
    {
        lock (_gate)
        {
            if (_request is null or { IsFaulted: true } or { IsCanceled: true })
                _request = Task.Run(async () =>
                {
                    await stop().ConfigureAwait(false);
                    close();
                });
            return _request;
        }
    }
}
