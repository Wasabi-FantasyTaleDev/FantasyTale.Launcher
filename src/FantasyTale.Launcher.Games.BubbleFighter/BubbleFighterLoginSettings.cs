using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter;

public sealed partial class BubbleFighterModule
{
    /// <summary>
    /// Points login.ini at the selected server, preserving other bytes and settings: serverAddr and serverPort in an
    /// existing file, and clanServerAddr and clanServerPort whenever the server serves clans for this build. Every clan
    /// client reads those two keys from login.ini (kr-812 sub_8EA9F0, default port 7400); a client shipped without the
    /// file (cn-717) gets one holding only the clan keys.
    /// </summary>
    public void PrepareLaunch(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var path = Path.Combine(context.InstanceFolder, "login.ini");
        var exists = File.Exists(path);
        var wanted = new List<(string Key, string Value)>();
        if (exists && (context.GameEndpoint ?? context.Server) is { } endpoint)
        {
            wanted.Add(("serverAddr", endpoint.Host.All(char.IsAscii) ? endpoint.Host : new IdnMapping().GetAscii(endpoint.Host)));
            wanted.Add(("serverPort", endpoint.Port.ToString(CultureInfo.InvariantCulture)));
        }
        if (context.ClanServer is { } clan)
        {
            wanted.Add(("clanServerAddr", clan.Host));
            wanted.Add(("clanServerPort", clan.Port.ToString(CultureInfo.InvariantCulture)));
        }
        if (wanted.Count == 0) return;
        for (var parent = path; parent is not null; parent = Path.GetDirectoryName(parent))
            if (Path.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked login settings are not supported.");
        var original = Array.Empty<byte>();
        if (exists)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > 65536) throw new IOException("login.ini exceeds the settings size limit.");
            original = new byte[checked((int)file.Length)]; file.ReadExactly(original);
        }
        var unicode = original.AsSpan().StartsWith(new byte[] { 0xff, 0xfe });
        var bigEndian = original.AsSpan().StartsWith(new byte[] { 0xfe, 0xff });
        var utf8Bom = original.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf });
        var skip = unicode || bigEndian ? 2 : utf8Bom ? 3 : 0;
        // Latin1 is reversible for ANSI and UTF-8 comments; endpoint fields use ASCII.
        var encoding = unicode ? Encoding.Unicode : bigEndian ? Encoding.BigEndianUnicode : Encoding.Latin1;
        var text = encoding.GetString(original, skip, original.Length - skip);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var updated = LoginSettingLine().Replace(text, match =>
        {
            var key = match.Groups["key"].Value;
            var target = wanted.FindIndex(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (target < 0) return match.Value;
            seen.Add(key);
            var value = wanted[target].Value;
            var current = match.Groups["value"].Value.Trim();
            var same = key.EndsWith("Port", StringComparison.OrdinalIgnoreCase)
                ? int.TryParse(current, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number.ToString(CultureInfo.InvariantCulture) == value
                : current.Equals(value, StringComparison.OrdinalIgnoreCase);
            return same ? match.Value : match.Groups["prefix"].Value + value;
        });
        // The shipped files are ASCII with CRLF; a new file follows them.
        var newline = !exists || text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\r') ? "\r" : "\n";
        foreach (var (key, value) in wanted.Where(item => !seen.Contains(item.Key)))
        {
            if (updated.Length > 0 && updated[^1] is not ('\r' or '\n')) updated += newline;
            updated += key + "=" + value + newline;
        }
        if (exists && updated == text) return;
        var replacement = original.AsSpan(0, skip).ToArray().Concat(encoding.GetBytes(updated)).ToArray();
        var temporary = path + ".fantasytale-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(replacement); output.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    [GeneratedRegex(@"(?:\A|(?<=[\r\n]))(?<prefix>[ \t]*(?<key>serverAddr|serverPort|clanServerAddr|clanServerPort)[ \t]*=[ \t]*)(?<value>[^\r\n]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LoginSettingLine();
}
