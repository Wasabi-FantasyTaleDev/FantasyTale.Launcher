using System.Text;

namespace FantasyTale.Launcher.Games.BubbleFighter.ResourceEdits;

/// <summary>Strict BOM-preserving text decoding; malformed or unencodable text is an error, never a replacement character.</summary>
internal sealed class TextCodec
{
    // UTF-32 LE must be tested before UTF-16 LE: its mark starts with the UTF-16 LE mark.
    private static readonly (string Name, byte[] Preamble)[] Marks =
    [
        ("utf-32le", [0xFF, 0xFE, 0x00, 0x00]), ("utf-32be", [0x00, 0x00, 0xFE, 0xFF]), ("utf-8", [0xEF, 0xBB, 0xBF]),
        ("utf-16le", [0xFF, 0xFE]), ("utf-16be", [0xFE, 0xFF])
    ];

    private TextCodec(string name, byte[] preamble) { Name = name; Encoding = Create(name); Preamble = preamble; }

    /// <summary>Canonical encoding name: utf-8, utf-16le, utf-16be, utf-32le or utf-32be.</summary>
    internal string Name { get; }

    /// <summary>Strict encoding without an implicit preamble.</summary>
    internal Encoding Encoding { get; }

    /// <summary>Exact BOM bytes found in the original member, written back unchanged.</summary>
    internal byte[] Preamble { get; }

    /// <summary>Encoding names a manifest may declare.</summary>
    internal static IEnumerable<string> Names => Marks.Select(m => m.Name);

    /// <summary>Detects a Unicode encoding by BOM; client members without a BOM are decoded as strict UTF-8.</summary>
    internal static TextCodec Detect(ReadOnlySpan<byte> bytes)
    {
        foreach (var (name, preamble) in Marks)
            if (bytes.StartsWith(preamble)) return new(name, preamble);
        return new("utf-8", []);
    }

    private static Encoding Create(string name) => name switch
    {
        "utf-8" => new UTF8Encoding(false, true),
        "utf-16le" => new UnicodeEncoding(false, false, true),
        "utf-16be" => new UnicodeEncoding(true, false, true),
        "utf-32le" => new UTF32Encoding(false, false, true),
        "utf-32be" => new UTF32Encoding(true, false, true),
        _ => throw new InvalidDataException("Unknown text encoding " + name + "; use " + string.Join(", ", Names))
    };

    /// <summary>
    /// Detects a plugin sheet's encoding. A BOM decides, and must agree with a declared encoding. Without a BOM the declared
    /// encoding is used; otherwise UTF-16 and UTF-32 are recognised by the zero bytes of their ASCII header, and anything
    /// else is strict UTF-8. Legacy code pages are never guessed.
    /// </summary>
    internal static TextCodec Sheet(ReadOnlySpan<byte> bytes, string? declared, string source)
    {
        if (declared is not null) _ = Create(declared);
        foreach (var (name, preamble) in Marks)
        {
            if (!bytes.StartsWith(preamble)) continue;
            if (declared is not null && declared != name) throw new InvalidDataException(source + ": the byte order mark says " + name + ", the manifest says " + declared);
            return new(name, preamble);
        }
        if (declared is not null) return new(declared, []);
        if (bytes.Length >= 4 && bytes[0] != 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0) return new("utf-32le", []);
        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] != 0) return new("utf-32be", []);
        if (bytes.Length >= 2 && bytes[0] != 0 && bytes[1] == 0) return new("utf-16le", []);
        if (bytes.Length >= 2 && bytes[0] == 0 && bytes[1] != 0) return new("utf-16be", []);
        return new("utf-8", []);
    }

    /// <summary>Decodes the text after the preamble, failing on invalid sequences.</summary>
    internal string Decode(byte[] bytes, string member)
    {
        try { return Encoding.GetString(bytes, Preamble.Length, bytes.Length - Preamble.Length); }
        catch (DecoderFallbackException error) { throw new InvalidDataException("Resource member is not valid " + Name + ": " + member, error); }
        catch (ArgumentException error) { throw new InvalidDataException("Resource member is not valid " + Name + ": " + member, error); }
    }

    /// <summary>Encodes text with the original preamble, failing on characters the encoding cannot represent.</summary>
    internal byte[] Encode(string text, string member)
    {
        try
        {
            var body = Encoding.GetBytes(text);
            var result = new byte[Preamble.Length + body.Length];
            Preamble.CopyTo(result, 0);
            body.CopyTo(result, Preamble.Length);
            return result;
        }
        catch (EncoderFallbackException error) { throw new InvalidDataException("Edited text cannot be encoded as " + Name + ": " + member, error); }
    }
}
