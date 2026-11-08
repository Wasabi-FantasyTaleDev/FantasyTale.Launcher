using System.Globalization;

namespace FantasyTale.Launcher.Core.Definitions;

internal sealed record PluginVersion(uint Major, uint Minor, uint Patch, string[] Pre) : IComparable<PluginVersion>
{
    private static readonly string[] Operators = [">=", "<=", ">", "<", "="];
    internal static PluginVersion Parse(string text)
    {
        var metadata = text.Split('+');
        if (metadata.Length > 2 || (metadata.Length == 2 && !Identifiers(metadata[1], false))) throw new FormatException("invalid semantic version");
        var parts = metadata[0].Split('-', 2);
        var numbers = parts[0].Split('.');
        if (numbers.Length != 3 || numbers.Any(n => n.Length == 0 || (n.Length > 1 && n[0] == '0') || !n.All(char.IsAsciiDigit)))
            throw new FormatException("invalid semantic version");
        if (parts.Length == 2 && !Identifiers(parts[1], true)) throw new FormatException("invalid prerelease identifiers");
        return new PluginVersion(uint.Parse(numbers[0], CultureInfo.InvariantCulture), uint.Parse(numbers[1], CultureInfo.InvariantCulture),
            uint.Parse(numbers[2], CultureInfo.InvariantCulture), parts.Length == 2 ? parts[1].Split('.') : []);
    }

    private static bool Identifiers(string text, bool prerelease) => text.Split('.').All(p => p.Length > 0 &&
        p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && (!prerelease || !p.All(char.IsAsciiDigit) || p.Length == 1 || p[0] != '0'));

    public int CompareTo(PluginVersion? other)
    {
        if (other is null) return 1;
        var result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        if (Pre.Length == 0 || other.Pre.Length == 0) return (Pre.Length == 0).CompareTo(other.Pre.Length == 0);
        for (var i = 0; i < Math.Min(Pre.Length, other.Pre.Length); i++)
        {
            var a = Pre[i]; var b = other.Pre[i];
            var na = a.All(char.IsAsciiDigit); var nb = b.All(char.IsAsciiDigit);
            result = na != nb ? (na ? -1 : 1) : na && a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
            if (result != 0) return result;
        }
        return Pre.Length.CompareTo(other.Pre.Length);
    }

    internal static bool Satisfies(string version, string range)
    {
        var current = Parse(version);
        if (range.Length == 0) return true;
        var tokens = range.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) throw new FormatException("empty version range");
        var result = true;
        foreach (var token in tokens)
        {
            var op = Operators.FirstOrDefault(o => token.StartsWith(o, StringComparison.Ordinal)) ?? "";
            var c = current.CompareTo(Parse(token[op.Length..]));
            result &= op switch { ">=" => c >= 0, "<=" => c <= 0, ">" => c > 0, "<" => c < 0, _ => c == 0 };
        }
        return result;
    }
}
