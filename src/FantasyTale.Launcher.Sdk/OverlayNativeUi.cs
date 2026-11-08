using System.Text.Json;

namespace FantasyTale.Launcher.Sdk;

/// <summary>Optional game-module service translating approved UI actions into native control operations.</summary>
public interface IOverlayNativeUiBridge
{
    /// <summary>Invokes a known target operation; unsupported builds return Unsupported without synthesizing input.</summary>
    ValueTask<OverlayNativeUiResult> InvokeAsync(OverlayNativeUiRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A semantic native UI action; it never carries process addresses or arbitrary game commands.</summary>
public sealed record OverlayNativeUiRequest(string TargetId, OverlayNativeUiOperation Operation, JsonElement? Value = null);

/// <summary>Operations a version-specific adapter can explicitly implement and allowlist.</summary>
public enum OverlayNativeUiOperation
{
    /// <summary>Use the native control's own activation path.</summary>
    Press,
    /// <summary>Give an editable native control input focus.</summary>
    Focus,
    /// <summary>Change a known control value through its native setter.</summary>
    SetValue,
    /// <summary>Close a native UI surface through its normal close handler.</summary>
    Close,
}

/// <summary>Result of a native UI operation, distinct from the HTML button being clicked.</summary>
public sealed record OverlayNativeUiResult(OverlayNativeUiStatus Status, string? Reason = null);

/// <summary>Explicit outcomes for an optional native UI adapter.</summary>
public enum OverlayNativeUiStatus
{
    /// <summary>The verified native operation completed.</summary>
    Succeeded,
    /// <summary>No implementation exists for this build or operation.</summary>
    Unsupported,
    /// <summary>The control is not present or ready to accept an operation.</summary>
    NotReady,
    /// <summary>The adapter rejected or failed the operation.</summary>
    Failed,
}

// TODO: game modules provide verified, per-build locator and action adapters.
// HTML actions reach their owning plugin first; the foundation never interprets
// a page-supplied string as an unrestricted native call, input command or packet.
