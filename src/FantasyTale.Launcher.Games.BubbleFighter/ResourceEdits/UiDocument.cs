using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>One native control in a parsed .ui form, exposed read-only to client plugins.</summary>
/// <param name="Path">Stable path of Name or Name[ArrayIndex] segments from the form root.</param>
/// <param name="Type">Native element type such as UiPanel, UiLabel or UiButton.</param>
/// <param name="Name">Control Name attribute.</param>
/// <param name="ArrayIndex">ArrayIndex attribute when present; same-named controls are told apart by it.</param>
/// <param name="Attributes">Every attribute in document order, values unchanged.</param>
/// <param name="Children">Nested controls in document order.</param>
public sealed record UiControl(string Path, string Type, string Name, int? ArrayIndex,
    IReadOnlyList<KeyValuePair<string, string>> Attributes, IReadOnlyList<UiControl> Children)
{
    /// <summary>Returns an attribute's unchanged text, or null if the attribute is absent.</summary>
    public string? Attribute(string name) => Attributes.Where(a => a.Key == name).Select(a => a.Value).FirstOrDefault();

    /// <summary>Parses a native integer attribute, returning null when absent and rejecting malformed values.</summary>
    public int? IntAttribute(string name) => Attribute(name) is { } value ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture) : null;

    /// <summary>Finds this control or a descendant by its complete canonical path.</summary>
    public UiControl? Find(string path) => Path == path ? this : Children.Select(c => c.Find(path)).FirstOrDefault(c => c is not null);
}

/// <summary>
/// Native .ui form (UTF-16 LE XML with root UiForm in sampled builds). Controls are elements carrying a Name attribute and
/// are addressed by Name[ArrayIndex] path. Only native element types already present in the form may be inserted.
/// </summary>
internal sealed class UiDocument
{
    private readonly XmlMember _xml;
    private readonly HashSet<string> _types;
    private readonly Dictionary<string, XElement> _originalControls = new(StringComparer.Ordinal);

