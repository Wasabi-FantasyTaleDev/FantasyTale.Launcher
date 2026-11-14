namespace FantasyTale.Launcher.Sdk;

/// <summary>Invokes native controls in one game session without choosing an Overlay fallback.</summary>
public interface IGameNativeUi
{
    /// <summary>Reports independently verified operations and their availability in the current game stage.</summary>
    /// <exception cref="GameNativeUiBusyException">The capability query was not submitted because the channel was occupied.</exception>
    ValueTask<GameNativeUiCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>Submits one operation with a caller-generated identity; acceptance does not imply presentation.</summary>
    /// <remarks>A busy result with reason channelBusy means this invocation was not submitted and may be attempted again.</remarks>
    ValueTask<GameNativeUiResult> InvokeAsync(GameNativeUiRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads an operation's latest outcome without repeating its presentation side effects.</summary>
    /// <exception cref="GameNativeUiBusyException">The query was not submitted; the earlier operation's outcome remains unchanged.</exception>
    ValueTask<GameNativeUiResult> GetResultAsync(string operationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the live forms the game loaded from one <c>.ui</c> resource, with their geometry, from the native load and
    /// destroy observers. The read never queues behind game-thread UI work; an empty list means no such form is open.
    /// </summary>
    /// <exception cref="NotSupportedException">This provider or client build does not track native forms.</exception>
    /// <exception cref="GameNativeUiBusyException">The read was not submitted because the channel was occupied.</exception>
    ValueTask<IReadOnlyList<GameNativeUiForm>> GetFormsAsync(string resource, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This native UI provider does not track forms.");
}

/// <summary>One live native form loaded from a <c>.ui</c> resource.</summary>
/// <param name="Resource">The resource the game loaded the form from.</param>
/// <param name="Sequence">A load counter that changes every time the game loads a form, so a reopened dialog is a new form.</param>
/// <param name="Visible">Whether the form's own visibility flag is set.</param>
/// <param name="X">Left edge in game UI pixels.</param>
/// <param name="Y">Top edge in game UI pixels.</param>
/// <param name="Width">Width in game UI pixels.</param>
/// <param name="Height">Height in game UI pixels.</param>
/// <param name="RootWidth">The game UI root's width, the viewport these pixels belong to, or zero when unknown.</param>
/// <param name="RootHeight">The game UI root's height, or zero when unknown.</param>
/// <param name="Selection">The form's selection-group value, such as the Create Room dialog's chosen mode, when the
/// provider knows the group; it equals the <see cref="GameNativeUiControlSnapshot.SelectionValue"/> of the selected button.</param>
/// <param name="Clicks">Native button clicks so far in the whole client; a change means the player pressed a button.</param>
public sealed record GameNativeUiForm(string Resource, long Sequence, bool Visible, int X, int Y, int Width, int Height,
    int RootWidth, int RootHeight, int? Selection = null, long Clicks = 0)
{
    /// <summary>
    /// The overlay anchor for a rectangle given relative to this form's top left, in game client pixels. The client draws
    /// its forms one to one in client pixels, so the anchor states no reference viewport and follows the form when it moves.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The width or height is not positive.</exception>
    public OverlayAnchor Region(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        return new(X + x, Y + y, width, height);
    }
}

/// <summary>Describes a native operation; colors are RGB or RGBA hex and banner identities belong to the caller.</summary>
public sealed record GameNativeUiRequest(string OperationId, string Kind, string Text)
{
    /// <summary>Gets the message-box title, independent of its body.</summary>
    public string Title { get; init; } = "";

    /// <summary>Gets the requested native message-box icon: none, notice, warning, question or error.</summary>
    public string Icon { get; init; } = "notice";

    /// <summary>Gets message-box buttons whose stable IDs are returned on selection.</summary>
    public IReadOnlyList<GameNativeUiButton> Buttons { get; init; } = [];

    /// <summary>Gets an optional caller-owned identity for adapters supporting banner replacement or closure.</summary>
    public string? AnnouncementId { get; init; }

    /// <summary>Gets the local chat entry style: normal or system; no network chat is sent.</summary>
    public string ChatStyle { get; init; } = "system";

    /// <summary>Gets an optional local chat caption; omitted captions use Fantasy Tale for system entries and no caption for normal entries.</summary>
    public string? ChatCaption { get; init; }

    /// <summary>Gets ordered colored text runs for a single local chat entry.</summary>
    public IReadOnlyList<GameNativeUiTextRun> Runs { get; init; } = [];

    /// <summary>Gets a native control selector; an omitted form enables automatic visible-control lookup.</summary>
    public GameNativeUiControlTarget? Target { get; init; }

    /// <summary>Gets the native button definition, accepted only by control.addButton.</summary>
    public GameNativeUiControlDefinition? Definition { get; init; }

    /// <summary>Gets the last observed native-control event sequence; only control.events accepts a cursor.</summary>
    public long? AfterSequence { get; init; }

    /// <summary>Gets an explicit editable-control character limit to apply through its native setter before setting text; omitted limits remain unchanged.</summary>
    public int? MaximumLength { get; init; }

    /// <summary>
    /// Gets the enabled state control.setEnabled applies through the game's own setter. It changes how the control looks and
    /// whether it accepts input; a selection group's value is unchanged, so on a selection button it is cosmetic.
    /// </summary>
    public bool? Enabled { get; init; }

    /// <summary>Rejects ambiguous identities, malformed colors and fields that do not belong to the selected operation.</summary>
    public void Validate()
    {
        ValidateIdentity(OperationId);
        ArgumentNullException.ThrowIfNull(Text);
        ArgumentNullException.ThrowIfNull(Title);
        ArgumentNullException.ThrowIfNull(Buttons);
        ArgumentNullException.ThrowIfNull(Runs);
        if (IsControlOperation(Kind))
        {
            if (Title.Length != 0 || Buttons.Count != 0 || Runs.Count != 0 || AnnouncementId is not null
                || ChatCaption is not null || Icon != "notice" || ChatStyle != "system")
                throw new ArgumentException("Native control operations do not accept presentation parameters.");
            if (Text.Contains('\0')) throw new ArgumentException("Native control text cannot contain NUL.");
            if (Kind is not ("control.setText" or "control.addButton") && Text.Length != 0)
                throw new ArgumentException("Only setting text or adding a button accepts native control text.");
            if (Kind == "control.events" && Target is not null) throw new ArgumentException("Control event polling is owner-scoped and does not accept a target.");
            if (Kind is "control.inspect" or "control.events") Target?.Validate(allowUnfiltered: true);
            else (Target ?? throw new ArgumentException("A native control target is required.")).Validate();
            if (Kind == "control.addButton") (Definition ?? throw new ArgumentException("A native button definition is required.")).Validate();
            else if (Definition is not null) throw new ArgumentException("Only control.addButton accepts a definition.");
            if (AfterSequence is not null && (Kind != "control.events" || AfterSequence < 0))
                throw new ArgumentException("Only control.events accepts a nonnegative cursor.");
            if (MaximumLength is not null && (Kind != "control.setText" || MaximumLength <= 0))
                throw new ArgumentException("Only control.setText accepts a positive editable-control length limit.");
            if ((Enabled is not null) != (Kind == "control.setEnabled"))
                throw new ArgumentException("control.setEnabled, and only it, needs an enabled state.");
            return;
        }
        if (Target is not null || Definition is not null || AfterSequence is not null || MaximumLength is not null || Enabled is not null)
            throw new ArgumentException("Only native control operations accept a target, definition or event cursor.");
        if (Kind is not ("messageBox" or "notification" or "announcement.show" or "announcement.close" or "chat"))
            throw new ArgumentException("Unknown native UI operation.", nameof(Kind));
        if (Text.Contains('\0') || Title.Contains('\0')) throw new ArgumentException("Native UI text cannot contain NUL.");
        if (Kind == "messageBox")
        {
            if (Icon is not ("none" or "notice" or "warning" or "question" or "error")) throw new ArgumentException("Unknown native UI icon.");
            if (Buttons.Count == 0) throw new ArgumentException("A message box needs at least one button.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var button in Buttons)
            {
                ArgumentNullException.ThrowIfNull(button);
                ValidateIdentity(button.Id);
                if (!ids.Add(button.Id) || button.Label is null || button.Label.Contains('\0'))
                    throw new ArgumentException("Native UI button IDs must be unique and labels must be non-null without NUL.");
            }
        }
        else if (Buttons.Count != 0 || Title.Length != 0) throw new ArgumentException("Only a message box accepts a title or buttons.");
        if (Kind.StartsWith("announcement.", StringComparison.Ordinal))
        {
            if (Kind == "announcement.close" || AnnouncementId is not null) ValidateIdentity(AnnouncementId);
        }
        else if (AnnouncementId is not null) throw new ArgumentException("Only an announcement accepts an announcement identity.");
        if (Kind == "announcement.close" && Text.Length != 0) throw new ArgumentException("Closing an announcement does not accept text.");
        if (Kind == "chat")
        {
            if (ChatStyle is not ("normal" or "system")) throw new ArgumentException("Unknown native chat style.");
            if (Runs.Count == 0 || Text.Length != 0) throw new ArgumentException("Chat entries use colored text runs instead of a separate body.");
            foreach (var run in Runs)
            {
                ArgumentNullException.ThrowIfNull(run);
                if (string.IsNullOrEmpty(run.Text) || run.Text.Contains('\0') || run.Color is null || run.Color.Length is not (7 or 9)
                    || run.Color[0] != '#' || !run.Color.AsSpan(1).ContainsOnlyHex())
                    throw new ArgumentException("Chat runs need nonempty text and an #RRGGBB or #RRGGBBAA color.");
            }
        }
        else if (Runs.Count != 0) throw new ArgumentException("Only chat accepts colored text runs.");
        if (ChatCaption is not null && (Kind != "chat" || ChatCaption.Contains('\0')))
            throw new ArgumentException("Only chat accepts a caption, which cannot contain NUL.");
    }

    /// <summary>Validates an opaque operation or announcement identity without accepting client addresses or paths.</summary>
    public static void ValidateIdentity(string? identity)
    {
        if (string.IsNullOrEmpty(identity) || identity.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-')))
            throw new ArgumentException("Native UI identities use ASCII letters, digits, dots, underscores or hyphens.");
    }

    /// <summary>Identifies the shared live-control operations without depending on a game or client build.</summary>
    public static bool IsControlOperation(string? kind) => kind is "control.inspect" or "control.setText"
        or "control.addButton" or "control.remove" or "control.events" or "control.activate" or "control.setEnabled";
}

/// <summary>Pairs a stable caller-owned button identity with its native label, which may be empty or whitespace for textured buttons.</summary>
public sealed record GameNativeUiButton(string Id, string Label);

/// <summary>Represents one text run with an #RRGGBB foreground color or #RRGGBBAA when an explicit alpha is needed.</summary>
public sealed record GameNativeUiTextRun(string Text, string Color);

/// <summary>Reports the native provider's per-operation capabilities for a specific client build.</summary>
public sealed record GameNativeUiCapabilities(string Build, IReadOnlyList<GameNativeUiCapability> Operations);

/// <summary>Reports support, current readiness and the parameters verified for one native operation.</summary>
public sealed record GameNativeUiCapability(string Kind, bool Supported, bool Ready, IReadOnlyList<string> Features, string? Reason = null);

/// <summary>Correlates presentation outcomes and completed or ambiguous native-control operations without implying success from queue admission.</summary>
public sealed record GameNativeUiResult(string OperationId, string Status, string? ButtonId = null, string? Reason = null)
{
    /// <summary>Gets inspected or changed controls, including candidates when a selector is ambiguous.</summary>
    public IReadOnlyList<GameNativeUiControlSnapshot> Controls { get; init; } = [];

    /// <summary>Gets retained native control actions strictly newer than the requested cursor.</summary>
    public IReadOnlyList<GameNativeUiControlEvent> Events { get; init; } = [];

    /// <summary>Gets the newest observed native event sequence, including when no new events were returned.</summary>
    public long? Cursor { get; init; }
}

internal static class NativeUiColor
{
    internal static bool ContainsOnlyHex(this ReadOnlySpan<char> value)
    {
        foreach (var c in value) if (!char.IsAsciiHexDigit(c)) return false;
        return true;
    }
}
