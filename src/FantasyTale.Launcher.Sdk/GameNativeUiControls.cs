namespace FantasyTale.Launcher.Sdk;

/// <summary>Creates isolated native UI clients for packages sharing one authenticated launch channel.</summary>
public interface IGameNativeUiScopes
{
    /// <summary>Binds result history, discovered control handles and created controls to a trusted owner identity.</summary>
    IGameNativeUi ForOwner(string owner);
}

/// <summary>Selects a live native control by an optional form resource, native name, exact current text or previously returned opaque handle.</summary>
public sealed record GameNativeUiControlTarget
{
    /// <summary>Gets the relative .ui resource identifying a form; omitted forms are searched in the visible native UI.</summary>
    public string? Form { get; init; }

    /// <summary>Gets the control's native Name; an explicit form without a name selects that form's own caption.</summary>
    public string? Name { get; init; }

    /// <summary>Gets an optional native array index to distinguish repeated control names within a form.</summary>
    public int? ArrayIndex { get; init; }

    /// <summary>Gets an optional exact-text search filter; directly setting a known control does not require its old text.</summary>
    public string? Text { get; init; }

    /// <summary>Gets an opaque handle returned to this owner by inspection, never a process address.</summary>
    public string? Id { get; init; }

    /// <summary>Rejects invalid resource names and ambiguous unfiltered mutation requests before contacting the game.</summary>
    public void Validate(bool allowUnfiltered = false)
    {
        if (Form is not null && (Form.Length == 0 || Form.Contains('\0') || Form.Contains('\\')
            || Form.Contains(':') || Form.StartsWith('/') || !Form.EndsWith(".ui", StringComparison.OrdinalIgnoreCase)
            || Form.Split('/').Any(part => part is "" or "." or "..")))
            throw new ArgumentException("A native form must be a relative .ui resource path.");
        if (Name is not null && (Name.Length == 0 || Name.Contains('\0')))
            throw new ArgumentException("A native control name must be nonempty and cannot contain NUL.");
        if (ArrayIndex is not null && (ArrayIndex < 0 || Name is null))
            throw new ArgumentException("A native array index must be nonnegative and qualify a control name.");
        if (Text?.Contains('\0') == true) throw new ArgumentException("A native text filter cannot contain NUL.");
        if (Id is not null) GameNativeUiRequest.ValidateIdentity(Id);
        if (!allowUnfiltered && Form is null && Name is null && Text is null && Id is null)
            throw new ArgumentException("A native control mutation needs a form, control name, text filter or inspected handle.");
    }
}

/// <summary>Describes a caller-owned native button using its parent's logical coordinates and an optional existing button template.</summary>
public sealed record GameNativeUiControlDefinition(string Name, int X, int Y, int Width, int Height)
{
    /// <summary>Gets an optional native button name in the resolved form whose supported appearance is reused.</summary>
    public string? Template { get; init; }

    /// <summary>Checks the name and rectangle; the native adapter additionally checks the actual parent and supported template.</summary>
    public void Validate()
    {
        GameNativeUiRequest.ValidateIdentity(Name);
        if (Template is not null && (Template.Length == 0 || Template.Contains('\0')))
            throw new ArgumentException("A native button template must name an existing control.");
        if (X < 0 || Y < 0 || Width <= 0 || Height <= 0 || X > int.MaxValue - Width || Y > int.MaxValue - Height)
            throw new ArgumentException("A native button needs a positive, non-overflowing rectangle inside its parent.");
    }
}

/// <summary>Captures one visible native control and a caller-scoped handle that can be used while that control remains live.</summary>
public sealed record GameNativeUiControlSnapshot(string Id, string Form, string Name, string Kind, string Text, bool Writable)
{
    /// <summary>Gets whether the native control currently accepts activation, when the adapter can prove it.</summary>
    public bool? Enabled { get; init; }

    /// <summary>Gets the native array index for repeated controls, when present.</summary>
    public int? ArrayIndex { get; init; }

    /// <summary>Gets the descriptor-defined numeric value represented by a proven native selection control.</summary>
    public int? SelectionValue { get; init; }

    /// <summary>Gets whether the native selection group's current value equals this control's value.</summary>
    public bool? Selected { get; init; }

    /// <summary>Gets the live editable-control limit, including overrides applied after loading its .ui resource.</summary>
    public int? MaximumLength { get; init; }

    /// <summary>Gets the control's horizontal position in its native parent's coordinate system when known.</summary>
    public int? X { get; init; }

    /// <summary>Gets the control's vertical position in its native parent's coordinate system when known.</summary>
    public int? Y { get; init; }

    /// <summary>Gets the native control's current logical width when known.</summary>
    public int? Width { get; init; }

    /// <summary>Gets the native control's current logical height when known.</summary>
    public int? Height { get; init; }
}

/// <summary>Reports a native control action after the supplied cursor; polling does not activate the control again.</summary>
public sealed record GameNativeUiControlEvent(long Sequence, string ControlId, string Event);
