using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>Loads and saves XML members while keeping the original BOM, encoding and declaration text byte-for-byte.</summary>
internal sealed class XmlMember
{
    private XmlMember(TextCodec codec, string prefix, XDocument document, string member, string newline)
    {
        Codec = codec; Prefix = prefix; Document = document; Member = member; Newline = newline;
    }

    internal TextCodec Codec { get; }
    internal string Prefix { get; }
    internal XDocument Document { get; }
    internal string Member { get; }

    /// <summary>Line break the original member uses between elements, restored on save.</summary>
    internal string Newline { get; }

    /// <summary>Parses strictly, preserving whitespace so untouched formatting survives a round trip.</summary>
    internal static XmlMember Load(byte[] bytes, string member)
    {
        var codec = TextCodec.Detect(bytes);
        var text = codec.Decode(bytes, member);
        var prefix = "";
        if (text.StartsWith("<?xml", StringComparison.Ordinal))
        {
            var end = text.IndexOf("?>", StringComparison.Ordinal) + 2;
            while (end < text.Length && text[end] is ' ' or '\t' or '\r' or '\n') end++;
            prefix = text[..end];
        }
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\r') ? "\r" : "\n";
        try
        {
            var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            if (document.Declaration?.Encoding is { Length: > 0 } encoding)
            {
                var declared = encoding.ToLowerInvariant();
                var matches = declared == codec.Name || declared == "utf-16" && codec.Name.StartsWith("utf-16", StringComparison.Ordinal)
                    || declared == "utf-32" && codec.Name.StartsWith("utf-32", StringComparison.Ordinal);
                if (!matches) throw new InvalidDataException(member + ": XML encoding declaration disagrees with the resource bytes: " + encoding);
            }
            return new(codec, prefix, document, member, newline);
        }
        catch (XmlException error) { throw new InvalidDataException(member + ": malformed XML", error); }
    }

    /// <summary>
    /// Serializes with the original declaration, encoding and layout line breaks; line breaks inside values are entitized
    /// where needed so they read back unchanged.
    /// </summary>
    internal byte[] Save()
    {
        var builder = new StringBuilder(Prefix);
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true, Indent = false, NewLineHandling = NewLineHandling.Entitize,
            ConformanceLevel = ConformanceLevel.Fragment, CheckCharacters = true
        };
        try
        {
            using var writer = XmlWriter.Create(new StringWriter(builder, CultureInfo.InvariantCulture), settings);
            var leading = Prefix.Length > 0;
            foreach (var node in Document.Nodes())
            {
                // The declaration prefix already carries the original whitespace that follows it.
                if (leading && node is XText) continue;
                leading = false;
                XmlLayoutWriter.Write(writer, node, Newline);
            }
        }
        // The writer rejects characters XML cannot carry, such as an unpaired surrogate, before the encoder sees them.
        catch (ArgumentException error) { throw new InvalidDataException("Edited text is not valid XML text: " + Member, error); }
        return Codec.Encode(builder.ToString(), Member);
    }
}
