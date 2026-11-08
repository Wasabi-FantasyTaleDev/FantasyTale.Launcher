namespace FantasyTale.Launcher.Sdk;

/// <summary>Invokes native controls in one game session without choosing an Overlay fallback.</summary>
public interface IGameNativeUi
{
    /// <summary>Reports independently verified operations and their availability in the current game stage.</summary>
    ValueTask<GameNativeUiCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>Submits one operation with a caller-generated identity; acceptance does not imply presentation.</summary>
    ValueTask<GameNativeUiResult> InvokeAsync(GameNativeUiRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads an operation's latest outcome without repeating its presentation side effects.</summary>
    ValueTask<GameNativeUiResult> GetResultAsync(string operationId, CancellationToken cancellationToken = default);
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

    /// <summary>Rejects ambiguous identities, malformed colors and fields that do not belong to the selected operation.</summary>
    public void Validate()
    {
        ValidateIdentity(OperationId);
        ArgumentNullException.ThrowIfNull(Text);
        ArgumentNullException.ThrowIfNull(Title);
        ArgumentNullException.ThrowIfNull(Buttons);
        ArgumentNullException.ThrowIfNull(Runs);
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
}

/// <summary>Pairs a stable caller-owned button identity with its native label, which may be empty or whitespace for textured buttons.</summary>
public sealed record GameNativeUiButton(string Id, string Label);

/// <summary>Represents one text run with an #RRGGBB foreground color or #RRGGBBAA when an explicit alpha is needed.</summary>
public sealed record GameNativeUiTextRun(string Text, string Color);

/// <summary>Reports the native provider's per-operation capabilities for a specific client build.</summary>
public sealed record GameNativeUiCapabilities(string Build, IReadOnlyList<GameNativeUiCapability> Operations);

/// <summary>Reports support, current readiness and the parameters verified for one native operation.</summary>
public sealed record GameNativeUiCapability(string Kind, bool Supported, bool Ready, IReadOnlyList<string> Features, string? Reason = null);

/// <summary>Correlates queued, shown, selected, dismissed, closed, unsupported, notReady, busy, failed or outcomeUnknown results.</summary>
public sealed record GameNativeUiResult(string OperationId, string Status, string? ButtonId = null, string? Reason = null);

internal static class NativeUiColor
{
    internal static bool ContainsOnlyHex(this ReadOnlySpan<char> value)
    {
        foreach (var c in value) if (!char.IsAsciiHexDigit(c)) return false;
        return true;
    }
}
