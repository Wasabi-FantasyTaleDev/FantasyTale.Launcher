using System.Text.Json;
using System.Threading.Channels;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;

/// <summary>
/// Draws the Mode Extension entry over the client's own Create Room dialog and opens the mode list from it, without
/// changing the dialog. While the server says room creation is available, the controller reads the native form tracker
/// (a read of the game's form load and destroy observers, never queued behind game-thread UI work) and gives the page
/// the dialog's rectangle; the page pins its button there. The dialog's destruction removes the entry. A confirmed
/// mode the dialog lists itself is selected through its own mode button; any other mode becomes a <c>#name#</c> tag at
/// the start of the room title, which the server turns into the room's mode and removes from the title others see.
/// </summary>
internal sealed class OverlayRoomModeController : IAsyncDisposable
{
    private readonly GameOverlayControllerContext _context;
    private readonly ModeExtensionData _data;
    private readonly Channel<Intent> _intents;
    private readonly CancellationTokenSource _stop = new();
    private readonly RoomCreateObservationGate _lifecycle;
    private readonly Task _work;
    private readonly (RoomBinding Binding, GameNativeUiFormWatcher Watcher)[] _watchers;
    // The binding and watcher of the dialog that is open now; every native operation targets this binding's form.
    private RoomBinding? _binding;
    private GameNativeUiFormWatcher? _watcher;
    private readonly GameNativeUiWaitOptions _wait;
    private OverlayAnchor? _entry;
    private string? _sessionId;
    private int? _selectedId;
    // The native selection once the controller last wrote a mode tag; another native choice afterwards removes the tag.
    private int? _taggedOver;
    // The dialog's own current mode, from the form read; the picker opens on it unless a tag overrides it.
    private int? _nativeSelection;
    // While a tag is in effect the dialog's selected mode button is re-enabled so it looks unselected (the tag decides the
    // mode: kr-812 keeps its form value, cn-406 then submits no selection), and the visible description label shows the
    // extension mode. Replaced label texts are kept
    // until the label is visible again and can be restored, together with the text written over them: a build with one
    // shared description label (kr-1256) rewrites it itself on a native pick, and that text must not be overwritten.
    private string? _disguisedButton;
    private readonly Dictionary<string, (string Original, string Written)> _restores = new(StringComparer.Ordinal);
    private bool _tagged;
    private Failure? _error;
    private long _revision;
    private bool _open, _busy, _pageReady;
    private string? _observation;
    private int _disposed;
    private readonly object _disposeGate = new();
    private Task? _disposal;

    internal OverlayRoomModeController(GameOverlayControllerContext context, ModeExtensionData data)
    {
        _context = context;
        _data = data;
        _intents = Channel.CreateBounded<Intent>(new BoundedChannelOptions(data.MaxPendingActions)
        { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest });
        context.Panel.MessageReceived += OnMessage;
        _lifecycle = new(context.Companion, data.Bindings.Count > 0, _ => _intents.Writer.TryWrite(new("lifecycle", null, null, null)));
        _watchers = context.NativeUi is { } native
            ? data.Bindings.Select(binding => (binding, new GameNativeUiFormWatcher(native, binding.Form))).ToArray()
            : [];
        _wait = new(TimeSpan.FromMilliseconds(data.OperationTimeoutMs), TimeSpan.FromMilliseconds(data.ResultPollMs));
        _work = Task.Run(RunAsync);
    }

