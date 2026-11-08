using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FantasyTale.Launcher.Sdk;

namespace FantasyTale.Launcher.Games.BubbleFighter;

public sealed partial class BubbleFighterModule
{
    /// <summary>Updates only an existing login.ini's serverAddr and serverPort when they differ from the selected endpoint, preserving other bytes and settings.</summary>
    public void PrepareLaunch(LaunchContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if ((context.GameEndpoint ?? context.Server) is not { } endpoint) return;
        var path = Path.Combine(context.InstanceFolder, "login.ini");
        if (!File.Exists(path)) return;
        for (var parent = path; parent is not null; parent = Path.GetDirectoryName(parent))
            if (Path.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked login settings are not supported.");
        byte[] original;
        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
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
        var host = endpoint.Host.All(char.IsAscii) ? endpoint.Host : new IdnMapping().GetAscii(endpoint.Host);
        var port = endpoint.Port.ToString(CultureInfo.InvariantCulture);
        var seenAddress = false; var seenPort = false;
        var updated = LoginSettingLine().Replace(text, match =>
        {
            var address = match.Groups["key"].Value.Equals("serverAddr", StringComparison.OrdinalIgnoreCase);
            if (address) seenAddress = true; else seenPort = true;
            var value = match.Groups["value"].Value;
            var same = address ? value.Trim().Equals(host, StringComparison.OrdinalIgnoreCase)
                : int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number == endpoint.Port;
            return same ? match.Value : match.Groups["prefix"].Value + (address ? host : port);
        });
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\r') ? "\r" : "\n";
        if (!seenAddress || !seenPort)
        {
            if (updated.Length > 0 && updated[^1] is not ('\r' or '\n')) updated += newline;
            if (!seenAddress) updated += "serverAddr=" + host + newline;
            if (!seenPort) updated += "serverPort=" + port + newline;
        }
        if (updated == text) return;
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

    [GeneratedRegex(@"(?:\A|(?<=[\r\n]))(?<prefix>[ \t]*(?<key>serverAddr|serverPort)[ \t]*=[ \t]*)(?<value>[^\r\n]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LoginSettingLine();
}
