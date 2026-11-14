using System.Text.Json;

namespace FantasyTale.Launcher.Sdk;

/// <summary>What changed in a watched native form since the previous observation.</summary>
[Flags]
public enum GameNativeUiFormChanges
{
    /// <summary>Nothing changed.</summary>
    None = 0,
    /// <summary>A form appeared, or the game loaded it again; the observation carries a new session.</summary>
    Opened = 1,
    /// <summary>The open form moved or was resized.</summary>
    Moved = 2,
    /// <summary>The form's selection-group value changed.</summary>
    SelectionChanged = 4,
    /// <summary>The client counted at least one native button click; the click may belong to any form.</summary>
    Clicked = 8,
    /// <summary>The form that was open is gone, or watching stopped; its session ended.</summary>
    Closed = 16,
}

/// <summary>Why a watched form is or is not reported open.</summary>
public enum GameNativeUiFormWatchStatus
{
    /// <summary>A visible form with a nonempty rectangle is open.</summary>
    Open,
    /// <summary>The read succeeded and no such form is open.</summary>
    NotOpen,
    /// <summary>The caller asked not to watch now, so nothing was read.</summary>
    Dormant,
    /// <summary>The read was not submitted because the channel was occupied; the previous state stands.</summary>
    Busy,
    /// <summary>The read failed; the previous state stands and a later observation may succeed.</summary>
    ReadFailed,
    /// <summary>The provider or client build does not track forms; no later observation reads again.</summary>
    Unsupported,
}

/// <summary>One observation of a watched form.</summary>
/// <param name="Status">Whether the form is open, and why not.</param>
/// <param name="Changes">What changed since the previous observation.</param>
/// <param name="SessionId">The current form's session, a new value every time the form opens; null when none is open.</param>
/// <param name="Form">The form as read now; null unless <paramref name="Status"/> is <see cref="GameNativeUiFormWatchStatus.Open"/>.</param>
public sealed record GameNativeUiFormObservation(GameNativeUiFormWatchStatus Status, GameNativeUiFormChanges Changes,
    string? SessionId, GameNativeUiForm? Form);

/// <summary>
/// Follows one native form, loaded from a <c>.ui</c> resource, through cheap form reads. Each form the game loads is one
/// session: reopening a dialog, or watching again after a pause, starts a new session, so actions from an earlier view can
/// be told apart and rejected. When several forms from the resource are visible the most recently loaded one is followed.
/// An instance is not thread-safe; drive it from one caller, with <see cref="ObserveAsync"/> or <see cref="RunAsync"/>.
/// </summary>
public sealed class GameNativeUiFormWatcher
{
    private readonly IGameNativeUi _native;
    private GameNativeUiForm? _form;
    private string? _session;
    private bool _unsupported;

    /// <summary>Watches the forms the game loads from <paramref name="resource"/>.</summary>
    public GameNativeUiFormWatcher(IGameNativeUi native, string resource)
    {
        ArgumentNullException.ThrowIfNull(native);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        _native = native;
        Resource = resource;
    }

    /// <summary>The <c>.ui</c> resource whose forms are watched.</summary>
    public string Resource { get; }

    /// <summary>The open form's session, or null when none is open.</summary>
    public string? SessionId => _session;

    /// <summary>The open form as last read, or null when none is open.</summary>
    public GameNativeUiForm? Form => _form;

    /// <summary>
    /// Reads the form once and reports what changed. With <paramref name="watch"/> false nothing is read and an open
    /// session ends, which is how a caller pauses watching outside the scenes where the form matters.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    public async ValueTask<GameNativeUiFormObservation> ObserveAsync(bool watch = true, CancellationToken cancellationToken = default)
    {
        if (_unsupported) return Close(GameNativeUiFormWatchStatus.Unsupported);
        if (!watch) return Close(GameNativeUiFormWatchStatus.Dormant);
        IReadOnlyList<GameNativeUiForm> forms;
        try { forms = await _native.GetFormsAsync(Resource, cancellationToken).ConfigureAwait(false); }
        catch (GameNativeUiBusyException) { return Unchanged(GameNativeUiFormWatchStatus.Busy); }
        catch (NotSupportedException)
        {
            _unsupported = true;
            return Close(GameNativeUiFormWatchStatus.Unsupported);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or JsonException or TimeoutException)
        {
            return Unchanged(GameNativeUiFormWatchStatus.ReadFailed);
        }
        var form = forms.Where(candidate => candidate.Visible && candidate.Width > 0 && candidate.Height > 0)
            .OrderByDescending(candidate => candidate.Sequence).FirstOrDefault();
        if (form is null) return Close(GameNativeUiFormWatchStatus.NotOpen);
        var previous = _form;
        _form = form;
        if (previous is null || previous.Sequence != form.Sequence)
        {
            _session = Guid.NewGuid().ToString("N");
            var changes = GameNativeUiFormChanges.Opened | (previous is null ? GameNativeUiFormChanges.None : GameNativeUiFormChanges.Closed);
            return new(GameNativeUiFormWatchStatus.Open, changes, _session, form);
        }
        var changed = GameNativeUiFormChanges.None;
        if (previous.X != form.X || previous.Y != form.Y || previous.Width != form.Width || previous.Height != form.Height)
            changed |= GameNativeUiFormChanges.Moved;
        if (previous.Selection != form.Selection) changed |= GameNativeUiFormChanges.SelectionChanged;
        if (previous.Clicks != form.Clicks) changed |= GameNativeUiFormChanges.Clicked;
        return new(GameNativeUiFormWatchStatus.Open, changed, _session, form);
    }

    /// <summary>
    /// Observes every <paramref name="interval"/> until canceled and passes each observation that changed something to
    /// <paramref name="changed"/>. <paramref name="shouldWatch"/>, when given, is asked before each read; while it answers
    /// false nothing is read. Returns when the token is canceled.
    /// </summary>
    public async Task RunAsync(TimeSpan interval, Func<GameNativeUiFormObservation, ValueTask> changed, Func<bool>? shouldWatch = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changed);
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), "The interval must be positive.");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var observation = await ObserveAsync(shouldWatch?.Invoke() ?? true, cancellationToken).ConfigureAwait(false);
                if (observation.Changes != GameNativeUiFormChanges.None) await changed(observation).ConfigureAwait(false);
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private GameNativeUiFormObservation Close(GameNativeUiFormWatchStatus status)
    {
        var changes = _form is null ? GameNativeUiFormChanges.None : GameNativeUiFormChanges.Closed;
        _form = null;
        _session = null;
        return new(status, changes, null, null);
    }

    private GameNativeUiFormObservation Unchanged(GameNativeUiFormWatchStatus status) =>
        new(status, GameNativeUiFormChanges.None, _session, _form);
}
