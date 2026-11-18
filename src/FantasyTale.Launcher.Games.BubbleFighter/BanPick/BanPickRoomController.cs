using System.Text.Json;
using FantasyTale.Launcher.Games.BubbleFighter.ModeExtension;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter.BanPick;

/// <summary>
/// Tells the Ban Rules page where the client's own room window and its select-map button are, so the page places its
/// button from the open form's native rectangle instead of from screen coordinates. Each build's room forms and the
/// button rectangles inside them are data (<c>room-layouts.json</c>); only the form's position comes from the client.
/// The four-state select-map sheet is read from the running client's own files and handed to the page, never stored.
/// </summary>
internal sealed class BanPickRoomController : IAsyncDisposable
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);
    private readonly GameOverlayControllerContext _context;
    private readonly BanPickRoomData _data;
    private readonly (BanPickLayout Layout, GameNativeUiFormWatcher Watcher)[] _watchers;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _work;
    private ClientSprite? _sprite;
    private bool _spriteRead, _pageReady;
    private long _revision;
    private string _last = "";
    private string _status = "starting";
    private OverlayAnchor? _host, _member;
    private string? _style;
    private int _disposed;

    internal BanPickRoomController(GameOverlayControllerContext context, BanPickRoomData data)
    {
        _context = context;
        _data = data;
        _watchers = context.NativeUi is { } native
            ? data.Layouts.Select(layout => (layout, new GameNativeUiFormWatcher(native, layout.Form))).ToArray() : [];
        context.Panel.MessageReceived += OnMessage;
        _work = Task.Run(RunAsync);
    }

    private void OnMessage(object? sender, OverlayMessageEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0 || args.PanelId != _context.Panel.PanelId || args.EventName != "ban-pick.ready") return;
        _pageReady = true;
        _last = "";
        Publish();
    }

    private async Task RunAsync()
    {
        try
        {
            _ = Task.Run(ReadSprite, _stop.Token);
            while (!_stop.IsCancellationRequested)
            {
                await ObserveAsync().ConfigureAwait(false);
                await Task.Delay(Poll, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private void ReadSprite()
    {
        try
        {
            if (_context.ClientFolder is { } folder && Directory.Exists(folder))
                _sprite = ClientSpriteReader.TryRead(folder, _data.Sprite, _stop.Token);
            Record(_sprite is null ? "sprite-missing." + _data.BuildId : "sprite-read");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        { Record("sprite-failed"); }
        _spriteRead = true;
        _last = "";
        Publish();
    }

    private async Task ObserveAsync()
    {
        if (_watchers.Length == 0) { Set("unsupported", null, null, null); return; }
        var observations = new GameNativeUiFormObservation[_watchers.Length];
        for (var index = 0; index < _watchers.Length; index++)
            observations[index] = await _watchers[index].Watcher.ObserveAsync(true, _stop.Token).ConfigureAwait(false);
        if (OverlayRoomModeController.Follow(observations) is { } followed)
        {
            var layout = _watchers[followed].Layout;
            var form = observations[followed].Form!;
            Set("open", Place(form, layout.Host), Place(form, layout.Member), layout.Style);
            return;
        }
        var statuses = observations.Select(observation => observation.Status).ToArray();
        if (statuses.Contains(GameNativeUiFormWatchStatus.Busy) || statuses.Contains(GameNativeUiFormWatchStatus.ReadFailed)) return;
        Set(statuses.All(status => status == GameNativeUiFormWatchStatus.Unsupported) ? "unsupported" : "closed", null, null, null);
    }

    /// <summary>A form-relative rectangle in game client pixels; forms are drawn one to one, so it follows the form.</summary>
    internal static OverlayAnchor Place(GameNativeUiForm form, RoomEntry rect) => form.Region(rect.X, rect.Y, rect.Width, rect.Height);

    private void Set(string status, OverlayAnchor? host, OverlayAnchor? member, string? style)
    {
        if (status != _status) Record("room-" + status);
        (_status, _host, _member, _style) = (status, host, member, style);
        Publish();
    }

    private void Publish()
    {
        if (!_pageReady || Volatile.Read(ref _disposed) != 0) return;
        var sprite = _sprite;
        var snapshot = JsonSerializer.Serialize(new { _status, _host, _member, _style, _spriteRead, sprite = sprite?.Png.Length });
        if (snapshot == _last) return;
        _last = snapshot;
        try
        {
            _context.Panel.Send("ban-pick.layout", new
            {
                revision = ++_revision, status = _status, style = _style,
                host = Rect(_host), member = Rect(_member), spriteRead = _spriteRead,
                sprite = sprite is null ? null : new { png = Convert.ToBase64String(sprite.Png), frames = sprite.Frames },
            });
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or JsonException)
        { _pageReady = false; Record("presentation-unavailable"); }
    }

    private static object? Rect(OverlayAnchor? anchor) => anchor is null ? null : new { x = anchor.X, y = anchor.Y, width = anchor.Width, height = anchor.Height };

    private void Record(string reason) => _context.Record?.Invoke("ban-pick-controller", reason);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _context.Panel.MessageReceived -= OnMessage;
        _stop.Cancel();
        try { await _work.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}
