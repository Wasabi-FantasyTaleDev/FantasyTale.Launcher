using System.Xml;
using System.Xml.Linq;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>
/// Writes a parsed member back with the original's prefixes and layout line breaks. LINQ to XML forgets which prefix an
/// element used and picks the last declared one, so unprefixed SpreadsheetML elements would come back as ss:Row; it also
/// loses CRLF, which parsing turns into LF. Each element is therefore unprefixed exactly when its namespace is the default
/// namespace in scope at that element (rich-text cells redeclare it), and whitespace between elements uses the original
/// line break. Text and attribute values still go through the writer's escaping, so data keeps its exact characters.
/// </summary>
internal static class XmlLayoutWriter
{
    internal static void Write(XmlWriter writer, XNode node, string newline)
    {
        switch (node)
        {
            case XElement element:
                var ns = element.Name.NamespaceName;
                writer.WriteStartElement(ns.Length == 0 || Default(element) == ns ? "" : Prefix(element, ns), element.Name.LocalName, ns);
                foreach (var attribute in element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration)
                    {
                        if (attribute.Name.Namespace == XNamespace.None) writer.WriteAttributeString("", "xmlns", XNamespace.Xmlns.NamespaceName, attribute.Value);
                        else writer.WriteAttributeString("xmlns", attribute.Name.LocalName, XNamespace.Xmlns.NamespaceName, attribute.Value);
                        continue;
                    }
                    var attributeNs = attribute.Name.NamespaceName;
                    writer.WriteAttributeString(attributeNs.Length == 0 ? "" : Prefix(element, attributeNs), attribute.Name.LocalName, attributeNs, attribute.Value);
                }
                if (element.IsEmpty) { writer.WriteEndElement(); return; }
                foreach (var child in element.Nodes()) Write(writer, child, newline);
                writer.WriteFullEndElement();
                return;
            case XText text when text is not XCData && text.Value.Length > 0 && text.Value.All(c => c is ' ' or '\t' or '\n'):
                writer.WriteRaw(text.Value.Replace("\n", newline, StringComparison.Ordinal));
                return;
            default:
                node.WriteTo(writer);
                return;
        }
    }

    private static string Default(XElement element) =>
        element.AncestorsAndSelf().Select(e => e.Attribute("xmlns")).FirstOrDefault(a => a is not null)?.Value ?? "";

    /// <summary>The nearest declared, unshadowed prefix of a namespace; null lets the writer declare one.</summary>
    private static string? Prefix(XElement element, string ns)
    {
        if (ns == XNamespace.Xml.NamespaceName) return "xml";
        var shadowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in element.AncestorsAndSelf().SelectMany(e => e.Attributes()).Where(a => a.Name.Namespace == XNamespace.Xmlns))
            if (shadowed.Add(declaration.Name.LocalName) && declaration.Value == ns) return declaration.Name.LocalName;
        return null;
    }
}