    private void OnMessage(object? sender, OverlayMessageEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0 || args.PanelId != _context.Panel.PanelId) return;
        if (args.EventName == "mode-extension.ready") { _intents.Writer.TryWrite(new("ready", null, null, null)); return; }
        if (args.EventName is not ("mode-extension.open" or "mode-extension.confirm" or "mode-extension.cancel" or "mode-extension.dismiss")) return;
        if (args.Data.ValueKind != JsonValueKind.Object) { Record("input-rejected"); return; }
        var sessionId = args.Data.TryGetProperty("sessionId", out var session) && session.ValueKind == JsonValueKind.String
            ? session.GetString() : null;
        if (sessionId is null || !Guid.TryParseExact(sessionId, "N", out _)) { Record("input-rejected"); return; }
        int? modeId = null;
        string? description = null;
        if (args.EventName == "mode-extension.confirm")
        {
            if (!args.Data.TryGetProperty("modeId", out var mode) || mode.ValueKind != JsonValueKind.Number || !mode.TryGetInt32(out var number))
            { Record("input-rejected"); return; }
            modeId = number;
            if (args.Data.TryGetProperty("description", out var text) && text.ValueKind != JsonValueKind.Null)
            {
                if (text.ValueKind != JsonValueKind.String || text.GetString() is not { } value || value.Contains('\0'))
                { Record("input-rejected"); return; }
                description = value;
            }
        }
        _intents.Writer.TryWrite(new(args.EventName["mode-extension.".Length..], sessionId, modeId, description));
    }

    private async Task RunAsync()
    {
        try
        {
            var tick = Task.Delay(_data.PollMs, _stop.Token);
            var input = _intents.Reader.WaitToReadAsync(_stop.Token).AsTask();
            while (!_stop.IsCancellationRequested)
            {
                await Task.WhenAny(tick, input).ConfigureAwait(false);
                if (input.IsCompleted)
                {
                    if (!await input.ConfigureAwait(false)) break;
                    while (_intents.Reader.TryRead(out var intent)) await HandleAsync(intent).ConfigureAwait(false);
                    input = _intents.Reader.WaitToReadAsync(_stop.Token).AsTask();
                }
                if (tick.IsCompleted)
                {
                    await tick.ConfigureAwait(false);
                    await ObserveAsync().ConfigureAwait(false);
                    tick = Task.Delay(_data.PollMs, _stop.Token);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    /// <summary>
    /// Reads whether the client's Create Room dialog is open and where, from every resource the build may load it from;
    /// outside the plaza nothing is read.
    /// </summary>
    private async Task ObserveAsync()
    {
        var state = _lifecycle.Current;
        if (_watchers.Length == 0) { RecordObservation("dormant." + state.Reason); Closed(); return; }
        var observations = new GameNativeUiFormObservation[_watchers.Length];
        for (var index = 0; index < _watchers.Length; index++)
            observations[index] = await _watchers[index].Watcher.ObserveAsync(state.Active, _stop.Token).ConfigureAwait(false);
        if (Follow(observations) is not { } followed)
        {
            var statuses = observations.Select(observation => observation.Status).ToArray();
            if (statuses.Contains(GameNativeUiFormWatchStatus.Busy)) return;
            if (statuses.Contains(GameNativeUiFormWatchStatus.ReadFailed)) { RecordObservation("form-read-failed"); return; }
            // Provider or build without form tracking: the entry cannot be placed, so it never appears.
            if (statuses.All(status => status == GameNativeUiFormWatchStatus.Unsupported)) { RecordObservation("form-tracking-unsupported"); Closed(); return; }
            if (statuses.Contains(GameNativeUiFormWatchStatus.Dormant)) { RecordObservation("dormant." + state.Reason); Closed(); return; }
            RecordObservation("waiting-dialog"); Closed(); return;
        }
        var seen = observations[followed];
        var form = seen.Form!;
        RecordObservation("dialog-open");
        var entry = Place(form, _watchers[followed].Binding.Entry);
        _nativeSelection = form.Selection;
        // A dialog from another resource is a new dialog even when its watcher saw it before this one closed.
        var switched = !ReferenceEquals(_watchers[followed].Binding, _binding);
        (_binding, _watcher) = _watchers[followed];
        if (switched || seen.Changes.HasFlag(GameNativeUiFormChanges.Opened))
        {
            // A new dialog: a fresh session, no stale selection, and the server forgets any earlier unconfirmed pick.
            Record(FormattableString.Invariant($"dialog-geometry.{form.X}.{form.Y}.{form.Width}.{form.Height}.{form.RootWidth}.{form.RootHeight}"));
            _sessionId = seen.SessionId;
            _selectedId = null;
            _tagged = false;
            _disguisedButton = null;
            _restores.Clear();
            _open = false;
            _busy = false;
            _error = null;
            _entry = entry;
            Publish();
            return;
        }
        if (entry != _entry) { _entry = entry; Publish(); }
        var clicked = seen.Changes.HasFlag(GameNativeUiFormChanges.Clicked);
        if (_busy || !(clicked || (_tagged && form.Selection != _taggedOver))) return;
        if (!_tagged && _restores.Count == 0) return;
        await ReconcileAsync(form.Selection).ConfigureAwait(false);
    }

    /// <summary>
    /// After a click or a selection change, decides from one read of the dialog whether the player picked one of its own
    /// modes. A different selection, or the re-enabled selected button being disabled again (the client's callback ran for
    /// it), means they did: that choice wins, so the tag goes. Replaced description texts are restored once visible.
    /// </summary>
    private async Task ReconcileAsync(int? selection)
    {
        if (_context.NativeUi is not { } native || _binding is not { } binding) return;
        try
        {
            var read = await RunAsync(native, new(Guid.NewGuid().ToString("N"), "control.inspect", "")
            { Target = new GameNativeUiControlTarget { Form = binding.Form } }).ConfigureAwait(false);
            if (read.Status != "completed") return;
            var picked = _tagged && (selection != _taggedOver || (_disguisedButton is { } id
                && read.Controls.Any(control => control.Id == id && control.Enabled == false)));
            if (picked)
            {
                _tagged = false;
                _disguisedButton = null;
                var failure = await ClearTagAsync().ConfigureAwait(false);
                if (failure is null) { _selectedId = null; Record("tag-cleared"); }
                else { _error = failure; Record("clear." + failure.Reason); }
            }
            if (!_tagged) await RestoreLabelsAsync(native, read.Controls).ConfigureAwait(false);
            Publish();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or JsonException
            or TimeoutException or ArgumentException) { Record("reconcile-interrupted"); }
    }

    /// <summary>Puts back the native description text of every replaced label that is visible in this read.</summary>
    private async Task RestoreLabelsAsync(IGameNativeUi native, IReadOnlyList<GameNativeUiControlSnapshot> controls)
    {
        foreach (var control in controls)
        {
            if (!_restores.Remove(control.Id, out var replaced)) continue;
            if (!RestoresTo(control, replaced)) continue;
            if (await SetTextAsync(native, control, replaced.Original, null).ConfigureAwait(false) is { } failure)
                Record("restore." + failure.Reason);
        }
    }

    /// <summary>
    /// Whether a replaced label still needs its original text: only while it shows what the extension wrote. When the
    /// client has written its own description since (kr-1256 sub_5C9550 rewrites lblGameModeDescription on every native
    /// selection), the label is already correct and restoring would put back a stale description.
    /// </summary>
    internal static bool RestoresTo(GameNativeUiControlSnapshot label, (string Original, string Written) replaced) =>
        string.Equals(label.Text, replaced.Written, StringComparison.Ordinal);

    /// <summary>
    /// Which of one read per bound resource shows the dialog to follow: of those reported open, the most recently loaded
    /// form; null when none is open.
    /// </summary>
    internal static int? Follow(IReadOnlyList<GameNativeUiFormObservation> observations)
    {
        int? result = null;
        for (var index = 0; index < observations.Count; index++)
            if (observations[index] is { Status: GameNativeUiFormWatchStatus.Open, Form: { } form }
                && (result is not { } best || form.Sequence > observations[best].Form!.Sequence))
                result = index;
        return result;
    }

    /// <summary>The entry region in game client pixels: the binding's form-relative rectangle, at the dialog's bottom left.</summary>
    internal static OverlayAnchor Place(GameNativeUiForm form, RoomEntry layout) => form.Region(layout.X, layout.Y, layout.Width, layout.Height);

    private void Closed()
    {
        _binding = null;
        _watcher = null;
        if (_sessionId is null && _entry is null && !_open) return;
        _entry = null;
        _sessionId = null;
        _open = false;
        _busy = false;
        _error = null;
        Publish();
    }

    private async Task HandleAsync(Intent intent)
    {
        switch (intent.Kind)
        {
            case "ready": _pageReady = true; Publish(); return;
            case "lifecycle": await ObserveAsync().ConfigureAwait(false); return;
        }
        // An action from a closed or reopened dialog's view is stale.
        if (intent.SessionId != _sessionId || _entry is null) { Record("stale-action-rejected"); Publish(); return; }
        switch (intent.Kind)
        {
            case "open":
                _open = true; _error = null; Publish(); return;
            case "cancel":
                _open = false; Publish(); return;
            case "dismiss":
                _error = null; Publish(); return;
            case "confirm":
                if (_busy) return;
                var mode = _data.Modes.SingleOrDefault(value => value.Id == intent.ModeId);
                if (mode is null) { _error = new("unsupported", "modeUnavailable"); Publish(); return; }
                _busy = true; _error = null; Publish();
                var before = await SelectionAsync().ConfigureAwait(false);
                var failure = await ApplyAsync(mode, intent.Description).ConfigureAwait(false);
                if (intent.SessionId != _sessionId) return;
                _busy = false;
                if (failure is null)
                {
                    // The entry names only an extension mode it applied by tag; a native mode is shown by the dialog itself.
                    _selectedId = _lastApplyTagged ? mode.Id : null; _open = false; Record("mode-selected");
                    _tagged = _lastApplyTagged;
                    // Read after the disguise: where the selection is the disabled mode button (cn-406 sub_770E80),
                    // re-enabling it leaves the dialog without one, and that is not the player's choice.
                    var after = _tagged ? await SelectionAsync().ConfigureAwait(false) : before;
                    _taggedOver = TaggedOver(before, after);
                }
                else { _error = failure; Record("apply." + failure.Reason); }
                Publish();
                return;
        }
    }

    /// <summary>
    /// Applies a confirmed mode. A mode the dialog lists is selected with its own button, so the dialog shows it and no tag
    /// is needed; an earlier tag is then removed. Any other mode is written as the <c>#name#</c> tag of the launcher's
    /// language, which the server's mode-name table resolves; the edit's length limit is raised by the tag's length so
    /// the player's title is never cut.
    /// </summary>
    private async Task<Failure?> ApplyAsync(ModeDefinition mode, string? description)
    {
        if (_context.NativeUi is not { } native || _binding is not { } binding) return new("unsupported", "nativeUiUnavailable");
        try
        {
            var read = await RunAsync(native, new(Guid.NewGuid().ToString("N"), "control.inspect", "")
            { Target = new GameNativeUiControlTarget { Form = binding.Form } }).ConfigureAwait(false);
            if (read.Status != "completed") return new(read.Status, read.Reason ?? "dialogUnavailable");
            var edits = read.Controls.Where(control => control.Name == binding.RoomName && control.Kind == "edit").ToArray();
            if (edits.Length != 1 || !edits[0].Writable) return new("unsupported", "roomTextUnavailable");
            var edit = edits[0];
            var listed = read.Controls.Where(control => control.Name == binding.ModeButton && control.SelectionValue == mode.Id).ToArray();
            if (listed.Length > 1) return new("ambiguous", "nativeModeAmbiguous");
            var plain = _data.ParseTitle(edit.Text).Room;
            _lastApplyTagged = listed.Length == 0;
            if (listed.Length == 1)
            {
                var button = listed[0];
                if (button.Enabled != true && button.Selected != true) return new("notReady", "nativeModeDisabled");
                if (plain != edit.Text && await SetTextAsync(native, edit, plain, null).ConfigureAwait(false) is { } cleared) return cleared;
                _disguisedButton = null;
                if (button.Selected == true)
                {
                    // Already the dialog's value: undo the disguise so it looks selected again, with its own description.
                    if (button.Enabled == true && await SetEnabledAsync(native, button, false).ConfigureAwait(false) is { } shown) return shown;
                    await RestoreLabelsAsync(native, read.Controls).ConfigureAwait(false);
                    return null;
                }
                var activated = await RunAsync(native, new(Guid.NewGuid().ToString("N"), "control.activate", "")
                { Target = new GameNativeUiControlTarget { Id = button.Id } }).ConfigureAwait(false);
                return activated.Status == "completed" ? null : new(activated.Status, activated.Reason ?? "nativeModeNotSelected");
            }
            var token = mode.Tokens.GetValueOrDefault(_data.Language) ?? mode.Tokens.GetValueOrDefault("en");
            if (string.IsNullOrEmpty(token)) return new("unsupported", "modeTokenUnavailable");
            var tagged = "#" + token + "#" + plain;
            var limit = checked(Math.Max(edit.MaximumLength ?? 0, plain.Length) + token.Length + 2);
            if (await SetTextAsync(native, edit, tagged, limit).ConfigureAwait(false) is { } titled) return titled;
            await DisguiseAsync(native, binding, read.Controls, description ?? mode.Names.GetValueOrDefault(_data.Language)
                ?? mode.Names.GetValueOrDefault("en") ?? mode.Key).ConfigureAwait(false);
            return null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or JsonException
            or TimeoutException or ArgumentException)
        {
            return new("failed", "applyInterrupted");
        }
    }

    private bool _lastApplyTagged;

    /// <summary>
    /// Makes the dialog look as if the extension mode were selected: its selected mode button is re-enabled (the client
    /// draws a selected mode as a disabled button, kr-812 sub_870ED0) and the visible description label shows the
    /// extension mode. The tag has already been written, so a failure here only leaves the native look.
    /// </summary>
    private async Task DisguiseAsync(IGameNativeUi native, RoomBinding binding, IReadOnlyList<GameNativeUiControlSnapshot> controls, string text)
    {
        try { await DisguiseCoreAsync(native, binding, controls, text).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or JsonException
            or TimeoutException or ArgumentException)
        {
            // The tag is the mode; an interrupted disguise must not report the applied mode as failed.
            Record("disguise.interrupted");
        }
    }

    private async Task DisguiseCoreAsync(IGameNativeUi native, RoomBinding binding, IReadOnlyList<GameNativeUiControlSnapshot> controls, string text)
    {
        if (controls.FirstOrDefault(control => control.Name == binding.ModeButton && control.Selected == true) is { } selected)
        {
            if (selected.Enabled == true) _disguisedButton = selected.Id;
            else if (await SetEnabledAsync(native, selected, true).ConfigureAwait(false) is { } failure) Record("disguise." + failure.Reason);
            else _disguisedButton = selected.Id;
        }
        foreach (var label in controls.Where(control => control.Name == binding.DescriptionLabel && control.Writable))
        {
            var original = _restores.TryGetValue(label.Id, out var earlier) ? earlier.Original : label.Text;
            _restores[label.Id] = (original, text);
            if (await SetTextAsync(native, label, text, null).ConfigureAwait(false) is { } failure) Record("describe." + failure.Reason);
        }
    }

    private async Task<Failure?> SetEnabledAsync(IGameNativeUi native, GameNativeUiControlSnapshot control, bool enabled)
    {
        var result = await RunAsync(native, new(Guid.NewGuid().ToString("N"), "control.setEnabled", "")
        { Target = new GameNativeUiControlTarget { Id = control.Id }, Enabled = enabled }).ConfigureAwait(false);
        return result.Status == "completed" ? null : new(result.Status, result.Reason ?? "enableNotApplied");
    }

    /// <summary>The dialog's current native selection, from the cheap form read.</summary>
    /// <summary>The watched dialog's own selection, or no read at all when the dialog or the provider is unavailable.</summary>
    private async Task<SelectionRead> SelectionAsync()
    {
        if (_context.NativeUi is not { } native || _binding is not { } binding) return default;
        try
        {
            var forms = await native.GetFormsAsync(binding.Form, _stop.Token).ConfigureAwait(false);
            var sequence = _watcher?.Form?.Sequence;
            return forms.FirstOrDefault(form => form.Sequence == sequence) is { } form ? new(true, form.Selection) : default;
        }
        catch (Exception error) when (error is GameNativeUiBusyException or NotSupportedException or IOException or InvalidDataException
            or InvalidOperationException or JsonException or TimeoutException) { return default; }
    }

    /// <summary>
    /// The selection a tag was written over: the one the dialog shows once the tag is applied, or the one before it when
    /// that read failed. A different selection afterwards is a native pick.
    /// </summary>
    internal static int? TaggedOver(SelectionRead before, SelectionRead after) => after.Read ? after.Value : before.Value;

    /// <summary>One read of a dialog's selection; <see cref="Read"/> is false when the read did not reach the dialog.</summary>
    internal readonly record struct SelectionRead(bool Read, int? Value);

    /// <summary>Removes the controller's mode tag from the room title, keeping the rest of the player's text.</summary>
    private async Task<Failure?> ClearTagAsync()
    {
        if (_context.NativeUi is not { } native || _binding is not { } binding) return new("unsupported", "nativeUiUnavailable");
        try
        {
            var read = await RunAsync(native, new(Guid.NewGuid().ToString("N"), "control.inspect", "")
            { Target = new GameNativeUiControlTarget { Form = binding.Form, Name = binding.RoomName } }).ConfigureAwait(false);
            if (read.Status != "completed" || read.Controls.Count != 1) return new(read.Status, read.Reason ?? "roomTextUnavailable");
            var plain = _data.ParseTitle(read.Controls[0].Text).Room;
            return plain == read.Controls[0].Text ? null : await SetTextAsync(native, read.Controls[0], plain, null).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or JsonException
            or TimeoutException or ArgumentException)
        {
            return new("failed", "clearInterrupted");
        }
    }

    private async Task<Failure?> SetTextAsync(IGameNativeUi native, GameNativeUiControlSnapshot edit, string text, int? maximumLength)
    {
        var write = await RunAsync(native, new(Guid.NewGuid().ToString("N"), "control.setText", text)
        { Target = new GameNativeUiControlTarget { Id = edit.Id }, MaximumLength = maximumLength }).ConfigureAwait(false);
        return write.Status == "completed" ? null : new(write.Status, write.Reason ?? "titleNotWritten");
    }

    /// <summary>Submits one native operation and waits for its result within the configured budget, never replaying it.</summary>
    private Task<GameNativeUiResult> RunAsync(IGameNativeUi native, GameNativeUiRequest request) =>
        native.InvokeUntilDoneAsync(request, _wait, _stop.Token);

    private void Publish()
    {
        if (!_pageReady || Volatile.Read(ref _disposed) != 0) return;
        try
        {
            var selected = _selectedId is { } id ? _data.Modes.SingleOrDefault(mode => mode.Id == id) : null;
            _context.Panel.Send("mode-extension.state", new
            {
                // The picker highlights the mode in effect: an applied tag, otherwise the dialog's own selection.
                revision = ++_revision, sessionId = _sessionId, open = _open, busy = _busy,
                selectedId = _tagged ? _selectedId : _nativeSelection,
                entry = _entry is null ? null : new
                {
                    x = _entry.X, y = _entry.Y, width = _entry.Width, height = _entry.Height,
                    referenceWidth = _entry.ReferenceWidth, referenceHeight = _entry.ReferenceHeight,
                },
                selectedName = selected is null ? null : selected.Names.GetValueOrDefault(_data.Language),
                modes = _data.Modes.Select(mode => new { id = mode.Id, key = mode.Key, names = mode.Names }).ToArray(),
                error = _error is null ? null : new { status = _error.Status, reason = _error.Reason, recovery = (string?)null },
            });
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or JsonException)
        { _pageReady = false; Record("presentation-unavailable"); }
    }

    private void Record(string reason)
    {
        var safe = reason.Length <= 150 && reason.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') ? reason : "controller-event";
        _context.Record?.Invoke("mode-controller", safe);
    }

    private void RecordObservation(string state)
    {
        if (_observation == state) return;
        _observation = state;
        Record(state);
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposal ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _context.Panel.MessageReceived -= OnMessage;
        _intents.Writer.TryComplete();
        _stop.Cancel();
        try
        {
            await _lifecycle.DisposeAsync().ConfigureAwait(false);
            await _work.ConfigureAwait(false);
        }
        finally { _stop.Dispose(); }
    }

    private sealed record Intent(string Kind, string? SessionId, int? ModeId, string? Description);
    private sealed class Failure(string status, string reason)
    {
        internal string Status { get; } = status;
        internal string Reason { get; } = reason;
    }
}
