using System.Text.Json;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Core.Hooks;

/// <summary>
/// Keeps the overlay's holes in step with the game's own message boxes: while the overlay shows a surface it polls the
/// build's message-box forms and tells the overlay where the visible ones are. Nothing is polled while the overlay shows
/// nothing, and any failure leaves the overlay without holes rather than blocking it.
/// </summary>
public sealed class NativeDialogHoleWatcher : IAsyncDisposable
{
    /// <summary>Game pixels the overlay keeps clear around each dialog.</summary>
    public const int Margin = 6;

    private readonly IOverlayHost _overlay;
    private readonly IGameNativeUi _native;
    private readonly string[] _resources;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly Task _run;
    private OverlayNativeHole[] _sent = [];

    /// <summary>Starts watching <paramref name="resources"/> for the lifetime of the returned watcher.</summary>
    /// <param name="overlay">The overlay whose presentation gates polling and which receives the holes.</param>
    /// <param name="native">The game's native UI service.</param>
    /// <param name="resources">The message-box <c>.ui</c> resources of the running build; must not be empty.</param>
    /// <param name="interval">Time between reads while a surface is shown; defaults to 150 ms.</param>
    public NativeDialogHoleWatcher(IOverlayHost overlay, IGameNativeUi native, IReadOnlyList<string> resources, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(resources);
        if (resources.Count == 0) throw new ArgumentException("At least one message-box resource is required.", nameof(resources));
        _overlay = overlay;
        _native = native;
        _resources = [.. resources];
        _interval = interval ?? TimeSpan.FromMilliseconds(150);
        if (_interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), "The interval must be positive.");
        _overlay.PresentationChanged += OnPresentationChanged;
        _run = Task.Run(RunAsync);
    }

    private void OnPresentationChanged(object? sender, EventArgs e)
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task RunAsync()
    {
        var token = _stop.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (_overlay.Presentation.Surfaces.Count == 0)
                {
                    // Nothing covers the game: drop any hole once, then sleep until the overlay shows something.
                    if (!await SendAsync([], token).ConfigureAwait(false)) return;
                    await _wake.WaitAsync(token).ConfigureAwait(false);
                    continue;
                }
                var holes = await ReadAsync(token).ConfigureAwait(false);
                if (holes is null) return;
                if (!await SendAsync(holes, token).ConfigureAwait(false)) return;
                await Task.Delay(_interval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    // Null means this build cannot track forms, so watching ends. A busy channel keeps the last answer.
    private async Task<OverlayNativeHole[]?> ReadAsync(CancellationToken token)
    {
        var forms = new List<GameNativeUiForm>();
        foreach (var resource in _resources)
        {
            try { forms.AddRange(await _native.GetFormsAsync(resource, token).ConfigureAwait(false)); }
            catch (GameNativeUiBusyException) { return _sent; }
            catch (NotSupportedException) { return null; }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException
                or JsonException or TimeoutException or ArgumentException)
            {
                return [];
            }
        }
        return OverlayNativeHole.FromForms(forms, Margin);
    }

    private async Task<bool> SendAsync(OverlayNativeHole[] holes, CancellationToken token)
    {
        if (holes.SequenceEqual(_sent)) return true;
        try
        {
            await _overlay.SetNativeHolesAsync(holes, token).ConfigureAwait(false);
            _sent = holes;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException
            or NotSupportedException or TimeoutException)
        {
            // The overlay is gone or cannot take holes; the game and the overlay keep working without them.
            return error is not (ObjectDisposedException or NotSupportedException);
        }
        return true;
    }

    /// <summary>Stops watching, waits for the last read to finish and removes the holes.</summary>
    public async ValueTask DisposeAsync()
    {
        _overlay.PresentationChanged -= OnPresentationChanged;
        await _stop.CancelAsync().ConfigureAwait(false);
        await _run.ConfigureAwait(false);
        if (_sent.Length > 0)
        {
            try { await _overlay.SetNativeHolesAsync([], CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException or NotSupportedException or OperationCanceledException or TimeoutException) { }
        }
        _stop.Dispose();
        _wake.Dispose();
    }
}