    private UiDocument(XmlMember xml)
    {
        _xml = xml;
        _types = xml.Document.Root!.DescendantsAndSelf().Where(e => e.Attribute("Name") is not null).Select(e => e.Name.LocalName).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Parses a .ui member strictly.</summary>
    internal static UiDocument Load(byte[] bytes, string member)
    {
        var xml = XmlMember.Load(bytes, member);
        if (xml.Document.Root?.Name != "UiForm") throw new InvalidDataException(member + ": not a native UI form");
        return new(xml);
    }

    /// <summary>Returns the read-only control tree for plugins that need native layout parameters.</summary>
    internal IReadOnlyList<UiControl> Controls() => Children(_xml.Document.Root!).Select(e => Describe(e, "")).ToArray();

    /// <summary>Every control path in document order, for pattern edits that select controls by regex.</summary>
    internal IReadOnlyList<string> Paths()
    {
        static IEnumerable<string> Walk(IEnumerable<UiControl> controls) => controls.SelectMany(c => Walk(c.Children).Prepend(c.Path));
        return Walk(Controls()).Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Encodes the edited form with its original BOM and declaration.</summary>
    internal byte[] Save() => _xml.Save();

    /// <summary>Reads one attribute of a control for conflict reports, or null when absent.</summary>
    internal string? Read(string path, string attribute) => (string?)Find(path)?.Attribute(attribute);

    /// <summary>Normalizes numeric path segments and resolves existing paths to their original control identity.</summary>
    internal string CanonicalPath(string path)
    {
        var canonical = string.Join('/', path.Split('/').Select(part =>
        {
            var (name, index, occurrence) = Segment(part);
            return name + (index is null ? "" : "[" + index.Value.ToString(CultureInfo.InvariantCulture) + "]")
                + (occurrence is null ? "" : "@" + occurrence.Value.ToString(CultureInfo.InvariantCulture));
        }));
        _ = Find(canonical);
        return canonical;
    }

    /// <summary>Checks all original-value conditions and pins existing controls before insertions can renumber siblings.</summary>
    internal void Prepare(IEnumerable<UiEdit> edits)
    {
        foreach (var edit in edits)
        {
            var control = Find(edit.Path);
            if (edit.Expect.Count > 0)
            {
                if (control is null) throw new InvalidDataException(_xml.Member + ": control not found " + edit.Path);
                Check(control, edit);
            }
            if (control is not null) _originalControls[edit.Path] = control;
        }
    }

    /// <summary>Control paths owned by a newly added subtree, for conflicts with another plugin's descendant edits.</summary>
    internal static IReadOnlyList<string> AddedPaths(UiEdit edit)
    {
        if (edit.Element is null) throw new InvalidDataException(edit.Path + ": add requires an element");
        try
        {
            var root = XElement.Parse(edit.Element);
            var split = edit.Path.LastIndexOf('/');
            var control = Describe(root, split < 0 ? "" : edit.Path[..split]);
            static IEnumerable<string> Walk(UiControl node) => node.Children.SelectMany(Walk).Prepend(node.Path);
            return Walk(control).ToArray();
        }
        catch (XmlException error) { throw new InvalidDataException(edit.Path + ": malformed control element", error); }
    }

    /// <summary>
    /// Removes several controls. Every target is resolved before any is removed, so removing one of several same-named
    /// siblings cannot renumber the Name@k paths of the others.
    /// </summary>
    internal void RemoveAll(IEnumerable<UiEdit> edits)
    {
        var resolved = edits.Select(edit =>
        {
            var control = Find(edit.Path) ?? throw new InvalidDataException(_xml.Member + ": control not found " + edit.Path);
            Check(control, edit);
            return control;
        }).ToArray();
        // A control may already be gone with a removed ancestor.
        foreach (var control in resolved) if (control.Parent is not null) control.Remove();
    }

    /// <summary>Applies one control edit; hiding is rejected until a native visibility field is proven.</summary>
    internal void Apply(UiEdit edit)
    {
        var member = _xml.Member;
        switch (edit.Action)
        {
            case EditAction.Patch:
            {
                var control = Find(edit.Path) ?? throw new InvalidDataException(member + ": control not found " + edit.Path);
                Check(control, edit);
                foreach (var (name, value) in edit.Set)
                {
                    if (name is "Name" or "ArrayIndex") throw new InvalidDataException(member + ": a patch cannot change control identity");
                    control.SetAttributeValue(name, value);
                }
                break;
            }
            case EditAction.Replace:
            {
                var control = Find(edit.Path) ?? throw new InvalidDataException(member + ": control not found " + edit.Path);
                Check(control, edit);
                var replacement = Fragment(edit);
                if ((string?)replacement.Attribute("Name") != (string?)control.Attribute("Name")
                    || (string?)replacement.Attribute("ArrayIndex") != (string?)control.Attribute("ArrayIndex"))
                    throw new InvalidDataException(member + ": a replacement must keep Name and ArrayIndex");
                control.ReplaceWith(replacement);
                break;
            }
            case EditAction.Add:
            {
                var split = edit.Path.LastIndexOf('/');
                var parentPath = split < 0 ? "" : edit.Path[..split];
                var parent = parentPath.Length == 0 ? _xml.Document.Root! : Find(parentPath) ?? throw new InvalidDataException(member + ": parent not found " + parentPath);
                if (Find(edit.Path) is not null) throw new InvalidDataException(member + ": control already exists " + edit.Path);
                var element = Fragment(edit);
                var (name, index, occurrence) = Segment(edit.Path[(split + 1)..]);
                if (occurrence is not null) throw new InvalidDataException(member + ": a new control needs a unique path, not " + edit.Path);
                if ((string?)element.Attribute("Name") != name || Int(element) != index)
                    throw new InvalidDataException(member + ": a new control's Name and ArrayIndex must match its path");
                var siblings = Children(parent).ToArray();
                var position = edit.Position ?? siblings.Length;
                if (position < 0 || position > siblings.Length) throw new InvalidDataException(member + ": insert position out of range");
                if (position < siblings.Length) siblings[position].AddBeforeSelf(element);
                else if (siblings.Length > 0) siblings[^1].AddAfterSelf(element);
                else parent.Add(element);
                break;
            }
            case EditAction.Remove:
            {
                var control = Find(edit.Path) ?? throw new InvalidDataException(member + ": control not found " + edit.Path);
                Check(control, edit);
                control.Remove();
                break;
            }
            case EditAction.Hide:
                throw new InvalidDataException("resource.ui.hide-unsupported: no native visibility field is proven for " + member + " " + edit.Path);
            default:
                throw new InvalidDataException(member + ": unsupported UI action " + edit.Action);
        }
    }

    private void Check(XElement control, UiEdit edit)
    {
        foreach (var (name, expected) in edit.Expect)
            if (((string?)control.Attribute(name) ?? "") != expected)
                throw new InvalidDataException(_xml.Member + ": precondition failed for " + edit.Path + " " + name);
    }

    private XElement Fragment(UiEdit edit)
    {
        if (edit.Element is null) throw new InvalidDataException(_xml.Member + ": " + edit.Action + " requires an element");
        XElement element;
        try { element = XElement.Parse(edit.Element, LoadOptions.PreserveWhitespace); }
        catch (XmlException error) { throw new InvalidDataException(_xml.Member + ": malformed control element", error); }
        foreach (var node in element.DescendantsAndSelf())
            if (node.Name.Namespace != XNamespace.None || (node.Attribute("Name") is not null && !_types.Contains(node.Name.LocalName)))
                throw new InvalidDataException(_xml.Member + ": control type is not native to this form: " + node.Name);
        foreach (var (name, value) in edit.Set)
        {
            if (name is "Name" or "ArrayIndex") throw new InvalidDataException(_xml.Member + ": attributes cannot change control identity");
            element.SetAttributeValue(name, value);
        }
        return element;
    }

    private XElement? Find(string path)
    {
        if (path.Length == 0) throw new InvalidDataException(_xml.Member + ": empty control path");
        // Descendants of a replaced subtree still have a Parent but no longer belong to the output.
        if (_originalControls.TryGetValue(path, out var original)) return original.Document == _xml.Document ? original : null;
        var current = _xml.Document.Root!;
        foreach (var part in path.Split('/'))
        {
            var (name, index, occurrence) = Segment(part);
            var matches = Children(current).Where(e => (string?)e.Attribute("Name") == name && Int(e) == index).ToArray();
            if (name.Length == 0 && occurrence is null) throw new InvalidDataException(_xml.Member + ": an unnamed control is addressed as @0, @1 and so on: " + path);
            if (occurrence is { } k)
            {
                if (matches.Length < 2 && name.Length > 0) throw new InvalidDataException(_xml.Member + ": " + part + " names a control that has no same-named sibling; drop @" + k);
                if (k >= matches.Length) return null;
                current = matches[k];
                continue;
            }
            if (matches.Length == 0) return null;
            if (matches.Length > 1) throw new InvalidDataException(_xml.Member + ": " + matches.Length + " controls share the path " + path + "; address one as " + part + "@0 to " + part + "@" + (matches.Length - 1));
            current = matches[0];
        }
        return current;
    }

    /// <summary>Controls whose nearest named ancestor is <paramref name="parent"/>, skipping unnamed container elements.</summary>
    private static IEnumerable<XElement> Children(XElement parent)
    {
        foreach (var child in parent.Elements())
        {
            if (child.Attribute("Name") is not null) yield return child;
            else foreach (var nested in Children(child)) yield return nested;
        }
    }

    private static UiControl Describe(XElement element, string parent)
    {
        var name = (string)element.Attribute("Name")!;
        var index = Int(element);
        // Same-named siblings that ArrayIndex does not tell apart get a 0-based occurrence in document order.
        var twins = element.Parent is null ? [element] : Children(NamedParent(element)).Where(e => (string?)e.Attribute("Name") == name && Int(e) == index).ToArray();
        // A control whose Name is empty is always addressed by occurrence, so its path segment is never empty.
        var occurrence = twins.Length > 1 || name.Length == 0 ? "@" + Array.IndexOf(twins, element).ToString(CultureInfo.InvariantCulture) : "";
        var path = (parent.Length == 0 ? "" : parent + "/") + name + (index is null ? "" : "[" + index.Value.ToString(CultureInfo.InvariantCulture) + "]") + occurrence;
        return new(path, element.Name.LocalName, name, index,
            element.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => KeyValuePair.Create(a.Name.LocalName, a.Value)).ToArray(),
            Children(element).Select(e => Describe(e, path)).ToArray());
    }

    /// <summary>Parses Name, Name[ArrayIndex], Name@occurrence or Name[ArrayIndex]@occurrence.</summary>
    private static (string Name, int? Index, int? Occurrence) Segment(string part)
    {
        int? occurrence = null;
        var at = part.LastIndexOf('@');
        if (at >= 0)
        {
            if (!int.TryParse(part.AsSpan(at + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var k)) throw new InvalidDataException("Invalid control path segment: " + part);
            occurrence = k;
            part = part[..at];
        }
        var open = part.IndexOf('[');
        if (open < 0) return (part, null, occurrence);
        if (!part.EndsWith(']') || !int.TryParse(part.AsSpan(open + 1, part.Length - open - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            throw new InvalidDataException("Invalid control path segment: " + part);
        return (part[..open], index, occurrence);
    }

    /// <summary>The nearest ancestor that is a control or the form root, whose Children include this element.</summary>
    private static XElement NamedParent(XElement element)
    {
        var parent = element.Parent!;
        while (parent.Parent is not null && parent.Attribute("Name") is null) parent = parent.Parent;
        return parent;
    }

    private static int? Int(XElement element) =>
        (string?)element.Attribute("ArrayIndex") is { } text ? int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture) : null;
}

/// <summary>One resolved control edit.</summary>
/// <param name="Action">Edit action.</param>
/// <param name="Path">Control path; for add, the path the new control will have.</param>
/// <param name="Set">Attributes written by a patch.</param>
/// <param name="Expect">Attribute preconditions checked before patch, replace or remove.</param>
/// <param name="Element">Native element XML for add and replace.</param>
/// <param name="Position">Zero-based position among the parent's controls for add; null appends.</param>
internal sealed record UiEdit(EditAction Action, string Path, IReadOnlyDictionary<string, string> Set,
    IReadOnlyDictionary<string, string> Expect, string? Element, int? Position);
